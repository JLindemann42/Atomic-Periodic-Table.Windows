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
}
