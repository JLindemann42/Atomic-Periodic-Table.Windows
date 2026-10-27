using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Nlu;

namespace Atomic.Core.Ai.Retrieval
{
    /// <summary>
    /// Owns the agent's index and keeps it warm across pages.
    ///
    /// <para>Building costs one ~1 MB JSON parse plus tokenizing about 780 short documents - well
    /// under the time the chat panel already spends showing its spinner. Everything is cached
    /// process-wide, so opening the panel a second time is free.</para>
    /// </summary>
    public sealed class RetrievalService
    {
        private readonly IElementSource _source;
        private readonly ConcurrentDictionary<string, Bm25Index> _indexes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, EntityResolver> _resolvers =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly object _aliasLock = new();
        private volatile IReadOnlyDictionary<string, IReadOnlyList<ElementAlias>>? _aliases;

        private RetrievalService(IElementSource source, KnowledgeStore store, DatasetIndex datasets)
        {
            _source = source;
            Store = store;
            Datasets = datasets;
            _detector = new Lazy<LanguageDetector?>(() => LanguageDetector.Parse(source.Text("trigrams.tsv")));
        }

        public KnowledgeStore Store { get; }

        public DatasetIndex Datasets { get; }

        /// <summary>Display overlay for a language, falling back to the base language.</summary>
        public LocalizedView? Localized(string language) =>
            KnowledgeStore.Overlay(_source, language) ??
            KnowledgeStore.Overlay(_source, KnowledgeStore.BaseLanguage);

        /// <summary>The retriever for a language, built on first use.</summary>
        public HybridRetriever Retriever(string language)
        {
            var view = Localized(language);
            var index = _indexes.GetOrAdd(
                language,
                lang => new Bm25Index(HybridRetriever.BuildCorpus(Store, Datasets, view), lang));
            return new HybridRetriever(Store, Datasets, index, Resolver(language), view);
        }

        /// <summary>The entity resolver for a language, built on first use.</summary>
        public EntityResolver Resolver(string language) =>
            _resolvers.GetOrAdd(language, lang => new EntityResolver(SharedAliases(), lang));

        /// <summary>The trigram model (roadmap 1.4), or null when the asset is missing.</summary>
        private LanguageDetector? Detector => _detector.Value;

        private readonly Lazy<LanguageDetector?> _detector;

        /// <summary>
        /// The language to answer <paramref name="message"/> in, starting from
        /// <paramref name="active"/>: by its letters, with element names as the tie-breaker, and by
        /// element names alone when there is no model.
        /// </summary>
        public string DetectLanguage(string message, string active)
        {
            string ByNames(string m, string a) => Resolver(a).DetectLanguage(m, a);
            return Detector?.Detect(message, active, ByNames) ?? ByNames(message, active);
        }

        private IReadOnlyDictionary<string, IReadOnlyList<ElementAlias>> SharedAliases()
        {
            if (_aliases is { } cached) return cached;
            lock (_aliasLock)
            {
                if (_aliases is { } inner) return inner;

                // Names and symbols from every shipped language, so a Swedish or Chinese name
                // resolves even while the chat is running in English.
                var tables = new Dictionary<string, IReadOnlyDictionary<string, (string, string, string)>>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var language in _source.AvailableLanguages())
                {
                    var table = _source.Load(language);
                    if (table is null) continue;

                    var rows = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal);
                    foreach (var (key, row) in table.Rows)
                    {
                        rows[key] = (
                            Field(row, "element"),
                            Field(row, "short"),
                            Field(row, "element_atomic_number"));
                    }
                    tables[language] = rows;
                }

                _aliases = EntityResolver.BuildAliases(tables);
                return _aliases;
            }
        }

        private static string Field(ElementRow row, string jsonKey) =>
            row.TryGetValue(jsonKey, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

        private static readonly object InstanceLock = new();
        private static volatile RetrievalService? _instance;

        /// <summary>
        /// Build or reuse the shared service.
        /// </summary>
        /// <returns>
        /// Null when the element data cannot be read, so callers keep working without retrieval
        /// rather than crashing.
        /// </returns>
        public static RetrievalService? Get(IElementSource source)
        {
            if (_instance is { } cached) return cached;
            lock (InstanceLock)
            {
                if (_instance is { } inner) return inner;

                var store = KnowledgeStore.Get(source);
                if (store is null) return null;

                _instance = new RetrievalService(source, store, DatasetIndex.Build(
                    // The electrochemical series stores element keys; resolving them through the
                    // store gives the English name. The per-language title comes from the overlay
                    // at query time, not from here - this index is shared across languages.
                    key => store.Element(key)?.Key));
                return _instance;
            }
        }

        public static void Clear()
        {
            lock (InstanceLock) { _instance = null; }
            KnowledgeStore.Clear();
        }
    }
}
