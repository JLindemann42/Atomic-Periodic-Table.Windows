using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Compose;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Exec;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Tests.Fixtures;

namespace Atomic.Core.Tests.Corpus
{
    /// <summary>
    /// Builds the real agent stack for the corpus tests, one wiring per language.
    ///
    /// <para><b>This is the reason the corpus exists in this shape.</b> Every other engine test
    /// constructs <c>QueryPlanner(..., retriever: null)</c>, so the dataset-retrieval fallback never
    /// runs and every must-defer assertion in the suite passes vacuously - a test can assert the
    /// engine does not claim "quiz me" and stay green while the shipped app answers it with the
    /// electron mass from the constants table. Here the retriever is real, so a deferral assertion
    /// means something.</para>
    ///
    /// <para>Everything is built once and cached: parsing 118 elements and indexing ~780 documents
    /// per language is far too slow to repeat per row.</para>
    /// </summary>
    public static class CorpusHarness
    {
        /// <summary>One fully wired stack.</summary>
        public sealed record Stack(
            string Language,
            KnowledgeStore Store,
            DatasetIndex Datasets,
            LocalizedView? Localized,
            ReswStringProvider Strings,
            EntityResolver Entities,
            HybridRetriever Retriever,
            Bm25Index Index,
            QueryPlanner Planner,
            QueryExecutor Executor,
            AnswerComposer Composer,
            AiEngine Engine);

        /// <summary>True when the real assets and strings are both reachable.</summary>
        public static bool Available =>
            TestAssets.Languages.Count > 0 && ReswStringProvider.AvailableLanguages().Count > 0;

        /// <summary>Languages the corpus can run in - those with an element table and a strings file.</summary>
        public static IReadOnlyList<string> Languages =>
            Available ? TestAssets.Languages : [];

        private static readonly ConcurrentDictionary<string, Stack> Stacks =
            new(StringComparer.Ordinal);

        /// <summary>The shared English-parsed numeric index. Built once for every language.</summary>
        private static readonly Lazy<KnowledgeStore> BaseStore =
            new(() => KnowledgeStore.Build(TestAssets.English));

        private static readonly Lazy<DatasetIndex> Datasets = new(() => DatasetIndex.Build());

        /// <summary>
        /// Element aliases across every shipped language at once, which is what lets a Swedish query
        /// resolve "Väte" and an English one resolve "Hydrogen" through the same resolver.
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<ElementAlias>>> Aliases =
            new(() => EntityResolver.BuildAliases(
                TestAssets.Languages.ToDictionary(
                    language => language,
                    language => (IReadOnlyDictionary<string, (string, string, string)>)
                        TestAssets.ElementSource.Load(language)!.Rows.ToDictionary(
                            entry => entry.Key,
                            entry => (
                                Text(entry.Value, "element"),
                                Text(entry.Value, "short"),
                                Text(entry.Value, "element_atomic_number")),
                            StringComparer.Ordinal),
                    StringComparer.Ordinal)));

        public static Stack StackFor(string language) =>
            Stacks.GetOrAdd(language, Build);

        private static Stack Build(string language)
        {
            var store = BaseStore.Value;
            var datasets = Datasets.Value;
            var localized = OverlayFor(language);

            // English fallback on, because that is what the app does. Without it every non-English
            // row would fail on the `str:<key>` sentinel from the untranslated agent strings before
            // its routing expectation was ever reached - measuring translation coverage instead of
            // behaviour. StringCoverageTest is where the missing translations get asserted.
            var strings = new ReswStringProvider(language, fallBackToEnglish: true);

            var entities = new EntityResolver(Aliases.Value, language);
            // A real corpus and a real index, so the dataset fallback behaves as it does in the app.
            var index = new Bm25Index(HybridRetriever.BuildCorpus(store, datasets, localized), language);
            var retriever = new HybridRetriever(store, datasets, index, entities, localized);
            var planner = new QueryPlanner(store, datasets, entities, new FieldResolver(strings), retriever);
            var executor = new QueryExecutor(store, datasets, localized, strings);
            var composer = new AnswerComposer(store, localized, strings);
            var engine = new AiEngine(store, datasets, localized, strings, entities, retriever);

            return new Stack(language, store, datasets, localized, strings, entities,
                retriever, index, planner, executor, composer, engine);
        }

        /// <summary>
        /// The seven-field display overlay for a language.
        ///
        /// <para>Built here rather than through <see cref="KnowledgeStore.Overlay"/> so the corpus
        /// does not depend on the process-wide overlay cache, which other tests clear in their
        /// constructors. A corpus run whose overlay vanished mid-file would report failures that
        /// depend on test ordering.</para>
        /// </summary>
        private static LocalizedView? OverlayFor(string language)
        {
            var table = TestAssets.ElementSource.Load(language);
            if (table is null) return null;

            var map = new Dictionary<string, LocalizedElement>(table.Count * 2, StringComparer.Ordinal);
            foreach (var (key, row) in table.Rows)
            {
                map[key] = new LocalizedElement(
                    Name: Text(row, "element"),
                    Description: Text(row, "description"),
                    SeriesName: Text(row, "element_group"),
                    Appearance: Text(row, "element_appearance"),
                    Phase: Text(row, "element_phase"),
                    ElectricalType: Text(row, "electrical_type"),
                    MagneticType: Text(row, "magnetic_type"));
            }
            return new LocalizedView(language, map);
        }

        // ElementRow is a global-using alias inside Atomic.Core, so the test project spells the
        // underlying type out.
        private static string Text(IReadOnlyDictionary<string, object?> row, string jsonKey) =>
            row.TryGetValue(jsonKey, out var value) && value is not null
                ? value as string ?? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;
    }
}
