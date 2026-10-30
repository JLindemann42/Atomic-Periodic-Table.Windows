using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// The ideal gas law, PV = nRT (roadmap 2.8), in litres, atmospheres, moles and kelvin. Given
    /// any three, it solves the fourth. Mirrors Android's <c>GasLaw</c>.
    /// </summary>
    public static class GasLaw
    {
        /// <summary>L·atm/(mol·K).</summary>
        public const double R = 0.082057366080960;

        public const double StpKelvin = 273.15;
        public const double StpAtm = 1.0;

        public enum Unknown { Pressure, Volume, Moles, Temperature }

        /// <summary>All four values, and which one was solved for.</summary>
        public sealed record Solution(Unknown Unknown, double Atm, double Litres, double Moles, double Kelvin)
        {
            /// <summary>The value that was asked for, in its unit above.</summary>
            public double Solved => Unknown switch
            {
                Unknown.Pressure => Atm,
                Unknown.Volume => Litres,
                Unknown.Moles => Moles,
                _ => Kelvin,
            };
        }

        /// <summary>Solve for the one value that is null, or null when not exactly one is, or a given one is not positive.</summary>
        public static Solution? Solve(double? atm, double? litres, double? moles, double? kelvin)
        {
            var given = new[] { atm, litres, moles, kelvin }.Where(v => v is not null).Select(v => v!.Value).ToList();
            if (given.Count != 3 || given.Any(v => v <= 0 || double.IsNaN(v) || double.IsInfinity(v))) return null;
            if (atm is null) return new Solution(Unknown.Pressure, moles!.Value * R * kelvin!.Value / litres!.Value, litres.Value, moles.Value, kelvin.Value);
            if (litres is null) return new Solution(Unknown.Volume, atm.Value, moles!.Value * R * kelvin!.Value / atm.Value, moles.Value, kelvin.Value);
            if (moles is null) return new Solution(Unknown.Moles, atm.Value, litres.Value, atm.Value * litres.Value / (R * kelvin!.Value), kelvin.Value);
            return new Solution(Unknown.Temperature, atm.Value, litres.Value, moles.Value, atm.Value * litres.Value / (moles.Value * R));
        }
    }

    /// <summary>
    /// The pH of a strong acid or base in water (roadmap 2.8). Mirrors Android's <c>AcidBase</c>.
    /// </summary>
    public static class AcidBase
    {
        /// <summary>The ion product of water at 25 °C.</summary>
        public const double Kw = 1.0e-14;

        private static readonly IReadOnlyDictionary<string, int> StrongAcids = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["HCl"] = 1, ["HBr"] = 1, ["HI"] = 1, ["HNO3"] = 1, ["HClO4"] = 1, ["HClO3"] = 1, ["H2SO4"] = 2,
        };

        private static readonly IReadOnlyDictionary<string, int> StrongBases = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["LiOH"] = 1, ["NaOH"] = 1, ["KOH"] = 1, ["RbOH"] = 1, ["CsOH"] = 1, ["Ca(OH)2"] = 2, ["Sr(OH)2"] = 2, ["Ba(OH)2"] = 2,
        };

        /// <param name="Factor">Protons per acid, or hydroxides per base.</param>
        /// <param name="Hydrogen">[H⁺] in mol/L, counting water's own ions, so a very dilute acid stays below 7.</param>
        public sealed record Result(string Formula, double Molarity, int Factor, bool Base, double Hydrogen, double Hydroxide)
        {
            public double PH => -Math.Log10(Hydrogen);

            public double POH => -Math.Log10(Hydroxide);
        }

        /// <summary>The strong acid or base the formula names, as the table writes it, or null.</summary>
        public static (string Formula, bool Base)? Strong(string formula, Func<string, IReadOnlyDictionary<string, int>?> composition)
        {
            if (composition(formula) is not { } target) return null;
            foreach (var acid in StrongAcids.Keys)
                if (Same(composition(acid), target)) return (acid, false);
            foreach (var b in StrongBases.Keys)
                if (Same(composition(b), target)) return (b, true);
            return null;
        }

        private static bool Same(IReadOnlyDictionary<string, int>? a, IReadOnlyDictionary<string, int> b) =>
            a is not null && a.Count == b.Count && a.All(e => b.TryGetValue(e.Key, out var n) && n == e.Value);

        /// <summary>The solution's pH, solving x = (c + √(c² + 4·Kw)) / 2 for the major ion.</summary>
        public static Result? Solve(string formula, bool @base, double molarity)
        {
            if (molarity <= 0 || double.IsNaN(molarity) || double.IsInfinity(molarity)) return null;
            if (!(@base ? StrongBases : StrongAcids).TryGetValue(formula, out var factor)) return null;
            var supplied = molarity * factor;
            var major = (supplied + Math.Sqrt(supplied * supplied + 4 * Kw)) / 2;
            var minor = Kw / major;
            return @base
                ? new Result(formula, molarity, factor, true, minor, major)
                : new Result(formula, molarity, factor, false, major, minor);
        }
    }

    /// <summary>
    /// The electron configuration of an ion (roadmap 2.8). A cation loses electrons from the
    /// outermost shell first, and within it the highest l; an anion gains them in filling order.
    /// Mirrors Android's <c>IonConfig</c>.
    /// </summary>
    public static class IonConfig
    {
        public sealed record Subshell(int N, char L, int Electrons)
        {
            public override string ToString() => $"{N}{L}{Superscript(Electrons)}";
        }

        public sealed record Configuration(string? Core, IReadOnlyList<Subshell> Subshells, string? IsoelectronicWith)
        {
            public override string ToString()
            {
                var parts = (Core is null ? [] : new[] { $"[{Core}]" }).Concat(Subshells.Select(s => s.ToString())).ToList();
                return parts.Count == 0 ? "—" : string.Join(" ", parts);
            }
        }

        private static readonly (int Z, string Symbol)[] Noble = [(2, "He"), (10, "Ne"), (18, "Ar"), (36, "Kr"), (54, "Xe"), (86, "Rn"), (118, "Og")];

        private static readonly string[] Filling =
            ["1s", "2s", "2p", "3s", "3p", "4s", "3d", "4p", "5s", "4d", "5p", "6s", "4f", "5d", "6p", "7s", "5f", "6d", "7p"];

        private static readonly IReadOnlyDictionary<char, int> Capacity = new Dictionary<char, int> { ['s'] = 2, ['p'] = 6, ['d'] = 10, ['f'] = 14 };
        private static readonly IReadOnlyDictionary<char, int> LOrder = new Dictionary<char, int> { ['s'] = 0, ['p'] = 1, ['d'] = 2, ['f'] = 3 };
        private static readonly Regex SubshellPattern = new(@"(\d)([spdf])\^?(\d+)", RegexOptions.CultureInvariant);

        /// <summary>Read the element files' "3d^6 4s^2" or "4f^14, 5d^4, 6s^2".</summary>
        public static List<Subshell> Parse(string text) =>
            SubshellPattern.Matches(text).Select(m => new Subshell(m.Groups[1].Value[0] - '0', m.Groups[2].Value[0], int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture))).ToList();

        private static int FillIndex(Subshell s) => Array.IndexOf(Filling, $"{s.N}{s.L}");

        /// <summary>The ion's configuration, or null when it would break into the noble-gas core.</summary>
        public static Configuration? Ion(int atomicNumber, string outer, int charge)
        {
            var subshells = Parse(outer);
            if (subshells.Count == 0) return null;
            // What the outer configuration does not account for is the core, and it must be a noble gas.
            var coreZ = atomicNumber - subshells.Sum(s => s.Electrons);
            string? core = null;
            if (coreZ != 0)
            {
                var match = Noble.FirstOrDefault(n => n.Z == coreZ);
                if (match.Symbol is null) return null;
                core = match.Symbol;
            }

            if (charge > 0)
            {
                for (var i = 0; i < charge; i++)
                {
                    // The outermost shell first, and within it the highest l.
                    var outermost = subshells.Where(s => s.Electrons > 0).OrderByDescending(s => s.N).ThenByDescending(s => LOrder[s.L]).FirstOrDefault();
                    if (outermost is null) return null;
                    subshells[subshells.IndexOf(outermost)] = outermost with { Electrons = outermost.Electrons - 1 };
                }
            }
            else if (charge < 0)
            {
                for (var i = 0; i < -charge; i++)
                {
                    // The last subshell begun, in filling order, until it is full; then the next one.
                    var last = subshells.Max(FillIndex);
                    var open = subshells.Where(s => s.Electrons < Capacity[s.L]).OrderByDescending(FillIndex).FirstOrDefault();
                    if (open is not null && FillIndex(open) >= last)
                    {
                        subshells[subshells.IndexOf(open)] = open with { Electrons = open.Electrons + 1 };
                    }
                    else
                    {
                        if (last + 1 >= Filling.Length) return null;
                        var next = Filling[last + 1];
                        subshells.Add(new Subshell(next[0] - '0', next[1], 1));
                    }
                }
            }

            var remaining = subshells.Where(s => s.Electrons > 0).ToList();
            var total = coreZ + remaining.Sum(s => s.Electrons);
            var noble = Noble.FirstOrDefault(n => n.Z == total).Symbol;
            // A full shell reads as its noble gas: Na⁺ is [Ne], Cl⁻ is [Ar].
            return noble is not null ? new Configuration(noble, [], noble) : new Configuration(core, remaining, null);
        }

        /// <summary>"Fe³⁺", "Cl⁻", "O²⁻".</summary>
        public static string IonLabel(string symbol, int charge)
        {
            var size = Math.Abs(charge);
            return symbol + (size == 1 ? "" : Superscript(size)) + (charge > 0 ? "⁺" : "⁻");
        }

        private static string Superscript(int n) => string.Concat(n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));
    }

    /// <summary>
    /// Empirical and molecular formulas (roadmap 2.8): from the mass of each element, or a
    /// percentage, which is the mass in 100 g; or from a molecular formula by dividing out its common
    /// factor. Mirrors Android's <c>Empirical</c>.
    /// </summary>
    public static class Empirical
    {
        /// <summary>How far a mole ratio may sit from a whole number and still be read as one.</summary>
        public const double Tolerance = 0.1;

        /// <summary>The largest multiplier tried to clear a ratio such as 1.5 or 1.33.</summary>
        public const int MaxMultiplier = 8;

        /// <summary>How far the given molar mass may sit from a whole multiple of the empirical one, relatively.</summary>
        public const double MolarMassTolerance = 0.02;

        /// <summary>One element's path from mass to subscript.</summary>
        public sealed record Row(string Symbol, double Mass, double AtomicMass, double Moles, double Ratio, int Count);

        /// <summary>The molecular formula, <paramref name="Factor"/> times the empirical one, for the <paramref name="MolarMass"/> the question gave.</summary>
        public sealed record Molecular(int Factor, IReadOnlyDictionary<string, int> Counts, double MolarMass)
        {
            public string Formula => Format(Counts);
        }

        /// <param name="Rows">The working, in Hill order; empty when the formula was reduced rather than built.</param>
        /// <param name="Multiplier">What the mole ratios were multiplied by to make them whole.</param>
        /// <param name="From">The molecular formula that was reduced, when one was given.</param>
        /// <param name="Divisor">The common factor that was divided out of <paramref name="From"/>.</param>
        public sealed record Result(
            IReadOnlyDictionary<string, int> Counts,
            double EmpiricalMass,
            IReadOnlyList<Row> Rows,
            int Multiplier,
            string? From,
            int Divisor,
            Molecular? MolecularFormula)
        {
            public string Formula => Format(Counts);
        }

        /// <summary>The empirical formula from each element's mass or percentage, or null when no ratio comes out whole.</summary>
        public static Result? FromMasses(IReadOnlyDictionary<string, double> masses, Func<string, double?> atomicMassOf, double? molarMass = null)
        {
            if (masses.Count < 2 || masses.Values.Any(m => m <= 0 || double.IsNaN(m) || double.IsInfinity(m))) return null;
            var atomic = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var symbol in masses.Keys)
            {
                if (atomicMassOf(symbol) is not { } am) return null;
                atomic[symbol] = am;
            }
            var moles = masses.ToDictionary(e => e.Key, e => e.Value / atomic[e.Key], StringComparer.Ordinal);
            var smallest = moles.Values.Min();
            var ratios = moles.ToDictionary(e => e.Key, e => e.Value / smallest, StringComparer.Ordinal);
            var multiplier = Enumerable.Range(1, MaxMultiplier)
                .FirstOrDefault(k => ratios.Values.All(r => Math.Abs(r * k - Math.Round(r * k, MidpointRounding.AwayFromZero)) <= Tolerance));
            if (multiplier == 0) return null;
            var order = Hill(masses.Keys);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var symbol in order) counts[symbol] = (int)Math.Round(ratios[symbol] * multiplier, MidpointRounding.AwayFromZero);
            var rows = order.Select(s => new Row(s, masses[s], atomic[s], moles[s], ratios[s], counts[s])).ToList();
            if (MassOf(counts, s => atomic.TryGetValue(s, out var v) ? v : null) is not { } mass) return null;
            return new Result(counts, mass, rows, multiplier, null, 1, molarMass is { } m ? MolecularOf(counts, mass, m) : null);
        }

        /// <summary>A molecular formula's empirical formula: its subscripts over their greatest common divisor.</summary>
        public static Result? FromFormula(string formula, IReadOnlyDictionary<string, int> counts, Func<string, double?> atomicMassOf)
        {
            if (counts.Count == 0 || counts.Values.Any(n => n <= 0)) return null;
            var divisor = counts.Values.Aggregate(Gcd);
            var reduced = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (symbol, n) in counts) reduced[symbol] = n / divisor;
            if (MassOf(reduced, atomicMassOf) is not { } mass) return null;
            return new Result(reduced, mass, [], 1, formula, divisor, null);
        }

        /// <summary>The molecular formula with <paramref name="molarMass"/>, or null when it is not close to a whole multiple.</summary>
        public static Molecular? MolecularOf(IReadOnlyDictionary<string, int> counts, double empiricalMass, double molarMass)
        {
            if (molarMass <= 0 || empiricalMass <= 0) return null;
            var factor = (int)Math.Round(molarMass / empiricalMass, MidpointRounding.AwayFromZero);
            if (factor < 1 || Math.Abs(molarMass / (empiricalMass * factor) - 1) > MolarMassTolerance) return null;
            var multiplied = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (symbol, n) in counts) multiplied[symbol] = n * factor;
            return new Molecular(factor, multiplied, molarMass);
        }

        /// <summary>"CH2O": each symbol with its count, a count of one left out.</summary>
        public static string Format(IReadOnlyDictionary<string, int> counts) =>
            string.Concat(counts.Select(e => e.Value == 1 ? e.Key : e.Key + e.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        /// <summary>Carbon first, then hydrogen, then the rest alphabetically; with no carbon, all alphabetically.</summary>
        public static List<string> Hill(IEnumerable<string> symbols)
        {
            var all = symbols.ToList();
            if (!all.Contains("C")) return all.OrderBy(s => s, StringComparer.Ordinal).ToList();
            var order = new List<string> { "C" };
            if (all.Contains("H")) order.Add("H");
            order.AddRange(all.Where(s => s != "C" && s != "H").OrderBy(s => s, StringComparer.Ordinal));
            return order;
        }

        private static double? MassOf(IReadOnlyDictionary<string, int> counts, Func<string, double?> atomicMassOf)
        {
            var total = 0.0;
            foreach (var (symbol, n) in counts)
            {
                if (atomicMassOf(symbol) is not { } am) return null;
                total += am * n;
            }
            return total;
        }

        private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
    }

    /// <summary>
    /// Gram-to-gram stoichiometry and the limiting reagent (roadmap 2.8), over a balanced equation.
    /// Every given amount becomes moles; divided by its coefficient it says how far the reaction could
    /// run on that amount alone. The smallest of those over the given reactants is how far it does
    /// run, and that reactant is the limiting one. Mirrors Android's <c>Stoichiometry</c>.
    /// </summary>
    public static class Stoichiometry
    {
        /// <summary>One species of the balanced equation.</summary>
        public sealed record Line(string Formula, int Coefficient, double MolarMass, bool Reactant);

        /// <summary>An amount the question gave, for the species at <paramref name="Index"/>; <paramref name="Grams"/> when it was given as a mass.</summary>
        public sealed record Given(int Index, double Moles, double? Grams);

        /// <summary>An amount of the species at <paramref name="Index"/>.</summary>
        public sealed record Amount(int Index, double Moles, double Grams);

        /// <param name="Extent">How many times the balanced equation runs, in moles.</param>
        /// <param name="Limiting">The reactant that runs out first, when two or more reactants were given.</param>
        /// <param name="Amounts">Every species' amount at that extent, in equation order.</param>
        /// <param name="Leftovers">What remains of each other given reactant.</param>
        public sealed record Result(
            IReadOnlyList<Line> Lines,
            IReadOnlyList<Given> Givens,
            double Extent,
            int? Limiting,
            IReadOnlyList<Amount> Amounts,
            IReadOnlyList<Amount> Leftovers)
        {
            /// <summary>"2H2 + O2 → 2H2O".</summary>
            public string Equation
            {
                get
                {
                    string Side(bool reactant) => string.Join(" + ", Lines.Where(l => l.Reactant == reactant)
                        .Select(l => (l.Coefficient == 1 ? "" : l.Coefficient.ToString(System.Globalization.CultureInfo.InvariantCulture)) + l.Formula));
                    return Side(true) + " → " + Side(false);
                }
            }
        }

        /// <summary>Below this a leftover is rounding, not excess.</summary>
        private const double Negligible = 1e-9;

        public static Result? Solve(IReadOnlyList<Line> lines, IReadOnlyList<Given> givens)
        {
            if (lines.Count == 0 || givens.Count == 0) return null;
            if (givens.Any(g => g.Index < 0 || g.Index >= lines.Count || g.Moles <= 0 || double.IsNaN(g.Moles) || double.IsInfinity(g.Moles))) return null;
            if (givens.Select(g => g.Index).Distinct().Count() != givens.Count) return null;
            var reactants = givens.Where(g => lines[g.Index].Reactant).ToList();
            // A product given alone fixes the extent; reactants given alongside one decide it instead.
            var deciding = reactants.Count > 0 ? reactants : givens.ToList();
            var first = deciding.MinBy(g => g.Moles / lines[g.Index].Coefficient)!;
            var extent = first.Moles / lines[first.Index].Coefficient;
            int? limiting = reactants.Count >= 2 ? first.Index : null;
            var amounts = lines.Select((line, i) => new Amount(i, extent * line.Coefficient, extent * line.Coefficient * line.MolarMass)).ToList();
            var leftovers = new List<Amount>();
            foreach (var given in reactants.Where(g => g.Index != first.Index))
            {
                var left = given.Moles - extent * lines[given.Index].Coefficient;
                if (left > Negligible) leftovers.Add(new Amount(given.Index, left, left * lines[given.Index].MolarMass));
            }
            return new Result(lines, givens, extent, limiting, amounts, leftovers);
        }
    }
}
