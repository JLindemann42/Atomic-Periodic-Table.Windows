using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Retrieval
{
    /// <summary>
    /// Shared string-matching helpers.
    ///
    /// <para>These were private methods on <c>AIAgentManager</c> on Android. The behaviour is
    /// preserved exactly, because every intent predicate in the planner depends on it.</para>
    /// </summary>
    public static class TextMatching
    {
        /// <summary>
        /// Lowercase, and strip combining marks <b>only where they are diacritics on Latin
        /// letters</b>, so <c>Väte</c> and <c>vate</c> compare equal.
        ///
        /// <para>The script test is not a nicety. In Latin script a combining mark is an accent,
        /// and dropping it makes two spellings of the same word agree. In Devanagari it is a vowel
        /// - dropping it leaves a bare consonant skeleton, and distinct words collapse onto each
        /// other. Hindi gold सोना became सन and mercury पारा became पर, so the word for "atomic"
        /// (परमाणु → परमण) began with mercury's skeleton and every Hindi question about atomic
        /// number resolved two elements: mercury and whatever was actually being asked about.</para>
        /// </summary>
        public static string NormalizeForLookup(string text)
        {
            // German ß is a *letter*, not an accented s, so NFD leaves it alone and "außerdem"
            // never equalled the "ausserdem" the lexicon spells. Folding it here is the standard
            // German equivalence and is what the rest of the pipeline already assumes.
            var folded = text.ToLowerInvariant().Replace("ß", "ss", StringComparison.Ordinal);
            var nfd = folded.Normalize(NormalizationForm.FormD);

            var builder = new StringBuilder(nfd.Length);
            char? baseChar = null;
            foreach (var ch in nfd)
            {
                if (IsCombiningMark(ch))
                {
                    // Keep the mark unless it is decorating a Latin letter.
                    if (baseChar is not { } b || !IsLatinLetter(b))
                        builder.Append(ch);
                }
                else
                {
                    baseChar = ch;
                    builder.Append(ch);
                }
            }

            // Recompose. Stripping marks requires the decomposed form, but returning it is a trap
            // for every non-Latin script: Urdu آئسوٹوپ decomposes from seven characters to nine, so
            // it no longer equalled the lexicon entry spelled the way a user types it, and the
            // whole isotope vocabulary matched nothing. Latin has already lost its marks by this
            // point, so recomposition cannot undo the work above.
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool IsCombiningMark(char ch) =>
            CharUnicodeInfo.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark;

        private static bool IsLatinLetter(char ch) => char.IsLetter(ch) && UnicodeScripts.IsLatin(ch);

        /// <summary>
        /// Split on anything that is not a letter, a combining mark or a digit.
        ///
        /// <para><c>\p{M}</c> is load-bearing for Indic scripts. A Devanagari vowel sign is a mark,
        /// not a letter, so splitting on non-letters tore सोने into स and न - every Hindi word
        /// arrived at the matchers as a handful of bare consonants.</para>
        /// </summary>
        public static IReadOnlyList<string> SplitQueryTokens(string query) =>
            TokenSplit.Split(query)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

        /// <summary>
        /// Whether a query mentions a keyword, first by word boundary and then by edit distance.
        ///
        /// <para>The fuzzy pass only considers query words longer than three characters, and
        /// scales the tolerance with keyword length, so short symbols are never fuzzily matched.</para>
        /// </summary>
        public static bool HasKeyword(string query, IReadOnlyList<string> keywords)
        {
            var lowerQuery = query.ToLowerInvariant();

            foreach (var keyword in keywords)
            {
                if (ContainsWord(lowerQuery, keyword)) return true;
            }

            var words = WordSplit.Split(lowerQuery).Where(w => w.Length > 3);
            foreach (var word in words)
            {
                foreach (var keyword in keywords)
                {
                    var threshold = keyword.Length switch
                    {
                        > 8 => 2,
                        > 3 => 1,
                        _ => 0,
                    };
                    foreach (var part in keyword.Split(' '))
                    {
                        if (part.Length > 3 && Levenshtein(word, part) <= threshold) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Whether a token appears in the query as a whole word, in either spelling.
        ///
        /// <para>The two spellings matter because normalisation folds Latin diacritics but leaves
        /// every other script alone, so a token may be written one way and stored the other.</para>
        ///
        /// <para>Boundary handling is delegated wholesale to <see cref="ContainsWord"/>, which
        /// knows that Han needs containment and everything else needs boundaries. The earlier rule
        /// keyed off the token being <i>ASCII-like</i> and fell back to bare containment for
        /// anything else, which quietly covered three scripts that do write spaces: Portuguese
        /// "cúrio" (curium) is a substring of "mercúrio", Hindi रेनियम (rhenium) of यूरेनियम,
        /// Urdu رینیم of یورینیم. Every query naming one of those elements resolved two, and a
        /// plain lookup was answered as a comparison.</para>
        /// </summary>
        public static bool ContainsToken(string rawQuery, string normalizedQuery, string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            return ContainsWord(rawQuery, token) || ContainsWord(normalizedQuery, token);
        }

        /// <summary>
        /// Whether <paramref name="needle"/> occurs in <paramref name="haystack"/> delimited by
        /// non-word characters on both sides.
        ///
        /// <para>Deliberately not a regex. Java's <c>\b</c> is defined over
        /// <c>[a-zA-Z0-9_]</c>, so a plain <c>\b</c> treats every accented letter as a boundary -
        /// <c>\bsm\b</c> matches inside the Swedish "smältpunkten" and <c>\bf\b</c> inside "för",
        /// which made samarium and fluorine appear in a query that named neither. On Android the
        /// obvious fix, the inline <c>(?U)</c> flag, works on the JVM but crashes on ICU's engine,
        /// so unit tests passed while the app died. Scanning by hand is portable, allocation-free
        /// and correct for every script.</para>
        /// </summary>
        public static bool ContainsWord(string haystack, string needle)
        {
            if (needle.Length == 0 || needle.Length > haystack.Length) return false;

            // Han writes no spaces, so every neighbouring character is a "word character" and the
            // boundary test can only ever succeed on a query that *is* the needle. That is why the
            // whole Chinese lexicon was unreachable in a sentence: 谁 in 谁发现了氦 has 发 after it,
            // so "who" was never recognised, and the same for 最, 族 and every degree word.
            if (IsHanText(needle)) return haystack.Contains(needle, StringComparison.Ordinal);

            var index = haystack.IndexOf(needle, StringComparison.Ordinal);
            while (index >= 0)
            {
                var before = CharAt(haystack, index - 1);
                var after = CharAt(haystack, index + needle.Length);
                if (!IsWordChar(before) && !IsWordChar(after)) return true;
                index = haystack.IndexOf(needle, index + 1, StringComparison.Ordinal);
            }
            return false;
        }

        private static char? CharAt(string text, int index) =>
            index >= 0 && index < text.Length ? text[index] : null;

        /// <summary>
        /// Unicode-aware: a letter, digit or combining mark in any script, plus the underscore.
        ///
        /// <para>The mark is load-bearing, and for the same reason it is in
        /// <see cref="SplitQueryTokens"/>: an Indic vowel sign is not a letter, so treating it as a
        /// boundary made every matra a word break. Hindi रेनियम (rhenium) sits inside यूरेनियम
        /// (uranium) preceded by ू - a mark - so rhenium was found in every Hindi question about
        /// uranium, and the lookup became a comparison.</para>
        /// </summary>
        private static bool IsWordChar(char? c) =>
            c is { } ch && (char.IsLetterOrDigit(ch) || ch == '_' || IsCombiningMark(ch));

        /// <summary>True when every letter of the text is Han, so word boundaries do not apply.</summary>
        public static bool IsHanText(string text)
        {
            var sawHan = false;
            foreach (var ch in text)
            {
                if (!char.IsLetter(ch)) continue;
                if (!UnicodeScripts.IsHan(ch)) return false;
                sawHan = true;
            }
            return sawHan;
        }

        /// <summary>Levenshtein edit distance.</summary>
        public static int Levenshtein(string s1, string s2)
        {
            // Two rolling rows rather than the full matrix. The fuzzy resolver runs this against
            // every alias in twelve languages for every unmatched query, so the allocation matters
            // more than the elegance; the recurrence is unchanged.
            var previous = new int[s2.Length + 1];
            var current = new int[s2.Length + 1];

            for (var j = 0; j <= s2.Length; j++) previous[j] = j;

            for (var i = 1; i <= s1.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= s2.Length; j++)
                {
                    var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(previous[j] + 1, current[j - 1] + 1),
                        previous[j - 1] + cost);
                }
                (previous, current) = (current, previous);
            }

            return previous[s2.Length];
        }

        /// <summary>Similarity in 0..1 derived from edit distance, for ranking fuzzy candidates.</summary>
        public static double Similarity(string a, string b)
        {
            var longest = Math.Max(a.Length, b.Length);
            if (longest == 0) return 1.0;
            return 1.0 - (double)Levenshtein(a, b) / longest;
        }

        /// <summary>Non-letter, non-mark, non-digit runs. Compiled through the Java shim.</summary>
        private static readonly System.Text.RegularExpressions.Regex TokenSplit =
            JavaRegex.Compile("[^\\p{L}\\p{M}0-9]+");

        /// <summary>As above but without marks, matching Android's <c>hasKeyword</c> exactly.</summary>
        private static readonly System.Text.RegularExpressions.Regex WordSplit =
            JavaRegex.Compile("[^\\p{L}0-9]+");
    }
}
