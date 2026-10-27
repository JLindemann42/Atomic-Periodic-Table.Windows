using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Nlu
{
    /// <summary>
    /// Corrects a misspelled word to the one vocabulary word within an edit or two (roadmap 1.6).
    /// Mirrors Android's <c>Speller.kt</c>.
    ///
    /// <para>Fuzzy matching used to cover element names only, and only for a query of one or two
    /// words, so a typo in a property, a family or an element inside a sentence declined the
    /// question. The planner asks this only when a question would otherwise be declined for want of
    /// a match, and keeps the correction only when the corrected question is answered from the
    /// element data.</para>
    ///
    /// <para>A word is corrected only when it is a Latin-script word of four letters or more the
    /// vocabulary does not know and that is not a stopword, a "yes, go on" word, a conversational
    /// word (<see cref="Lexicon.NEVER_CORRECTED"/>) or a word asking for the personality layer;
    /// when the nearest vocabulary words are within one edit, or two for a word of eight letters or
    /// more, a transposition counting as one; and when they all mean the same thing.</para>
    ///
    /// <para>SymSpell finds its candidates through an index of deletions, buying speed with memory.
    /// This runs only on a decline, over a few thousand words, so it measures every candidate of a
    /// plausible length instead.</para>
    /// </summary>
    public sealed class Speller
    {
        /// <summary>Shorter words are left alone: "mas", "oro" and "fer" are each one edit from a dozen words.</summary>
        public const int MinLength = 4;

        /// <summary>From this length a word may be two edits from its correction.</summary>
        public const int LongWord = 8;

        private static readonly Regex Word = new(@"\p{L}+", RegexOptions.CultureInvariant);

        private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _senses;
        private readonly HashSet<string> _protected;
        private readonly Dictionary<int, List<string>> _byLength;

        /// <param name="senses">Each vocabulary word, folded, and what it means: an element, a field, an operator.</param>
        /// <param name="known">Further words that are never corrected.</param>
        public Speller(IReadOnlyDictionary<string, IReadOnlySet<string>> senses, IEnumerable<string> known)
        {
            _senses = senses;
            _protected = known.Concat(senses.Keys).ToHashSet(StringComparer.Ordinal);
            _byLength = senses.Keys.GroupBy(k => k.Length).ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary><paramref name="query"/> with each misspelled word replaced by its correction, or the query itself.</summary>
        public string Correct(string query) => Word.Replace(query, m => Correction(m.Value) ?? m.Value);

        private string? Correction(string word)
        {
            var folded = TextMatching.NormalizeForLookup(word);
            if (folded.Length < MinLength || !folded.All(IsLatinLetter)) return null;
            if (_protected.Contains(folded) || FuzzyMatch.IsAffirmative(folded)) return null;
            var maxEdits = folded.Length >= LongWord ? 2 : 1;

            var best = int.MaxValue;
            var nearest = new List<string>();
            for (var length = folded.Length - maxEdits; length <= folded.Length + maxEdits; length++)
            {
                if (!_byLength.TryGetValue(length, out var candidates)) continue;
                foreach (var candidate in candidates)
                {
                    var distance = Distance(folded, candidate, maxEdits);
                    if (distance > maxEdits || distance > best) continue;
                    if (distance < best) { best = distance; nearest.Clear(); }
                    nearest.Add(candidate);
                }
            }
            if (nearest.Count == 0) return null;
            // One meaning, however many spellings of it are this close.
            IEnumerable<string> shared = _senses[nearest[0]];
            foreach (var candidate in nearest.Skip(1)) shared = shared.Intersect(_senses[candidate], StringComparer.Ordinal);
            return shared.Any() ? nearest.Min(StringComparer.Ordinal) : null;
        }

        private static bool IsLatinLetter(char c) => c is >= 'a' and <= 'z' || UnicodeScripts.IsLatin(c);

        /// <summary>
        /// Optimal string alignment distance - Levenshtein with a transposition as one edit - or
        /// <paramref name="limit"/> + 1 as soon as it is certain to exceed it.
        /// </summary>
        public static int Distance(string a, string b, int limit)
        {
            if (Math.Abs(a.Length - b.Length) > limit) return limit + 1;
            var rows = new int[a.Length + 1, b.Length + 1];
            for (var i = 0; i <= a.Length; i++) rows[i, 0] = i;
            for (var j = 0; j <= b.Length; j++) rows[0, j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                var rowMin = int.MaxValue;
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    var d = Math.Min(Math.Min(rows[i - 1, j] + 1, rows[i, j - 1] + 1), rows[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d = Math.Min(d, rows[i - 2, j - 2] + 1);
                    rows[i, j] = d;
                    rowMin = Math.Min(rowMin, d);
                }
                if (rowMin > limit) return limit + 1;
            }
            return rows[a.Length, b.Length];
        }

        /// <summary>
        /// The speller for a planner: element names, the words of every field alias, and the
        /// lexicon's operator words.
        /// </summary>
        public static Speller Build(IReadOnlyDictionary<string, string> elementNames,
            IReadOnlyDictionary<string, HashSet<string>> fieldWords)
        {
            var senses = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            void Add(string word, string sense)
            {
                if (word.Length < MinLength || !word.All(IsLatinLetter)) return;
                if (!senses.TryGetValue(word, out var set)) senses[word] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(sense);
            }
            void AddPhrases(IEnumerable<string> phrases, string sense)
            {
                foreach (var phrase in phrases)
                    foreach (var word in phrase.Split(' ')) Add(word, sense);
            }
            foreach (var (name, key) in elementNames) Add(name, "element:" + key);
            foreach (var (word, ids) in fieldWords)
                foreach (var id in ids) Add(word, "field:" + id);
            AddPhrases(Lexicon.MOST, "most");
            AddPhrases(Lexicon.LEAST, "least");
            AddPhrases(Lexicon.AVERAGE, "average");
            AddPhrases(Lexicon.MEDIAN, "median");
            AddPhrases(Lexicon.SUM, "sum");
            AddPhrases(Lexicon.COUNT, "count");
            AddPhrases(Lexicon.METAL_WORDS, "metal");
            foreach (var (word, ids) in Lexicon.SERIES_WORDS)
                AddPhrases([word], "series:" + string.Join(",", ids.Select(i => i.ToString()).OrderBy(s => s, StringComparer.Ordinal)));
            foreach (var (word, phase) in Lexicon.PHASE_WORDS) AddPhrases([word], "phase:" + phase);

            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var list in Lexicon.STOPWORDS.Values)
                foreach (var entry in list) known.UnionWith(entry.Split(' '));
            foreach (var entry in Lexicon.DEFER_TO_PERSONALITY) known.UnionWith(entry.Split(' '));
            known.UnionWith(Lexicon.NEVER_CORRECTED);
            // A word that is a stopword somewhere is no misspelling, but it is not a correction to
            // reach either: "more" may be no one's element.
            foreach (var word in known) senses.Remove(word);
            return new Speller(senses.ToDictionary(e => e.Key, e => (IReadOnlySet<string>)e.Value, StringComparer.Ordinal), known);
        }
    }
}
