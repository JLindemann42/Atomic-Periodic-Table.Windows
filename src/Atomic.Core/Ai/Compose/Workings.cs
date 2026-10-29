using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai.Compose
{
    /// <summary>
    /// A calculation's working, step by step (roadmap 2.2). Mirrors Android's <c>Workings</c>.
    ///
    /// <para>Every answer already shows a short working - the atom tally, n = c·V, the number of
    /// half-lives - because a number a reader cannot reproduce has to be taken on faith. Asked for the
    /// steps, it shows the whole derivation instead: the equations a balance solves, each conversion
    /// with its units, each formula with its numbers put in. The lines are mostly mathematics, which
    /// reads the same in every language; the words around them are strings.</para>
    ///
    /// <para>Each method returns the steps in order, without numbers, or an empty list when there is
    /// nothing to derive - the caller then keeps the short working.</para>
    /// </summary>
    public sealed class Workings
    {
        private const string Letters = "abcdefghijklmnopqrstuvwxyz";

        private readonly IStringProvider _strings;

        public Workings(IStringProvider strings) => _strings = strings;

        /// <summary>PV = nRT rearranged for the unknown, with n = m ÷ M first when a mass was given (roadmap 2.8).</summary>
        public IReadOnlyList<string> GasLaw(ExecutionResult.GasLaw result)
        {
            var s = result.Solution;
            var r = $"{Number(Data.GasLaw.R)} L·atm/(mol·K)";
            var steps = new List<string> { _strings.Get(S.ai_steps_gas) };
            if (result.Grams is { } grams && result.MolarMass is { } molarMass && result.Substance is not null)
                steps.Add($"n = m ÷ M = {Number(grams)} g ÷ {Number(molarMass)} g/mol = {Number(s.Moles)} mol");
            var p = $"{Number(s.Atm)} atm";
            var v = $"{Number(s.Litres)} L";
            var n = $"{Number(s.Moles)} mol";
            var t = $"{Number(s.Kelvin)} K";
            steps.Add(s.Unknown switch
            {
                Data.GasLaw.Unknown.Pressure => $"P = nRT ÷ V = {n} × {r} × {t} ÷ {v} = {p}",
                Data.GasLaw.Unknown.Volume => $"V = nRT ÷ P = {n} × {r} × {t} ÷ {p} = {v}",
                Data.GasLaw.Unknown.Moles => $"n = PV ÷ RT = {p} × {v} ÷ ({r} × {t}) = {n}",
                _ => $"T = PV ÷ nR = {p} × {v} ÷ ({n} × {r}) = {t}",
            });
            return steps;
        }

        /// <summary>Full dissociation, then the negative logarithm (roadmap 2.8).</summary>
        public IReadOnlyList<string> Ph(ExecutionResult.Ph result)
        {
            var r = result.Result;
            var supplied = $"{r.Factor} × {Number(r.Molarity)} M = {Number(r.Molarity * r.Factor)} M";
            return r.Base
                ?
                [
                    _strings.Get(S.ai_steps_ph),
                    $"[OH⁻] = {supplied}",
                    $"pOH = −log₁₀({Number(r.Hydroxide)}) = {Number(r.POH)}",
                    $"pH = 14 − pOH = {Number(r.PH)}",
                ]
                :
                [
                    _strings.Get(S.ai_steps_ph),
                    $"[H⁺] = {supplied}",
                    $"pH = −log₁₀({Number(r.Hydrogen)}) = {Number(r.PH)}",
                ];
        }

        /// <summary>From the neutral atom to the ion, electron by electron (roadmap 2.8).</summary>
        public IReadOnlyList<string> Ion(ExecutionResult.IonConfiguration result)
        {
            var label = Data.IonConfig.IonLabel(result.Element.Symbol, result.Charge);
            var change = result.Charge > 0 ? $"−{result.Charge} e⁻" : $"+{-result.Charge} e⁻";
            return
            [
                _strings.Get(S.ai_steps_ion),
                $"{result.Element.Symbol}: {result.Neutral}",
                $"{label}: {change} → {result.Ion}",
            ];
        }

        /// <summary>M = Σ count × atomic mass.</summary>
        public IReadOnlyList<string> Formula(FormulaResult result)
        {
            if (result.Parts.Count == 0) return [];
            var steps = new List<string> { _strings.Get(S.ai_steps_formula) };
            steps.AddRange(result.Parts.Select(part =>
                $"{part.Symbol}: {part.Count} × {Number(part.MassContribution / part.Count)} g/mol = {Number(part.MassContribution)} g/mol"));
            var sum = string.Join(" + ", result.Parts.Select(p => Number(p.MassContribution)));
            steps.Add($"M({result.Formula}) = {sum} = {Number(result.MolarMass)} g/mol");
            return steps;
        }

        /// <summary>n = m ÷ M, N = n × N_A, or n = N ÷ N_A.</summary>
        public IReadOnlyList<string> Moles(ExecutionResult.MoleConversion result)
        {
            if (result.Moles is not { } moles) return [];
            var legend = _strings.Get(S.ai_steps_moles_legend);
            var substance = result.Substance is { } s ? $"({s})" : string.Empty;
            if (result.Grams is { } grams)
            {
                if (moles == 0.0) return [];
                var molarMass = grams / moles;
                return
                [
                    legend,
                    $"M{substance} = {Number(molarMass)} g/mol",
                    $"n = m ÷ M = {Number(grams)} g ÷ {Number(molarMass)} g/mol = {Number(moles)} mol",
                ];
            }
            var avogadro = $"{Number(ChemistryMath.Avogadro)} /mol";
            return result.ToParticles
                ? [legend, $"N = n × N_A = {Number(moles)} mol × {avogadro} = {Number(result.Particles)}"]
                : [legend, $"n = N ÷ N_A = {Number(result.Particles)} ÷ {avogadro} = {Number(moles)} mol"];
        }

        /// <summary>The volume in litres, then n = c × V, m = n × M, c = n ÷ V or V = n ÷ c; or c₁V₁ = c₂V₂.</summary>
        public IReadOnlyList<string> Solution(ExecutionResult.SolutionCalc result)
        {
            var legend = _strings.Get(S.ai_steps_solution_legend);
            if (result.Dilution is { } d)
            {
                var added = SolutionMath.SolventToAdd(d.V1, d.V2);
                return
                [
                    legend,
                    "c₁ × V₁ = c₂ × V₂",
                    $"{Number(d.C1)} mol/L × {Number(d.V1 * 1000)} mL = {Number(d.C2)} mol/L × {Number(d.V2 * 1000)} mL",
                    $"V₂ − V₁ = {Number(d.V2 * 1000)} mL − {Number(d.V1 * 1000)} mL = {Number(added * 1000)} mL",
                ];
            }
            if (result.Moles is not { } moles || result.Molarity is not { } molarity || result.Litres is not { } litres)
                return [];
            var molarMass = result.MolarMass;
            var steps = new List<string> { legend };
            var volume = $"V = {Number(litres * 1000)} mL = {Number(litres)} L";
            // n from a mass, where the question gave one and the unknown is the concentration or volume.
            var molesFromMass = result.Grams is { } grams && molarMass is { } mass
                ? $"n = m ÷ M = {Number(grams)} g ÷ {Number(mass)} g/mol = {Number(moles)} mol"
                : null;
            switch (result.Kind)
            {
                case ExecutionResult.SolutionKind.Molarity:
                    if (molesFromMass is not null) { steps.Add($"M = {Number(molarMass!.Value)} g/mol"); steps.Add(molesFromMass); }
                    steps.Add(volume);
                    steps.Add($"c = n ÷ V = {Number(moles)} mol ÷ {Number(litres)} L = {Number(molarity)} mol/L");
                    break;
                case ExecutionResult.SolutionKind.Volume:
                    if (molesFromMass is not null) { steps.Add($"M = {Number(molarMass!.Value)} g/mol"); steps.Add(molesFromMass); }
                    steps.Add($"V = n ÷ c = {Number(moles)} mol ÷ {Number(molarity)} mol/L = {Number(litres)} L = {Number(litres * 1000)} mL");
                    break;
                default:
                    steps.Add(volume);
                    steps.Add($"n = c × V = {Number(molarity)} mol/L × {Number(litres)} L = {Number(moles)} mol");
                    if (result.Kind == ExecutionResult.SolutionKind.MassNeeded && molarMass is { } m)
                    {
                        steps.Add($"M = {Number(m)} g/mol");
                        steps.Add($"m = n × M = {Number(moles)} mol × {Number(m)} g/mol = {Number(moles * m)} g");
                    }
                    break;
            }
            return steps;
        }

        /// <summary>n = t ÷ t½, N ÷ N₀ = (½)ⁿ, N = N₀ × (½)ⁿ; or the same read backwards for the time.</summary>
        public IReadOnlyList<string> Decay(ExecutionResult.Decay result)
        {
            if (result.HalfLifeSeconds is not { } halfLifeSeconds || result.HalfLivesElapsed is not { } n ||
                result.FractionRemaining is not { } fraction) return [];
            var unit = result.ElapsedUnit ?? "s";
            if (UnitConverter.Convert(halfLifeSeconds, "s", unit) is not { } halfLife) return [];
            double? elapsed = result.ElapsedSeconds is { } seconds ? UnitConverter.Convert(seconds, "s", unit) : null;
            var amount = result.AmountUnit is { } a ? $" {a}" : string.Empty;
            var steps = new List<string> { _strings.Get(S.ai_steps_decay_legend), $"t½ = {Number(halfLife)} {unit}" };
            switch (result.Mode)
            {
                case ExecutionResult.DecayMode.Elapsed:
                    if (result.InitialAmount is not { } initial || result.FinalAmount is not { } final) return [];
                    steps.Add($"N ÷ N₀ = {Number(final)}{amount} ÷ {Number(initial)}{amount} = {Number(fraction)}");
                    steps.Add($"n = log₂(N₀ ÷ N) = {Number(n)}");
                    steps.Add($"t = n × t½ = {Number(n)} × {Number(halfLife)} {unit} = {Number(n * halfLife)} {unit}");
                    break;
                case ExecutionResult.DecayMode.Remaining:
                    if (result.InitialAmount is not { } start || elapsed is not { } t) return [];
                    steps.Add($"n = t ÷ t½ = {Number(t)} {unit} ÷ {Number(halfLife)} {unit} = {Number(n)}");
                    steps.Add($"N ÷ N₀ = (½)ⁿ = {Number(fraction)}");
                    steps.Add($"N = N₀ × (½)ⁿ = {Number(start)}{amount} × {Number(fraction)} = {Number(start * fraction)}{amount}");
                    break;
                default:
                    if (elapsed is not { } time) return [];
                    steps.Add($"n = t ÷ t½ = {Number(time)} {unit} ÷ {Number(halfLife)} {unit} = {Number(n)}");
                    steps.Add($"N ÷ N₀ = (½)ⁿ = {Number(fraction)} = {Number(fraction * 100)} %");
                    break;
            }
            return steps;
        }

        /// <summary>
        /// One unknown coefficient per formula, one equation per element, the smallest whole numbers
        /// that solve them, and the tally that checks them.
        /// </summary>
        public IReadOnlyList<string> Balance(ExecutionResult.BalancedEquation result)
        {
            if (result.Reason is not null) return [];
            var symbols = result.Tally.Select(t => t.Symbol).ToHashSet(StringComparer.Ordinal);
            var terms = result.Reactants.Concat(result.Products).ToList();
            if (terms.Count > Letters.Length) return [];
            var counts = new List<IReadOnlyDictionary<string, int>>();
            foreach (var term in terms)
            {
                if (ChemistryMath.ElementCounts(term.Formula, symbols.Contains) is not { } c) return [];
                counts.Add(c);
            }
            var letters = terms.Select((_, i) => Letters[i].ToString()).ToList();
            var reactants = Enumerable.Range(0, result.Reactants.Count).ToList();
            var products = Enumerable.Range(result.Reactants.Count, result.Products.Count).ToList();

            string Side(IEnumerable<int> range) => string.Join(" + ", range.Select(i => $"{letters[i]} {terms[i].Formula}"));
            string Atoms(string symbol, IEnumerable<int> range) => string.Join(" + ", range
                .Where(i => counts[i].TryGetValue(symbol, out var count) && count > 0)
                .Select(i => counts[i][symbol] == 1 ? letters[i] : $"{counts[i][symbol]}{letters[i]}"));

            var steps = new List<string>
            {
                _strings.Get(S.ai_steps_balance_unknowns),
                $"{Side(reactants)} → {Side(products)}",
                _strings.Get(S.ai_steps_balance_equations),
            };
            steps.AddRange(result.Tally.Select(t => $"{t.Symbol}: {Atoms(t.Symbol, reactants)} = {Atoms(t.Symbol, products)}"));
            steps.Add(_strings.Get(S.ai_steps_balance_solve));
            steps.Add(string.Join(", ", terms.Select((t, i) => $"{letters[i]} = {t.Coefficient}")));
            steps.Add(_strings.Get(S.ai_equation_check_header));
            steps.AddRange(result.Tally.Select(t => _strings.Get(S.ai_equation_tally_row, t.Symbol, t.Left, t.Right)));
            return steps;
        }

        /// <summary>
        /// Four significant figures, and powers of ten outside 0.001 to a million: the steps are where
        /// a reader checks the arithmetic, and the two decimals the answers round to would show a
        /// remaining fraction of 0.0039 as 0. Rounds half up, as Android's BigDecimal does.
        /// </summary>
        private static string Number(double value)
        {
            if (value == 0.0 || !double.IsFinite(value)) return UnitConverter.FormatValue(value);
            var magnitude = Math.Abs(value);
            var exponent = (int)Math.Floor(Math.Log10(magnitude));
            if (magnitude >= 1e6 || magnitude < 1e-3)
            {
                var mantissa = Math.Round(value / Math.Pow(10, exponent), 3, MidpointRounding.AwayFromZero);
                return $"{mantissa.ToString("0.###", CultureInfo.InvariantCulture)} × 10{Superscript(exponent)}";
            }
            var scale = Math.Pow(10, exponent - 3);
            var rounded = Math.Round(value / scale, MidpointRounding.AwayFromZero) * scale;
            return rounded.ToString("0.############", CultureInfo.InvariantCulture);
        }

        private static string Superscript(int n)
        {
            const string digits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
            return (n < 0 ? "⁻" : string.Empty) + string.Concat(Math.Abs(n).ToString(CultureInfo.InvariantCulture).Select(c => digits[c - '0']));
        }
    }
}
