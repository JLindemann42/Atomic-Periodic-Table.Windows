using System;
using System.Linq;
using Atomic.Core.Models;
using Xunit;

namespace Atomic.Core.Tests.Models
{
    public class AlloyModelTest
    {
        /// <summary>
        /// Every base metal has to be a lowercase English element key, because the "go to base
        /// element" link feeds it straight to the element repository. A capitalised or misspelled
        /// base would compile, pass every other test, and fail only when a user taps the link.
        /// </summary>
        [Fact]
        public void EveryBaseIsALowercaseElementKey()
        {
            string[] elements =
            [
                "iron", "copper", "tin", "aluminium", "titanium",
                "nickel", "silver", "gold", "mercury",
            ];

            foreach (var alloy in AlloyModel.All)
                Assert.Contains(alloy.Base, elements);
        }

        [Fact]
        public void EveryBaseHasAStringKeyNamingConvention()
        {
            // AlloyPage builds "alloy_base_" + Base and looks it up. A base with a space or an
            // uppercase letter would produce a key that does not exist in any .resw.
            foreach (var metal in AlloyModel.BaseMetals)
                Assert.Equal(metal.ToLowerInvariant(), metal);
        }

        [Fact]
        public void BaseMetalsIsDerivedAndDeduplicated()
        {
            Assert.Equal(AlloyModel.BaseMetals.Count, AlloyModel.BaseMetals.Distinct(StringComparer.Ordinal).Count());
            Assert.All(AlloyModel.All, a => Assert.Contains(a.Base, AlloyModel.BaseMetals));
        }

        /// <summary>
        /// The reason search covers the composition and not just the name: zinc is what brass is
        /// made of, and it appears nowhere in the word "Brass".
        /// </summary>
        [Fact]
        public void SearchMatchesComposition()
        {
            var hits = AlloyModel.Filter(null, "zinc");
            Assert.Contains(hits, a => a.Name == "Brass");
        }

        [Fact]
        public void SearchIsCaseInsensitiveAndTrimmed()
        {
            Assert.Equal(
                AlloyModel.Filter(null, "STEEL").Select(a => a.Name),
                AlloyModel.Filter(null, "  steel ").Select(a => a.Name));
        }

        [Fact]
        public void BaseFilterAndSearchCombine()
        {
            var copper = AlloyModel.Filter("copper", null);
            Assert.All(copper, a => Assert.Equal("copper", a.Base));

            var tinnedCopper = AlloyModel.Filter("copper", "tin");
            Assert.Contains(tinnedCopper, a => a.Name == "Bronze");
            Assert.DoesNotContain(tinnedCopper, a => a.Name == "Solder");  // tin-based, not copper
        }

        [Fact]
        public void NullFilterAndBlankSearchReturnEverything()
        {
            Assert.Equal(AlloyModel.All.Count, AlloyModel.Filter(null, null).Count);
            Assert.Equal(AlloyModel.All.Count, AlloyModel.Filter(null, "   ").Count);
            Assert.Equal(AlloyModel.All.Count, AlloyModel.Filter("", "").Count);
        }

        [Fact]
        public void NamesAreUniqueBecauseTheListLooksThemUpByName()
        {
            // AlloyPage passes the name through Button.Tag and looks the alloy back up by it.
            var names = AlloyModel.All.Select(a => a.Name).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void EveryAlloyIsFullyPopulated()
        {
            foreach (var a in AlloyModel.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(a.Name));
                Assert.False(string.IsNullOrWhiteSpace(a.Composition));
                Assert.False(string.IsNullOrWhiteSpace(a.Description));
                Assert.StartsWith("https://en.wikipedia.org/wiki/", a.Wiki, StringComparison.Ordinal);
            }
        }
    }
}
