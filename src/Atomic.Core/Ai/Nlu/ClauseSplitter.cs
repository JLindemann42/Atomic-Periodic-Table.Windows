using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Nlu
{
    /// <summary>
    /// Splits a question that asks two things at once.
    ///
    /// <para>"What is the density of gold and how does it compare to lead" is two questions sharing
    /// a subject. Answering only the second, which is what happened before, silently drops half of
    /// what was asked.</para>
    ///
    /// <para>The difficulty is not finding "and" - it is knowing when <i>not</i> to split on it.
    /// The word joins two clauses in the example above, but joins two elements in "compare gold and
    /// silver", two bounds in "between 1000 and 2000 kelvin", and two aspects in "reactivity,
    /// density and applications". Splitting any of those would break a question that currently
    /// works, so the rule is deliberately narrow: the tail must read as a question in its own
    /// right.</para>
    /// </summary>
    public static class ClauseSplitter
    {
        /// <summary>
        /// Conjunctions that can join two independent questions.
        ///
        /// <para>Sentence boundaries are included because two questions are often just two
        /// sentences - "Tell me about hydrogen. Furthermore, what is the density of iron" was
        /// answered as a two-element density comparison because nothing here matched it at all.</para>
        /// </summary>
        private static readonly string[] Conjunctions =
        [
            ". ", "? ", "! ",
            " and ", " and, ", " also ", " plus ",
            " och ", " samt ", " und ", " sowie ", " y ", " e ", " et ", " en ",
            " और ", " اور ", " 和 ", " 并且 ",
            // Filipino joins two questions with "at". Chinese joins them with 以及, which - unlike
            // the bare 和 - never joins two nouns, so it is safe without spaces in a script that
            // has none.
            " at ", "以及", "还有",
        ];

        /// <summary>
        /// Openers that make a clause a question rather than a continuation.
        /// "…and how does it compare" splits; "…and silver" does not.
        /// </summary>
        private static readonly string[] ClauseOpeners =
        [
            "how", "what", "which", "why", "when", "where", "who", "is", "are", "does", "do",
            "can", "tell me", "show me", "list", "give me",
            // "beratta" is Android's "berätta", folded. Unfolded it can never match, because this
            // list is compared against text Tokenizer.Normalize has already folded - and unlike the
            // other six entries of its kind, this one has no folded twin elsewhere in the list, so
            // a Swedish second clause opening "berätta om..." genuinely never counted as opening a
            // question. Folded here with corpus evidence; see docs/porting-notes.md D13.
            "hur", "vad", "vilka", "vilket", "varfor", "ar", "visa", "beratta",
            "wie", "was", "welche", "warum", "ist", "sind", "zeige",
            "como", "que", "cual", "por que", "es", "muestra",
            "comment", "quel", "quelle", "pourquoi", "est", "montre",
            "come", "cosa", "quale", "perche", "quanto",
            "hoe", "wat", "watter", "paano", "ano", "alin",
            "कैसे", "क्या", "कौन", "کیسے", "کیا", "如何", "什么", "哪个",
            // The elided Italian and Portuguese interrogative. Only "quale" was listed, and every
            // second clause is written "qual è …" / "qual é …".
            "qual",
            // Hindi writes its possessive as one word, so a second clause opens with the pronoun
            // and puts the interrogative at the end: "…और इसका गलनांक क्या है". Chinese does the
            // same with 它的. Neither could be reached by a leading question word or by a property
            // linker.
            "इसका", "इसकी", "इनका", "इनकी", "उसका", "उसकी",
            "它的", "它们的",
        ];

        /// <summary>Constructs where a conjunction is part of the phrase and must not be split.</summary>
        private static readonly string[] Protected =
            ["between", "from", "compare", "difference between", "vs", "versus"];

        /// <summary>Shortest a clause can be and still carry a question.</summary>
        private const int MinClause = 8;

        /// <summary>
        /// The same, for Chinese.
        ///
        /// <para>Measured in characters, and a Han character carries roughly what a whole word does
        /// - 它的熔点是多少 is a complete question in seven characters. The Latin floor rejected
        /// every Chinese second clause before it was ever examined.</para>
        /// </summary>
        private const int MinClauseHan = 4;

        private static int FloorFor(string text) => IsHan(text) ? MinClauseHan : MinClause;

        private static bool IsHan(string text) => text.Any(UnicodeScripts.IsHan);

        /// <summary>
        /// Words a second clause can open with that carry no meaning of their own.
        ///
        /// <para>Stripped before judging the tail, so the marker neither hides the question behind
        /// it nor gets mistaken for part of it. Discourse markers first, then the bare conjunctions
        /// that survive a sentence-boundary split ("…of gold? and what about silver").</para>
        /// </summary>
        private static readonly HashSet<string> LeadingConnectives =
            Lexicon.DISCOURSE_MARKERS
                .Concat(["and", "also", "plus", "och", "und", "y", "et", "e"])
                .ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// Linkers that tie a property to an element, in the languages the app ships.
        ///
        /// <para>Their presence is what makes a tail with no question word still a question:
        /// "densitet av väte" and "the density of iron" are complete asks. Matching a linker is a
        /// far cheaper test than running the field and element resolvers, and it keeps this class
        /// pure - it is called from the engine and from tests with no resolvers to hand.</para>
        /// </summary>
        private static readonly HashSet<string> PropertyLinkers = new(StringComparer.Ordinal)
        {
            "of", "for", "in",
            // Android writes "för" here, which can never match folded text. Harmless in its case:
            // "för" folds to "for", which is already listed one line above, so the behaviour was
            // always right. Folded anyway for consistency, which makes it a duplicate rather than a
            // second entry. D13.
            "av", "for", "hos",
            "von", "des", "der", "vom",
            "de", "del", "de la", "du",
            "di", "dell", "do", "da",
            "ng", "ka", "ki", "का", "की", "کا", "کی", "的",
        };

        /// <summary>
        /// Split into two clauses, or return null when the query is a single question.
        /// </summary>
        /// <returns>The clauses in order, or null when the query should be planned whole.</returns>
        public static IReadOnlyList<string>? Split(string query)
        {
            var normalized = TextMatching.NormalizeForLookup(query);
            var floor = FloorFor(query);

            foreach (var (conjunction, at) in Occurrences(normalized, floor))
            {
                // Work on the original text so the clauses keep their capitalisation, which the
                // formula parser and element resolver both rely on.
                //
                // Guarded because normalisation can change a string's length - "außerdem" folds to
                // "ausserdem" - so an offset measured on the normalised text can run past the end
                // of the original.
                if (at > query.Length || at + conjunction.Length > query.Length) continue;

                var head = query[..at].Trim();
                var tail = query[(at + conjunction.Length)..].Trim();
                if (head.Length < floor || tail.Length < floor) continue;

                // A protecting construct only matters when it reaches across the conjunction, which
                // means it opened in the head: "compare gold and silver" and "between 1000 and
                // 2000" must stay whole. The same word in the tail is a new clause of its own -
                // "…and how does it compare to lead" is a second question, not a protected phrase.
                var headNormalized = TextMatching.NormalizeForLookup(head);
                if (Protected.Any(p => TextMatching.ContainsWord(headNormalized, p))) continue;

                var tailNormalized = TextMatching.NormalizeForLookup(tail);

                // A discourse marker introduces a second question without being one: "…och sen
                // densitet av väte", "…Furthermore, what is the density of iron". Strip it and
                // judge what follows, so the marker neither hides the question nor becomes part of
                // it. Dropped by token rather than by character offset, for the length reason above.
                var tailText = tail;
                var strippedMarker = false;

                var firstWordEnd = tailText.IndexOfAny([' ', ',']);
                if (firstWordEnd < 0) firstWordEnd = tailText.Length;
                var firstWord = TextMatching.NormalizeForLookup(tailText[..firstWordEnd]).Trim(',');

                if (LeadingConnectives.Contains(firstWord))
                {
                    tailText = tailText[firstWordEnd..].TrimStart(' ', ',');
                    tailNormalized = TextMatching.NormalizeForLookup(tailText);
                    strippedMarker = true;
                }
                if (string.IsNullOrWhiteSpace(tailText)) continue;

                // Chinese writes no space after the opener, so requiring one meant 它的熔点是多少 -
                // a complete second question - never counted as opening one.
                var opensAQuestion = ClauseOpeners.Any(
                    opener => string.Equals(tailNormalized, opener, StringComparison.Ordinal) ||
                              tailNormalized.StartsWith(opener + " ", StringComparison.Ordinal) ||
                              (IsHan(opener) && tailNormalized.StartsWith(opener, StringComparison.Ordinal)));

                // A tail that names both a property and an element is a question whatever it opens
                // with: "densitet av väte" and "the density of iron" are complete asks with no
                // question word. Checked only after the protected-construct guard above, so
                // "compare gold and silver" is never reached here.
                //
                // A stripped marker is itself strong evidence of a second question, so the tail
                // after one is held to a lower bar: "…och dessutom dess densitet" leaves only "dess
                // densitet", which is two words and carries no linker, but is unambiguously a
                // further ask.
                if (!opensAQuestion && !ReadsAsAsk(tailNormalized, lenient: strippedMarker)) continue;

                return [head, tailText];
            }
            return null;
        }

        /// <summary>
        /// Every place a conjunction could split the query, earliest first.
        ///
        /// <para><i>Every</i> place, not the first per conjunction. Italian and Portuguese normalise
        /// their copula "è"/"é" to the same letter as their conjunction "e", so "qual è il numero
        /// atomico del ferro <b>e</b> qual è il suo punto di fusione" contains " e " twice - and
        /// the first one is four characters in, below the minimum clause length. Taking only the
        /// first occurrence discarded the conjunction entirely and the compound question was
        /// answered as one.</para>
        /// </summary>
        private static IReadOnlyList<(string Conjunction, int At)> Occurrences(string normalized, int floor)
        {
            var found = new List<(string, int)>();
            foreach (var conjunction in Conjunctions)
            {
                var from = 0;
                while (from <= normalized.Length - conjunction.Length)
                {
                    var at = normalized.IndexOf(conjunction, from, StringComparison.Ordinal);
                    if (at < 0) break;
                    if (at >= floor) found.Add((conjunction, at));
                    from = at + 1;
                }
            }
            return found.OrderBy(f => f.Item2).ToList();
        }

        /// <summary>
        /// Whether a clause with no question word still reads as an ask.
        ///
        /// <para>Requires a property linker and something on both sides of it, which is the shape of
        /// "densitet av väte" and "the density of iron". A bare continuation - "…and silver" - has
        /// no linker and stays joined, which is what the protected-construct rules depend on.</para>
        /// </summary>
        private static bool ReadsAsAsk(string tailNormalized, bool lenient = false)
        {
            var tokens = TextMatching.SplitQueryTokens(tailNormalized);
            if (lenient) return tokens.Count >= 2;
            if (tokens.Count < 3) return false;

            var linkerAt = -1;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!PropertyLinkers.Contains(tokens[i])) continue;
                linkerAt = i;
                break;
            }
            return linkerAt > 0 && linkerAt < tokens.Count - 1;
        }
    }
}
