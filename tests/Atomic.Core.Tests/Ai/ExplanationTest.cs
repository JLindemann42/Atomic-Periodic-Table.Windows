using System;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// Reviewed explanations for why questions, each shown with the data it explains (roadmap 2.4).
    /// Mirrors Android's <c>ExplanationTest</c>.
    /// </summary>
    public class ExplanationTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language);

        private static void Explains(string query, string key, ChatCardKind? card, string language = "en")
        {
            var reply = Chat(language).Ask(query);
            Assert.True(reply.Plan?.Intent == Intent.Explanation, $"{query}: {reply.Plan?.Intent}");
            Assert.True(reply.Plan!.Literal == key, $"{query}: {reply.Plan.Literal}");
            Assert.True(reply.Card?.Kind == card, $"{query}: {reply.Card?.Kind}");
        }

        [Fact]
        public void ATrendIsExplainedByItsFieldAndItsDirection()
        {
            Explains("why does atomic radius increase down a group", "radius-down-group", ChatCardKind.TrendLine);
            Explains("why does radius decrease left to right, despite adding electrons", "radius-across-period", ChatCardKind.TrendLine);
            Explains("why does ionization energy increase across a period", "ionization-across-period", ChatCardKind.TrendLine);
        }

        [Fact]
        public void AConceptIsExplainedByItsOwnWords()
        {
            Explains("why are noble gases unreactive", "noble-gas-inert", ChatCardKind.TrendLine);
            Explains("why is fluorine the most electronegative element", "fluorine-electronegativity", ChatCardKind.TrendLine);
            Explains("why is mercury a liquid", "mercury-liquid", ChatCardKind.TrendLine);
            Explains("why is there a big jump in sodium's second ionization energy", "ionization-jumps", ChatCardKind.IonizationSeries);
            Explains("why does the density peak in the middle of the table", "density-table", ChatCardKind.HeatMap);
        }

        [Fact]
        public void TheAnswerIsTheExplanation()
        {
            var text = Chat().Ask("why does atomic radius increase down a group").Text;
            Assert.StartsWith("Each step down a group adds a whole new electron shell", text, StringComparison.Ordinal);
        }

        [Fact]
        public void AProFieldsCardStaysPro()
        {
            const string query = "why does electron affinity increase across a period";
            Assert.Equal(ChatCardKind.TrendLine, Chat().Ask(query).Card?.Kind);
            var free = new ChatOrchestrator(
                Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true),
                () => new Entitlements(IsPro: false, IsProPlus: false), null, "en").Ask(query);
            Assert.Equal(Intent.Explanation, free.Plan?.Intent);
            Assert.Null(free.Card);
        }

        [Fact]
        public void ADefinitionIsNotAWhy() =>
            Assert.NotEqual(Intent.Explanation, Chat().Ask("what is electronegativity").Plan?.Intent);
    }
}
