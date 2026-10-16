using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Retrieval
{
    /// <summary>
    /// The retrieval stack end to end, against the real shipped element tables.
    ///
    /// <para>No Kotlin equivalent - Android exercises this path only through the 3,162-row corpus,
    /// which arrives in Phase 4. Until then these are what prove the wiring holds: a store that
    /// parses, a corpus that indexes, and a retriever that fuses the two.</para>
    ///
    /// <para>The service caches process-wide, so the fixture builds it once and no test mutates it.</para>
    /// </summary>
    public class RetrievalServiceTest : IDisposable
    {
        private readonly RetrievalService _service;

        public RetrievalServiceTest()
        {
            RetrievalService.Clear();
            _service = RetrievalService.Get(TestAssets.ElementSource)
                ?? throw new InvalidOperationException("element assets not readable");
        }

        public void Dispose() => RetrievalService.Clear();

        [Fact]
        public void StoreParsesEveryElement()
        {
            Assert.Equal(118, _service.Store.Size);
            Assert.Equal("gold", _service.Store.Element("gold")?.Key);
            Assert.Equal("gold", _service.Store.BySymbol("Au")?.Key);
            Assert.Equal("gold", _service.Store.ByNumber(79)?.Key);
        }

        [Fact]
        public void DerivedFieldsAreFilledInForEveryElement()
        {
            // Period, group, valence and the synthetic flag are computed rather than stored, so a
            // gap here means the derivation missed a block, not that the data is thin.
            Assert.Equal(118, _service.Store.CoverageOf("period"));
            Assert.Equal(118, _service.Store.CoverageOf("synthetic"));

            var gold = _service.Store.Element("gold")!;
            Assert.Equal(6, gold.Period);
            Assert.Equal(11, gold.GroupNumber);
            Assert.False(gold.Synthetic);

            Assert.True(_service.Store.Element("americium")!.Synthetic);
        }

        [Fact]
        public void CorpusCoversEveryElementAndEveryDatasetRow()
        {
            var corpus = HybridRetriever.BuildCorpus(
                _service.Store, _service.Datasets, _service.Localized("en"));

            Assert.Equal(_service.Store.Size + _service.Datasets.Rows.Count, corpus.Count);
            Assert.All(corpus, d => Assert.False(string.IsNullOrWhiteSpace(d.Body)));
        }

        [Fact]
        public void FindsAnElementByNameThroughTheFusedRetriever()
        {
            var hit = _service.Retriever("en").Best("what is the density of gold");
            Assert.NotNull(hit);
            Assert.Equal(new RetrievedRef.Element("gold"), hit!.Ref);
        }

        [Fact]
        public void StructuredEvidenceOutranksLexicalAlone()
        {
            // "gold" resolves as an entity *and* matches lexically, so it must beat a dictionary
            // row that only shares a word.
            var hits = _service.Retriever("en").Search("gold", limit: 3);
            Assert.Equal(new RetrievedRef.Element("gold"), hits[0].Ref);
            Assert.True(hits[0].Structured > 0.0);
        }

        [Fact]
        public void DatasetRowsAreReachable()
        {
            var hits = _service.Retriever("en").Search("poisson ratio of basalt", limit: 5);
            Assert.Contains(hits, h => h.Ref is RetrievedRef.Dataset { Name: DatasetIndex.Datasets.Poisson });
        }

        /// <summary>
        /// The gloss that makes symbol-keyed tables findable by element name. Without it, "standard
        /// electrode potential of zinc" shares no word with the row titled "Zn" and retrieves a
        /// dictionary definition instead.
        /// </summary>
        [Fact]
        public void SymbolKeyedRowsAreFoundByTheElementName()
        {
            var corpus = HybridRetriever.BuildCorpus(
                _service.Store, _service.Datasets, _service.Localized("en"));

            var zinc = corpus.Single(d => d.Id == $"{DatasetIndex.Datasets.Electrode}:Zn");
            Assert.Contains("zinc", zinc.Body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void OffTopicQueriesAreDeclinedRatherThanAnswered()
        {
            var retriever = _service.Retriever("en");
            var hit = retriever.Best("write me a poem about the sea");

            // BM25 always ranks something, so a hit is expected - what matters is that the
            // relevance test rejects it.
            if (hit is not null)
                Assert.False(retriever.LooksRelevant("write me a poem about the sea", hit));
        }

        [Fact]
        public void SwedishNamesResolveAndTitlesAreLocalized()
        {
            var swedish = _service.Retriever("sv");
            var hit = swedish.Best("vad är guldets densitet");

            Assert.NotNull(hit);
            Assert.Equal(new RetrievedRef.Element("gold"), hit!.Ref);
            // "guldets" is inflected, and the title comes from the Swedish overlay.
            Assert.Equal("Guld", hit.Title);
        }

        [Fact]
        public void AForeignNameStillResolvesWhileTheChatRunsInEnglish()
        {
            // The alias table spans every shipped language on purpose, so a user can write the
            // element in their own language regardless of the interface language.
            Assert.Equal("gold", _service.Resolver("en").Resolve("guld")?.Key);
        }

        [Fact]
        public void IndexesAndResolversAreCachedPerLanguage()
        {
            Assert.Same(_service.Resolver("sv"), _service.Resolver("sv"));
            Assert.NotSame(_service.Resolver("sv"), _service.Resolver("de"));
        }

        /// <summary>
        /// The store parses once from English and overlays the seven prose fields per language, so
        /// a Swedish question about gold's phase is not answered "Solid".
        /// </summary>
        [Fact]
        public void ProseFieldsComeFromTheLanguageOverlay()
        {
            var view = _service.Localized("sv")!;

            Assert.Equal("Fast", view.Text("gold", "phase"));
            Assert.Equal("Guld", view.Text("gold", "name"));
            Assert.Equal("Övergångsmetaller", view.Text("gold", "series"));

            // A field the overlay does not carry is null rather than an English fallback, so the
            // caller can decide whether to say so.
            Assert.Null(view.Text("gold", "density"));
            Assert.Null(view.Text("not an element", "phase"));
        }

        /// <summary>
        /// <b>A data gap, not a code defect, and it is on both platforms.</b>
        ///
        /// <para>Two of the seven overlaid fields are barely translated in the shipped JSON:
        /// <c>element_appearance</c> is identical to English for every element in Afrikaans,
        /// Italian and Swedish, and for 65-93% of them in six more languages; only Urdu is mostly
        /// done. <c>electrical_type</c> is 69-95% English everywhere.</para>
        ///
        /// <para>That means the agent will answer a Swedish question about gold's appearance with
        /// "Metallic yellow" - correctly reading the Swedish file, which simply holds the English
        /// string. The overlay has nothing to fix; the translation does. Pinned here so the next
        /// person to see it in an answer knows it is expected and where it comes from.</para>
        /// </summary>
        [Fact]
        public void AppearanceIsUntranslatedInTheDataAndThatIsKnown()
        {
            Assert.Equal(_service.Localized("en")!.Text("gold", "appearance"),
                         _service.Localized("sv")!.Text("gold", "appearance"));
        }

        /// <summary>
        /// The whole stack under a comma-decimal, dotted-thousands culture. Parsing an element
        /// table under sv-SE with an uncontrolled parse reads 1.234 as 1234.
        /// </summary>
        [Fact]
        public void BuildingUnderAHostileCultureGivesTheSameNumbers()
        {
            var goldDensity = _service.Store.Element("gold")!.Quantity("density")!.Value;

            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");
                RetrievalService.Clear();
                var swedishBuild = RetrievalService.Get(TestAssets.ElementSource)!;
                Assert.Equal(goldDensity, swedishBuild.Store.Element("gold")!.Quantity("density")!.Value);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }
}
