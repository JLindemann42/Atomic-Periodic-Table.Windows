using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// Ported from <c>CalculatorMathTest.kt</c>: the ideal gas law, pH, ion configurations, empirical
    /// formulas and stoichiometry (roadmap 2.8).
    /// </summary>
    public class CalculatorMathTest
    {
        [Fact]
        public void OneMoleAtStpIsTwentyTwoPointFourLitres()
        {
            var s = GasLaw.Solve(1.0, null, 1.0, 273.15)!;
            Assert.Equal(GasLaw.Unknown.Volume, s.Unknown);
            Assert.Equal(22.414, s.Litres, 0.001);
            Assert.Equal(1.0, GasLaw.Solve(null, 22.414, 1.0, 273.15)!.Atm, 0.001);
            Assert.Equal(273.15, GasLaw.Solve(1.0, 22.414, 1.0, null)!.Kelvin, 0.01);
            Assert.Equal(2.0, GasLaw.Solve(1.0, 44.828, null, 273.15)!.Moles, 0.001);
        }

        [Fact]
        public void TheGasLawNeedsExactlyThreePositiveValues()
        {
            Assert.Null(GasLaw.Solve(1.0, 2.0, null, null));
            Assert.Null(GasLaw.Solve(1.0, 2.0, 3.0, 4.0));
            Assert.Null(GasLaw.Solve(1.0, -2.0, 3.0, null));
        }

        [Fact]
        public void StrongAcidsAndBases()
        {
            Assert.Equal(2.0, AcidBase.Solve("HCl", false, 0.01)!.PH, 1e-6);
            Assert.Equal(12.0, AcidBase.Solve("NaOH", true, 0.01)!.PH, 1e-6);
            // Two protons and two hydroxides.
            Assert.Equal(1.0, AcidBase.Solve("H2SO4", false, 0.05)!.PH, 1e-6);
            Assert.Equal(13.0, AcidBase.Solve("Ca(OH)2", true, 0.05)!.PH, 1e-6);
            // Water's own ions keep a very dilute acid below 7.
            Assert.Equal(6.98, AcidBase.Solve("HCl", false, 1e-8)!.PH, 0.005);
            Assert.Null(AcidBase.Solve("CH3COOH", false, 0.1));
        }

        [Fact]
        public void AStrongAcidIsFoundByItsComposition()
        {
            static IReadOnlyDictionary<string, int>? Composition(string f) =>
                ChemistryMath.ParseFormula(f, _ => 1.0)?.Parts.ToDictionary(p => p.Symbol, p => p.Count);
            Assert.Equal(("HNO3", false), AcidBase.Strong("HNO3", Composition));
            Assert.Equal(("Ba(OH)2", true), AcidBase.Strong("BaO2H2", Composition));
            Assert.Null(AcidBase.Strong("NaCl", Composition));
        }

        [Fact]
        public void IonConfigurations()
        {
            Assert.Equal("[Ar] 3d⁵", IonConfig.Ion(26, "3d^6 4s^2", 3)!.ToString());
            Assert.Equal("[Ar] 3d⁶", IonConfig.Ion(26, "3d^6 4s^2", 2)!.ToString());
            Assert.Equal("[Ar] 3d¹⁰", IonConfig.Ion(29, "3d^10 4s^1", 1)!.ToString());
            Assert.Equal("[Ne]", IonConfig.Ion(11, "3s^1", 1)!.ToString());
            Assert.Equal("Ne", IonConfig.Ion(11, "3s^1", 1)!.IsoelectronicWith);
            Assert.Equal("[Ar]", IonConfig.Ion(17, "3s^2 3p^5", -1)!.ToString());
            Assert.Equal("[Ne]", IonConfig.Ion(8, "2s^2 2p^4", -2)!.ToString());
            Assert.Equal("[He]", IonConfig.Ion(1, "1s^1", -1)!.ToString());
            // The inert pair: lead keeps its 6s².
            Assert.Equal("[Xe] 4f¹⁴ 5d¹⁰ 6s²", IonConfig.Ion(82, "4f^14, 5d^10, 6s^2, 6p^2", 2)!.ToString());
            // Sodium cannot lose a second electron without breaking into the neon core.
            Assert.Null(IonConfig.Ion(11, "3s^1", 2));
            Assert.Equal("Fe³⁺", IonConfig.IonLabel("Fe", 3));
            Assert.Equal("Cl⁻", IonConfig.IonLabel("Cl", -1));
        }

        private static readonly Dictionary<string, double> Masses = new()
        {
            ["H"] = 1.00784, ["C"] = 12.011, ["O"] = 15.999, ["Fe"] = 55.845, ["N"] = 14.007,
        };

        private static double? Mass(string symbol) => Masses.TryGetValue(symbol, out var m) ? m : null;

        [Fact]
        public void EmpiricalFormulasFromPercentages()
        {
            Assert.Equal("CH2O", Empirical.FromMasses(new Dictionary<string, double> { ["C"] = 40, ["H"] = 6.7, ["O"] = 53.3 }, Mass)!.Formula);
            // A ratio of 1.33 is cleared by three.
            var magnetite = Empirical.FromMasses(new Dictionary<string, double> { ["Fe"] = 72.4, ["O"] = 27.6 }, Mass)!;
            Assert.Equal("Fe3O4", magnetite.Formula);
            Assert.Equal(3, magnetite.Multiplier);
            // Hill order: carbon, hydrogen, then the rest alphabetically, whatever order they were given in.
            Assert.Equal("CH2O", Empirical.FromMasses(new Dictionary<string, double> { ["O"] = 53.3, ["H"] = 6.7, ["C"] = 40 }, Mass)!.Formula);
            Assert.Null(Empirical.FromMasses(new Dictionary<string, double> { ["C"] = 40 }, Mass));
        }

        [Fact]
        public void EmpiricalAndMolecularFormulas()
        {
            var glucose = Empirical.FromFormula("C6H12O6", new Dictionary<string, int> { ["C"] = 6, ["H"] = 12, ["O"] = 6 }, Mass)!;
            Assert.Equal("CH2O", glucose.Formula);
            Assert.Equal(6, glucose.Divisor);
            var butene = Empirical.FromMasses(new Dictionary<string, double> { ["C"] = 85.6, ["H"] = 14.4 }, Mass, molarMass: 56.0)!;
            Assert.Equal("CH2", butene.Formula);
            Assert.Equal("C4H8", butene.MolecularFormula!.Formula);
            // Not near a whole multiple of 30.03.
            Assert.Null(Empirical.MolecularOf(new Dictionary<string, int> { ["C"] = 1, ["H"] = 2, ["O"] = 1 }, 30.026, 45.0));
        }

        private static Stoichiometry.Line Line(string formula, int coefficient, bool reactant) =>
            new(formula, coefficient, ChemistryMath.ParseFormula(formula, Mass)!.MolarMass, reactant);

        private static readonly Stoichiometry.Line[] Water = [Line("H2", 2, true), Line("O2", 1, true), Line("H2O", 2, false)];

        [Fact]
        public void GramsThroughAnEquation()
        {
            var r = Stoichiometry.Solve(Water, [new Stoichiometry.Given(0, 4 / 2.01568, 4.0)])!;
            Assert.Null(r.Limiting);
            Assert.Equal(35.749, r.Amounts[2].Grams, 0.01);
            Assert.Equal(31.75, r.Amounts[1].Grams, 0.01);
            Assert.Equal("2H2 + O2 → 2H2O", r.Equation);
            // From a product back to a reactant.
            var back = Stoichiometry.Solve(Water, [new Stoichiometry.Given(2, 2.0, null)])!;
            Assert.Equal(2.0, back.Amounts[0].Moles, 1e-9);
        }

        [Fact]
        public void TheLimitingReagentRunsOutFirst()
        {
            var r = Stoichiometry.Solve(Water, [new Stoichiometry.Given(0, 4 / 2.01568, 4.0), new Stoichiometry.Given(1, 40 / 31.998, 40.0)])!;
            Assert.Equal(0, r.Limiting);
            Assert.Equal(35.749, r.Amounts[2].Grams, 0.01);
            Assert.Single(r.Leftovers);
            Assert.Equal(8.25, r.Leftovers[0].Grams, 0.01);
            Assert.Null(Stoichiometry.Solve(Water, []));
        }

        [Fact]
        public void AnEquationIsCutOutOfItsSentence()
        {
            var symbols = new HashSet<string> { "H", "O", "C", "N", "In", "I" };
            bool Known(string s) => symbols.Contains(s);
            Assert.Equal("2H2 + O2 -> 2H2O", EquationBalancer.SpanOf("how many grams of water from 4 g of H2 in 2H2 + O2 -> 2H2O", Known)!.Equation);
            Assert.Equal("2H2 + O2 -> 2H2O", EquationBalancer.SpanOf("2H2 + O2 -> 2H2O with 4 g H2 and 40 g O2", Known)!.Equation);
            Assert.Equal("2H2 + O2 -> 2H2O", EquationBalancer.SpanOf("2 H2 + O2 = 2 H2O", Known)!.Equation);
            Assert.Equal("H2 + O2 -> H2O", EquationBalancer.SpanOf("H2+O2->H2O", Known)!.Equation);
            Assert.Null(EquationBalancer.SpanOf("the arrow -> points nowhere", Known));
        }
    }
}
