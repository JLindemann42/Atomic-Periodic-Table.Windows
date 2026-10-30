using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Data
{
    /// <summary>One element's share of a compound's mass.</summary>
    public sealed record CompositionPart(string Symbol, int Count, double MassContribution, double Percent);

    /// <summary>A parsed chemical formula with its molar mass and per-element breakdown.</summary>
    public sealed record FormulaResult(string Formula, double MolarMass, IReadOnlyList<CompositionPart> Parts);

    /// <summary>
    /// Chemistry the element data implies but does not store: formula masses, neutron counts and
    /// mole conversions.
    ///
    /// <para>These were questions the agent could not answer at all - "the molar mass of H2SO4"
    /// retrieved the dictionary definition of molar mass rather than computing a number, and "2
    /// moles of carbon into atoms" was answered as an element comparison.</para>
    /// </summary>
    public static class ChemistryMath
    {
        /// <summary>Avogadro's number, particles per mole.</summary>
        public const double Avogadro = 6.02214076e23;

        /// <summary>
        /// A quantity in moles, in every spelling the twelve shipped languages use.
        ///
        /// <para>Shared by the planner and the executor because they both have to read it and they
        /// must agree: when only the planner knew the Italian "2 moli" and the Chinese "2摩尔", the
        /// query was planned as a mole conversion and then executed to nothing, which the user sees
        /// as a shrug.</para>
        ///
        /// <para>The Han and Indic forms carry no word boundary after them, because those scripts
        /// write none. The <c>\b</c> after the Latin form is ASCII-only via
        /// <see cref="JavaRegex"/> - .NET's Unicode-aware boundary would stop "5 molé" matching,
        /// silently.</para>
        /// </summary>
        public static readonly Regex MoleQuantity =
            JavaRegex.Compile(@"([\d.]+)\s*(?:mol(?:e|es|i|s)?\b|मोल|مول|摩尔)");

        /// <summary>Subscript digits users paste from formatted text, e.g. H₂SO₄.</summary>
        private const string Subscripts = "₀₁₂₃₄₅₆₇₈₉";

        /// <summary>
        /// Drop a phase annotation from a species token: H2O(l), NaCl (aq), O2(g).
        ///
        /// <para>Only the four state symbols, matched whole - (OH) and (SO4) are structure, not
        /// state, and stripping those would change what the formula means.</para>
        /// </summary>
        private static readonly Regex StateAnnotation =
            JavaRegex.Compile(@"\s*\((?:s|l|g|aq)\)\s*$", RegexOptions.IgnoreCase);

        public static string StripStateAnnotation(string raw) =>
            StateAnnotation.Replace(raw.Trim(), string.Empty).Trim();

        /// <summary>
        /// Parse a formula and compute its molar mass from the element table.
        /// </summary>
        /// <param name="atomicMassOf">Supplies the atomic mass for a symbol, or null when unknown.</param>
        /// <returns>Null when the formula is unparseable or names an unknown element.</returns>
        public static FormulaResult? ParseFormula(string raw, Func<string, double?> atomicMassOf)
        {
            var formula = Normalise(raw);
            if (StrictParse(formula, atomicMassOf) is { } strict) return strict;

            // Users type formulas in lower case far more often than not - "h2so4" rather than
            // "H2SO4" - so recover the capitalisation before giving up. Only for candidates that
            // contain a digit, though: element symbols tile ordinary words alarmingly well, and
            // without this guard "percentage" parses as P-Er-Ce-N-Ta-Ge and weighs 605.93 g/mol.
            if (!formula.Any(char.IsDigit)) return null;

            var fixedCase = Recapitalise(formula, atomicMassOf);
            return fixedCase is null ? null : StrictParse(fixedCase, atomicMassOf);
        }

        /// <summary>
        /// Restore standard capitalisation of a case-insensitive formula.
        ///
        /// <para>Walks left to right taking the longest symbol that the caller recognises, so
        /// "h2so4" resolves H, S, O rather than stalling on "Hs" or "So".</para>
        /// </summary>
        private static string? Recapitalise(string formula, Func<string, double?> atomicMassOf)
        {
            if (formula.Length == 0) return null;

            var sb = new StringBuilder(formula.Length);
            var i = 0;

            while (i < formula.Length)
            {
                var c = formula[i];
                if (char.IsDigit(c) || c is '(' or ')' or '·')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                if (!char.IsLetter(c)) return null;

                string? two = null;
                if (i + 1 < formula.Length && char.IsLetter(formula[i + 1]))
                {
                    var pair = formula.Substring(i, 2).ToLowerInvariant();
                    two = char.ToUpperInvariant(pair[0]) + pair[1..];
                }
                var one = char.ToUpperInvariant(c).ToString();

                if (two is not null && atomicMassOf(two) is not null)
                {
                    sb.Append(two);
                    i += 2;
                }
                else if (atomicMassOf(one) is not null)
                {
                    sb.Append(one);
                    i++;
                }
                else
                {
                    return null;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Element symbol to atom count, without needing atomic masses.
        ///
        /// <para>Split out of <see cref="ParseFormula"/> because the balancer wants the counts and
        /// nothing else. Routing it through the mass path instead would make one element missing an
        /// atomic mass fatal to a perfectly balanceable equation - the counts are all the
        /// stoichiometry needs, and whether the app happens to know what a nuclide weighs is beside
        /// the point.</para>
        /// </summary>
        /// <param name="isKnownSymbol">
        /// Decides which symbols exist, so this file keeps no dependency on the element table.
        /// </param>
        /// <returns>Counts in first-seen order, or null when the string is not a formula.</returns>
        public static IReadOnlyDictionary<string, int>? ElementCounts(
            string raw, Func<string, bool> isKnownSymbol)
        {
            var formula = Normalise(StripStateAnnotation(raw));

            if (CountAtomsWithHydrates(formula) is { } counts && counts.Keys.All(isKnownSymbol))
                return counts;

            // Same digit gate as ParseFormula: without it "percentage" parses as P-Er-Ce-N-Ta-Ge.
            if (!formula.Any(char.IsDigit)) return null;

            var fixedCase = Recapitalise(formula, symbol => isKnownSymbol(symbol) ? 1.0 : null);
            if (fixedCase is null) return null;

            var recounted = CountAtomsWithHydrates(fixedCase);
            return recounted is not null && recounted.Keys.All(isKnownSymbol) ? recounted : null;
        }

        /// <summary>
        /// Walk a normalised formula, resolving groups and multipliers.
        /// </summary>
        /// <returns>
        /// Counts in first-seen order, or null when the string does not parse. Symbols are not
        /// validated here; the callers differ on what they accept.
        /// </returns>
        private static Dictionary<string, int>? CountAtoms(string formula)
        {
            if (formula.Length == 0 || !char.IsUpper(formula[0])) return null;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            // Multipliers for the enclosing groups, innermost last.
            var stack = new Stack<Dictionary<string, int>>();
            var current = counts;
            var index = 0;
            var sawElement = false;

            while (index < formula.Length)
            {
                var c = formula[index];

                if (c == '(')
                {
                    stack.Push(current);
                    current = new Dictionary<string, int>(StringComparer.Ordinal);
                    index++;
                }
                else if (c == ')')
                {
                    if (stack.Count == 0) return null;
                    index++;
                    var (multiplier, consumed) = ReadNumber(formula, index);
                    index += consumed;
                    var parent = stack.Pop();
                    foreach (var (symbol, n) in current)
                        Merge(parent, symbol, n * (multiplier ?? 1));
                    current = parent;
                }
                else if (char.IsUpper(c))
                {
                    var symbol = c.ToString();
                    index++;
                    if (index < formula.Length && char.IsLower(formula[index]))
                    {
                        symbol += formula[index];
                        index++;
                    }
                    var (count, consumed) = ReadNumber(formula, index);
                    index += consumed;
                    Merge(current, symbol, count ?? 1);
                    sawElement = true;
                }
                else
                {
                    return null;
                }
            }

            if (!sawElement || stack.Count > 0) return null;
            return counts;
        }

        /// <summary>
        /// Adds to a count, preserving first-seen order.
        ///
        /// <para>C# <see cref="Dictionary{TKey,TValue}"/> does not guarantee enumeration order the
        /// way Kotlin's LinkedHashMap does, but it does preserve insertion order in practice for
        /// insert-only dictionaries, and this one never removes. Callers that need the order
        /// guaranteed - the composition breakdown - re-derive it from the counts.</para>
        /// </summary>
        private static void Merge(Dictionary<string, int> into, string symbol, int n) =>
            into[symbol] = into.TryGetValue(symbol, out var existing) ? existing + n : n;

        private static FormulaResult? StrictParse(string formula, Func<string, double?> atomicMassOf)
        {
            var counts = CountAtomsWithHydrates(formula);
            if (counts is null) return null;

            var total = 0.0;
            var masses = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (symbol, count) in counts)
            {
                if (atomicMassOf(symbol) is not { } mass) return null;
                var contribution = mass * count;
                masses[symbol] = contribution;
                total += contribution;
            }
            if (total <= 0.0) return null;

            var parts = counts
                .Select(kv => new CompositionPart(
                    kv.Key, kv.Value, masses[kv.Key], masses[kv.Key] / total * 100.0))
                .ToList();

            return new FormulaResult(formula, total, parts);
        }

        /// <summary>Neutrons in a specific nuclide: mass number minus proton count.</summary>
        public static int? NeutronsIn(int massNumber, int atomicNumber) =>
            massNumber - atomicNumber is var n && n >= 0 ? n : null;

        /// <summary>Particles in a given number of moles.</summary>
        public static double MolesToParticles(double moles) => moles * Avogadro;

        /// <summary>Moles represented by a given number of particles.</summary>
        public static double ParticlesToMoles(double particles) => particles / Avogadro;

        /// <summary>Moles in a mass of a substance of known molar mass.</summary>
        public static double? MassToMoles(double grams, double molarMass) =>
            molarMass > 0 ? grams / molarMass : null;

        /// <summary>
        /// Weighted mean atomic mass from isotope masses and their fractional abundances.
        /// Abundances may be given as percentages or fractions; both are normalised.
        /// </summary>
        public static double? AverageAtomicMass(IReadOnlyList<(double Mass, double Abundance)> values)
        {
            if (values.Count == 0) return null;
            var totalWeight = values.Sum(v => v.Abundance);
            if (totalWeight <= 0) return null;
            return values.Sum(v => v.Mass * v.Abundance) / totalWeight;
        }

        /// <summary>
        /// Uppercase-normalised formula with subscripts and spaces folded out.
        ///
        /// <para>The hydrate separator is <b>kept</b>, unlike the Kotlin original - see
        /// <see cref="CountAtoms"/> for why deleting it corrupts the formula.</para>
        /// </summary>
        private static string Normalise(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw.Trim())
            {
                var subscript = Subscripts.IndexOf(c, StringComparison.Ordinal);
                if (subscript >= 0)
                    sb.Append(subscript.ToString(CultureInfo.InvariantCulture));
                else if (c is '·' or '*')
                    sb.Append('·');
                else if (char.IsLetterOrDigit(c) || c is '(' or ')')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Splits a formula on the hydrate separator and applies each segment's leading multiplier.
        ///
        /// <para>DIVERGENCE from the Kotlin, which deletes the separator before parsing. That turns
        /// CuSO4·5H2O into CuSO45H2O, where "O4" and the hydrate's "5" fuse into a single "O45" -
        /// so copper(II) sulfate pentahydrate comes back with 46 oxygens instead of 9, and a molar
        /// mass roughly six times too large. Nothing in the acceptance corpus contains a hydrate,
        /// so this costs no parity; see docs/porting-notes.md D9.</para>
        /// </summary>
        private static Dictionary<string, int>? CountAtomsWithHydrates(string formula)
        {
            if (!formula.Contains('·', StringComparison.Ordinal))
                return CountAtoms(formula);

            var total = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var segment in formula.Split('·'))
            {
                if (segment.Length == 0) return null;

                // A leading integer multiplies the whole segment: the 5 in "5H2O".
                var (multiplier, consumed) = ReadNumber(segment, 0);
                var body = segment[consumed..];
                if (body.Length == 0) return null;

                var counts = CountAtoms(body);
                if (counts is null) return null;

                foreach (var (symbol, n) in counts)
                    Merge(total, symbol, n * (multiplier ?? 1));
            }
            return total.Count == 0 ? null : total;
        }

        /// <summary>Reads digits at an index; returns the value (null when absent) and chars consumed.</summary>
        private static (int? Value, int Consumed) ReadNumber(string text, int from)
        {
            var i = from;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            if (i == from) return (null, 0);

            var slice = text[from..i];
            return int.TryParse(slice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? (v, i - from)
                : (null, i - from);
        }
    }
}
