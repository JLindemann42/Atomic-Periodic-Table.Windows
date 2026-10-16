using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Data
{
    /// <summary>One side's term before balancing: a formula and the atom counts it contributes.</summary>
    public sealed record Species(string Formula, IReadOnlyDictionary<string, int> Counts);

    /// <summary>A balanced term, ready to render.</summary>
    public sealed record Term(int Coefficient, string Formula);

    /// <summary>Per-element proof that both sides agree, or the evidence that they cannot.</summary>
    public sealed record ElementTally(string Symbol, int Left, int Right);

    /// <summary>
    /// Balances a chemical equation with exact rational arithmetic.
    ///
    /// <para>The Android app ships a balancer in <c>ChemicalReactionsActivity</c> and it does not
    /// work: it augments the system with a zero right-hand side and then reads the solution out of
    /// that same zero column, so every input comes back all zeros or NaN. This is a fresh
    /// implementation rather than a repair of that one, and it is deliberately kept in the data
    /// layer where it can be tested without a UI.</para>
    ///
    /// <para>Every step runs in <see cref="Fraction"/>. Row reduction over a stoichiometry matrix
    /// produces thirds and sevenths constantly, and in floating point a leftover 1e-12 is
    /// indistinguishable from a real zero pivot - which is the difference between an equation that
    /// balances and one that does not. There is no epsilon here because any epsilon would be a
    /// guess.</para>
    ///
    /// <para>The failure modes are as important as the success. An equation with several
    /// independent balances is reported as such rather than answered with whichever one the
    /// elimination happened to produce.</para>
    /// </summary>
    public static class EquationBalancer
    {
        /// <summary>Why an equation could not be balanced. Each is a sentence the user is owed.</summary>
        public enum Reason
        {
            /// <summary>An element appears on only one side, so no coefficients can fix it.</summary>
            OneSidedElement,

            /// <summary>The system admits only the trivial all-zero solution.</summary>
            NoSolution,

            /// <summary>
            /// Several independent balances exist.
            ///
            /// <para><c>CO + O2 + H2 -&gt; CO2 + H2O</c> is the everyday example: two reactions
            /// written as one, and picking a basis vector out of the null space would present one
            /// arbitrary answer as the answer.</para>
            /// </summary>
            Underdetermined,

            /// <summary>A coefficient ran past <see cref="MaxCoefficient"/>, or the rationals overflowed.</summary>
            TooLarge,

            /// <summary>A species did not parse as a chemical formula.</summary>
            ParseFailed,
        }

        public abstract record Result
        {
            private Result() { }

            public sealed record Balanced(
                IReadOnlyList<Term> Reactants,
                IReadOnlyList<Term> Products,
                IReadOnlyList<ElementTally> Tally) : Result;

            public sealed record Failed(Reason Reason, string? Detail = null) : Result;
        }

        /// <summary>
        /// What reading the text produced.
        ///
        /// <para>Three different situations used to be one null: no arrow yet, an arrow over
        /// unreadable formulas, and an equation too long to solve. Callers cannot tell them apart
        /// from a null, so they all guessed the same way - the tool showed "type an equation" to
        /// somebody who had, and the assistant said "I could not read that" about an equation it
        /// had read perfectly well and merely found too long.</para>
        /// </summary>
        public abstract record ParseOutcome
        {
            private ParseOutcome() { }

            public sealed record Sides(
                IReadOnlyList<Species> Reactants,
                IReadOnlyList<Species> Products) : ParseOutcome;

            /// <summary>No reaction arrow. The normal state while somebody is still typing.</summary>
            public sealed record NoArrowOutcome : ParseOutcome { internal NoArrowOutcome() { } }

            /// <summary>An arrow, but a side would not read as chemical formulas.</summary>
            public sealed record NotAnEquationOutcome : ParseOutcome { internal NotAnEquationOutcome() { } }

            /// <summary>Read fine, but past <see cref="MaxSpecies"/>.</summary>
            public sealed record TooManySpeciesOutcome : ParseOutcome { internal TooManySpeciesOutcome() { } }

            public static readonly ParseOutcome NoArrow = new NoArrowOutcome();
            public static readonly ParseOutcome NotAnEquation = new NotAnEquationOutcome();
            public static readonly ParseOutcome TooManySpecies = new TooManySpeciesOutcome();
        }

        /// <summary>Beyond this the input is not a question anybody typed on purpose.</summary>
        public const int MaxSpecies = 12;

        /// <summary>
        /// Where a side stops being parsed at all.
        ///
        /// <para><see cref="MaxSpecies"/> is a verdict - an equation of thirteen species is
        /// reported as too large. This is a guard: prose that happens to be full of + signs must
        /// never reach the reduction and set it multiplying rationals across a matrix that
        /// wide.</para>
        /// </summary>
        private const int ParseCeiling = 40;

        /// <summary>A real equation does not need coefficients this large.</summary>
        public const int MaxCoefficient = 1000;

        /// <summary>
        /// Reaction arrows.
        ///
        /// <para>A bare <c>=</c> counts, since plenty of people write one, but never <c>==</c>,
        /// <c>&lt;=</c>, <c>&gt;=</c> or <c>!=</c>: those appear in comparisons, and reading one as
        /// an equation would claim a query that is not one. The Kotlin file used to claim that in a
        /// comment while testing <c>raw.contains("=")</c>, which is true of all four - so
        /// "H2 == O2" was declined by the planner, which had the lookarounds, and balanced by the
        /// tool, which called straight in. The pattern lives here now and the planner uses it, so
        /// the two cannot disagree again.</para>
        ///
        /// <para>Matching rather than scanning a list of literals also fixes the split point: a
        /// regex match returns the <i>earliest</i> arrow, where taking the first literal that
        /// occurs anywhere cut "A = B -&gt; C" at the arrow. The <c>-{1,2}&gt;</c> branch is
        /// greedy, so <c>--&gt;</c> is still never read as <c>-</c> followed by <c>-&gt;</c>.</para>
        /// </summary>
        public static readonly Regex Arrow =
            JavaRegex.Compile(@"(?:<->|-{1,2}>|=>|→|⇌|⇄|⟶|(?<![<>=!])=(?![=>]))");

        /// <summary>Splits a side on +, but only a + that separates: Na+ is a charge.</summary>
        private static readonly Regex Plus = JavaRegex.Compile(@"\s\+\s|\s\+|\+\s");

        /// <summary>A leading coefficient the user supplied, which is re-derived rather than kept.</summary>
        private static readonly Regex LeadingCoefficient = JavaRegex.Compile(@"^(\d+)\s*(?=[A-Za-z(])");

        /// <summary>
        /// Split raw text into reactants and products, saying which way it failed if it did.
        ///
        /// <para><see cref="ParseOutcome.NoArrow"/> is the one a caller must not treat as an error:
        /// it is what half a typed equation looks like, and what a sentence that was never an
        /// equation looks like.</para>
        /// </summary>
        public static ParseOutcome Parse(string raw, Func<string, bool> isKnownSymbol)
        {
            var arrow = Arrow.Match(raw);
            if (!arrow.Success) return ParseOutcome.NoArrow;

            var left = SideOf(raw[..arrow.Index], isKnownSymbol);
            if (left is null) return ParseOutcome.NotAnEquation;

            var right = SideOf(raw[(arrow.Index + arrow.Length)..], isKnownSymbol);
            if (right is null) return ParseOutcome.NotAnEquation;

            if (left.Count == 0 || right.Count == 0) return ParseOutcome.NotAnEquation;

            // A verdict, not a parse error: the equation was read, there is just too much of it.
            if (left.Count + right.Count > MaxSpecies) return ParseOutcome.TooManySpecies;

            return new ParseOutcome.Sides(left, right);
        }

        /// <summary>
        /// Split raw text into reactants and products.
        /// </summary>
        /// <returns>
        /// The two sides, or null when the text is not an equation this can solve - which is how
        /// the planner declines a sentence containing a stray arrow rather than claiming it. Use
        /// <see cref="Parse"/> where the reason matters; it is the same work with the reason kept.
        /// </returns>
        public static (IReadOnlyList<Species> Reactants, IReadOnlyList<Species> Products)? ParseEquation(
            string raw, Func<string, bool> isKnownSymbol) =>
            Parse(raw, isKnownSymbol) is ParseOutcome.Sides s ? (s.Reactants, s.Products) : null;

        /// <summary>
        /// Parse one side of an equation.
        ///
        /// <para>Everything before the first parseable species is dropped, so "balance this
        /// reaction: Fe + O2" loses its preamble rather than failing on it. Once a species has
        /// parsed, though, a token that does not parse fails the whole side - a half-read equation
        /// is worse than none.</para>
        /// </summary>
        private static List<Species>? SideOf(string raw, Func<string, bool> isKnownSymbol)
        {
            var species = new List<Species>();

            foreach (var token in Plus.Split(raw))
            {
                var cleaned = LeadingCoefficient
                    .Replace(ChemistryMath.StripStateAnnotation(token).Trim(), string.Empty)
                    .Trim();
                if (cleaned.Length == 0) continue;

                // An equation written in a sentence carries words on both ends: "balance this
                // reaction: Fe" in front and "Fe2O3 संतुलित करें" behind. Trying only the whole
                // token drops the species along with the prose, and the equation then fails as
                // one-sided on an element that was written down - a confident, wrong diagnosis of
                // a good question. So when the token as a whole does not parse, each word in it is
                // tried in turn.
                var candidates = new List<string> { cleaned };
                candidates.AddRange(cleaned.Split(' ', ':').Select(p => p.Trim()));

                Species? parsed = null;
                foreach (var candidate in candidates)
                {
                    if (candidate.Length == 0) continue;
                    if (ChemistryMath.ElementCounts(candidate, isKnownSymbol) is { } counts)
                    {
                        parsed = new Species(candidate, counts);
                        break;
                    }
                }

                if (parsed is null)
                {
                    // Prose in front of the equation is expected; prose inside it is not.
                    if (species.Count == 0) continue;
                    return null;
                }

                species.Add(parsed);

                // Stop reading rather than hand the reduction a matrix this wide. Anything over
                // MaxSpecies is refused upstream anyway, so nothing solvable is lost here.
                if (species.Count > ParseCeiling) return species;
            }

            return species;
        }

        /// <summary>
        /// Find the smallest positive integer coefficients that balance the equation.
        ///
        /// <para>The matrix has one row per element and one column per species, reactants positive
        /// and products negative, so a balanced equation is exactly a positive vector in the null
        /// space.</para>
        /// </summary>
        public static Result Balance(IReadOnlyList<Species> reactants, IReadOnlyList<Species> products)
        {
            if (reactants.Count == 0 || products.Count == 0)
                return new Result.Failed(Reason.ParseFailed);

            if (reactants.Count + products.Count > MaxSpecies)
                return new Result.Failed(Reason.TooLarge);

            var species = reactants.Concat(products).ToList();
            var symbols = species.SelectMany(s => s.Counts.Keys).Distinct(StringComparer.Ordinal).ToList();

            // An element written on one side only can never be evened out, whatever the
            // coefficients. Saying so names the actual mistake, where "no solution" leaves the user
            // hunting for it.
            foreach (var symbol in symbols)
            {
                var onLeft = reactants.Any(s => s.Counts.ContainsKey(symbol));
                var onRight = products.Any(s => s.Counts.ContainsKey(symbol));
                if (!onLeft || !onRight)
                    return new Result.Failed(Reason.OneSidedElement, symbol);
            }

            var matrix = new List<List<Fraction>>(symbols.Count);
            foreach (var symbol in symbols)
            {
                var row = new List<Fraction>(species.Count);
                for (var column = 0; column < species.Count; column++)
                {
                    var count = (long)(species[column].Counts.TryGetValue(symbol, out var n) ? n : 0);
                    var signed = column < reactants.Count ? count : -count;
                    if (Fraction.Of(signed) is not { } f) return new Result.Failed(Reason.TooLarge);
                    row.Add(f);
                }
                matrix.Add(row);
            }

            var pivots = Reduce(matrix);
            if (pivots is null) return new Result.Failed(Reason.TooLarge);

            var pivotColumns = pivots.Values.ToHashSet();
            var freeColumns = Enumerable.Range(0, species.Count)
                .Where(c => !pivotColumns.Contains(c))
                .ToList();

            // Nullity 0 means only the zero vector solves it; more than 1 means several independent
            // balances exist and choosing between them would be a guess dressed up as an answer.
            if (freeColumns.Count == 0) return new Result.Failed(Reason.NoSolution);
            if (freeColumns.Count > 1) return new Result.Failed(Reason.Underdetermined);

            var freeColumn = freeColumns[0];
            var solution = Enumerable.Repeat(Fraction.Zero, species.Count).ToList();
            solution[freeColumn] = Fraction.One;
            foreach (var (row, column) in pivots)
            {
                // With the matrix in reduced row echelon form, the pivot row reads
                // x_pivot + coefficient * x_free = 0.
                solution[column] = -matrix[row][freeColumn];
            }

            var integers = ToSmallestIntegers(solution);
            if (integers is null) return new Result.Failed(Reason.TooLarge);
            if (integers.Any(v => v <= 0L)) return new Result.Failed(Reason.NoSolution);
            if (integers.Any(v => v > MaxCoefficient)) return new Result.Failed(Reason.TooLarge);

            var coefficients = integers.Select(v => (int)v).ToList();

            var tally = new List<ElementTally>(symbols.Count);
            foreach (var symbol in symbols)
            {
                var left = 0;
                var right = 0;
                for (var i = 0; i < species.Count; i++)
                {
                    var atoms = (species[i].Counts.TryGetValue(symbol, out var n) ? n : 0) * coefficients[i];
                    if (i < reactants.Count) left += atoms;
                    else right += atoms;
                }
                tally.Add(new ElementTally(symbol, left, right));
            }

            // The elimination is believed only after its answer is checked back against the input.
            // If they disagree the bug is here, and emitting a confident wrong equation would hide it.
            if (tally.Any(t => t.Left != t.Right)) return new Result.Failed(Reason.NoSolution);

            return new Result.Balanced(
                reactants.Select((s, i) => new Term(coefficients[i], s.Formula)).ToList(),
                products.Select((s, i) => new Term(coefficients[reactants.Count + i], s.Formula)).ToList(),
                tally);
        }

        /// <summary>
        /// Reduce a matrix to reduced row echelon form in place.
        /// </summary>
        /// <returns>Row index to pivot column, or null when the rationals overflowed.</returns>
        private static Dictionary<int, int>? Reduce(List<List<Fraction>> matrix)
        {
            var pivots = new Dictionary<int, int>();
            if (matrix.Count == 0) return pivots;

            var columns = matrix[0].Count;
            var row = 0;

            for (var column = 0; column < columns; column++)
            {
                var pivotRow = -1;
                for (var r = row; r < matrix.Count; r++)
                {
                    if (!matrix[r][column].IsZero) { pivotRow = r; break; }
                }
                if (pivotRow < 0) continue;

                (matrix[row], matrix[pivotRow]) = (matrix[pivotRow], matrix[row]);

                var pivot = matrix[row][column];
                for (var c = 0; c < columns; c++)
                {
                    if (matrix[row][c] / pivot is not { } divided) return null;
                    matrix[row][c] = divided;
                }

                for (var r = 0; r < matrix.Count; r++)
                {
                    if (r == row) continue;
                    var factor = matrix[r][column];
                    if (factor.IsZero) continue;

                    for (var c = 0; c < columns; c++)
                    {
                        if (matrix[row][c] * factor is not { } product) return null;
                        if (matrix[r][c] - product is not { } difference) return null;
                        matrix[r][c] = difference;
                    }
                }

                pivots[row] = column;
                row++;
                if (row == matrix.Count) break;
            }

            return pivots;
        }

        /// <summary>Scale a rational solution to the smallest positive integers, or null on overflow.</summary>
        private static List<long>? ToSmallestIntegers(IReadOnlyList<Fraction> solution)
        {
            var multiplier = 1L;
            foreach (var f in solution)
            {
                if (Fraction.Lcm(multiplier, f.Den) is not { } next) return null;
                multiplier = next;
            }

            var scaled = new List<long>(solution.Count);
            foreach (var f in solution)
            {
                var value = Try.OrNullValue(() => checked(f.Num * (multiplier / f.Den)));
                if (value is not { } v) return null;
                scaled.Add(v);
            }

            var divisor = scaled.Aggregate(0L, Fraction.Gcd);
            if (divisor == 0L) return null;

            return scaled.Select(v => v / divisor).ToList();
        }
    }
}
