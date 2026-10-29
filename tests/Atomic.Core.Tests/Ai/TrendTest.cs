using System;
using System.Linq;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>One field across a set of elements, or the whole table (roadmap 2.3). Mirrors Android's <c>TrendTest</c>.</summary>
    public class TrendTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language);

        private static ChatCard? CardOf(ChatReply reply) => ChatCardCodec.Decode(ChatCardCodec.Encode(reply.Card));

        [Fact]
        public void APeriodIsALineInAtomicNumberOrder()
        {
            var reply = Chat().Ask("electronegativity across period 3");
            Assert.Equal(Intent.Trend, reply.Plan?.Intent);
            Assert.Equal([new Filter.InPeriod(3)], reply.Plan!.Filters);
            Assert.Contains("Na 0.93 · Mg 1.31", reply.Text, StringComparison.Ordinal);
            Assert.Contains("It rises overall, from Sodium (0.93) to Chlorine (3.16).", reply.Text, StringComparison.Ordinal);
            Assert.Equal(ChatCardKind.TrendLine, reply.Card?.Kind);
        }

        [Fact]
        public void AGroupReadsDownwards()
        {
            var reply = Chat().Ask("how does atomic radius change down group 1");
            Assert.Equal(Intent.Trend, reply.Plan?.Intent);
            Assert.Equal(ChatCardKind.TrendLine, reply.Card?.Kind);
        }

        [Fact]
        public void ASeriesIsALineToo()
        {
            var reply = Chat().Ask("show the electronegativity trend of the halogens");
            Assert.Equal(Intent.Trend, reply.Plan?.Intent);
            Assert.Contains("It falls overall", reply.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void TheWholeTableIsAMap()
        {
            var reply = Chat().Ask("heat map of density");
            Assert.Equal(Intent.Trend, reply.Plan?.Intent);
            Assert.Equal(ChatCardKind.HeatMap, reply.Card?.Kind);
            Assert.Contains("Highest: ", reply.Text, StringComparison.Ordinal);
            Assert.Contains("Lowest: Hydrogen", reply.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void ARankingInAPeriodIsStillARanking()
        {
            var reply = Chat().Ask("which element in period 3 has the highest electronegativity");
            Assert.NotEqual(Intent.Trend, reply.Plan?.Intent);
            Assert.Equal(1, reply.Plan?.Limit);
        }

        [Fact]
        public void TheCardsDrawWhatTheAnswerStates()
        {
            var reply = Chat().Ask("electronegativity across period 3");
            var series = TrendSeriesReducer.Of(CardOf(reply)!, Service.Store);
            Assert.NotNull(series);
            Assert.Equal(["Na", "Mg", "Al", "Si", "P", "S", "Cl"], series!.Points.Select(p => p.Symbol));

            var map = HeatMapReducer.Of("density", Service.Store);
            Assert.NotNull(map);
            // The element with the largest value is the darkest cell, whichever it is in the data.
            Assert.Equal(1.0, map!.Cells.Where(c => c.Value is not null).MaxBy(c => c.Value)!.Fraction!.Value, 9);
            var osmium = map.Cells.First(c => c.Symbol == "Os");
            Assert.Equal((6, 8), (osmium.Row, osmium.Column));
            var lanthanum = map.Cells.First(c => c.Symbol == "La");
            Assert.Equal((8, 3), (lanthanum.Row, lanthanum.Column));
        }
    }
}
