using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Atomic.Core.Ai.Data;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Retrieval
{
    /// <summary>What a retrieval hit refers to.</summary>
    public abstract record RetrievedRef
    {
        private RetrievedRef() { }

        public sealed record Element(string Key) : RetrievedRef;

        public sealed record Dataset(string Name, string Id) : RetrievedRef;
    }

    /// <summary>A fused retrieval result. Score combines structured, lexical and fuzzy evidence.</summary>
    public sealed record RetrievedHit(
        RetrievedRef Ref,
        string Title,
        double Score,
        double Structured = 0.0,
        double Lexical = 0.0);

    /// <summary>Fusion weights, isolated so they can be tuned from an eval set without touching logic.</summary>
    public static class RetrievalWeights
    {
        public const double Structured = 1.0;
        public const double Lexical = 0.55;
        public const double Fuzzy = 0.25;

        /// <summary>Below this, a lexical-only hit is not trustworthy enough to answer from.</summary>
        public const double MinAnswerable = 0.30;
    }

    /// <summary>
    /// Combines exact entity resolution with BM25 over the app's own content.
    ///
    /// <para>The corpus is built at runtime from live data rather than shipped as a file. That
    /// removes the 42 MB of precomputed embeddings the previous design carried, guarantees the text
    /// can never drift from what the app displays, and keeps every language correctly separated -
    /// the shipped corpus had all twelve languages concatenated into the file served as
    /// English.</para>
    /// </summary>
    public sealed class HybridRetriever
    {
        private readonly KnowledgeStore _store;
        private readonly DatasetIndex _datasets;
        private readonly Bm25Index _index;
        private readonly EntityResolver _entities;
        private readonly LocalizedView? _localized;

        public HybridRetriever(
            KnowledgeStore store,
            DatasetIndex datasets,
            Bm25Index index,
            EntityResolver entities,
            LocalizedView? localized)
        {
            _store = store;
            _datasets = datasets;
            _index = index;
            _entities = entities;
            _localized = localized;
        }

        /// <summary>
        /// Whether the query names something the corpus has never heard of.
        ///
        /// <para>Deliberately the weakest possible question to ask of the index - not "is this a
        /// good match" but "has this word ever been written about the periodic table". That is the
        /// one thing lexical statistics answer reliably, and it is enough for the two narrow
        /// decisions it serves: whether a short follow-up is continuing the thread, and whether a
        /// dataset match is worth trusting.</para>
        /// </summary>
        public bool MentionsUnknownTopic(string query) => _index.MentionsUnknownTerm(query);

        /// <summary>
        /// Every reference-table row holding any of the query's content, scored by how much of it
        /// the row covers (roadmap 1.1). Elements are excluded: an element named in the query is
        /// resolved by the entity resolver, never by matching its description.
        /// </summary>
        public IReadOnlyList<CoverageHit> DatasetCoverage(string query, IEnumerable<string> stopwords) =>
            _index.Coverage(query, stopwords, id => !id.StartsWith("element:", StringComparison.Ordinal));

        /// <summary>
        /// Whether a retrieved hit is plausibly about what was asked.
        ///
        /// <para>BM25 ranks; it never abstains. This is the abstention test: an off-topic query
        /// retrieves a row whose title has nothing in common with it, which is exactly what "write
        /// me a poem" -> "Electron mass" looks like.</para>
        /// </summary>
        public bool LooksRelevant(string query, RetrievedHit hit) =>
            _index.SharesTermWithTitle(query, hit.Title);

        /// <summary>Search across elements and datasets, best first.</summary>
        public IReadOnlyList<RetrievedHit> Search(string query, int limit = 5)
        {
            var fused = new Dictionary<string, RetrievedHit>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (var match in _entities.ResolveAll(query, limit))
            {
                var title = _localized?.Name(match.Key) ?? match.Key;
                var id = $"element:{match.Key}";
                fused[id] = new RetrievedHit(
                    new RetrievedRef.Element(match.Key), title,
                    RetrievalWeights.Structured * match.Score, Structured: match.Score);
                order.Add(id);
            }

            foreach (var hit in _index.SearchNormalized(query, limit * 2))
            {
                if (RefOf(hit.Id) is not { } reference) continue;
                var lexical = RetrievalWeights.Lexical * hit.Score;

                if (fused.TryGetValue(hit.Id, out var existing))
                {
                    fused[hit.Id] = existing with
                    {
                        Score = existing.Score + lexical,
                        Lexical = hit.Score,
                    };
                }
                else
                {
                    fused[hit.Id] = new RetrievedHit(reference, hit.Title, lexical, Lexical: hit.Score);
                    order.Add(hit.Id);
                }
            }

            // Insertion order before the sort, mirroring Kotlin's LinkedHashMap, so equal scores
            // break the same way on both platforms.
            return order.Select(id => fused[id])
                .OrderByDescending(h => h.Score)
                .Take(limit)
                .ToList();
        }

        /// <summary>
        /// The single best hit, or null when nothing clears
        /// <see cref="RetrievalWeights.MinAnswerable"/>. Returning null is deliberate: a weak
        /// lexical match is worse than admitting no answer.
        /// </summary>
        public RetrievedHit? Best(string query)
        {
            var top = Search(query, 1).FirstOrDefault();
            return top is not null && top.Score >= RetrievalWeights.MinAnswerable ? top : null;
        }

        private static RetrievedRef? RefOf(string documentId)
        {
            var separator = documentId.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0) return null;
            var kind = documentId[..separator];
            var id = documentId[(separator + 1)..];
            return string.Equals(kind, "element", StringComparison.Ordinal)
                ? new RetrievedRef.Element(id)
                : new RetrievedRef.Dataset(kind, id);
        }

        /// <summary>
        /// Build the searchable corpus from live app data.
        ///
        /// <para>Roughly 780 documents: 118 elements plus every dataset row. This is the same
        /// coverage the shipped <c>passages.jsonl</c> had, generated at runtime instead - every
        /// per-language passages file was byte-identical to the <c>description</c> field already in
        /// the element JSON, so shipping it was pure duplication.</para>
        /// </summary>
        public static IReadOnlyList<Document> BuildCorpus(
            KnowledgeStore store, DatasetIndex datasets, LocalizedView? localized)
        {
            var documents = new List<Document>(store.Size + datasets.Rows.Count);

            foreach (var element in store.Elements)
            {
                LocalizedElement? view = null;
                localized?.Elements.TryGetValue(element.Key, out view);
                var name = view?.Name ?? element.Key;

                var body = new StringBuilder();
                body.Append(name).Append(' ').Append(element.Key).Append(' ');
                body.Append(element.Symbol).Append(' ')
                    .Append(element.AtomicNumber.ToString(CultureInfo.InvariantCulture)).Append(' ');
                if (view is not null)
                {
                    body.Append(view.Description).Append(' ');
                    body.Append(view.SeriesName).Append(' ');
                    body.Append(view.Appearance).Append(' ');
                    body.Append(view.Phase).Append(' ');
                    body.Append(view.ElectricalType).Append(' ');
                    body.Append(view.MagneticType).Append(' ');
                }
                body.Append(element.Series.ToString().Replace('_', ' ')).Append(' ');
                body.Append(element.Block).Append(" block ");
                if (element.Value("discovered_by") is FieldValue.Text discoverer)
                    body.Append(discoverer.Raw).Append(' ');
                if (element.Value("crystal_structure") is FieldValue.Enum crystal)
                    body.Append(crystal.Localized);

                documents.Add(new Document($"element:{element.Key}", name, body.ToString()));
            }

            foreach (var row in datasets.Rows)
            {
                documents.Add(new Document(
                    Id: $"{row.Dataset}:{row.Id}",
                    Title: row.Title,
                    // Rows titled with a chemical symbol carry the element's *name* too. The
                    // solubility table is indexed by ion - "Na^+", "Ag^+" - and the electrode table
                    // by symbol, so "solubility of sodium chloride" and "standard electrode
                    // potential of zinc" shared no word with the row that answers them and
                    // retrieved a dictionary definition instead. Users write the name; the table is
                    // keyed by the symbol.
                    Body: row.Body + ElementGloss(row, store, localized)));
            }

            return documents;
        }

        /// <summary>
        /// The element name behind a row titled with a chemical symbol, or the empty string.
        ///
        /// <para>Charge and stoichiometry are stripped first, so "Na^+", "Ag^+" and "Fe^3+" all
        /// reach the symbol the store knows. Returns both the English key and the localized name,
        /// because the user may write either.</para>
        /// </summary>
        private static string ElementGloss(DatasetRow row, KnowledgeStore store, LocalizedView? localized)
        {
            var match = SymbolHead.Match(row.Title.Trim());
            if (!match.Success) return string.Empty;

            var element = store.BySymbol(match.Groups[1].Value);
            if (element is null) return string.Empty;

            var name = localized?.Name(element.Key) ?? string.Empty;
            return $" {element.Key} {name}";
        }

        /// <summary>A leading chemical symbol, before any charge or subscript.</summary>
        private static readonly System.Text.RegularExpressions.Regex SymbolHead =
            JavaRegex.Compile("^([A-Z][a-z]?)(?:\\^|\\d|\\s*$|\\s)");
    }
}
