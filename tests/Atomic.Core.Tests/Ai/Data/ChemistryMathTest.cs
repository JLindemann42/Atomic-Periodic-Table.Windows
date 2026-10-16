using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// Ported from <c>ChemistryMathTest.kt</c>. Atomic masses come from the shipped element table,
    /// so the numbers here are the ones the app would actually produce.
    /// </summary>
    public class ChemistryMathTest
    {
        private static readonly Dictionary<string, double> MassBySymbol = BuildMasses();
        private static readonly HashSet<string> Symbols = new(MassBySymbol.Keys, StringComparer.Ordinal);

        private static Dictionary<string, double> BuildMasses()
        {
            var spec = FieldRegistry.ById["atomic_mass"];
            var masses = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var row in TestAssets.English.Rows.Values)
            {
                if (!row.TryGetValue("short", out var s) || s is not string symbol) continue;
                if (!row.TryGetValue("element_atomicmass", out var raw)) continue;
                if (ValueParser.Parse(raw, spec) is FieldValue.Num num)
                    masses[symbol] = num.Quantity.Value;
            }
            return masses;
        }

        private static double? MassOf(string symbol) =>
            MassBySymbol.TryGetValue(symbol, out var m) ? m : null;

        private static bool Known(string symbol) => Symbols.Contains(symbol);

        [Fact]
        public void TheElementTableSuppliedEnoughMassesToTestWith() =>
            Assert.True(MassBySymbol.Count > 110, $"only {MassBySymbol.Count} masses parsed");

        // ---- formula parsing ---------------------------------------------------------------------

        [Fact]
        public void SulfuricAcid()
        {
            var r = ChemistryMath.ParseFormula("H2SO4", MassOf)!;
            Assert.Equal(98.07, r.MolarMass, 0.05);
            Assert.Equal(3, r.Parts.Count);
            Assert.Equal(4, r.Parts.Single(p => p.Symbol == "O").Count);
            // Oxygen is about 65% of the mass.
            Assert.Equal(65.2, r.Parts.Single(p => p.Symbol == "O").Percent, 0.5);
        }

        [Fact]
        public void NestedGroupsMultiplyThrough()
        {
            var counts = ChemistryMath.ElementCounts("Fe2(SO4)3", Known)!;
            Assert.Equal(2, counts["Fe"]);
            Assert.Equal(3, counts["S"]);
            Assert.Equal(12, counts["O"]);
        }

        [Fact]
        public void HydrateMultipliersApplyToTheirWholeSegment()
        {
            // DIVERGENCE from the Kotlin - see docs/porting-notes.md D9. Deleting the separator
            // before parsing fuses the sulfate's "O4" with the hydrate's "5" into a single "O45",
            // so this came back with 46 oxygens.
            var counts = ChemistryMath.ElementCounts("CuSO4·5H2O", Known)!;
            Assert.Equal(1, counts["Cu"]);
            Assert.Equal(1, counts["S"]);
            Assert.Equal(9, counts["O"]);   // 4 from the sulfate, 5 from the water
            Assert.Equal(10, counts["H"]);
        }

        [Fact]
        public void HydrateMolarMassIsRight()
        {
            // Copper(II) sulfate pentahydrate is 249.68 g/mol. The old parse gave about 1,420.
            var r = ChemistryMath.ParseFormula("CuSO4·5H2O", MassOf)!;
            Assert.Equal(249.68, r.MolarMass, 0.05);
        }

        [Fact]
        public void AsteriskIsTheSameSeparator()
        {
            // Plenty of sources write CuSO4*5H2O.
            var withDot = ChemistryMath.ElementCounts("CuSO4·5H2O", Known)!;
            var withStar = ChemistryMath.ElementCounts("CuSO4*5H2O", Known)!;
            Assert.Equal(withDot["O"], withStar["O"]);
        }

        [Fact]
        public void AHydrateWithNoMultiplierCountsOnce()
        {
            var counts = ChemistryMath.ElementCounts("Na2CO3·H2O", Known)!;
            Assert.Equal(4, counts["O"]);   // 3 from the carbonate, 1 from the water
            Assert.Equal(2, counts["H"]);
        }

        [Fact]
        public void RepeatedSymbolsAccumulate()
        {
            // CH3COOH names carbon twice and oxygen twice.
            var counts = ChemistryMath.ElementCounts("CH3COOH", Known)!;
            Assert.Equal(2, counts["C"]);
            Assert.Equal(4, counts["H"]);
            Assert.Equal(2, counts["O"]);
        }

        [Fact]
        public void LowerCaseFormulasRecover()
        {
            var r = ChemistryMath.ParseFormula("h2so4", MassOf)!;
            Assert.Equal(98.07, r.MolarMass, 0.05);
        }

        [Fact]
        public void OrdinaryWordsAreNotFormulas()
        {
            // Element symbols tile English alarmingly well. Without the digit gate, "percentage"
            // parses as P-Er-Ce-N-Ta-Ge and weighs 605.93 g/mol.
            Assert.Null(ChemistryMath.ParseFormula("percentage", MassOf));
            Assert.Null(ChemistryMath.ElementCounts("percentage", Known));
            Assert.Null(ChemistryMath.ParseFormula("balance", MassOf));
        }

        [Fact]
        public void CapitalisedWordsThatHappenToTileAreStillRejectedWhenTheyShould()
        {
            // "CoIn" is cobalt-iodine-nitrogen as written, and that is the correct reading of a
            // capitalised formula - the guard is only about the lower-case recovery path.
            Assert.NotNull(ChemistryMath.ElementCounts("CoIn", Known));
            Assert.Null(ChemistryMath.ElementCounts("coin", Known));
        }

        [Fact]
        public void UnbalancedParenthesesDoNotParse()
        {
            Assert.Null(ChemistryMath.ElementCounts("Fe2(SO4", Known));
            Assert.Null(ChemistryMath.ElementCounts("Fe2SO4)3", Known));
        }

        [Fact]
        public void UnknownSymbolsAreRefused() => Assert.Null(ChemistryMath.ElementCounts("Xx2O3", Known));

        [Fact]
        public void CountsDoNotNeedAtomicMasses()
        {
            // Routing counts through the mass path would make one element with no atomic mass
            // fatal to a perfectly balanceable equation.
            Assert.NotNull(ChemistryMath.ElementCounts("Og2O3", s => s is "Og" or "O"));
        }

        [Fact]
        public void StateAnnotationsComeOffButStructureStaysOn()
        {
            Assert.Equal("H2O", ChemistryMath.StripStateAnnotation("H2O(l)"));
            Assert.Equal("NaCl", ChemistryMath.StripStateAnnotation("NaCl (aq)"));
            Assert.Equal("O2", ChemistryMath.StripStateAnnotation("O2(g)"));
            // (OH) is structure, not state.
            Assert.Equal("Fe(OH)3", ChemistryMath.StripStateAnnotation("Fe(OH)3"));
        }

        // ---- the mole quantity pattern -------------------------------------------------------

        [Theory]
        [InlineData("2 moles of carbon", "2")]
        [InlineData("0.5 mol", "0.5")]
        [InlineData("3 moli", "3")]
        [InlineData("2 मोल", "2")]
        [InlineData("2 مول", "2")]
        [InlineData("2摩尔", "2")]
        public void MoleQuantitiesAreReadInEveryShippedLanguage(string text, string expected)
        {
            var m = ChemistryMath.MoleQuantity.Match(text);
            Assert.True(m.Success, text);
            Assert.Equal(expected, m.Groups[1].Value);
        }

        [Fact]
        public void MoleculeIsNotAMoleQuantity() =>
            Assert.False(ChemistryMath.MoleQuantity.IsMatch("2 molecules"));

        // ---- conversions ----------------------------------------------------------------------

        [Fact]
        public void MolesAndParticlesRoundTrip()
        {
            Assert.Equal(1.204428152e24, ChemistryMath.MolesToParticles(2), 1e15);
            Assert.Equal(2.0, ChemistryMath.ParticlesToMoles(1.204428152e24), 1e-9);
        }

        [Fact]
        public void MassToMolesNeedsAPositiveMolarMass()
        {
            Assert.Equal(0.5, ChemistryMath.MassToMoles(49.0, 98.0)!.Value, 1e-9);
            Assert.Null(ChemistryMath.MassToMoles(49.0, 0.0));
            Assert.Null(ChemistryMath.MassToMoles(49.0, -1.0));
        }

        [Fact]
        public void NeutronCountsCannotBeNegative()
        {
            Assert.Equal(8, ChemistryMath.NeutronsIn(14, 6));   // C-14
            Assert.Equal(0, ChemistryMath.NeutronsIn(1, 1));    // protium
            Assert.Null(ChemistryMath.NeutronsIn(1, 6));        // nonsense in, nothing out
        }

        [Fact]
        public void AverageAtomicMassNormalisesWhicheverWayAbundanceIsGiven()
        {
            // Chlorine: 75.77% Cl-35, 24.23% Cl-37.
            var asPercent = ChemistryMath.AverageAtomicMass([(34.969, 75.77), (36.966, 24.23)])!.Value;
            var asFraction = ChemistryMath.AverageAtomicMass([(34.969, 0.7577), (36.966, 0.2423)])!.Value;

            Assert.Equal(35.45, asPercent, 0.01);
            Assert.Equal(asPercent, asFraction, 1e-9);
        }

        [Fact]
        public void AverageAtomicMassRefusesNonsense()
        {
            Assert.Null(ChemistryMath.AverageAtomicMass([]));
            Assert.Null(ChemistryMath.AverageAtomicMass([(35.0, 0.0)]));
        }

        [Fact]
        public void FormulaParsingIsCultureInvariant()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal(98.07, ChemistryMath.ParseFormula("H2SO4", MassOf)!.MolarMass, 0.05);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
