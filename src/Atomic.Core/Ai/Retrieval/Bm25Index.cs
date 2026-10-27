using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Ai.Retrieval
{
    /// <summary>A document in the retrieval corpus. <paramref name="Id"/> is prefixed by kind,
    /// e.g. <c>element:gold</c>.</summary>
    /// <param name="AltTitles">
    /// The title in the index's language, and other wordings of it (roadmap 1.3). They count as
    /// title for coverage, so "neutronens massa" names the "Neutron mass" row's Swedish title as
    /// fully as "neutron mass" names the English one.
    /// </param>
    public sealed record Document(string Id, string Title, string Body, IReadOnlyList<string>? AltTitles = null);

    /// <summary>
    /// A scored search result.
    ///
    /// <para>Whether <paramref name="Score"/> is a raw BM25 sum or a value normalised against the
    /// top hit depends on which search method produced it - see
    /// <see cref="Bm25Index.SearchNormalized"/>, whose scores are ordinal only.</para>
    /// </summary>
    public sealed record SearchHit(string Id, string Title, double Score);

    /// <summary>
    /// How well one document covers a query's content (roadmap 1.1). Unlike a BM25 score these
    /// are absolute - a fraction of <i>this query's</i> weight - so a threshold on them means the
    /// same thing for every query, and a query nothing covers scores low instead of winning by
    /// default.
    /// </summary>
    /// <param name="Coverage">The share of the query's content weight found anywhere in the document.</param>
    /// <param name="TitleRecall">The share of the query's content weight found in the title.</param>
    /// <param name="TitlePrecision">
    /// The share of the title's weight the query names; 1.0 when the query names everything the
    /// title says, which is what tells "tell me about quartz" the mineral row ("Quartz") from the
    /// elastic one ("α-Quartz (SiO2)").
    /// </param>
    /// <param name="Bm25">The raw BM25 score, kept as the tie-break.</param>
    /// <param name="Words">How many content words the query had.</param>
    /// <param name="NamesTitle">
    /// Whether the query names every word of the title, in one of its wordings. A formula in
    /// brackets is notation, not a word, so "periclase" names "Periclase (MgO)" while "life" does
    /// not name "Half-Life".
    /// </param>
    public sealed record CoverageHit(
        string Id,
        string Title,
        double Coverage,
        double TitleRecall,
        double TitlePrecision,
        double Bm25,
        int Words,
        bool NamesTitle = false);

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

        /// <summary>Each document's title and alternative titles, tokenized separately.</summary>
        private readonly List<HashSet<string>>[] _titleVariants;

        /// <summary>
        /// Each title wording as its words, each word as the terms it tokenizes to, with any
        /// bracketed formula left out. A hyphen separates words here, as it does for the tokenizer:
        /// "Half-Life" is two words, and naming one of them is not naming the title.
        /// </summary>
        private readonly IReadOnlyList<HashSet<string>>[][] _titleWords;

        /// <summary>Every title term of a document, in any variant.</summary>
        private readonly HashSet<string>[] _titleTerms;
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
            _titleVariants = new List<HashSet<string>>[documents.Count];
            _titleWords = new IReadOnlyList<HashSet<string>>[documents.Count][];
            _titleTerms = new HashSet<string>[documents.Count];
            _lengths = new int[documents.Count];
            _postings = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);

            long total = 0;
            for (var docIndex = 0; docIndex < documents.Count; docIndex++)
            {
                var doc = documents[docIndex];
                _ids[docIndex] = doc.Id;
                _titles[docIndex] = doc.Title;
                _titleVariants[docIndex] = new[] { doc.Title }.Concat(doc.AltTitles ?? Array.Empty<string>())
                    .Select(t => Tokenizer.Tokenize(t, language).ToHashSet(StringComparer.Ordinal))
                    .ToList();
                _titleTerms[docIndex] = _titleVariants[docIndex].SelectMany(v => v).ToHashSet(StringComparer.Ordinal);
                _titleWords[docIndex] = new[] { doc.Title }.Concat(doc.AltTitles ?? Array.Empty<string>())
                    .Select(t => (IReadOnlyList<HashSet<string>>)NonWord.Split(Bracketed.Replace(t, " "))
                        .Where(w => w.Length > 0)
                        .Select(w => Tokenizer.Tokenize(w, language).ToHashSet(StringComparer.Ordinal))
                        .Where(forms => forms.Count > 0)
                        .ToList())
                    .ToArray();

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
        /// Score every document that holds any of the query's content words by how much of that
        /// content it covers (roadmap 1.1).
        ///
        /// <para><see cref="SearchNormalized"/> divides by the top hit, so the best match always
        /// scores 1.0 and no threshold can tell a good match from the least bad one. Coverage
        /// divides by the <i>query</i>: the IDF of the content words a document contains over the
        /// IDF of all of them. A word the corpus has never seen counts at full weight, so "what is
        /// the nernst equation" cannot be covered by the Arrhenius equation just because both say
        /// "equation".</para>
        ///
        /// <para>Every document holding a content word is scored, not BM25's top few: "Electron
        /// mass" is not in BM25's top six for "what is the mass of an electron", and re-ranking
        /// those could never find it.</para>
        /// </summary>
        /// <param name="stopwords">Words that are not content, already folded; tokenized here like the query.</param>
        /// <param name="accept">Which document ids may be returned.</param>
        /// <returns>Candidates in no particular order; ranking is the caller's policy.</returns>
        public IReadOnlyList<CoverageHit> Coverage(
            string query, IEnumerable<string> stopwords, Func<string, bool> accept)
        {
            var stop = stopwords.SelectMany(w => Tokenizer.Tokenize(w, _language)).ToHashSet(StringComparer.Ordinal);
            var groups = ContentWords(query, stop);
            if (groups.Count == 0 || _ids.Length == 0) return [];
            var strictForms = Tokenizer.Tokenize(query, _language).ToHashSet(StringComparer.Ordinal);

            // A word weighs what its most specific form the corpus knows weighs; a word the corpus
            // has never seen in any form weighs the most a term can. Only the tokenizer's own forms
            // set the weight - the looser forms ContentWords adds may only say the word is present.
            var weights = groups.Select(forms =>
                MaxIdf(forms.Where(f => strictForms.Contains(f))) ??
                MaxIdf(forms) ??
                InverseDocumentFrequency(0)).ToList();
            var total = weights.Sum();
            if (total <= 0.0) return [];

            var covered = new Dictionary<int, double>();
            var inTitle = new Dictionary<int, double>();
            var bm25 = new Dictionary<int, double>();
            for (var g = 0; g < groups.Count; g++)
            {
                var w = weights[g];
                var inDoc = new HashSet<int>();
                var inDocTitle = new HashSet<int>();
                foreach (var term in groups[g])
                {
                    if (!_postings.TryGetValue(term, out var bucket)) continue;
                    foreach (var (docIndex, frequency) in bucket)
                    {
                        if (!accept(_ids[docIndex])) continue;
                        inDoc.Add(docIndex);
                        if (_titleTerms[docIndex].Contains(term)) inDocTitle.Add(docIndex);
                        var tf = (double)frequency;
                        var norm = 1.0 - _b + _b * (_lengths[docIndex] / Math.Max(_averageLength, 1.0));
                        Add(bm25, docIndex, w * tf * (_k1 + 1.0) / (tf + _k1 * norm));
                    }
                }
                foreach (var docIndex in inDoc) Add(covered, docIndex, w);
                foreach (var docIndex in inDocTitle) Add(inTitle, docIndex, w);
            }

            var termSet = groups.SelectMany(f => f).ToHashSet(StringComparer.Ordinal);
            return covered.Select(e =>
            {
                // The best of the title's wordings: a query names one of them, not all at once.
                var titlePrecision = _titleVariants[e.Key].Select(variant =>
                {
                    var title = variant.Where(t => !stop.Contains(t)).ToList();
                    var titleWeight = title.Sum(t => InverseDocumentFrequency(DocumentFrequency(t)));
                    var named = title.Where(termSet.Contains).Sum(t => InverseDocumentFrequency(DocumentFrequency(t)));
                    return titleWeight > 0.0 ? named / titleWeight : 0.0;
                }).DefaultIfEmpty(0.0).Max();
                return new CoverageHit(
                    _ids[e.Key],
                    _titles[e.Key],
                    e.Value / total,
                    (inTitle.TryGetValue(e.Key, out var t) ? t : 0.0) / total,
                    titlePrecision,
                    bm25.TryGetValue(e.Key, out var s) ? s : 0.0,
                    groups.Count,
                    _titleWords[e.Key].Any(wording =>
                    {
                        var content = wording.Where(forms => forms.Any(f => !stop.Contains(f))).ToList();
                        return content.Count > 0 && content.All(forms => forms.Any(termSet.Contains));
                    }));
            }).ToList();

            static void Add(Dictionary<int, double> map, int key, double value) =>
                map[key] = map.TryGetValue(key, out var running) ? running + value : value;
        }

        private double? MaxIdf(IEnumerable<string> forms)
        {
            double? best = null;
            foreach (var form in forms)
            {
                var df = DocumentFrequency(form);
                if (df == 0) continue;
                var idf = InverseDocumentFrequency(df);
                if (best is null || idf > best) best = idf;
            }
            return best;
        }

        /// <summary>
        /// The query's content words, each as the index terms it tokenizes to.
        ///
        /// <para><see cref="Tokenizer"/> emits a word's surface form and its folded stem as
        /// separate terms ("joules", "joul"). Counted separately, the surface form the corpus never
        /// wrote down weighed as an unknown word. A word and its stem are one word: covered when any
        /// form is. Han text is the exception - its terms are single characters and bigrams, not
        /// variants of one word - so each stays its own.</para>
        ///
        /// <para>The tokenizer folds a suffix only off words longer than five letters, so "joules"
        /// becomes "joul" while the row's "joule" stays "joule". Changing that would move every BM25
        /// score and the shared tokenizer golden files, so instead a Latin word also tries itself
        /// with one plural or inflection ending removed, whatever its length.</para>
        /// </summary>
        private List<List<string>> ContentWords(string query, HashSet<string> stop)
        {
            var words = new List<List<string>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in Whitespace.Split(query))
            {
                var forms = Tokenizer.Tokenize(word, _language).Distinct(StringComparer.Ordinal)
                    .Where(f => !stop.Contains(f) && !seen.Contains(f) && !f.Any(char.IsDigit))
                    .ToList();
                if (forms.Count == 0) continue;
                seen.UnionWith(forms);
                var stem = forms.MinBy(f => f.Length)!;
                if (!forms.All(f => f.StartsWith(stem, StringComparison.Ordinal)))
                {
                    foreach (var form in forms.Where(f => !IsStraddlingBigram(f, stop))) words.Add([form]);
                    continue;
                }
                var looser = forms.SelectMany(form => LooseEndings
                    .Where(e => form.EndsWith(e, StringComparison.Ordinal) && form.Length - e.Length >= MinLooseStem)
                    .Select(e => form[..^e.Length]));
                words.Add(forms.Concat(looser).Distinct(StringComparer.Ordinal).ToList());
            }
            return words;
        }

        /// <summary>
        /// A Han bigram that runs across a function character and that no document contains.
        ///
        /// <para>Han bigrams are every adjacent pair, so 动能的公式 also yields 能的 and 的公. Those
        /// are not words but seams around the particle 的, and counted as unknown content they
        /// outweighed the whole question. A bigram the corpus does contain stays, function character
        /// or not.</para>
        /// </summary>
        private bool IsStraddlingBigram(string term, HashSet<string> stop) =>
            term.Length == 2 && term.All(Tokenizer.IsHan) &&
            term.Any(c => stop.Contains(c.ToString())) && DocumentFrequency(term) == 0;

        private static readonly System.Text.RegularExpressions.Regex Whitespace = new(@"\s+");

        /// <summary>A formula or note in brackets: "(SiO2)", "(radioactive decay)".</summary>
        private static readonly System.Text.RegularExpressions.Regex Bracketed = new(@"\([^)]*\)");

        /// <summary>What separates the words of a title.</summary>
        private static readonly System.Text.RegularExpressions.Regex NonWord = new(@"[^\p{L}\p{M}\p{N}]+");

        /// <summary>Endings a query word may also be tried without, for coverage only.</summary>
        private static readonly string[] LooseEndings = ["es", "en", "er", "s", "e"];

        /// <summary>Shortest stem a loose ending may leave: "gas" must not become "ga".</summary>
        private const int MinLooseStem = 4;

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
