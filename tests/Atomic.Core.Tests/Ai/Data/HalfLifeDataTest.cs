using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// The isotope half-lives.
    ///
    /// <para>Much of the column was not data. Lead 181-201 were all "0.1 ms" (Pb-198 is 2.4
    /// <i>hours</i>); osmium 172-185 climbed in steps of exactly half a second; dysprosium's values
    /// had been pasted over holmium and erbium; and three strings parsed to nothing, so the app
    /// showed no half-life at all. The figures below are from NUBASE, via the
    /// <c>mendeleev</c> package, and the structural tests are the fingerprints those defects
    /// left.</para>
    /// </summary>
    public class HalfLifeDataTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private const double Hour = 3600.0;
        private const double Day = 86400.0;
        private const double Year = 3.15576e7;

        private static double? HalfLife(string element, int mass) =>
            Service.Store.Element(element)!.Isotopes.Single(i => i.Nucleons == mass).HalfLifeSeconds;

        /// <summary>Within 2%: the tables carry the reference's own digits.</summary>
        private static void Near(double expected, double? actual)
        {
            Assert.NotNull(actual);
            Assert.InRange(actual!.Value / expected, 0.98, 1.02);
        }

        [Fact] public void LeadsPlaceholderRampIsGone() => Near(2.4 * Hour, HalfLife("lead", 198));

        [Fact] public void HolmiumDoesNotKeepDysprosiumsValue() => Near(26.812 * Hour, HalfLife("holmium", 166));

        [Fact] public void ErbiumDoesNotKeepDysprosiumsValue() => Near(9.392 * Day, HalfLife("erbium", 169));

        /// <summary>
        /// Slots the stable-isotope pass had to blank because they were wrongly marked stable.
        /// </summary>
        [Fact] public void ARadioactiveIsotopeNoLongerMarkedStableHasItsHalfLife() =>
            Near(4570 * Year, HalfLife("holmium", 163));

        /// <summary>
        /// "N/A" was left on six radioactive isotopes last time because it is not a stable marker.
        /// A measurement exists for each.
        /// </summary>
        [Theory]
        [InlineData("hafnium", 175, 70.65 * Day)]
        [InlineData("thallium", 197, 2.84 * Hour)]
        [InlineData("thallium", 200, 26.1 * Hour)]
        [InlineData("rhenium", 187, 4.16e10 * Year)]
        public void AnIsotopeWithNoHalfLifeRecordedNowHasOne(string element, int mass, double expected) =>
            Near(expected, HalfLife(element, mass));

        /// <summary>
        /// Calcium-48 decays - observed, at 5.6x10^19 years - so it was left as "no data" rather
        /// than called stable. The measurement is the honest thing to put there.
        /// </summary>
        [Fact] public void ACalcium48DecayIsRecordedAsADecay() => Near(5.6e19 * Year, HalfLife("calcium", 48));

        /// <summary>
        /// "2.5x102^-21 (s)" and "0.178.3 (s)" parsed to nothing, so the app showed no half-life
        /// for these three at all.
        /// </summary>
        [Theory]
        [InlineData("helium", 9)]
        [InlineData("helium", 10)]
        [InlineData("lithium", 9)]
        public void AMalformedHalfLifeIsReadable(string element, int mass) =>
            Assert.NotNull(HalfLife(element, mass));

        /// <summary>
        /// Every slot that claims a half-life can be read as one. A string that parses to nothing
        /// is shown as no half-life, which is the same as the data being absent.
        /// </summary>
        [Fact]
        public void NoHalfLifeIsWrittenThatCannotBeRead()
        {
            var unreadable = Service.Store.Elements
                .SelectMany(e => e.Isotopes.Select(i => (e.Key, Isotope: i)))
                .Where(x => !x.Isotope.Stable && x.Isotope.HalfLifeSeconds is null)
                .Where(x => !IsNoData(x.Isotope.HalfLifeDisplay))
                .Select(x => $"{x.Key} {x.Isotope.Name}: '{x.Isotope.HalfLifeDisplay}'")
                .ToList();

            Assert.Empty(unreadable);
        }

        private static bool IsNoData(string s) =>
            s.Trim() is "" or "---" or "--" or "N/A" or "unknown" or "empty" or "???"
            || s.StartsWith("---", StringComparison.Ordinal);

        /// <summary>
        /// The same half-life at the same mass number in three elements is a pasted column. One
        /// element can own a value; nine such triples existed and every one was dysprosium's
        /// values copied over holmium and erbium.
        /// </summary>
        [Fact]
        public void NoHalfLifeIsCopiedAcrossThreeElements()
        {
            var copied = Service.Store.Elements
                .SelectMany(e => e.Isotopes.Where(i => i.HalfLifeSeconds is not null)
                    .Select(i => (e.Key, i.Nucleons, i.HalfLifeDisplay)))
                .GroupBy(x => (x.Nucleons, x.HalfLifeDisplay))
                .Where(g => g.Select(x => x.Key).Distinct().Count() >= 3)
                .Select(g => $"A={g.Key.Nucleons} '{g.Key.HalfLifeDisplay}': " +
                             string.Join(", ", g.Select(x => x.Key).Distinct()))
                .ToList();

            Assert.Empty(copied);
        }

        /// <summary>
        /// A ramp is the other fingerprint: lead had 21 isotopes at "0.1 ms". No element has a
        /// real half-life repeated four times.
        /// </summary>
        [Fact]
        public void NoElementRepeatsOneHalfLifeFourTimes()
        {
            var repeated = Service.Store.Elements
                .SelectMany(e => e.Isotopes.Where(i => i.HalfLifeSeconds is not null)
                    .Select(i => (e.Key, i.HalfLifeDisplay)))
                .GroupBy(x => x)
                .Where(g => g.Count() >= 4)
                .Select(g => $"{g.Key.Key}: '{g.Key.HalfLifeDisplay}' x{g.Count()}")
                .ToList();

            Assert.Empty(repeated);
        }

        /// <summary>
        /// And a half-life that climbs in equal steps across consecutive mass numbers - osmium
        /// 172-185 read 5.5 s, 6.0 s, 6.5 s - is a number someone counted, not one anyone measured.
        /// </summary>
        [Fact]
        public void NoHalfLifeRampsInEqualSteps()
        {
            var ramps = new List<string>();
            foreach (var e in Service.Store.Elements)
            {
                var run = e.Isotopes.Where(i => i.HalfLifeSeconds is not null && i.Nucleons is not null)
                    .OrderBy(i => i.Nucleons).ToList();

                for (var i = 0; i + 3 < run.Count; i++)
                {
                    var window = run.Skip(i).Take(4).ToList();
                    if (window[3].Nucleons - window[0].Nucleons != 3) continue;

                    var steps = window.Zip(window.Skip(1), (a, b) => b.HalfLifeSeconds!.Value - a.HalfLifeSeconds!.Value)
                        .ToList();

                    if (steps.All(s => Math.Abs(s) > 0 && Math.Abs(s - steps[0]) < 1e-9 * Math.Abs(steps[0])))
                        ramps.Add($"{e.Key} A={window[0].Nucleons}..{window[3].Nucleons}");
                }
            }

            Assert.Empty(ramps);
        }
    }
}
