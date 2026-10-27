using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Atomic.Core.Ai.Retrieval;

namespace Atomic.Core.Ai.Nlu
{
    /// <summary>
    /// Which language a message is written in, from its character trigrams (roadmap 1.4).
    ///
    /// <para>Both platforms used to judge by element names alone, so a question naming no element
    /// carried no signal: "hur många ädelgaser finns det" was answered in English. This is a naive
    /// Bayes model over the trigrams of each word, trained by Android's
    /// <c>LanguageModelGoldenTest</c> on the app strings, the per-language lexicon, the element
    /// descriptions and the dataset titles, and shipped as counts in
    /// <c>shared/language/trigrams.tsv</c>. Mirrors Android's <c>LanguageDetector.kt</c>; the
    /// features must be extracted identically, or the same counts mean something else.</para>
    ///
    /// <para>Element names stay as the tie-breaker, and as the whole answer for a message too short
    /// to judge by its letters: "guld" is Swedish because only Swedish calls gold that, and "gold"
    /// after a German turn stays German because German calls it that too.</para>
    /// </summary>
    public sealed class LanguageDetector
    {
        /// <summary>Below this many words the letters are too few to judge by, and names decide.</summary>
        public const int MinWords = 2;

        /// <summary>Nats within which the two best languages are a tie, broken by element names.</summary>
        public const double TieMargin = 1.0;

        /// <summary>
        /// Nats by which a language must beat the one in play to take over. This and
        /// <see cref="WordClip"/> were measured on Android's dev corpus: from 12 to 14 here and 14 to
        /// 16 there, no row is switched away from its language and every <c>language.tsv</c> row
        /// passes; these sit in the middle.
        /// </summary>
        public const double SwitchMargin = 13.0;

        /// <summary>The most a language can lose on one word.</summary>
        public const double WordClip = 16.0;

        /// <summary>The model file's name for a language's total.</summary>
        public const string Total = "*";

        private readonly IReadOnlyDictionary<string, Dictionary<string, int>> _counts;
        private readonly HashSet<string> _vocabulary;
        private readonly Dictionary<string, double> _denominators;

        /// <param name="counts">Trigram counts per language, a space written as a space.</param>
        /// <param name="totals">Every trigram the language was trained on, kept or not.</param>
        public LanguageDetector(IReadOnlyDictionary<string, Dictionary<string, int>> counts,
            IReadOnlyDictionary<string, int> totals)
        {
            _counts = counts;
            _vocabulary = counts.Values.SelectMany(c => c.Keys).ToHashSet(StringComparer.Ordinal);
            Languages = counts.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            _denominators = Languages.ToDictionary(l => l,
                l => Math.Log((totals.TryGetValue(l, out var t) ? t : 0) + (double)_vocabulary.Count),
                StringComparer.Ordinal);
        }

        public IReadOnlyList<string> Languages { get; }

        /// <summary>
        /// How well each language explains <paramref name="message"/>, 0 for the best, over the
        /// trigrams some language knows. Empty when it has none.
        ///
        /// <para>Each word votes, and no word decides: under every language a word scores its
        /// log-likelihood less the best language's, floored at -<see cref="WordClip"/>. One long word
        /// whose letters happen to look German otherwise outvoted every short Spanish word around
        /// it.</para>
        /// </summary>
        public IReadOnlyDictionary<string, double> Scores(string message)
        {
            var total = new Dictionary<string, double>(StringComparer.Ordinal);
            var anyKnown = false;
            foreach (var word in Words(message))
            {
                var known = word.Where(_vocabulary.Contains).ToList();
                if (known.Count == 0) continue;
                anyKnown = true;
                var likelihood = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var lang in Languages)
                {
                    var table = _counts[lang];
                    var denominator = _denominators[lang];
                    var sum = 0.0;
                    foreach (var t in known) sum += Math.Log((table.TryGetValue(t, out var c) ? c : 0) + 1.0) - denominator;
                    likelihood[lang] = sum;
                }
                var best = likelihood.Values.Max();
                foreach (var (lang, ll) in likelihood)
                    total[lang] = (total.TryGetValue(lang, out var running) ? running : 0.0) + Math.Max(ll - best, -WordClip);
            }
            return anyKnown ? total : new Dictionary<string, double>();
        }

        /// <summary>
        /// The language to answer <paramref name="message"/> in.
        ///
        /// <para>A tie keeps the language in play: switching on weak evidence is worse than not
        /// switching. So a message of one word is judged by its element names, and a longer one
        /// switches only when the best language beats <paramref name="active"/> by
        /// <see cref="SwitchMargin"/>.</para>
        /// </summary>
        /// <param name="byElementNames">The element-name detector, <c>EntityResolver.DetectLanguage</c>.</param>
        public string Detect(string message, string active, Func<string, string, string> byElementNames)
        {
            if (WordCount(message) < MinWords) return byElementNames(message, active);
            var scores = Scores(message);
            if (scores.Count == 0) return byElementNames(message, active);

            var ranked = scores.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
            var choice = ranked[0].Key;
            // Close languages can score within a hair of each other on a short question. A name only
            // one of them uses settles it.
            if (ranked.Count > 1 && ranked[0].Value - ranked[1].Value < TieMargin)
            {
                var named = byElementNames(message, active);
                if (named == ranked[1].Key) choice = named;
            }

            if (SameLanguage(choice, active)) return active;
            if (!scores.TryGetValue(Bare(active), out var activeScore)) return choice;
            return scores[choice] - activeScore >= SwitchMargin ? choice : active;
        }

        /// <summary>The trigrams of every word of <paramref name="text"/>; see <see cref="Words"/>.</summary>
        public static IReadOnlyList<string> Features(string text) => Words(text).SelectMany(w => w).ToList();

        /// <summary>
        /// The trigrams of each word, lowercased and NFKC-normalised, padded with a space at each
        /// end: "vad är" is <c> va</c>, <c>vad</c>, <c>ad </c>, <c> är</c>, <c>är </c>. Anything that
        /// is not a letter or a combining mark separates words. Accents are kept.
        ///
        /// <para>Three exceptions. A chemical formula is no language's words ("H2O" would otherwise
        /// be "h" and "o", and "o" is Portuguese); nor is a unit name from
        /// <see cref="Lexicon.UNIT_WORDS"/>, which every language borrows; and a Han character is a
        /// word of its own, since Chinese writes no spaces.</para>
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<string>> Words(string text)
        {
            var output = new List<IReadOnlyList<string>>();
            var word = new StringBuilder();
            void Flush()
            {
                if (word.Length == 0) return;
                var padded = " " + word + " ";
                if (!Units.Contains(word.ToString()))
                    output.Add(Enumerable.Range(0, padded.Length - 2).Select(i => padded.Substring(i, 3)).ToList());
                word.Clear();
            }
            foreach (var c in WithoutFormulas(text.Normalize(NormalizationForm.FormKC)))
            {
                if (Tokenizer.IsHan(c)) { Flush(); word.Append(c); Flush(); }
                else if (char.IsLetter(c) || IsMark(c)) word.Append(char.ToLowerInvariant(c));
                else Flush();
            }
            Flush();
            return output;
        }

        /// <summary>Words in <paramref name="text"/>, formulas aside; a Han character counts as one.</summary>
        public static int WordCount(string text)
        {
            var words = 0;
            var inWord = false;
            foreach (var c in WithoutFormulas(text.Normalize(NormalizationForm.FormKC)))
            {
                if (Tokenizer.IsHan(c)) { words++; inWord = false; }
                else if (char.IsLetter(c) || IsMark(c)) { if (!inWord) words++; inWord = true; }
                else inWord = false;
            }
            return words;
        }

        /// <summary>
        /// Reads the model file: <c>lang</c>, trigram, count, with a space written <c>_</c> and the
        /// trigram <c>*</c> holding the language's total. Null when there is no model, so the caller
        /// keeps element names alone.
        /// </summary>
        public static LanguageDetector? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var line in text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') ||
                    line.StartsWith("lang\t", StringComparison.Ordinal)) continue;
                var cells = line.TrimEnd('\r').Split('\t');
                if (cells.Length < 3 || !int.TryParse(cells[2], NumberStyles.None, CultureInfo.InvariantCulture, out var count)) continue;
                if (cells[1] == Total) totals[cells[0]] = count;
                else
                {
                    if (!counts.TryGetValue(cells[0], out var table)) counts[cells[0]] = table = new Dictionary<string, int>(StringComparer.Ordinal);
                    table[cells[1].Replace('_', ' ')] = count;
                }
            }
            return counts.Count == 0 ? null : new LanguageDetector(counts, totals);
        }

        /// <summary>
        /// <paramref name="text"/> with chemical formulas blanked: a run of element symbols, brackets
        /// and counts with a digit or two capitals in it. A lone capital stays.
        /// </summary>
        private static string WithoutFormulas(string text) => Formula.Replace(text, m =>
            m.Value.Any(char.IsAsciiDigit) || m.Value.Count(char.IsAsciiLetterUpper) >= 2
                ? new string(' ', m.Value.Length)
                : m.Value);

        /// <summary><c>[0-9]</c>, not <c>\d</c>: .NET's <c>\d</c> is every Unicode digit.</summary>
        private static readonly Regex Formula = new(
            @"(?<![\p{L}\p{N}])(?:[A-Z][a-z]?[0-9]*|[()][0-9]*)+(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);

        /// <summary>The unit names that are one word, which carry no evidence of a language.</summary>
        private static readonly HashSet<string> Units =
            Lexicon.UNIT_WORDS.Keys.Where(k => !k.Contains(' ', StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);

        private static bool IsMark(char c) => CharUnicodeInfo.GetUnicodeCategory(c) is
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

        private static string Bare(string language)
        {
            var dash = language.IndexOf('-', StringComparison.Ordinal);
            return dash > 0 ? language[..dash] : language;
        }

        private static bool SameLanguage(string a, string b) =>
            string.Equals(Bare(a), Bare(b), StringComparison.OrdinalIgnoreCase);
    }
}
