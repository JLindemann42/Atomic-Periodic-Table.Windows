using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// Which of the app's own tools a question is about - "what can you do", "do you have
    /// flashcards", "vad kan du gora".
    ///
    /// <para>This is the one half of Android's <c>LocalKnowledgeManager</c> that the engine does not
    /// already cover. Its other seven resolvers - equations, constants, indicators, Poisson values,
    /// geology, ions and the dictionary - all read tables that <c>DatasetIndex</c> owns here, and
    /// <c>datasets.tsv</c> holds the engine to answering them. Questions about the app itself have
    /// no dataset behind them, so without this they reached the decline.</para>
    ///
    /// <para>Both halves were already ported and neither was reachable: the vocabulary has been
    /// sitting in <see cref="Lexicon.APP_FEATURE_WORDS"/> since P4 and the nine answers have been
    /// translated into all twelve languages since P2. Only the twenty lines joining them were
    /// missing.</para>
    /// </summary>
    public static class AppFeatures
    {
        /// <summary>
        /// The features in the order Android declares them, because the first match wins and some
        /// questions match two.
        ///
        /// <para><b>This list exists because <see cref="System.Collections.Frozen.FrozenDictionary{TKey,TValue}"/>
        /// does not promise enumeration order</b>, while the Kotlin <c>mapOf</c> it was generated
        /// from enumerates in declaration order. Iterating the dictionary directly would answer
        /// "help me balance a reaction" - which matches both <c>reaction_balancer</c> and the
        /// "help me" in <c>capabilities</c> - differently on the two platforms, and differently
        /// between .NET versions. <c>EveryFeatureIsOrdered</c> fails if Android adds a tenth.</para>
        /// </summary>
        private static readonly string[] Order =
        [
            "nuclide_table",
            "emission_spectrum",
            "flashcards",
            "achievements",
            "molar_mass",
            "unit_converter",
            "ideal_gas",
            "reaction_balancer",
            "capabilities",
        ];

        /// <summary>The answer each feature is described by, mirroring Android's FEATURE_STRINGS.</summary>
        private static readonly Dictionary<string, string> Answers = new(System.StringComparer.Ordinal)
        {
            ["nuclide_table"] = S.ai_feature_nuclide_table,
            ["emission_spectrum"] = S.ai_feature_emission_spectrum,
            ["flashcards"] = S.ai_feature_flashcards,
            ["achievements"] = S.ai_feature_achievements,
            ["molar_mass"] = S.ai_feature_molar_mass,
            ["unit_converter"] = S.ai_feature_unit_converter,
            ["ideal_gas"] = S.ai_feature_ideal_gas,
            ["reaction_balancer"] = S.ai_feature_reaction_balancer,
            ["capabilities"] = S.ai_feature_capabilities,
        };

        /// <summary>The features, in match order. Exposed for the ordering test.</summary>
        public static IReadOnlyList<string> Features => Order;

        /// <summary>
        /// The resource key answering <paramref name="normalizedQuery"/>, or null if the question is
        /// not about the app.
        /// </summary>
        /// <param name="normalizedQuery">
        /// Already through <see cref="TextMatching.NormalizeForLookup"/>. The keywords are
        /// diacritic-folded ASCII - "vad kan du gora", not "göra" - so they can only be matched
        /// against folded text. Android compares them against its own lightly-normalized string
        /// that folds nothing, which is why a Swedish user asking the full "vad kan du göra" only
        /// matches there by way of the shorter "vad kan du" also being in the list. Folding first
        /// matches everything Android matches and the accented spellings besides.
        /// </param>
        /// <remarks>
        /// Matched on word boundaries, not by <c>Contains</c>. Android's comment records what a bare
        /// substring search did: it found "unit" inside "opportunity" and "reaction" inside
        /// "interaction", and answered a chemistry question with a tour of the app.
        /// </remarks>
        public static string? Resolve(string normalizedQuery)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery)) return null;

            foreach (var feature in Order)
            {
                if (!Lexicon.APP_FEATURE_WORDS.TryGetValue(feature, out var keywords)) continue;
                if (!keywords.Any(k => TextMatching.ContainsWord(normalizedQuery, k))) continue;

                return Answers[feature];
            }

            return null;
        }
    }
}