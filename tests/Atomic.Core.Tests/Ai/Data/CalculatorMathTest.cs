using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>Ported from <c>CalculatorMathTest.kt</c>: the ideal gas law, pH, and ion configurations (roadmap 2.8).</summary>
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
    }
}
