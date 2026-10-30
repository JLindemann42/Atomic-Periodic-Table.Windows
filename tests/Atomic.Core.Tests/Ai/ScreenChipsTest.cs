using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Atomic.Core.Text;
using Xunit;
using Xunit.Abstractions;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>A chat opened from a screen knows where it came from (roadmap 2.9). Mirrors Android's <c>ScreenChipsTest</c>.</summary>
    public class ScreenChipsTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private readonly ITestOutputHelper _output;

        public ScreenChipsTest(ITestOutputHelper output) => _output = output;

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language);

        private static ScreenChips Chips => Service.ScreenChips;

        [Fact]
        public void EveryScreenHasEnglishChips()
        {
            var screens = typeof(Screens).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
            Assert.True(screens.Count >= 17, string.Join(", ", screens));
            foreach (var screen in screens) Assert.True(Chips.ForScreen(screen, "en").Count == 2, screen);
        }

        [Fact]
        public void AScreensChipsComeFirst()
        {
            var chat = Chat();
            var general = chat.OpeningSuggestions();
            chat.OpenedFrom(Screens.IdealGas);
            var opened = chat.OpeningSuggestions();
            Assert.Equal(Chips.ForScreen(Screens.IdealGas, "en"), opened.Take(2));
            Assert.True(opened.Count == 3 && general.Contains(opened[2]));
            // A new thread is still opened from the same screen.
            chat.Reset();
            Assert.Equal(opened, chat.OpeningSuggestions());
        }

        [Fact]
        public void AnElementScreenFocusesItsElement()
        {
            var chat = Chat();
            chat.OpenedFrom(Screens.Nuclides, "carbon");
            var reply = chat.Ask("what is its density");
            Assert.Contains("Carbon", reply.Text);
        }

        [Fact]
        public void AScreenWithoutChipsGetsTheGeneralOnes()
        {
            var chat = Chat();
            var general = chat.OpeningSuggestions();
            chat.OpenedFrom("no_such_screen");
            Assert.Equal(general, chat.OpeningSuggestions());
        }

        /// <summary>
        /// The promise the follow-up chips keep, for these too: in every language, every chip a
        /// screen offers is a question the assistant answers.
        /// </summary>
        [Fact]
        public void EveryChipIsAnsweredInItsLanguage()
        {
            var failures = new List<string>();
            foreach (var (screen, language) in Chips.Keys.OrderBy(k => k.Screen).ThenBy(k => k.Language))
            {
                var noData = new ReswStringProvider(language, fallBackToEnglish: true).Get(S.ai_no_data);
                var chips = Chips.ForScreen(screen, language);
                for (var i = 0; i < chips.Count; i++)
                {
                    var chat = Chat(language);
                    chat.OpenedFrom(screen);
                    var answer = chat.Ask(chips[i]);
                    if (answer.DeclineReason is not null || answer.Text == noData)
                        failures.Add($"{screen}/{language}#{i}: {chips[i]} -> {answer.DeclineReason}");
                }
            }
            failures.ForEach(_output.WriteLine);
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
    }
}
