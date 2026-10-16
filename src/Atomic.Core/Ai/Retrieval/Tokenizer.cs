using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Retrieval
{
    /// <summary>
    /// Splits text into index terms for all twelve supported languages.
    /// Ported from <c>ai/retrieval/Tokenizer.kt</c>.
    ///
    /// <para>The same entry point is used for indexing and for querying. That symmetry is not
    /// optional: any transformation applied to one side and not the other silently destroys
    /// recall.</para>
    ///
    /// <para>Notable per-script handling:</para>
    /// <list type="bullet">
    /// <item><b>Han (zh)</b> has no spaces, so a whitespace split yields nothing useful. Emitting
    /// every character <i>and</i> every adjacent bigram is the standard dictionary-free CJK
    /// retrieval technique. BM25's IDF then does the discrimination for free: 金 ("gold") appears
    /// inside 金属 ("metal") all over the corpus and sinks toward zero weight, while 金属 itself
    /// stays informative.</item>
    /// <item><b>Arabic script (ur)</b> is written with optional diacritics and several visually
    /// identical letter variants; normalising them is what makes a typed query match authored
    /// text.</item>
    /// <item><b>Latin</b> gets one light, language-agnostic suffix fold applied to both sides, so
    /// it can only ever help recall even where it is linguistically wrong.</item>
    /// </list>
    ///
    /// <para>There is deliberately no stopword list. BM25 already discounts terms that appear in
    /// most documents, which is more robust than a hand-maintained list in twelve languages.</para>
    ///
    /// <para>Output is pinned by <c>tests/golden/tokens.tsv</c>, dumped from the JVM. Any change
    /// here that alters a single token will fail that test, which is the point.</para>
    /// </summary>
    public static class Tokenizer
    {
        /// <summary>Suffixes folded off longer Latin-script tokens. Identical at index and query time.</summary>
        private static readonly string[] LatinSuffixes = ["es", "en", "er", "s", "e"];

        /// <summary>Arabic-script letters that vary by orthography but should match each other.</summary>
        private static readonly Dictionary<char, char> ArabicFold = new()
        {
            ['ي'] = 'ی', // ي -> ی
            ['ك'] = 'ک', // ك -> ک
            ['ه'] = 'ہ', // ه -> ہ
            ['أ'] = 'ا', // أ -> ا
            ['إ'] = 'ا', // إ -> ا
            ['آ'] = 'ا', // آ -> ا
            ['ٱ'] = 'ا', // ٱ -> ا
        };

        /// <summary>Arabic diacritics and the zero-width non-joiner, all stripped.</summary>
        private static bool IsArabicDiacritic(char c) =>
            c is >= 'ً' and <= 'ْ' || c is 'ٰ' or '‌' or 'ـ';

        /// <param name="language">
        /// ISO code. Only affects Latin folding, since scripts are detected per character.
        /// </param>
        public static IReadOnlyList<string> Tokenize(string text, string language = "en")
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return tokens;

            var buffer = new StringBuilder();
            var hanRun = new StringBuilder();

            foreach (var raw in Normalize(text))
            {
                if (UnicodeScripts.IsHan(raw))
                {
                    FlushLatin(buffer, language, tokens);
                    hanRun.Append(raw);
                }
                // Combining marks belong to the token they follow. Devanagari vowel signs are
                // marks rather than letters, so treating them as delimiters would split घनत्व
                // into three fragments.
                else if (char.IsLetterOrDigit(raw) || IsCombiningMark(raw))
                {
                    FlushHan(hanRun, tokens);
                    buffer.Append(raw);
                }
                else
                {
                    FlushHan(hanRun, tokens);
                    FlushLatin(buffer, language, tokens);
                }
            }

            FlushHan(hanRun, tokens);
            FlushLatin(buffer, language, tokens);
            return tokens;
        }

        private static void FlushLatin(StringBuilder buffer, string language, List<string> tokens)
        {
            if (buffer.Length == 0) return;
            EmitAlphabetic(buffer.ToString(), language, tokens);
            buffer.Clear();
        }

        /// <summary>
        /// Han text yields both unigrams and adjacent bigrams.
        /// 过渡金属 becomes 过, 渡, 金, 属, 过渡, 渡金, 金属.
        /// </summary>
        private static void FlushHan(StringBuilder hanRun, List<string> tokens)
        {
            if (hanRun.Length == 0) return;
            var run = hanRun.ToString();
            foreach (var c in run)
                tokens.Add(c.ToString());
            for (var i = 0; i < run.Length - 1; i++)
                tokens.Add(run.Substring(i, 2));
            hanRun.Clear();
        }

        private static void EmitAlphabetic(string token, string language, List<string> into)
        {
            if (token.Length == 0) return;
            var cleaned = IsArabicScript(token) ? FoldArabic(token) : token;
            if (cleaned.Length == 0) return;

            into.Add(cleaned);
            var folded = FoldLatinSuffix(cleaned, language);
            if (!string.Equals(folded, cleaned, StringComparison.Ordinal))
                into.Add(folded);
        }

        /// <summary>
        /// NFKC, lowercase, and fold accents.
        ///
        /// <para>Combining marks are dropped <b>only after Latin base letters</b>, so é folds to e
        /// while Devanagari vowel signs survive: a matra is not optional decoration, and stripping
        /// it turns घनत्व into a different word. Arabic harakat are dropped regardless, because
        /// they genuinely are optional in Urdu orthography and users rarely type them.</para>
        /// </summary>
        public static string Normalize(string text)
        {
            // ToLowerInvariant, never ToLower: under tr-TR the latter maps I to a dotless ı and
            // every ASCII lookup key in the engine stops matching.
            var nfkc = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            var decomposed = nfkc.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder(decomposed.Length);
            var lastBaseWasLatin = false;

            foreach (var c in decomposed)
            {
                if (IsArabicDiacritic(c))
                    continue;

                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    if (lastBaseWasLatin)
                        continue;
                    sb.Append(c);
                    continue;
                }

                lastBaseWasLatin = UnicodeScripts.IsLatin(c);
                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Fold one plural or inflection suffix off tokens longer than five characters.
        /// Applied to index and query alike, so it never costs recall.
        /// </summary>
        public static string FoldLatinSuffix(string token, string language = "en")
        {
            if (token.Length <= 5) return token;

            foreach (var c in token)
                if (char.IsDigit(c))
                    return token;

            foreach (var suffix in LatinSuffixes)
            {
                if (token.Length - suffix.Length >= 4 &&
                    token.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return token[..^suffix.Length];
                }
            }
            return token;
        }

        private static string FoldArabic(string token)
        {
            var sb = new StringBuilder(token.Length);
            foreach (var c in token)
            {
                if (IsArabicDiacritic(c)) continue;
                sb.Append(ArabicFold.TryGetValue(c, out var folded) ? folded : c);
            }
            return sb.ToString();
        }

        public static bool IsHan(char c) => UnicodeScripts.IsHan(c);

        /// <summary>Non-spacing, spacing-combining and enclosing marks all attach to a base letter.</summary>
        private static bool IsCombiningMark(char c) =>
            CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark;

        private static bool IsArabicScript(string token)
        {
            foreach (var c in token)
                if (UnicodeScripts.IsArabic(c))
                    return true;
            return false;
        }

        /// <summary>True when the text is written in a script with no word spacing.</summary>
        public static bool IsUnspacedScript(string text)
        {
            foreach (var c in text)
                if (IsHan(c))
                    return true;
            return false;
        }
    }
}
