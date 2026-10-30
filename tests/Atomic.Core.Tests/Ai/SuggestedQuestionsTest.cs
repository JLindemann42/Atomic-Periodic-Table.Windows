using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Atomic.Core.Text;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// Every question the app offers, in every language it offers it in, is answered.
    ///
    /// <para>The suggestion chips are the one set of queries the agent cannot be allowed to
    /// decline: the app puts them in front of the user as things to tap, they are shipped and
    /// professionally translated in all twelve languages, and a decline is a dead end the user was
    /// invited into. They are also the only multilingual query set that is translated rather than
    /// written by whoever added the handler - the corpus has 56 English rows and one each for
    /// Italian, Hindi, Filipino, Afrikaans and Urdu, so it cannot show that a capability works in
    /// twelve languages. This can.</para>
    ///
    /// <para>27 of the 168 were declined when this was written.</para>
    /// </summary>
    public class SuggestedQuestionsTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        /// <summary>The twelve shipped languages, as the app names them.</summary>
        private static readonly string[] Languages =
            ["en", "sv", "de", "fr", "es", "it", "pt-BR", "hi", "ur", "zh-Hans", "af", "fil"];

        public static TheoryData<string, string> EverySuggestion()
        {
            var data = new TheoryData<string, string>();

            foreach (var language in Languages)
            {
                var strings = new ReswStringProvider(language, fallBackToEnglish: true);

                // A real element, localized, because that is what the chip is rendered with.
                var name = Service.Localized(language)?.Name("gold") ?? "Gold";

                foreach (var q in strings.Array("ai_suggest_general")
                             .Concat(strings.Array("ai_suggest_tools"))
                             .Concat(strings.Array("ai_suggest_element").Select(t =>
                                 Try.OrNull(() => JavaFormat.Format(CultureInfo.InvariantCulture, t, name)) ?? t)))
                {
                    data.Add(language, q);
                }
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(EverySuggestion))]
        public void ASuggestedQuestionIsAnswered(string language, string query)
        {
            var chat = new ChatOrchestrator(
                Service, l => new ReswStringProvider(l, fallBackToEnglish: true),
                null, null, language, new Random(5));

            var reply = chat.Ask(query);

            Assert.False(
                string.IsNullOrWhiteSpace(reply.Text),
                $"[{language}] answered nothing at all: {query}");

            Assert.NotEqual(
                new ReswStringProvider(language, true).Get(S.ai_no_data),
                reply.Text);
        }

        /// <summary>
        /// Every language has chips to offer. A language whose arrays failed to load would pass
        /// every row above by having no rows at all.
        /// </summary>
        [Theory]
        [MemberData(nameof(LanguageNames))]
        public void EveryLanguageOffersSuggestions(string language)
        {
            var strings = new ReswStringProvider(language, fallBackToEnglish: true);

            Assert.NotEmpty(strings.Array("ai_suggest_general"));
            Assert.NotEmpty(strings.Array("ai_suggest_element"));
        }

        public static TheoryData<string> LanguageNames()
        {
            var data = new TheoryData<string>();
            foreach (var l in Languages) data.Add(l);
            return data;
        }

        /// <summary>
        /// The element tables are named for the bare language - elements_pt.json, elements_zh.json -
        /// while the app speaks pt-BR and zh-Hans. Those two silently fell back to English, so
        /// Portuguese and Chinese answers carried English element names.
        /// </summary>
        [Theory]
        [InlineData("pt-BR", "Ouro")]
        [InlineData("zh-Hans", "金")]
        [InlineData("sv", "Guld")]
        public void ARegionalTagStillFindsItsElementNames(string language, string expected)
        {
            Assert.Equal(expected, Service.Localized(language)?.Name("gold"));
        }
    }
}
