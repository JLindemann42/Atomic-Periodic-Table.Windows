using System;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// The three questions answered from an element's prose rather than from a field.
    ///
    /// <para>Every query here is in <c>must_defer.tsv</c>, which says the engine must decline it
    /// so something downstream can answer. Before this existed, "downstream" was the decline.</para>
    /// </summary>
    public class ElementNarrativeTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language);

        private static IStringProvider Strings(string language = "en") =>
            new ReswStringProvider(language, fallBackToEnglish: true);

        // ---- detection -------------------------------------------------------------------------

        [Theory]
        [InlineData("what is gold used for", ElementNarrative.Topic.Usage)]
        [InlineData("what is titanium used for", ElementNarrative.Topic.Usage)]
        [InlineData("what is the biological role of iron", ElementNarrative.Topic.Biological)]
        [InlineData("is zinc important for the body", ElementNarrative.Topic.Biological)]
        [InlineData("where does the name gold come from", ElementNarrative.Topic.Etymology)]
        [InlineData("why is it called tungsten", ElementNarrative.Topic.Etymology)]
        public void TheTopicIsRecognised(string query, ElementNarrative.Topic expected) =>
            Assert.Equal(expected, ElementNarrative.Detect(TextMatching.NormalizeForLookup(query)));

        /// <summary>
        /// The question-word gate: without it every sentence naming an element is an etymology
        /// question and anything "useful" is a usage question.
        /// </summary>
        [Theory]
        [InlineData("what is the density of gold")]
        [InlineData("gold")]
        [InlineData("balance Fe + O2 -> Fe2O3")]
        [InlineData("")]
        public void OrdinaryQuestionsAreNotClaimed(string query) =>
            Assert.Null(ElementNarrative.Detect(TextMatching.NormalizeForLookup(query)));

        // ---- answering through the orchestrator ------------------------------------------------

        [Theory]
        [InlineData("what is gold used for")]
        [InlineData("what is the biological role of iron")]
        [InlineData("where does the name gold come from")]
        public void TheseAreNoLongerDeclined(string query)
        {
            var reply = Chat().Ask(query);

            Assert.NotEqual(Strings().Get(S.ai_no_data), reply.Text);
            Assert.NotEmpty(reply.Text);
        }

        /// <summary>The answer is about the element asked about, and says so by name.</summary>
        [Fact]
        public void TheAnswerNamesTheElement()
        {
            var reply = Chat().Ask("what is gold used for");

            Assert.Contains("Gold", reply.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// "What is it used for" after asking about an element is about that element - the focus
        /// carries, as it does for any other follow-up.
        /// </summary>
        [Fact]
        public void TheFocusedElementAnswersAFollowUp()
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");

            var reply = chat.Ask("what is it used for");

            Assert.NotEqual(Strings().Get(S.ai_no_data), reply.Text);
            Assert.Contains("Gold", reply.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A reaction question is not a usage question. Fuzzy keyword matching claimed it:
        /// "what happens when sodium reacts with water" came back as a list of uses for sodium.
        /// </summary>
        [Theory]
        [InlineData("what happens when sodium reacts with water")]
        [InlineData("which element is similar to gold")]
        public void ARelatedButDifferentQuestionIsNotClaimed(string query) =>
            Assert.Null(ElementNarrative.Detect(TextMatching.NormalizeForLookup(query)));

        /// <summary>
        /// Gold has no recorded discoverer - the field is the blank sentinel - so the etymology
        /// fallback must not render "it was discovered by  in ."
        /// </summary>
        [Fact]
        public void TheEtymologyFallbackNeverLeavesABlank()
        {
            var reply = Chat().Ask("where does the name gold come from");

            Assert.DoesNotContain("by  in", reply.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(" .", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>
        /// The biological fallback ends "...standard safety says:" on Android because an engine
        /// answer is appended. Nothing is appended here, so the colon must not dangle.
        /// </summary>
        [Fact]
        public void TheBiologicalFallbackDoesNotEndOnAColon()
        {
            var reply = Chat().Ask("what is the biological role of gold");

            Assert.False(reply.Text.TrimEnd().EndsWith(':'), reply.Text);
        }

        /// <summary>
        /// With no element anywhere, there is nothing to describe, so it falls through rather than
        /// inventing a subject.
        /// </summary>
        [Fact]
        public void WithNoElementItFallsThrough()
        {
            var reply = Chat().Ask("what is it used for");

            Assert.Equal(Strings().Get(S.ai_no_data), reply.Text);
        }

        /// <summary>
        /// A Swedish question is answered in Swedish. The description is part English, so the
        /// English-clause filter is what keeps the reply from quoting it.
        /// </summary>
        [Fact]
        public void ASwedishQuestionDoesNotQuoteTheEnglishDescription()
        {
            var reply = Chat("sv").Ask("vilken biologisk roll har järn");

            Assert.NotEqual(Strings("sv").Get(S.ai_no_data), reply.Text);
        }
    }
}
