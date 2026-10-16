using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Ai.Retrieval
{
    /// <summary>A document in the retrieval corpus. <paramref name="Id"/> is prefixed by kind,
    /// e.g. <c>element:gold</c>.</summary>
    public sealed record Document(string Id, string Title, string Body);

    /// <summary>
    /// A scored search result.
    ///
    /// <para>Whether <paramref name="Score"/> is a raw BM25 sum or a value normalised against the
    /// top hit depends on which search method produced it - see
    /// <see cref="Bm25Index.SearchNormalized"/>, whose scores are ordinal only.</para>
    /// </summary>
    public sealed record SearchHit(string Id, string Title, double Score);

    /// <summary>
    /// Okapi BM25 over the app's own data.
    ///
    /// <para>This replaces the previous embedding search, which could never work: its query
    /// embedder returned a hash-seeded pseudo-random vector, so cosine scores hovered near zero
    /// and the 0.65 threshold gating it never fired.</para>
    ///
    /// <para><c>b</c> is set below the usual 0.75 because the corpus is deliberately heterogeneous
    /// - a three-token Poisson row sits beside a two-hundred-token element description - and a
    /// lower <c>b</c> reduces the length normalisation that would otherwise over-favour the short
    /// rows.</para>
    /// </summary>
    public sealed class Bm25Index
    {
        private readonly string _language;
        private readonly double _k1;
        private readonly double _b;

        private readonly string[] _ids;
        private readonly string[] _titles;
        private readonly int[] _lengths;

        /// <summary>Term -> (document index -> term frequency).</summary>
        private readonly Dictionary<string, Dictionary<int, int>> _postings;

        private readonly double _averageLength;

        public Bm25Index(
            IReadOnlyList<Document> documents,
            string language = "en",
            double k1 = 1.2,
            double b = 0.6)
        {
            _language = language;
            _k1 = k1;
            _b = b;

            _ids = new string[documents.Count];
            _titles = new string[documents.Count];
            _lengths = new int[documents.Count];
            _postings = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);

            long total = 0;
            for (var docIndex = 0; docIndex < documents.Count; docIndex++)
            {
                var doc = documents[docIndex];
                _ids[docIndex] = doc.Id;
                _titles[docIndex] = doc.Title;

                var tokens = Tokenizer.Tokenize($"{doc.Title} {doc.Body}", language);
                _lengths[docIndex] = tokens.Count;
                total += tokens.Count;

                foreach (var token in tokens)
                {
                    if (!_postings.TryGetValue(token, out var bucket))
                        _postings[token] = bucket = new Dictionary<int, int>();
                    bucket[docIndex] = bucket.TryGetValue(docIndex, out var n) ? n + 1 : 1;
                }
            }

            _averageLength = documents.Count == 0 ? 0.0 : (double)total / documents.Count;
        }

        public int Size => _ids.Length;

        /// <summary>
        /// Search the index.
        /// </summary>
        /// <param name="query">Raw query text; tokenized exactly as documents were.</param>
        /// <param name="limit">Maximum hits to return.</param>
        /// <param name="prefixFilter">
        /// When set, restricts results to ids starting with it - a large precision win when the
        /// planner already knows it wants an element rather than a dictionary entry.
        /// </param>
        public IReadOnlyList<SearchHit> Search(string query, int limit = 5, string? prefixFilter = null)
        {
            var tokens = Tokenizer.Tokenize(query, _language);
            if (tokens.Count == 0 || _ids.Length == 0) return [];

            var scores = new Dictionary<int, double>();
            foreach (var token in tokens.Distinct(StringComparer.Ordinal))
            {
                if (!_postings.TryGetValue(token, out var bucket)) continue;
                var idf = InverseDocumentFrequency(bucket.Count);
                if (idf <= 0.0) continue;

                foreach (var (docIndex, frequency) in bucket)
                {
                    if (prefixFilter is not null &&
                        !_ids[docIndex].StartsWith(prefixFilter, StringComparison.Ordinal)) continue;

                    var tf = (double)frequency;
                    var norm = 1.0 - _b + _b * (_lengths[docIndex] / Math.Max(_averageLength, 1.0));
                    var contribution = idf * tf * (_k1 + 1.0) / (tf + _k1 * norm);
                    scores[docIndex] = scores.TryGetValue(docIndex, out var running)
                        ? running + contribution
                        : contribution;
                }
            }

            return scores
                .OrderByDescending(e => e.Value)
                .Take(limit)
                .Select(e => new SearchHit(_ids[e.Key], _titles[e.Key], e.Value))
                .ToList();
        }

        /// <summary>
        /// Search and normalise scores into 0..1 relative to the top hit.
        ///
        /// <para><b>These scores are ordinal, not absolute.</b> Dividing by the top hit means the
        /// best result always scores exactly 1.0 - whether it was an excellent match or the least
        /// bad of a corpus that contains nothing relevant. A threshold applied to this number
        /// therefore answers nothing about match quality, which is why raising the planner's
        /// dataset threshold from 0.45 to 0.55 had no measurable effect: every candidate arrived at
        /// exactly the same value.</para>
        ///
        /// <para>Use <see cref="Search"/> if you need magnitudes comparable across queries.</para>
        /// </summary>
        public IReadOnlyList<SearchHit> SearchNormalized(string query, int limit = 5, string? prefixFilter = null)
        {
            var hits = Search(query, limit, prefixFilter);
            if (hits.Count == 0) return hits;
            var top = hits[0].Score;
            if (top <= 0.0) return hits;
            return hits.Select(h => h with { Score = h.Score / top }).ToList();
        }

        /// <summary>
        /// Probabilistic IDF with the +1 smoothing that keeps it non-negative. A term in every
        /// document scores ~0, which is what makes a stopword list unnecessary.
        /// </summary>
        private double InverseDocumentFrequency(int documentFrequency) =>
            Math.Log(1.0 + (Size - documentFrequency + 0.5) / (documentFrequency + 0.5));

        /// <summary>
        /// Whether the query contains a content word the corpus has never seen.
        ///
        /// <para>Tokenised here rather than by the caller, because the answer is only meaningful
        /// against the same vocabulary the postings were built from: <see cref="Tokenizer"/> stems,
        /// and a query split any other way produces terms that are "absent" for reasons that have
        /// nothing to do with the subject.</para>
        /// </summary>
        /// <param name="minimumLength">
        /// Below this a token is a function word, which is absent or present for reasons unrelated
        /// to the topic.
        /// </param>
        public bool MentionsUnknownTerm(string query, int minimumLength = 4) =>
            Tokenizer.Tokenize(query, _language).Any(
                term => term.Length >= minimumLength &&
                        !term.Any(char.IsDigit) &&
                        DocumentFrequency(term) == 0);

        /// <summary>
        /// Whether a query and a document title have any content word in common.
        ///
        /// <para>The companion to <see cref="MentionsUnknownTerm"/>, and the half that makes it
        /// usable. An unknown word on its own does not mean the query is off-topic - real questions
        /// are full of ordinary verbs ("define", "convert", "tell") that never appear in a corpus
        /// of element data. What separates "define molar mass" from "write me a poem" is that the
        /// first shares words with the row it retrieved and the second shares nothing with the
        /// electron mass.</para>
        /// </summary>
        public bool SharesTermWithTitle(string query, string title)
        {
            var titleTerms = Tokenizer.Tokenize(title, _language).ToHashSet(StringComparer.Ordinal);
            return Tokenizer.Tokenize(query, _language)
                .Any(t => t.Length >= MinSharedTerm && titleTerms.Contains(t));
        }

        /// <summary>Below this a shared token is a coincidence of grammar rather than of subject.</summary>
        private const int MinSharedTerm = 3;

        /// <summary>Document frequency of a term, exposed for diagnostics and tests.</summary>
        public int DocumentFrequency(string term) =>
            _postings.TryGetValue(term, out var bucket) ? bucket.Count : 0;
    }
}
