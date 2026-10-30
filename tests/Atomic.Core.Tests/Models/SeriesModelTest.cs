using System.Linq;
using Atomic.Core.Models;
using Xunit;

namespace Atomic.Core.Tests.Models
{
    /// <summary>
    /// The electrochemical series (roadmap 0.8, D58). Android's <c>SeriesModelTest</c> asserts the
    /// same things against the same 33 couples.
    /// </summary>
    public class SeriesModelTest
    {
        [Fact]
        public void ThereAre33Couples() => Assert.Equal(33, SeriesModel.All.Count);

        /// <summary>The series is read top to bottom; erbium at -3.0 above lithium at -3.04 was out of order.</summary>
        [Fact]
        public void ItIsStrictlyMostNegativeFirst()
        {
            foreach (var (above, below) in SeriesModel.All.Zip(SeriesModel.All.Skip(1)))
                Assert.True(above.Voltage < below.Voltage,
                    $"{above.ElementKey} {above.Voltage} sits above {below.ElementKey} {below.Voltage}");
        }

        /// <summary>
        /// Five couples were wrong in both apps. Copper's -0.159 V would make it more reactive than
        /// tin and lead; the four lanthanide and actinide values were up to 1.7 V too negative.
        /// </summary>
        [Theory]
        [InlineData("copper", 0.3419)]
        [InlineData("thorium", -1.899)]
        [InlineData("praseodymium", -2.353)]
        [InlineData("erbium", -2.331)]
        [InlineData("lanthanum", -2.379)]
        public void TheCorrectedCouplesHoldTheHandbookValues(string key, double volts) =>
            Assert.Equal(volts, SeriesModel.All.Single(s => s.ElementKey == key).Voltage, 9);

        /// <summary>Every row is an element-table key, so the deep link resolves.</summary>
        [Fact]
        public void EveryKeyIsDistinctAndLowercase()
        {
            Assert.Equal(SeriesModel.All.Count, SeriesModel.All.Select(s => s.ElementKey).Distinct().Count());
            Assert.All(SeriesModel.All, s => Assert.Equal(s.ElementKey.ToLowerInvariant(), s.ElementKey));
        }
    }
}
