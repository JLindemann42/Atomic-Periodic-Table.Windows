using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Retrieval;
using Xunit;

namespace Atomic.Core.Tests.Ai.Retrieval
{
    /// <summary>Ported from Bm25IndexTest.kt, case for case.</summary>
    public class Bm25IndexTest
    {
        private static readonly IReadOnlyList<Document> Corpus =
        [
            new("element:gold", "Gold", "Gold is a chemical element with symbol Au and atomic number 79. A dense soft yellow transition metal."),
            new("element:silver", "Silver", "Silver is a chemical element with symbol Ag and atomic number 47. A soft white lustrous transition metal."),
            new("element:tungsten", "Tungsten", "Tungsten is a chemical element with symbol W and atomic number 74. It has the highest melting point of all elements."),
            new("element:helium", "Helium", "Helium is a chemical element with symbol He and atomic number 2. A colourless odourless inert noble gas."),
            new("dictionary:Density", "Density", "Dictionary entry in categories chemistry physics. The density of a material is its mass per unit volume."),
            new("dictionary:Melting point", "Melting point", "Dictionary entry. The temperature at which a solid becomes a liquid."),
            new("constant:Pi", "Pi", "Constant in category mathematics. Value 3.14159265358979323846."),
            new("poisson:Andesite", "Andesite", "Poisson ratio for rock. Range 0.20 to 0.35."),
            new("equation:Density", "Density", "Equation in category General. p Density m Mass V Volume."),
            new("geology:Graphite", "Graphite", "Mineral. Group native element. Colour black. Streak black. Hardness 1.5."),
        ];

        private static readonly Bm25Index Index = new(Corpus);

        [Fact]
        public void FindsTheObviousDocument() =>
            Assert.Equal("element:tungsten", Index.Search("tungsten melting point")[0].Id);

        [Fact]
        public void RankingPrefersTheMoreSpecificMatch() =>
            Assert.Equal("element:helium", Index.Search("noble gas")[0].Id);

        /// <summary>
        /// Terms appearing in most documents contribute almost nothing, which is what makes a
        /// hand-maintained stopword list in twelve languages unnecessary.
        /// </summary>
        [Fact]
        public void HighDocumentFrequencyTermsCarryAlmostNoWeight()
        {
            // "chemical" and "element" appear in four of the ten documents; "tungsten" in one.
            var commonOnly = Index.Search("chemical element").FirstOrDefault()?.Score ?? 0.0;
            var rareOnly = Index.Search("tungsten").FirstOrDefault()?.Score ?? 0.0;
            Assert.True(rareOnly > commonOnly / 2, "a rare term must outscore common ones");
        }

        [Fact]
        public void DocumentFrequencyIsReported()
        {
            Assert.Equal(1, Index.DocumentFrequency("tungsten"));
            Assert.Equal(0, Index.DocumentFrequency("frobnicate"));
            Assert.True(Index.DocumentFrequency("element") >= 4);
        }

        [Fact]
        public void PrefixFilterRestrictsToOneKind()
        {
            // "density" matches an element, a dictionary entry and an equation; the filter isolates one.
            var all = Index.Search("density");
            var onlyDictionary = Index.Search("density", prefixFilter: "dictionary:");

            Assert.Contains(all, h => !h.Id.StartsWith("dictionary:", System.StringComparison.Ordinal));
            Assert.NotEmpty(onlyDictionary);
            Assert.All(onlyDictionary,
                       h => Assert.StartsWith("dictionary:", h.Id, System.StringComparison.Ordinal));
        }

        [Fact]
        public void NormalizedScoresAreRelativeToTheTopHit()
        {
            var hits = Index.SearchNormalized("tungsten melting point");
            Assert.Equal(1.0, hits[0].Score, 1e-9);
            Assert.All(hits, h => Assert.InRange(h.Score, 0.0, 1.0));
        }

        [Fact]
        public void EmptyAndUnmatchedQueriesReturnNothing()
        {
            Assert.Empty(Index.Search(""));
            Assert.Empty(Index.Search("   "));
            Assert.Empty(Index.Search("frobnicate xyzzy"));
        }

        [Fact]
        public void LimitIsRespected() => Assert.True(Index.Search("element", limit: 2).Count <= 2);

        [Fact]
        public void EmptyIndexIsSafe() => Assert.Empty(new Bm25Index([]).Search("anything"));

        [Fact]
        public void MatchingIsCaseAndAccentInsensitive() =>
            Assert.Equal(Index.Search("gold").FirstOrDefault()?.Id,
                         Index.Search("GOLD").FirstOrDefault()?.Id);

        [Fact]
        public void ChineseCorpusIsSearchableWithoutWordSpacing()
        {
            var zh = new Bm25Index(
            [
                new("element:gold", "金", "金是一种化学元素 符号为Au 原子序数为79 是一种过渡金属"),
                new("element:helium", "氦", "氦是一种化学元素 符号为He 原子序数为2 是一种惰性气体"),
            ],
            language: "zh");

            Assert.Equal("element:gold", zh.Search("过渡金属")[0].Id);
            Assert.Equal("element:helium", zh.Search("惰性气体")[0].Id);
        }

        /// <summary>
        /// Not in the Kotlin suite. BM25 always ranks something, so abstention is what stops an
        /// off-topic query being answered - and it takes both halves.
        ///
        /// <para>MentionsUnknownTerm alone is not a topicality test and must not be read as one:
        /// "what" is a perfectly ordinary word that appears nowhere in a corpus of element
        /// descriptions, so an on-topic question trips it just as an off-topic one does. That is
        /// the documented contract - the weakest possible question, "has this word ever been
        /// written about the periodic table" - and SharesTermWithTitle is the half that
        /// discriminates.</para>
        /// </summary>
        [Fact]
        public void UnknownTermAloneDoesNotMeanOffTopic()
        {
            Assert.True(Index.MentionsUnknownTerm("what is the density of gold"));
            Assert.True(Index.MentionsUnknownTerm("write me a poem about frobnication"));

            // A query with no unknown content word at all is unambiguously on-topic.
            Assert.False(Index.MentionsUnknownTerm("density of gold"));
        }

        [Fact]
        public void SharedTitleTermsAreWhatSeparateOnTopicFromOffTopic()
        {
            Assert.True(Index.SharesTermWithTitle("melting point of tungsten", "Tungsten"));

            // The case this exists for: "write me a poem" retrieves "Electron mass" and shares
            // nothing with it, so the answer is declined rather than composed.
            Assert.False(Index.SharesTermWithTitle("write me a poem", "Density"));

            // Below three characters a shared token is grammar, not subject.
            Assert.False(Index.SharesTermWithTitle("is it a metal", "Pi"));
        }
    }
}
