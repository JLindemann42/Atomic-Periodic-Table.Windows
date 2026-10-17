using System;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// What an element does when it meets something. General chemistry rather than a field
    /// lookup, which is why the engine declines it and ten authored strings answer it.
    /// </summary>
    public class ElementReactionsTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language);

        private static IStringProvider Strings(string language = "en") =>
            new ReswStringProvider(language, fallBackToEnglish: true);

        [Theory]
        [InlineData("what happens when sodium reacts with water")]
        [InlineData("what happens if I mix sodium and chlorine")]
        [InlineData("does helium react with anything")]
        public void TheseAreNoLongerDeclined(string query)
        {
            var reply = Chat().Ask(query);

            Assert.NotEqual(Strings().Get(S.ai_no_data), reply.Text);
            Assert.NotEmpty(reply.Text);
        }

        /// <summary>An alkali metal in water gets the violent-reaction answer, not the generic one.</summary>
        [Fact]
        public void AnAlkaliMetalInWaterReactsViolently()
        {
            var reply = Chat().Ask("what happens when sodium reacts with water");

            Assert.Equal(Strings().Get(S.ai_reaction_alkali_water, "Sodium"), reply.Text);
        }

        /// <summary>
        /// Magnesium is the exception with a string of its own: slow in cold water, rapid in
        /// steam, where the rest of its group react readily.
        /// </summary>
        [Fact]
        public void MagnesiumGetsItsOwnAnswer()
        {
            var reply = Chat().Ask("what happens when magnesium reacts with water");

            Assert.Equal(Strings().Get(S.ai_reaction_magnesium_water, "Magnesium"), reply.Text);
        }

        /// <summary>An alkali metal and a halogen make a salt.</summary>
        [Fact]
        public void AnAlkaliMetalAndAHalogenMakeASalt()
        {
            var reply = Chat().Ask("what happens if I mix sodium and chlorine");

            Assert.Equal(Strings().Get(S.ai_reaction_alkali_halogen, "Sodium", "Chlorine"), reply.Text);
        }

        /// <summary>A noble gas settles the answer whichever side of the pair it is on.</summary>
        [Theory]
        [InlineData("what happens when helium reacts with sodium")]
        [InlineData("what happens when sodium reacts with helium")]
        public void ANobleGasSettlesThePair(string query)
        {
            var reply = Chat().Ask(query);

            Assert.Equal(Strings().Get(S.ai_reaction_noble_gas, "Helium"), reply.Text);
        }

        /// <summary>"What does it react with" after an element is discussed is about that one.</summary>
        [Fact]
        public void TheFocusedElementAnswersABareQuestion()
        {
            var chat = Chat();
            chat.Ask("what is the density of sodium");

            var reply = chat.Ask("what does it react with");

            Assert.NotEqual(Strings().Get(S.ai_no_data), reply.Text);
            Assert.Contains("Sodium", reply.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// With no element anywhere there is nothing to answer about, so it falls through rather
        /// than inventing a subject.
        /// </summary>
        [Fact]
        public void WithNoElementItFallsThrough()
        {
            var reply = Chat().Ask("what reacts with water");

            Assert.Equal(Strings().Get(S.ai_no_data), reply.Text);
        }
    }
}
