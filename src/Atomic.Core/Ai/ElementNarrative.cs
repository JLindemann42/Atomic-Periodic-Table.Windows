using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Text;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// The three questions answered from an element's prose description rather than from a field:
    /// what it is used for, what it does in the body, and where its name comes from.
    ///
    /// <para>The acceptance corpus requires the engine to decline all three - they are in
    /// <c>must_defer.tsv</c> ("what is the biological role of iron", "where does the name gold come
    /// from", "à quoi sert le titane") - because no stored field holds the answer. Declining is
    /// only correct if something downstream picks them up; on Android that is
    /// <c>handleUsageQuery</c>, <c>handleBiologicalQuery</c> and <c>handleEtymologyQuery</c>, and
    /// until now there was nothing here, so all three reached the decline.</para>
    ///
    /// <para>As with the app-feature answers, both halves were already present: the six strings are
    /// translated in all twelve languages, and <c>description</c> is populated for every element.
    /// Only the code between them was missing.</para>
    /// </summary>
    public static class ElementNarrative
    {
        /// <summary>Which of the three a question is asking.</summary>
        public enum Topic
        {
            Usage,
            Biological,
            Etymology,
        }

        // ---- Vocabulary, verbatim from Android ------------------------------------------------

        private static readonly string[] UsageWords =
        [
            "use", "uses", "used", "application", "applications", "where to find", "function",
            "utility", "industry", "medicine",
            "anvands", "anvandning", "anvandningar", "nytta", "funktion",
            "verwendung", "verwendungen", "nutzen", "industrie", "medizin",
            "utilisation", "utilisations", "usage", "usages", "medecine",
            "uso", "usos", "utilidad", "industria", "medicina",
            "usi", "impiego", "impieghi", "utilidade", "utilizacao", "utilizacoes",
            "उपयोग", "प्रयोग", "काम", "istimal", "استعمال", "استعمالات",
            "用途", "应用", "使用", "yongtu", "yingyong", "shiyong",
            "gebruik", "gebruike", "gamit", "paggamit", "mga gamit",
        ];

        private static readonly string[] BiologicalWords =
        [
            "body", "blood", "health", "toxic", "poison", "biological", "medicine", "essential", "diet", "nutrition",
            "kropp", "halsa", "giftig", "biologisk", "korper", "blut", "gesundheit", "medizin",
            "corps", "sang", "sante", "toxique", "biologique", "medecine", "essentiel",
            "cuerpo", "sangre", "salud", "toxico", "biologico", "medicina", "esencial",
            "corpo", "sangue", "salute", "tossico", "biologico", "essenziale",
            "saude", "essencial",
            "शरीर", "रक्त", "स्वास्थ्य", "जहरीला", "जैविक", "दवा",
            "身体", "血液", "健康", "毒", "生物", "医学", "shenti", "xueye", "jiankang", "du", "shengwu", "yixue",
            "jism", "khoon", "sehat", "zeher", "biologiese", "kalusugan", "katawan", "dugo",
        ];

        private static readonly string[] EtymologyWords =
        [
            "name", "called", "named", "origin", "etymology", "greek", "latin", "word", "meaning",
            "namn", "ursprung", "betydelse", "herkunft", "etymologie", "griechisch", "latein", "wort", "bedeutung",
            "nom", "origine", "grec", "mot", "signification",
            "nombre", "origen", "etimologia", "griego", "latin", "palabra", "significado",
            "nome", "etimologia", "greco", "latino", "parola", "significato",
            "origem", "grego", "latim", "palavra",
            "नाम", "उत्पत्ति", "व्युत्पत्ति", "यूनानी", "लैटिन", "शब्द", "अर्थ",
            "名字", "叫", "起源", "语源", "希腊", "拉丁", "词", "意思",
            "mingzi", "jiao", "qiyuan", "yuyuan", "xila", "lading", "ci", "yisi",
            "naam", "kaha jata", "asl", "itimaloji", "pangalan", "pinagmulan", "kahulugan",
        ];

        /// <summary>"What"/"how" words. Usage needs one, so "useful" alone is not a question.</summary>
        private static readonly string[] AsksWhatOrHow =
        [
            "what", "how", "vad", "hur", "was", "wie", "quel", "comment", "que", "como", "cosa", "come",
            "क्या", "कैसे", "什么", "怎么", "kya", "kaise", "wat", "ano", "paano",
        ];

        /// <summary>"Why"/"where" words. Etymology needs one, or every mention of a name matches.</summary>
        private static readonly string[] AsksWhyOrWhere =
        [
            "why", "how", "where", "hur", "varfor", "varifran", "warum", "wie", "wo", "pourquoi", "comment",
            "por que", "como", "perche", "porque", "onde",
            "क्यों", "कैसे", "为什么", "weishenme", "kyun", "kaise", "hoekom", "bakit", "paano",
        ];

        /// <summary>Sentences in the description that are about each topic.</summary>
        private static readonly string[] UsageSentenceWords =
        [
            "use", "industry", "medicine", "application", "important", "anvandning", "viktig",
        ];

        private static readonly string[] BiologicalSentenceWords =
        [
            "living", "organism", "role", "body", "blood", "health", "toxic", "essential",
        ];

        private static readonly string[] EtymologySentenceWords =
        [
            "name", "greek", "latin", "word", "from", "namn", "grek", "ord",
        ];

        /// <summary>
        /// English function words. Four or more of them make a sentence an English clause rather
        /// than a term, which is how a non-English answer avoids quoting the English description.
        /// </summary>
        private static readonly string[] EnglishFunctionWords =
        [
            "the", "and", "which", "with", "has", "have", "is", "are", "was", "were", "been", "of",
            "from", "that", "its", "it", "only", "also", "most", "used", "found", "other", "than",
            "but", "such", "these", "those", "there", "their", "when", "while", "into", "about",
        ];

        private const int EnglishClauseThreshold = 4;

        // ---- Detection --------------------------------------------------------------------------

        /// <summary>
        /// Which topic <paramref name="normalizedQuery"/> asks about, or null.
        /// </summary>
        /// <remarks>
        /// Biological needs no question word - "is zinc important for the body" has none and is in
        /// the corpus. The other two do, matching Android: without the gate, "gold is useful" and
        /// every sentence naming an element would be claimed.
        /// </remarks>
        public static Topic? Detect(string? normalizedQuery)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery)) return null;

            var q = normalizedQuery;

            if (Has(q, BiologicalWords)) return Topic.Biological;
            if (Has(q, EtymologyWords) && Mentions(q, AsksWhyOrWhere)) return Topic.Etymology;

            // A heading reads as a request without a question word: "Uses of gold",
            // "Verwendung von Gold", "%1$s के उपयोग". The app offers exactly that as a
            // suggestion chip in all twelve languages, and every one of them was declined - the
            // question-word gate exists to stop "gold is useful" being claimed, and a statement
            // does not open with the word.
            if (Has(q, UsageWords) && (Mentions(q, AsksWhatOrHow) || OpensWith(q, UsageWords)))
                return Topic.Usage;

            return null;
        }

        /// <summary>
        /// Exact, word-boundary matching - deliberately not <c>FuzzyMatch.HasKeyword</c>.
        /// </summary>
        /// <remarks>
        /// That helper falls back to Levenshtein within one or two edits, which over vocabulary
        /// this short and this multilingual claims the wrong questions: "what happens when sodium
        /// reacts with water" came back as a list of uses for sodium. A topic this broad needs the
        /// word to actually be there.
        /// </remarks>
        private static bool Has(string query, IReadOnlyList<string> words) =>
            words.Any(w => TextMatching.ContainsWord(query, w));

        /// <summary>
        /// Whether the query leads with one of <paramref name="words"/> - the heading shape,
        /// which asks for something without asking a question.
        /// </summary>
        /// <remarks>
        /// Hindi and Urdu put the possessor first ("%1$s के उपयोग"), so the word trails the
        /// element rather than opening the query. Those two are covered by the last token as well.
        /// </remarks>
        private static bool OpensWith(string query, IReadOnlyList<string> words)
        {
            var tokens = TextMatching.SplitQueryTokens(query)
                .Where(t => t.Trim().Length > 0).ToList();

            if (tokens.Count == 0) return false;

            // Chinese writes no spaces, so "金 的用途" is two tokens and the word is inside one
            // of them; the whole-query ends-with covers that, where token equality cannot.
            return words.Any(w =>
                string.Equals(tokens[0], w, StringComparison.Ordinal) ||
                string.Equals(tokens[^1], w, StringComparison.Ordinal) ||
                query.StartsWith(w, StringComparison.Ordinal) ||
                query.EndsWith(w, StringComparison.Ordinal));
        }

        private static bool Mentions(string query, IReadOnlyList<string> words) =>
            words.Any(w => query.Contains(w, StringComparison.Ordinal));

        // ---- Answering ----------------------------------------------------------------------------

        /// <summary>
        /// The answer, or null when there is no description to read.
        /// </summary>
        /// <param name="description">The element's <c>description</c> field.</param>
        /// <param name="elementName">Already localized for display.</param>
        /// <param name="language">The chat's language, which decides whether English sentences count.</param>
        public static string? Answer(
            Topic topic,
            string? description,
            string elementName,
            string language,
            IStringProvider strings,
            string? discoveredBy = null,
            string? year = null)
        {
            var sentences = InActiveLanguage(Split(description), language, fallBackToEnglish: true);

            var wanted = topic switch
            {
                Topic.Usage => UsageSentenceWords,
                Topic.Biological => BiologicalSentenceWords,
                Topic.Etymology => EtymologySentenceWords,
                _ => UsageSentenceWords,
            };

            var matched = sentences
                .Where(s => wanted.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matched.Count > 0)
            {
                var header = topic switch
                {
                    Topic.Usage => S.ai_usage_header,
                    Topic.Biological => S.ai_bio_role_header,
                    Topic.Etymology => S.ai_etymology_header,
                    _ => S.ai_usage_header,
                };

                return strings.Get(header, elementName)
                       + "\n\n"
                       + string.Join(". ", matched.Select(s => s.Trim())).Trim()
                       + ".";
            }

            // Nothing in the prose. Say so rather than answering a different question.
            return topic switch
            {
                Topic.Usage => strings.Get(S.ai_no_usage_data, elementName),
                Topic.Biological => strings.Get(S.ai_no_bio_data, elementName).TrimEnd().TrimEnd(':'),
                Topic.Etymology => strings.Get(
                    S.ai_no_etymology_data, elementName, Known(discoveredBy, strings), Known(year, strings)),
                _ => null,
            };
        }

        /// <summary>
        /// The description's sentences in the chat's language. Shared with
        /// <see cref="ElementOverview"/> so both use one definition of "this sentence is English".
        /// </summary>
        /// <param name="fallBackToEnglish">
        /// Whether to keep English sentences when nothing else is left. Right for an answer whose prose
        /// is the answer ("what is gold used for"); wrong for one that already answered without it.
        /// </param>
        public static List<string> SentencesFor(string? description, string language, bool fallBackToEnglish = true) =>
            InActiveLanguage(Split(description), language, fallBackToEnglish);

        /// <summary>
        /// Whether one sentence of a description is about <paramref name="topic"/> - for the rich
        /// overview, which sorts the description's sentences into its sections with the same
        /// vocabulary this class answers questions with.
        /// </summary>
        public static bool IsAbout(Topic topic, string sentence)
        {
            var q = TextMatching.NormalizeForLookup(sentence);
            return topic switch
            {
                Topic.Usage => Has(q, UsageWords),
                Topic.Biological => Has(q, BiologicalWords),
                Topic.Etymology => Has(q, EtymologyWords),
                _ => false,
            };
        }

        /// <summary>
        /// The sentences of a language's description that were actually translated.
        /// </summary>
        /// <remarks>
        /// <para>An untranslated sentence in a language file is not merely English - it is a verbatim
        /// copy of the English description's sentence. So it is recognised by comparison, exactly,
        /// rather than by counting English words: "In a pure form, it is a bright, slightly reddish
        /// yellow, dense, soft, malleable, and ductile metal" has only three of them and passed the word
        /// test in every language whose file had kept it. The word test still runs after, for English
        /// that was edited rather than copied.</para>
        /// <para>Nothing falls back to English. For English itself the English description is returned.</para>
        /// </remarks>
        public static List<string> TranslatedSentences(string? localized, string? english, string language)
        {
            if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return SplitWhole(english);

            var copies = new HashSet<string>(SplitWhole(english), StringComparer.Ordinal);
            return SplitWhole(localized)
                .Where(s => !copies.Contains(s) && !LooksEnglish(s))
                .ToList();
        }

        /// <summary>
        /// Sentences with their own punctuation kept, for text that is shown as written.
        /// </summary>
        /// <remarks>
        /// <see cref="Split"/> breaks at every full stop and drops it, which is fine for matching
        /// words but cut "half-lives range between 159,200 years and 4.5 billion years" at "4.", and
        /// never split Chinese at all - it ends sentences with "。", so a whole paragraph became one
        /// sentence and then gained a Latin full stop. This breaks only at a terminator followed by a
        /// space, or after a full-width one, and leaves each sentence its own ending.
        /// </remarks>
        private static List<string> SplitWhole(string? description) =>
            string.IsNullOrWhiteSpace(description)
                ? []
                : WholeSentence.Split(description.Trim())
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();

        private static readonly System.Text.RegularExpressions.Regex WholeSentence =
            Atomic.Core.Text.JavaRegex.Compile(@"(?<=[.!?])\s+|(?<=[\u3002\uFF01\uFF1F])");

        /// <summary>A value, or the shipped word for "unknown" - never a blank or a sentinel.</summary>
        private static string Known(string? value, IStringProvider strings) =>
            string.IsNullOrWhiteSpace(value) || value.Trim() == "---"
                ? strings.Get(S.unknown)
                : value.Trim();

        private static List<string> Split(string? description) =>
            string.IsNullOrWhiteSpace(description)
                ? []
                : description.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();

        /// <summary>
        /// Drop English sentences when the chat is not in English - but only if something is left,
        /// because a half-translated description is still better than no answer.
        /// </summary>
        private static List<string> InActiveLanguage(List<string> sentences, string language, bool fallBackToEnglish)
        {
            if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return sentences;

            var translated = sentences.Where(s => !LooksEnglish(s)).ToList();
            return translated.Count > 0 || !fallBackToEnglish ? translated : sentences;
        }

        private static bool LooksEnglish(string sentence)
        {
            var lower = sentence.ToLowerInvariant();
            var hits = 0;
            foreach (var word in EnglishFunctionWords)
            {
                if (TextMatching.ContainsWord(lower, word)) hits++;
                if (hits >= EnglishClauseThreshold) return true;
            }
            return false;
        }
    }
}
