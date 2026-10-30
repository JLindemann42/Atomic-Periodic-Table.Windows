using System;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>A calculation's working, step by step (roadmap 2.2). Mirrors Android's <c>StepsTest</c>.</summary>
    public class StepsTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language);

        private static void Contains(string text, params string[] lines)
        {
            foreach (var line in lines) Assert.True(text.Contains(line, StringComparison.Ordinal), $"missing \"{line}\" in:\n{text}");
        }

        [Fact]
        public void ABalanceShowsTheEquationsItSolves()
        {
            Contains(Chat().Ask("balance Fe + O2 -> Fe2O3 step by step").Text,
                "### Step by step", "a Fe + b O2 → c Fe2O3", "Fe: a = 2c", "O: 2b = 3c",
                "a = 4, b = 3, c = 2", "**Fe** — 4 on the left, 4 on the right");
        }

        [Fact]
        public void TheStepsReplaceTheShortWorkingRatherThanRepeatIt()
        {
            var brief = Chat().Ask("balance Fe + O2 -> Fe2O3").Text;
            var full = Chat().Ask("balance Fe + O2 -> Fe2O3 step by step").Text;
            Assert.DoesNotContain("### Step by step", brief, StringComparison.Ordinal);
            // The atom check moves into the steps, as their last one, rather than appearing twice.
            Assert.Contains("### Atom check", brief, StringComparison.Ordinal);
            Assert.DoesNotContain("### Atom check", full, StringComparison.Ordinal);
            Assert.Equal(2, Regex.Matches(full, "on the left, ").Count);
        }

        [Fact]
        public void ASolutionAfterwardsShowsEachFormulaWithItsNumbers()
        {
            var chat = Chat();
            var answer = chat.Ask("How many grams of NaCl for 250 mL of 0.5 mol/L?");
            Assert.Contains("Show the steps", answer.Suggestions);
            Contains(chat.Ask("show the steps").Text,
                "V = 250 mL = 0.25 L", "n = c × V = 0.5 mol/L × 0.25 L = 0.125 mol",
                "m = n × M = 0.125 mol × 58.44 g/mol = 7.305 g");
        }

        [Fact]
        public void ADecayShowsTheHalfLivesAndTheFraction()
        {
            var chat = Chat();
            chat.Ask("How much of 100 g of carbon-14 is left after 11460 years?");
            Contains(chat.Ask("show the steps").Text, "t½ =", "n = t ÷ t½ = 11460 yr ÷", "N = N₀ × (½)ⁿ = 100 g ×");
        }

        [Fact]
        public void MolesShowAvogadro()
        {
            Contains(Chat().Ask("how many atoms are in 2.5 moles of iron, step by step").Text,
                "N = n × N_A = 2.5 mol × 6.022 × 10²³ /mol = 1.506 × 10²⁴");
        }

        [Fact]
        public void AMolarMassShowsEachElementsShare()
        {
            Contains(Chat().Ask("molar mass of H2O step by step").Text, "H: 2 × 1.008 g/mol = 2.016 g/mol", "M(H2O) =");
        }

        [Fact]
        public void TheStepsAreNotOfferedTwice()
        {
            var chat = Chat();
            chat.Ask("balance Fe + O2 -> Fe2O3");
            Assert.DoesNotContain("Show the steps", chat.Ask("show the steps").Suggestions);
        }

        /// <summary>Each language's own balancing chip, then its own steps chip, in its own words.</summary>
        [Fact]
        public void EveryLanguageShowsTheSteps()
        {
            foreach (var language in TestAssets.Languages)
            {
                var strings = new ReswStringProvider(language, fallBackToEnglish: true);
                var chat = Chat(language);
                chat.Ask(strings.Array("ai_suggest_tools")[0]);
                var text = chat.Ask(strings.Get(S.ai_follow_steps)).Text;
                Assert.True(text.Contains("### " + strings.Get(S.ai_steps_header), StringComparison.Ordinal), $"{language}:\n{text}");
                Assert.True(text.Contains("a Fe + b O2 → c Fe2O3", StringComparison.Ordinal), $"{language}:\n{text}");
            }
        }
    }
}
