using System;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// Naming an element and asking nothing in particular.
    ///
    /// <para>"gold", "tell me about gold" and "tell me about iron" are all in
    /// <c>must_defer.tsv</c>: the engine must decline them, and something downstream must answer.
    /// Until now nothing did, so the most basic question in the app replied "Hmm, I couldn't find
    /// information about that."</para>
    /// </summary>
    public class ElementOverviewTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language);

        private static IStringProvider Strings(string language = "en") =>
            new ReswStringProvider(language, fallBackToEnglish: true);

        [Theory]
        [InlineData("gold")]
        [InlineData("tell me about gold")]
        [InlineData("tell me about iron")]
        [InlineData("element 79")]
        public void NamingAnElementIsAnswered(string query)
        {
            var reply = Chat().Ask(query);

            Assert.NotEqual(Strings().Get(S.ai_no_data), reply.Text);
            Assert.NotEmpty(reply.Text);
        }

        /// <summary>The atomic number resolves to the right element - 79 is gold, not iron.</summary>
        [Fact]
        public void AnAtomicNumberIdentifiesItsElement()
        {
            var reply = Chat().Ask("element 79");

            Assert.Contains("79", reply.Text, StringComparison.Ordinal);
            Assert.Contains("Au", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>The basic facts are there, with their shipped labels.</summary>
        [Fact]
        public void TheOverviewCarriesTheBasicFacts()
        {
            var reply = Chat().Ask("gold");

            Assert.Contains("Au", reply.Text, StringComparison.Ordinal);
            Assert.Contains("79", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>
        /// A question that asks something specific is still the engine's, not an overview.
        /// </summary>
        [Theory]
        [InlineData("what is the density of gold")]
        [InlineData("compare gold and silver")]
        public void ARealQuestionIsNotTurnedIntoAnOverview(string query)
        {
            var reply = Chat().Ask(query);

            Assert.Equal(ReplySource.Engine, reply.Source);
        }

        /// <summary>Asking about an element puts it in focus, like any other answer.</summary>
        [Fact]
        public void TheOverviewSetsTheFocus()
        {
            var chat = Chat();
            chat.Ask("gold");

            var reply = chat.Ask("what is its density");

            Assert.Contains("19.3", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>A Swedish overview is answered, and in Swedish.</summary>
        [Fact]
        public void ASwedishOverviewIsAnswered()
        {
            var reply = Chat("sv").Ask("berätta om guld");

            Assert.NotEqual(Strings("sv").Get(S.ai_no_data), reply.Text);
            Assert.Contains("Au", reply.Text, StringComparison.Ordinal);
        }
    }
}
