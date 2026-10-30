using System;
using System.Collections.Generic;
using Atomic.Core.Ai.Nlu;
using Xunit;

namespace Atomic.Core.Tests.Ai.Nlu
{
    /// <summary>
    /// The speller's rules (roadmap 1.6), on a vocabulary small enough to read. Mirrors Android's
    /// <c>SpellerTest</c>.
    /// </summary>
    public class SpellerTest
    {
        private static IReadOnlySet<string> Sense(string s) => new HashSet<string>(StringComparer.Ordinal) { s };

        private readonly Speller _speller = new(
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["density"] = Sense("field:density"),
                ["gold"] = Sense("element:gold"), ["goal"] = Sense("element:other"),
                ["plomb"] = Sense("element:lead"), ["plomo"] = Sense("element:lead"),
                ["atommassa"] = Sense("field:atomic_mass"),
                ["bore"] = Sense("element:boron"),
            },
            known: ["more"]);

        [Fact]
        public void ATranspositionIsOneEdit()
        {
            Assert.Equal("what is the density of gold", _speller.Correct("what is the denisty of gold"));
            Assert.Equal(1, Speller.Distance("denisty", "density", 2));
        }

        [Fact]
        public void ALongWordMayBeTwoEditsAway() =>
            Assert.Equal("vad är atommassa för silver", _speller.Correct("vad är atommasan för silver"));

        [Fact]
        public void TwoSpellingsOfOneThingAreNoAmbiguity() =>
            Assert.Equal("du plomb", _speller.Correct("du plomp"));

        [Fact]
        public void AWordBetweenTwoMeaningsIsLeftAlone() =>
            Assert.Equal("goad", _speller.Correct("goad"));

        [Fact]
        public void ProtectedShortAndForeignWordsAreLeftAlone()
        {
            Assert.Equal("more", _speller.Correct("more"));
            Assert.Equal("gol", _speller.Correct("gol"));
            Assert.Equal("金的密度", _speller.Correct("金的密度"));
            Assert.Equal("density of gold", _speller.Correct("density of gold"));
        }
    }
}
