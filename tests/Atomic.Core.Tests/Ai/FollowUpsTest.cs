using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Atomic.Core.Text;
using Xunit;
using Xunit.Abstractions;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>Follow-up chips that fit the answer just given (roadmap 2.1). Mirrors Android's <c>FollowUpsTest</c>.</summary>
    public class FollowUpsTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private readonly ITestOutputHelper _output;

        public FollowUpsTest(ITestOutputHelper output) => _output = output;

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language);

        private static IStringProvider Strings(string language) => new ReswStringProvider(language, fallBackToEnglish: true);

        /// <summary>
        /// Questions every language has a chip for already, so each can be asked in its own words: a
        /// property lookup, a ranking, a list and a calculation.
        /// </summary>
        private static IEnumerable<string> Seeds(string language)
        {
            var strings = Strings(language);
            var gold = Service.Localized(language)?.Name("gold") ?? "Gold";
            yield return JavaFormat.Format(System.Globalization.CultureInfo.InvariantCulture,
                strings.Array("ai_suggest_element")[1], gold);
            yield return strings.Array("ai_suggest_general")[3];
            yield return strings.Array("ai_suggest_general")[2];
            yield return strings.Array("ai_suggest_tools")[0];
        }

        [Fact]
        public void AfterAPropertyTheChipsAreAboutThatProperty()
        {
            var reply = Chat().Ask("what is the density of gold");
            Assert.Equal(
                ["Density of Gold and Silver", "Which element has the highest density?", "Density of Gold in kg/m³"],
                reply.Suggestions);
        }

        [Fact]
        public void ARankingInvitesItsOtherEnd()
        {
            var reply = Chat().Ask("which element has the highest melting point");
            Assert.Equal("Which element has the lowest melting point?", reply.Suggestions[0]);
        }

        [Fact]
        public void AListInvitesACount()
        {
            var reply = Chat().Ask("list the noble gases");
            Assert.Equal("How many are there?", reply.Suggestions[0]);
            var chat = Chat();
            chat.Ask("list the noble gases");
            Assert.Equal(Intent.Aggregate, chat.Ask(reply.Suggestions[0]).Plan?.Intent);
        }

        [Fact]
        public void AChipNeverRepeatsTheQuestion()
        {
            var reply = Chat().Ask("atomic mass of gold");
            Assert.DoesNotContain("Atomic mass of Gold", reply.Suggestions);
        }

        /// <summary>
        /// The invariant that keeps the templates honest: in every language, every chip offered after
        /// an answer is a question the assistant answers. Prints what each language is offered.
        /// </summary>
        [Fact]
        public void EveryChipIsAnsweredInEveryLanguage()
        {
            foreach (var language in TestAssets.Languages)
            {
                var noData = Strings(language).Get(S.ai_no_data);
                foreach (var seed in Seeds(language))
                {
                    var chips = Chat(language).Ask(seed).Suggestions;
                    _output.WriteLine($"[follow-ups] {language}: {seed} -> [{string.Join(", ", chips)}]");
                    foreach (var chip in chips)
                    {
                        var chat = Chat(language);
                        chat.Ask(seed);
                        var answer = chat.Ask(chip);
                        Assert.True(answer.DeclineReason is null, $"{language}: {seed} -> {chip}: {answer.Text}");
                        Assert.True(answer.Text != noData, $"{language}: {seed} -> {chip}");
                    }
                }
            }
        }
    }
}
