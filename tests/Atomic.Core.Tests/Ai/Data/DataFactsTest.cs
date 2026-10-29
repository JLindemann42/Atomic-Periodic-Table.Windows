using System;
using System.Linq;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>Facts read off the data for the chat (roadmap 2.7). Mirrors Android's <c>DataFactsTest</c>.</summary>
    public class DataFactsTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static readonly DataFacts Facts = new(Service.Store);

        [Fact]
        public void EveryFieldHasATopAndABottom()
        {
            var extremes = Facts.Extremes();
            Assert.Equal(DataFacts.Fields.Count * 2, extremes.Count);
            Assert.All(extremes, f => Assert.True(f.Rank == 1 && f.Of >= 20));
        }

        /// <summary>Seaborgium's predicted 35 g/cm³ would make it the densest; past fermium is left out.</summary>
        [Fact]
        public void TheExtremesAreTheKnownOnes()
        {
            string Top(string field) => Facts.Extremes().Single(f => f.FieldId == field && f.Highest).ElementKey;
            string Bottom(string field) => Facts.Extremes().Single(f => f.FieldId == field && !f.Highest).ElementKey;
            Assert.Equal("osmium", Top("density"));
            Assert.Equal("hydrogen", Bottom("density"));
            Assert.Equal("tungsten", Top("melting_point"));
            Assert.Equal("fluorine", Top("electronegativity"));
            Assert.Equal("caesium", Bottom("electronegativity"));
            Assert.Equal("helium", Top("ionization_energy"));
            Assert.Equal("silver", Top("thermal_conductivity"));
            Assert.DoesNotContain(Facts.Extremes(), f => (Service.Store.Element(f.ElementKey)?.AtomicNumber ?? 0) > DataFacts.LastMeasured);
        }

        [Fact]
        public void AnElementsFactIsItsBestPlace()
        {
            var osmium = Facts.ForElement("osmium");
            Assert.NotNull(osmium);
            Assert.Equal("density", osmium!.FieldId);
            Assert.Equal(1, osmium.Rank);
            Assert.Null(Facts.ForElement("seaborgium"));
        }

        [Fact]
        public void TheSentenceReadsInEachLanguage()
        {
            var osmium = Facts.Extremes().First(f => f.FieldId == "density" && f.Highest);
            var en = DataFacts.Render(osmium, new ReswStringProvider("en", fallBackToEnglish: true), Service.Localized("en"));
            Assert.StartsWith($"**Osmium** has the highest density of the {osmium.Of} elements", en, StringComparison.Ordinal);
            Assert.Contains("22.5", en);
            var sv = DataFacts.Render(osmium, new ReswStringProvider("sv", fallBackToEnglish: true), Service.Localized("sv"));
            Assert.StartsWith("**Osmium** har högst", sv, StringComparison.Ordinal);
            Assert.DoesNotContain("str:", sv);
        }

        [Fact]
        public void TheDayCyclesThroughEveryFact()
        {
            var all = Facts.Extremes();
            Assert.Equal(all.ToHashSet(), Enumerable.Range(0, all.Count).Select(i => Facts.ForDay(i)!).ToHashSet());
            Assert.Equal(Facts.ForDay(0), Facts.ForDay(all.Count));
        }

        [Fact]
        public void TheChatsFunFactsIncludeDataFacts()
        {
            var asked = Enumerable.Range(0, 40).Select(seed =>
                new ChatOrchestrator(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, "en", new Random(seed))
                    .Ask("tell me a fun fact").Text);
            Assert.Contains(asked, t => t.Contains("elements with a recorded value", StringComparison.Ordinal));
        }
    }
}
