using System;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// "Which element is similar to gold" - in <c>must_defer.tsv</c>, so the engine declines it
    /// and this answers it.
    /// </summary>
    public class ElementSimilarityTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language);

        private static IStringProvider Strings(string language = "en") =>
            new ReswStringProvider(language, fallBackToEnglish: true);

        [Theory]
        [InlineData("which element is similar to carbon")]
        [InlineData("which element is similar to gold")]
        [InlineData("vilket grundämne liknar kol")]
        public void TheseAreNoLongerDeclined(string query)
        {
            var reply = Chat(query.StartsWith("vilket", StringComparison.Ordinal) ? "sv" : "en").Ask(query);

            var noData = Strings(query.StartsWith("vilket", StringComparison.Ordinal) ? "sv" : "en").Get(S.ai_no_data);
            Assert.NotEqual(noData, reply.Text);
        }

        /// <summary>
        /// Gold's group, not gold's series: copper and silver sit in the same column. The series
        /// would answer "every transition metal", which is true and useless.
        /// </summary>
        [Fact]
        public void TheAnswerIsTheGroupNotTheWholeSeries()
        {
            var reply = Chat().Ask("which element is similar to gold");

            Assert.Contains("Copper", reply.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Silver", reply.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Iron", reply.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The element asked about is not listed as similar to itself.</summary>
        [Fact]
        public void TheElementIsNotItsOwnNeighbour()
        {
            var reply = Chat().Ask("which element is similar to gold");

            var listed = reply.Text.Split(':')[^1];
            Assert.DoesNotContain("Gold,", listed, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>With an element in focus, "what is similar" means that one.</summary>
        [Fact]
        public void TheFocusedElementAnswersABareQuestion()
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");

            var reply = chat.Ask("which elements are similar");

            Assert.Contains("Copper", reply.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>With no element anywhere, it says which element it needs.</summary>
        [Fact]
        public void WithNoElementItAsksWhichOne()
        {
            var reply = Chat().Ask("which elements are similar");

            Assert.Equal(Strings().Get(S.ai_similarity_no_element), reply.Text);
        }

        /// <summary>
        /// A lanthanide has no group number - it sits outside the numbered columns - so it gets
        /// the series sentence rather than an empty list.
        /// </summary>
        [Fact]
        public void AnElementOutsideTheNumberedGroupsFallsBackToItsSeries()
        {
            var reply = Chat().Ask("which element is similar to neodymium");

            Assert.NotEqual(Strings().Get(S.ai_no_data), reply.Text);
            Assert.Contains("Neodymium", reply.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
