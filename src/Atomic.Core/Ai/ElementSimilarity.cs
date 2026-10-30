using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// "Which element is similar to gold" - the elements that share its group.
    ///
    /// <para>In <c>must_defer.tsv</c> ("which element is similar to carbon", "vilket grundämne
    /// liknar kol"), so the engine declines it by design: similarity is a judgement about where an
    /// element sits, not a field that can be looked up. Android answers it in
    /// <c>handleSimilarityQuery</c>; there was no Windows equivalent, so it reached the decline.</para>
    ///
    /// <para>Matched on group <i>number</i>, as Android does, rather than on the series. For gold
    /// that gives copper, silver and roentgenium - the column it belongs to - where the series
    /// would give every transition metal, which is true but useless.</para>
    /// </summary>
    public static class ElementSimilarity
    {
        /// <summary>Words that make a question about similarity, from Android's list.</summary>
        private static readonly string[] SimilarityWords =
        [
            "similar", "like", "related", "equivalent", "analogy",
            "liknar", "liknande", "ahnlich", "verwandt",
            "similaire", "proche", "pareil", "parecido",
            "somigliante", "simile", "simili", "semelhante", "igual",
            "समान", "जैसा", "मिलता-जुलता", "ek jaisa", "muqabla",
            "相似", "类似", "xiangsi", "leishi", "soortgelyk", "katulad", "pareho",
        ];

        /// <summary>Whether this asks what an element resembles.</summary>
        public static bool Asks(string? normalizedQuery) =>
            !string.IsNullOrWhiteSpace(normalizedQuery)
            && SimilarityWords.Any(w => TextMatching.ContainsWord(normalizedQuery, w));

        /// <summary>
        /// The answer for <paramref name="target"/>, or the "which element?" message when there is
        /// none in play.
        /// </summary>
        /// <param name="nameOf">Localizes an element's name for display.</param>
        public static string Answer(
            ElementRecord? target,
            IReadOnlyList<ElementRecord> all,
            IStringProvider strings,
            Func<ElementRecord, string> nameOf)
        {
            if (target is null) return strings.Get(S.ai_similarity_no_element);

            var name = nameOf(target);
            var series = strings.Get(target.Series.LabelKey());

            // No group number at all - the lanthanides and actinides, which sit outside the
            // numbered columns. The series is still a true thing to say about them.
            if (target.GroupNumber is not { } group)
                return strings.Get(S.ai_similarity_generic, name, series);

            var shared = all
                .Where(e => e.GroupNumber == group && !string.Equals(e.Key, target.Key, StringComparison.Ordinal))
                .OrderBy(e => e.AtomicNumber)
                .Select(nameOf)
                .ToList();

            if (shared.Count == 0) return strings.Get(S.ai_similarity_generic, name, series);

            return strings.Get(S.ai_similarity_result_header, name, series)
                   + string.Join(", ", shared)
                   + strings.Get(S.ai_similarity_result_footer);
        }
    }
}
