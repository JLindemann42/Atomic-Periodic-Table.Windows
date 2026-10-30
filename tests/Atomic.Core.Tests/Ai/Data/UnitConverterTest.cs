using Atomic.Core.Ai.Data;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>Ported from <c>UnitConverterTest.kt</c>.</summary>
    public class UnitConverterTest
    {
        [Theory]
        [InlineData("(g/cm^3)", "g/cm³")]
        [InlineData("g/cm3", "g/cm³")]
        [InlineData("G/CM3", "g/cm³")]
        [InlineData("Kelvin", "K")]
        [InlineData("celsius", "°C")]
        [InlineData("FAHRENHEIT", "°F")]
        [InlineData("GPa", "GPa")]
        [InlineData("gpa", "GPa")]
        [InlineData("ppm", "mg/kg")]
        [InlineData("angstrom", "Å")]
        public void AliasesFoldOntoOneSpelling(string raw, string expected) =>
            Assert.Equal(expected, UnitConverter.Canonical(raw));

        [Fact]
        public void CaseIsPreservedWhereItCarriesMeaning()
        {
            // M is molar, m is metre. Lowercasing before the alias lookup - which is right for
            // "Fahrenheit" and "GPa" - would make these the same unit.
            Assert.Equal("M", UnitConverter.Canonical("M"));
            Assert.Equal("m", UnitConverter.Canonical("m"));
            Assert.Equal(Dimension.Molarity, UnitConverter.DimensionOf("M"));
            Assert.Equal(Dimension.Length, UnitConverter.DimensionOf("m"));
        }

        [Fact]
        public void TheTwoMuCharactersAgree()
        {
            // The element data writes U+00B5 MICRO SIGN; NFKC folds it to U+03BC GREEK SMALL MU.
            // Both sides of every comparison run through Canonical, so these must be one unit.
            Assert.Equal(UnitConverter.Canonical("µs"), UnitConverter.Canonical("μs"));
            Assert.Equal(UnitConverter.Canonical("µΩm"), UnitConverter.Canonical("μΩm"));
        }

        [Fact]
        public void TemperatureIsAffineNotMultiplicative()
        {
            Assert.Equal(273.15, UnitConverter.Convert(0, "°C", "K")!.Value, 1e-9);
            Assert.Equal(32.0, UnitConverter.Convert(0, "°C", "°F")!.Value, 1e-9);
            Assert.Equal(-40.0, UnitConverter.Convert(-40, "°F", "°C")!.Value, 1e-9);
            // Gold melts at 1337.33 K.
            Assert.Equal(1064.18, UnitConverter.Convert(1337.33, "K", "°C")!.Value, 1e-6);
        }

        [Theory]
        [InlineData(1.0, "g/cm³", "kg/m³", 1000.0)]
        [InlineData(1.0, "GPa", "MPa", 1000.0)]
        [InlineData(100.0, "pm", "Å", 1.0)]
        [InlineData(1.0, "%", "mg/kg", 10000.0)]
        [InlineData(1.0, "h", "min", 60.0)]
        [InlineData(1.0, "d", "h", 24.0)]
        public void MultiplicativeUnitsConvertThroughTheirBase(
            double value, string from, string to, double expected) =>
            Assert.Equal(expected, UnitConverter.Convert(value, from, to)!.Value, 1e-6);

        [Fact]
        public void DifferentDimensionsDoNotConvert()
        {
            Assert.Null(UnitConverter.Convert(1, "K", "GPa"));
            Assert.Null(UnitConverter.Convert(1, "m", "s"));
            Assert.Null(UnitConverter.Convert(1, "mol", "L"));
        }

        [Fact]
        public void MolarityIsNotConcentration()
        {
            // mol/L and mg/kg are both "concentration" in loose speech. Sharing a dimension would
            // let a ranking mix a molarity with a crustal abundance and call it meaningful.
            Assert.Null(UnitConverter.Convert(1, "M", "mg/kg"));
            Assert.NotEqual(UnitConverter.DimensionOf("M"), UnitConverter.DimensionOf("mg/kg"));
        }

        [Fact]
        public void SeaWaterUnitIsRecognisedButNotConvertible()
        {
            // Turning µg/l into mg/kg needs sea water's density. Leaving it unconvertible makes an
            // "in ppm" request fall back to the authored figure rather than inventing a number.
            Assert.Equal("µg/l", UnitConverter.Canonical("ug/l"));
            Assert.Null(UnitConverter.DimensionOf("µg/l"));
            Assert.Null(UnitConverter.Convert(1, "µg/l", "mg/kg"));
            Assert.False(UnitConverter.Convertible("µg/l", "mg/kg"));
        }

        [Fact]
        public void EnergyBridgesOnlyWhenAskedOutright()
        {
            // One eV per particle is 96.485 kJ per mole. Convert refuses the crossing so a ranking
            // cannot silently mix an ionisation energy with a fusion enthalpy; the explicit
            // conversion intent goes through ConvertAcrossBridges.
            Assert.Null(UnitConverter.Convert(1, "eV", "kJ/mol"));
            Assert.Equal(96.48533212, UnitConverter.ConvertAcrossBridges(1, "eV", "kJ/mol")!.Value, 1e-6);
            Assert.True(UnitConverter.IsBridged("eV", "kJ/mol"));
            Assert.False(UnitConverter.IsBridged("GPa", "MPa"));

            // Through each base unit, so J/mol works as well as kJ/mol.
            Assert.Equal(1.0, UnitConverter.ConvertAcrossBridges(96485.33212, "J/mol", "eV")!.Value, 1e-4);
        }

        [Fact]
        public void KnownUnitRefusesWordsThatAreNotUnits()
        {
            // Canonical echoes anything it does not recognise, which is right for an authored
            // value but answers "yes" to everything when deciding whether a query word is a unit.
            Assert.Equal("K", UnitConverter.KnownUnit("kelvin"));
            Assert.Null(UnitConverter.KnownUnit("gold"));
            Assert.Null(UnitConverter.KnownUnit("heaviest"));
            Assert.Equal("gold", UnitConverter.Canonical("gold"));
        }

        [Fact]
        public void BareYearAndSpanishAndAreNotTimeUnits()
        {
            // Spanish "y" means "and": "compara uranio-235 y uranio-238" once read as "235 years"
            // and became a decay calculation.
            Assert.Null(UnitConverter.KnownUnit("y"));
            Assert.Null(UnitConverter.KnownUnit("an"));
            Assert.Equal("yr", UnitConverter.KnownUnit("years"));
            Assert.Equal("yr", UnitConverter.KnownUnit("år"));
            Assert.Equal("yr", UnitConverter.KnownUnit("años"));
        }

        [Fact]
        public void BareAIsAngstromNotYear()
        {
            // In this app's data "1.5 a" means 1.5 Å far more often than 1.5 years.
            Assert.Equal("Å", UnitConverter.Canonical("a"));
            Assert.Equal(Dimension.Length, UnitConverter.DimensionOf("a"));
        }

        [Fact]
        public void MillimetreIsNotMillimolar()
        {
            // Mapping "mm" to millimolar would make "0.5 mm" a concentration without warning.
            Assert.Null(UnitConverter.KnownUnit("mm"));
        }

        [Fact]
        public void ConvertingAQuantityCarriesTheRangeAndMarksTheUnitAuthored()
        {
            var q = new Quantity(160, 350, "MPa", "160-350 (MPa)");
            var converted = UnitConverter.Convert(q, "GPa")!;

            Assert.Equal(0.16, converted.Value, 1e-9);
            Assert.Equal(0.35, converted.High!.Value, 1e-9);
            Assert.Equal("GPa", converted.Unit);
            Assert.True(converted.IsRange);
            // The unit is written into Display here, so a renderer must not add it a second time.
            Assert.True(converted.UnitAuthored);
            Assert.Contains("GPa", converted.Display, System.StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(1000.0, "1000")]
        [InlineData(1000.004, "1000")]
        [InlineData(1064.185, "1064.19")]
        [InlineData(0.5, "0.5")]
        public void FormatValueTrimsTrailingNoise(double value, string expected) =>
            Assert.Equal(expected, UnitConverter.FormatValue(value));

        [Fact]
        public void FormatValueIsCultureInvariant()
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");
                // A comma decimal separator in a chemistry value is wrong, not merely surprising.
                Assert.Equal("1064.19", UnitConverter.FormatValue(1064.185));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }
    }
}
