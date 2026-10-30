using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Atomic.Core.Ai.Retrieval;

namespace Atomic.Core.Text
{
    /// <summary>
    /// The three helpers Android's <c>AIAgentManagerTest</c> asserts through reflection.
    ///
    /// <para>That file is not ported (D1), but its test is the only coverage these three behaviours
    /// have, so they are lifted here as public functions and the test comes with them. Two of the
    /// three delegate to <see cref="TextMatching"/>, which already owns the matching rules - a
    /// second copy is precisely how the boundary bugs recorded there got in.</para>
    /// </summary>
    public static class FuzzyMatch
    {
        /// <summary>Levenshtein edit distance.</summary>
        public static int Levenshtein(string a, string b) => TextMatching.Levenshtein(a, b);

        /// <summary>
        /// Whether a query mentions a keyword, first by word boundary and then by edit distance.
        /// </summary>
        public static bool HasKeyword(string query, IReadOnlyList<string> keywords) =>
            TextMatching.HasKeyword(query, keywords);

        /// <summary>
        /// Whether a reply means "yes, go on".
        ///
        /// <para>Used for a bare acknowledgement of a suggested follow-up - the user taps a chip or
        /// types "sure", and there is no question to plan from. Matched on word boundaries through
        /// <see cref="TextMatching.ContainsWord"/>, because a plain containment check reads the
        /// "ja" inside "japan" and the "ha" inside "hafnium".</para>
        /// </summary>
        public static bool IsAffirmative(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return false;

            var lower = query.Trim().ToLowerInvariant();
            var normalized = TextMatching.NormalizeForLookup(query);

            foreach (var word in Affirmatives)
            {
                if (TextMatching.ContainsToken(lower, normalized, word)) return true;
            }
            return false;
        }

        /// <summary>
        /// "Yes" and "tell me more", in the twelve shipped languages.
        ///
        /// <para>Folded, like every other vocabulary compared against normalised text - "sí" is
        /// written "si", "gärna" is "garna". Android's list carries the accented spellings and
        /// compares them against a lowercased-but-unfolded query, which is a narrower version of the
        /// D13 defect: those entries match only when the user types the accent.</para>
        /// </summary>
        private static readonly FrozenSet<string> Affirmatives = new[]
        {
            // English
            "yes", "sure", "ok", "okay", "yep", "yeah", "absolutely", "please", "do it", "more",
            "next", "tell me more", "something else", "anything else", "other", "another",
            // Swedish
            "ja", "visst", "okej", "garna", "absolut", "snalla", "gor det", "mer", "nasta",
            "beratta mer", "annat", "nagot annat", "nat annat", "vidare",
            // German
            "jawohl", "sicher", "gerne", "bitte", "mehr", "erzahl mir mehr", "erzahlen sie mir mehr",
            // French
            "oui", "bien sur", "d'accord", "s'il vous plait", "plus", "dis-m'en plus", "dites-m'en plus",
            // Spanish
            "si", "claro", "por supuesto", "por favor", "mas", "cuentame mas", "cuenteme mas",
            // Portuguese
            "sim", "com certeza", "mais", "conta-me mais", "me conte mais",
            // Hindi / Urdu
            "हाँ", "ज़रूर", "ठीक है", "ji", "ha", "zaroor", "thik hai", "aur", "zhada",
            "mujhe aur batao", "mujhe aur batayein", "mazeed", "mazeed batayein",
            // Chinese
            "是", "是的", "当然", "好的", "shi", "shide", "dangran", "haode", "geng duo", "jixu",
            "更多", "继续", "告诉我更多", "多跟我说说",
            // Filipino
            "oo", "sige", "oo naman", "walang anuman", "pa", "dagdag", "magkwento ka pa", "sabihin mo pa",
            // Italian
            "ancora", "di piu", "altro", "continua", "dimmi di piu", "mi dica di piu",
            // Afrikaans
            "vertel my meer", "sekerlik", "asseblief",
        }.ToFrozenSet(StringComparer.Ordinal);
    }
}
