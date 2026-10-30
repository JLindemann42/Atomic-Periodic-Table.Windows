using System.Collections.Generic;
using Atomic.Core.Ai.Data;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// Ported from <c>ValueParserTest.kt</c>. Every case is a value shape that actually occurs in
    /// the shipped element table; if a rule regresses, real elements start answering with wrong
    /// numbers or sentinel text.
    ///
    /// <para>The specs come from the real <see cref="FieldRegistry"/>, so these cases exercise the
    /// parser against the same metadata production uses - a field that loses its AllowsRange flag
    /// or its canonical unit fails here rather than in an answer.</para>
    /// </summary>
    public class ValueParserTest
    {
        private static FieldSpec Spec(string id) => FieldRegistry.ById[id];

        private static readonly FieldSpec Density = Spec("density");
        private static readonly FieldSpec MeltingPoint = Spec("melting_point");
        private static readonly FieldSpec Vickers = Spec("vickers_hardness");
        private static readonly FieldSpec Crust = Spec("abundance_earth_crust");
        private static readonly FieldSpec SoundSolid = Spec("speed_of_sound_solid");
        private static readonly FieldSpec Resistivity = Spec("resistivity");
        private static readonly FieldSpec FusionHeat = Spec("fusion_heat");
        private static readonly FieldSpec Lattice = Spec("lattice_constants");
        private static readonly FieldSpec Debye = Spec("debye_temperature");
        private static readonly FieldSpec NfpaHealth = Spec("nfpa_health");
        private static readonly FieldSpec Series = Spec("series");
        private static readonly FieldSpec YearDiscovered = Spec("year_discovered");

        private static Quantity Num(object? raw, FieldSpec spec)
        {
            var parsed = ValueParser.Parse(raw, spec);
            var num = Assert.IsType<FieldValue.Num>(parsed);
            return num.Quantity;
        }

        // ---- Rule 1: absent-value sentinels ------------------------------------------------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("---")]
        [InlineData("N/A")]
        [InlineData("?")]
        [InlineData("???")]
        [InlineData("unknown")]
        public void BareSentinelsAreMissing(string? raw) =>
            Assert.True(ValueParser.Parse(raw, Density).IsMissing);

        [Theory]
        // "--- (pm)" occurs 32 times; a plain == "---" check would leak it to the user.
        [InlineData("--- (pm)")]
        [InlineData("--- (K)")]
        [InlineData("---  (K)")]
        [InlineData("--- J/(g·K)")]
        [InlineData("--- (kJ/mol)")]
        [InlineData("--- (g/cm^3)")]
        public void SentinelsThatKeptTheirUnitAreStillMissing(string raw) =>
            Assert.True(ValueParser.Parse(raw, Density).IsMissing);

        // ---- Rule 2: typed inputs ----------------------------------------------------------

        [Fact]
        public void NfpaIntegersParseAsNumbers()
        {
            Assert.Equal(2.0, Num(2, NfpaHealth).Value);
            Assert.Equal(0.0, Num(0, NfpaHealth).Value);
        }

        [Fact]
        public void NestedLatticeConstantsBecomeStruct()
        {
            var parsed = ValueParser.Parse(
                new Dictionary<string, string> { ["a"] = "4.04958 Å" }, Lattice);
            var s = Assert.IsType<FieldValue.Struct>(parsed);
            Assert.Equal(4.04958, s.Parts["a"].Value, 1e-6);
        }

        [Fact]
        public void DebyeTemperatureStructKeepsBothLimits()
        {
            var parsed = ValueParser.Parse(new Dictionary<string, string>
            {
                ["low_temperature_limit"] = "428 K",
                ["room_temperature"] = "390 K",
            }, Debye);

            var s = Assert.IsType<FieldValue.Struct>(parsed);
            Assert.Equal(428.0, s.Parts["low_temperature_limit"].Value);
            Assert.Equal(390.0, s.Parts["room_temperature"].Value);
        }

        // ---- Rules 3-4: notes, allotropes, molecular forms ---------------------------------

        [Fact]
        public void BracketedQualifierBecomesNoteNotPartOfTheNumber()
        {
            var q = Num("5000 (m/s) [room temperature]", SoundSolid);
            Assert.Equal(5000.0, q.Value);
            Assert.Equal("room temperature", q.Note);
            Assert.Equal("m/s", q.Unit);
        }

        [Fact]
        public void AllotropePrefixBecomesNote()
        {
            var q = Num("Br2: 332.0 (K)", MeltingPoint);
            Assert.Equal(332.0, q.Value);
            Assert.Equal("Br2", q.Note);
        }

        [Fact]
        public void NegativeCelsiusIsNotMistakenForARange()
        {
            // melting_point does not allow ranges precisely so this stays negative.
            var q = Num("Br2: -7.2 (°C)", MeltingPoint);
            Assert.Equal(-7.2, q.Value, 1e-9);
            Assert.Null(q.High);
        }

        [Fact]
        public void MolecularFormAnnotationIsStripped() =>
            Assert.Equal(0.479, Num("0.479 (Cl2) (kJ/mol)", FusionHeat).Value, 1e-9);

        [Fact]
        public void MultilineAllotropeValuesTakeTheFirstAndNoteTheRest()
        {
            var q = Num("13750 nΩm [Graphite] \n 8 nΩm [Diamond]", Resistivity);
            Assert.Equal(13750.0, q.Value);
            Assert.NotNull(q.Note);
        }

        // ---- Rules 7-9: uncertainty, approximation, scientific notation --------------------

        [Fact]
        public void GluedUncertaintyIsDropped() =>
            Assert.Equal(7.17e5, Num("7.17(24)×10^5 (Years)", Crust).Value, 1.0);

        [Fact]
        public void ApproximationMarkersSetTheFlag()
        {
            Assert.True(Num("~4×10^-20 (s)", Crust).Approximate);
            Assert.True(Num("<0.5", Crust).Approximate);
            Assert.True(Num("> 1.2", Crust).Approximate);
            Assert.False(Num("2.5", Crust).Approximate);
        }

        [Fact]
        public void ScientificNotationWithUnicodeMultiplyAndMinus()
        {
            // U+00D7 multiplication sign and U+2212 minus sign both occur in the data.
            Assert.Equal(8.0e4, Num("8.0 × 10^4", Crust).Value, 1.0);
            Assert.Equal(5.0e-21, Num("5.0×10^−21 (s)", Crust).Value, 1e-24);
            Assert.Equal(5.7e-22, Num("570*10^-24 (s)", Crust).Value, 1e-25);
        }

        [Fact]
        public void BarePowerWithoutMantissa() =>
            Assert.Equal(1.0e-8, Num("10^-8", Crust).Value, 1e-12);

        // ---- Rule 10: ranges, only where the field allows them -----------------------------

        [Fact]
        public void HardnessRangeParsesBothEnds()
        {
            var q = Num("160-350 (MPa)", Vickers);
            Assert.Equal(160.0, q.Value);
            Assert.Equal(350.0, q.High);
            Assert.Equal(255.0, q.Mid);
            Assert.True(q.IsRange);
            Assert.Equal("MPa", q.Unit);
        }

        [Fact]
        public void RangeIsNotAppliedToFieldsThatDoNotAllowIt()
        {
            var q = Num("2743 (K)", MeltingPoint);
            Assert.False(q.IsRange);
            Assert.Equal(2743.0, q.Value);
        }

        // ---- Rules 11-12: plain numbers, separators, percent, text -------------------------

        [Fact]
        public void ThousandsSeparatorIsHandled() =>
            Assert.Equal(29600.0, Num("29,600", Crust).Value);

        [Fact]
        public void PercentBecomesItsOwnUnit()
        {
            var q = Num("0.00007%", Crust);
            Assert.Equal(0.00007, q.Value, 1e-12);
            Assert.Equal("%", q.Unit);
        }

        [Fact]
        public void TextWithNoNumberStaysText()
        {
            var parsed = ValueParser.Parse("Deep Antiquity", YearDiscovered);
            Assert.IsType<FieldValue.Text>(parsed);
        }

        [Fact]
        public void DisplayAlwaysEchoesTheAuthoredString()
        {
            // A verbatim lookup must be able to render the source without a formatter.
            Assert.Equal("160-350 (MPa)", Num("160-350 (MPa)", Vickers).Display);
            Assert.Equal("8.0 × 10^4", Num("8.0 × 10^4", Crust).Display);
        }

        [Fact]
        public void UnitAuthoredDistinguishesReadFromSupplied()
        {
            // "8400 mg/kg" already states its unit inside Display; "80500" does not, and a caller
            // has to add it. Guessing from the string is the bug this flag exists to prevent.
            Assert.True(Num("8400 mg/kg", Crust).UnitAuthored);
            Assert.False(Num("80500", Crust).UnitAuthored);
            Assert.Equal("mg/kg", Num("80500", Crust).Unit);
        }

        // ---- Enum handling -----------------------------------------------------------------

        [Fact]
        public void EnumValuesAreCanonicalisedAcrossSpellings()
        {
            // The data uses 15 spellings for 11 concepts; these three all mean the same series.
            foreach (var raw in new[] { "Post-Transition Metals", "Post-transition Metal", "post transition metals" })
            {
                var e = Assert.IsType<FieldValue.Enum>(ValueParser.Parse(raw, Series));
                Assert.Equal(nameof(SeriesId.PostTransitionMetal), e.Canonical);
                Assert.Equal(raw, e.Localized);
            }
        }

        [Fact]
        public void LanthanoidsAndLanthanidesCollapseTogether()
        {
            // The legacy agent used group.contains("Lanthanide"), which never matched "Lanthanoids".
            foreach (var raw in new[] { "Lanthanide", "Lanthanoid", "Lanthanoids" })
            {
                var e = Assert.IsType<FieldValue.Enum>(ValueParser.Parse(raw, Series));
                Assert.Equal(nameof(SeriesId.Lanthanoid), e.Canonical);
            }
        }

        [Fact]
        public void SodiumIsNotMistakenForAnAbsentValue()
        {
            // "na" is deliberately not a null token: it is sodium's symbol, and treating it as one
            // erased sodium from every symbol lookup.
            Assert.False(ValueParser.IsNullToken("Na"));
            Assert.False(ValueParser.IsNullToken("na"));
            Assert.True(ValueParser.IsNullToken("n/a"));
        }
    }
}
