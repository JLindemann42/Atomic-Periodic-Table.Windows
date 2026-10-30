using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// Which isotopes the tables call stable.
    ///
    /// <para>It was wrong three ways that no parser can see: stable written as "N/A" for atomic
    /// numbers 70-82, so gold had forty isotopes and none stable; stable isotopes carrying a
    /// metastable state's half-life; and radioactive isotopes marked stable, because dysprosium's
    /// half-life column was pasted over holmium and erbium. These pin the corrected data against
    /// the table of stable nuclides, so a re-import cannot bring any of it back.</para>
    /// </summary>
    public class StableIsotopeDataTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static IReadOnlyList<int> Stable(string key) =>
            Service.Store.Element(key)!.Isotopes
                .Where(i => i.Stable)
                .Select(i => i.Nucleons!.Value)
                .OrderBy(a => a)
                .ToList();

        /// <summary>
        /// Gold was the reported case: "40 known isotopes, 0 of them stable".
        /// </summary>
        [Theory]
        [InlineData("gold", new[] { 197 })]
        [InlineData("lutetium", new[] { 175 })]
        [InlineData("praseodymium", new[] { 141 })]
        [InlineData("tantalum", new[] { 181 })]
        [InlineData("holmium", new[] { 165 })]
        [InlineData("iridium", new[] { 191, 193 })]
        [InlineData("thallium", new[] { 203, 205 })]
        [InlineData("lead", new[] { 204, 206, 207, 208 })]
        [InlineData("mercury", new[] { 196, 198, 199, 200, 201, 202, 204 })]
        [InlineData("platinum", new[] { 194, 195, 196, 198 })]
        public void TheStableIsotopesAreTheRealOnes(string element, int[] expected) =>
            Assert.Equal(expected, Stable(element));

        /// <summary>
        /// "N/A" is not a stable marker. The same block holds isotopes that are radioactive with a
        /// half-life nobody filled in, and treating every "N/A" as stable would have been wrong in
        /// seven places.
        /// </summary>
        [Theory]
        [InlineData("hafnium", 175)]   // 70 days
        [InlineData("thallium", 197)]  // 2.8 hours
        [InlineData("thallium", 198)]
        [InlineData("thallium", 199)]
        [InlineData("thallium", 200)]
        [InlineData("rhenium", 187)]   // 4 x 10^10 years
        public void ARadioactiveIsotopeWithNoHalfLifeIsNotPromoted(string element, int mass)
        {
            var isotope = Service.Store.Element(element)!.Isotopes.Single(i => i.Nucleons == mass);

            Assert.False(isotope.Stable);
        }

        /// <summary>
        /// Dysprosium's half-life column had been pasted over holmium and erbium. Holmium has one
        /// stable isotope and listed six; erbium has six and listed eight, only two of them real.
        /// </summary>
        [Fact]
        public void HolmiumAndErbiumDoNotInheritDysprosiumsStableIsotopes()
        {
            Assert.Equal(new[] { 165 }, Stable("holmium"));
            Assert.Equal(new[] { 162, 164, 166, 167, 168, 170 }, Stable("erbium"));
            Assert.DoesNotContain(157, Stable("dysprosium"));
        }

        /// <summary>
        /// Iridium's two stable isotopes carried the half-lives of their isomers - Ir-193m is 10.5
        /// days - so the element had no stable isotope at all.
        /// </summary>
        [Fact]
        public void AStableGroundStateDoesNotCarryItsIsomersHalfLife()
        {
            var iridium = Service.Store.Element("iridium")!.Isotopes;

            Assert.All(iridium.Where(i => i.Nucleons is 191 or 193), i => Assert.True(i.Stable));
        }

        /// <summary>
        /// The elements with no stable isotope at all are exactly the ones that really have none:
        /// technetium, promethium, bismuth (Bi-209 outlives the universe but does decay) and
        /// everything heavier.
        /// </summary>
        [Fact]
        public void OnlyTheGenuinelyUnstableElementsHaveNoStableIsotope()
        {
            var none = Service.Store.Elements
                .Where(e => e.Isotopes.Count > 0 && e.StableIsotopeCount == 0)
                .Select(e => e.AtomicNumber)
                .OrderBy(z => z)
                .ToList();

            // Subset, not equality: 107 onwards carry no isotope data at all.
            var unstable = new HashSet<int>(new[] { 43, 61 }.Concat(Enumerable.Range(83, 118 - 83 + 1)));

            Assert.Empty(none.Where(z => !unstable.Contains(z)));

            // And all of the elements that do have one are accounted for - 43, 61 and 83-106.
            Assert.Equal(new[] { 43, 61 }.Concat(Enumerable.Range(83, 106 - 83 + 1)), none);
        }

        /// <summary>
        /// The mass number comes from iso_A, not from the atomic mass. Reading iso_mass and keeping
        /// only its digits made gold's 196966568, and the isotope chart's axis ran to nine digits.
        /// </summary>
        [Fact]
        public void TheMassNumberIsTheMassNumber()
        {
            var gold = Service.Store.Element("gold")!.Isotopes.Single(i => i.Nucleons == 197);

            Assert.Equal(197, gold.MassNumber);
            Assert.All(
                Service.Store.Elements.SelectMany(e => e.Isotopes),
                i => Assert.True(
                    i.MassNumber is null or < 400,
                    $"{i.Name} has mass number {i.MassNumber}"));
        }
    }
}
