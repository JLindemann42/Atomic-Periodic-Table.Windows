using System.Linq;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// Questions about the app itself, which the engine defers on by design - capabilities.tsv
    /// asserts the deferral, and nothing downstream of it was answering.
    /// </summary>
    public class AppFeaturesTest
    {
        private static string? Ask(string query) =>
            AppFeatures.Resolve(TextMatching.NormalizeForLookup(query));

        [Theory]
        [InlineData("what can you do", S.ai_feature_capabilities)]
        [InlineData("what do you do?", S.ai_feature_capabilities)]
        [InlineData("do you have flashcards", S.ai_feature_flashcards)]
        [InlineData("tell me about the unit converter", S.ai_feature_unit_converter)]
        [InlineData("what is the nuclide table", S.ai_feature_nuclide_table)]
        [InlineData("is there a reaction balancer", S.ai_feature_reaction_balancer)]
        [InlineData("show me my achievements", S.ai_feature_achievements)]
        [InlineData("ideal gas", S.ai_feature_ideal_gas)]
        [InlineData("emission spectrum viewer", S.ai_feature_emission_spectrum)]
        public void EnglishQuestionsFindTheirFeature(string query, string expected) =>
            Assert.Equal(expected, Ask(query));

        /// <summary>
        /// Every language ships the answers, so every language must be able to ask. These are the
        /// spellings a person types, accents included - which is the case Android's unfolded
        /// normalization only reaches through the shorter keyword that happens to precede it.
        /// </summary>
        [Theory]
        [InlineData("vad kan du göra", S.ai_feature_capabilities)]
        [InlineData("was kannst du", S.ai_feature_capabilities)]
        [InlineData("qué puedes hacer", S.ai_feature_capabilities)]
        [InlineData("cosa puoi fare", S.ai_feature_capabilities)]
        [InlineData("你能做什么", S.ai_feature_capabilities)]
        [InlineData("तुम क्या कर सकते हो", S.ai_feature_capabilities)]
        [InlineData("har du flashkort", S.ai_feature_flashcards)]
        [InlineData("enhetsomvandlare", S.ai_feature_unit_converter)]
        public void OtherLanguagesFindTheirFeatureToo(string query, string expected) =>
            Assert.Equal(expected, Ask(query));

        /// <summary>
        /// The two Android names in its own comment: a bare substring search found "unit" inside
        /// "opportunity" and "reaction" inside "interaction", and gave a tour of the app instead of
        /// answering the chemistry.
        /// </summary>
        [Theory]
        [InlineData("what is the opportunity cost of extracting lithium")]
        [InlineData("describe the interaction between sodium and water")]
        [InlineData("what is the density of gold")]
        [InlineData("balance h2 + o2 -> h2o")]
        [InlineData("")]
        [InlineData("   ")]
        public void ChemistryIsNotMistakenForTheAppTour(string query) => Assert.Null(Ask(query));

        /// <summary>
        /// The Han keywords cannot be matched on word boundaries - every neighbouring character is
        /// a word character, so the boundary test only ever succeeds on a query that *is* the
        /// keyword. TextMatching handles that; this pins it for a sentence, not a bare term.
        /// </summary>
        [Fact]
        public void ChineseMatchesInsideASentence() =>
            Assert.Equal(S.ai_feature_capabilities, Ask("请问你能做什么呢"));

        /// <summary>
        /// The match order is written down separately from the dictionary, so it can drift from it.
        /// A tenth feature arriving from Android must fail here rather than be silently unreachable.
        /// </summary>
        [Fact]
        public void EveryFeatureIsOrdered()
        {
            Assert.Equal(
                Lexicon.APP_FEATURE_WORDS.Keys.OrderBy(k => k, System.StringComparer.Ordinal),
                AppFeatures.Features.OrderBy(k => k, System.StringComparer.Ordinal));

            Assert.Equal(AppFeatures.Features.Count, AppFeatures.Features.Distinct().Count());
        }

        /// <summary>
        /// Order is the tiebreak, and this is the query that shows it: "help me" is a capabilities
        /// keyword, so a question naming a specific tool must still be answered about that tool.
        /// </summary>
        [Fact]
        public void ANamedToolBeatsTheGeneralCapabilitiesAnswer() =>
            Assert.Equal(S.ai_feature_reaction_balancer, Ask("help me with the reaction balancer"));
    }
}
