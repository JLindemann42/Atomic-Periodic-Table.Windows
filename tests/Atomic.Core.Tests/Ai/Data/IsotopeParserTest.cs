using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// Ported from <c>IsotopeParserTest.kt</c>, plus a sweep over all 118 shipped elements.
    /// </summary>
    public class IsotopeParserTest
    {
        private const double Year = 3.15576e7;

        [Theory]
        [InlineData("5730 years", 5730 * Year)]
        [InlineData("12.32 Years", 12.32 * Year)]
        [InlineData("1.5 days", 1.5 * 86400)]
        [InlineData("8.02 d", 8.02 * 86400)]
        [InlineData("2.6 hours", 2.6 * 3600)]
        [InlineData("14.9 min", 14.9 * 60)]
        [InlineData("100 ms", 0.1)]
        [InlineData("2 seconds", 2.0)]
        public void CommonSpellingsNormaliseToSeconds(string raw, double expected) =>
            Assert.Equal(expected, IsotopeParser.HalfLifeSeconds(raw)!.Value, expected * 1e-9);

        [Fact]
        public void ABareMIsMinutesNotMetres()
        {
            // "8.718 (m)" and "4.12 m" both occur; reading m as metres would make the half-life
            // unparseable rather than wrong, which is why it was easy to miss.
            Assert.Equal(8.718 * 60, IsotopeParser.HalfLifeSeconds("8.718 (m)")!.Value, 1e-6);
            Assert.Equal(4.12 * 60, IsotopeParser.HalfLifeSeconds("4.12 m")!.Value, 1e-6);
        }

        [Fact]
        public void TheCorruptedMinutesSpellingIsRepaired()
        {
            // "18.3 minu-Tes" appears 19 times in the data.
            Assert.Equal(18.3 * 60, IsotopeParser.HalfLifeSeconds("18.3 minu-Tes")!.Value, 1e-6);
        }

        [Fact]
        public void HyphensAreNotStrippedGenerally()
        {
            // Repairing minu-Tes by removing hyphens would destroy the negative exponent here.
            Assert.Equal(5.7e-22, IsotopeParser.HalfLifeSeconds("570*10^-24 (s)")!.Value, 1e-30);
        }

        [Fact]
        public void ScientificNotationWithUnicodeSigns()
        {
            Assert.Equal(5.0e-21, IsotopeParser.HalfLifeSeconds("5.0×10^−21 (s)")!.Value, 1e-30);
            Assert.Equal(7.17e5 * Year, IsotopeParser.HalfLifeSeconds("7.17(24)×10^5 (Years)")!.Value,
                7.17e5 * Year * 1e-9);
        }

        [Fact]
        public void MillionAndBillionYearsAreOneToken()
        {
            Assert.Equal(4.5e9 * Year, IsotopeParser.HalfLifeSeconds("4.5 billion years")!.Value,
                4.5e9 * Year * 1e-9);
            Assert.Equal(1.28e6 * Year, IsotopeParser.HalfLifeSeconds("1.28 million y")!.Value,
                1.28e6 * Year * 1e-9);
        }

        [Theory]
        [InlineData("Stable")]
        [InlineData("stable")]
        [InlineData("Observationally Stable")]
        public void StableIsNotAHalfLife(string raw)
        {
            Assert.True(IsotopeParser.IsStable(raw));
            Assert.Null(IsotopeParser.HalfLifeSeconds(raw));
        }

        [Theory]
        [InlineData("")]
        [InlineData("---")]
        [InlineData("unknown")]
        [InlineData("???")]
        [InlineData("no idea at all")]
        public void UnparseableHalfLivesAreNullRatherThanZero(string raw) =>
            Assert.Null(IsotopeParser.HalfLifeSeconds(raw));

        [Fact]
        public void ApproximationMarkersAreIgnoredForTheMagnitude()
        {
            Assert.Equal(60.0, IsotopeParser.HalfLifeSeconds("~1 min")!.Value, 1e-9);
            Assert.Equal(60.0, IsotopeParser.HalfLifeSeconds("> 1 min")!.Value, 1e-9);
        }

        [Fact]
        public void TheYearAgreesWithTheUnitConverter()
        {
            // A decay answer that used a different year from the isotope table it cites would
            // contradict itself. Both are the Julian year, 365.25 days.
            Assert.Equal(
                UnitConverter.Convert(1, "yr", "s")!.Value,
                IsotopeParser.HalfLifeSeconds("1 year")!.Value,
                1e-6);
        }

        // ---- against the shipped data ---------------------------------------------------------

        [Fact]
        public void AllSlotsAreScannedNotJustUpToTheFirstGap()
        {
            // Bromine's block runs 1-5, skips 6, then continues to 34. Stopping at the first gap
            // dropped 29 of its isotopes, including the stable ones.
            var bromine = TestAssets.English["bromine"]!;
            var isotopes = IsotopeParser.Parse(bromine);

            Assert.True(isotopes.Count > 20,
                $"expected bromine's later slots to be read, got {isotopes.Count}");
            Assert.Contains(isotopes, i => i.Stable);
        }

        [Fact]
        public void EveryElementParsesWithoutThrowing()
        {
            var total = 0;
            foreach (var (key, row) in TestAssets.English.Rows)
            {
                var isotopes = IsotopeParser.Parse(row);
                total += isotopes.Count;
                Assert.All(isotopes, i => Assert.False(string.IsNullOrWhiteSpace(i.Name), key));
            }
            Assert.True(total > 3000, $"expected ~3100 isotopes across the table, got {total}");
        }

        [Fact]
        public void MostHalfLivesResolveToANumber()
        {
            // A regression in the unit table shows up here as a coverage drop rather than a crash.
            var decaying = new List<Isotope>();
            foreach (var row in TestAssets.English.Rows.Values)
                decaying.AddRange(IsotopeParser.Parse(row).Where(i => !i.Stable));

            var parsed = decaying.Count(i => i.HalfLifeSeconds is not null);
            var ratio = (double)parsed / decaying.Count;

            Assert.True(ratio > 0.95,
                $"only {parsed}/{decaying.Count} ({ratio:P1}) of decaying isotopes have a parsed half-life");
        }

        [Fact]
        public void CarbonFourteenLooksRight()
        {
            var c14 = IsotopeParser.Parse(TestAssets.English["carbon"]!)
                .FirstOrDefault(i => i.Nucleons == 14);

            Assert.NotNull(c14);
            Assert.False(c14!.Stable);
            // 5,700 years, give or take the source's rounding.
            Assert.InRange(c14.HalfLifeSeconds!.Value, 5000 * Year, 6500 * Year);
        }

        [Fact]
        public void StableIsotopesCarryNoHalfLife()
        {
            var iron = IsotopeParser.Parse(TestAssets.English["iron"]!);
            var stable = iron.Where(i => i.Stable).ToList();

            Assert.NotEmpty(stable);
            Assert.All(stable, i => Assert.Null(i.HalfLifeSeconds));
        }
    }
}
