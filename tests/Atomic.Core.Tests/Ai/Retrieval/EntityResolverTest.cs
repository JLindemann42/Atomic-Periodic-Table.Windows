using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Retrieval;
using Xunit;

namespace Atomic.Core.Tests.Ai.Retrieval
{
    /// <summary>Ported from EntityResolverTest.kt, case for case.</summary>
    public class EntityResolverTest
    {
        /// <summary>name, symbol, atomic number - the three surface forms per element per language.</summary>
        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string, string, string)>> Tables =
            new Dictionary<string, IReadOnlyDictionary<string, (string, string, string)>>
            {
                ["en"] = new Dictionary<string, (string, string, string)>
                {
                    ["gold"] = ("Gold", "Au", "79"),
                    ["silver"] = ("Silver", "Ag", "47"),
                    ["argon"] = ("Argon", "Ar", "18"),
                    ["indium"] = ("Indium", "In", "49"),
                    ["sulfur"] = ("Sulfur", "S", "16"),
                    ["hydrogen"] = ("Hydrogen", "H", "1"),
                    ["iron"] = ("Iron", "Fe", "26"),
                },
                ["sv"] = new Dictionary<string, (string, string, string)>
                {
                    ["gold"] = ("Guld", "Au", "79"),
                    ["hydrogen"] = ("Väte", "H", "1"),
                    ["argon"] = ("Argon", "Ar", "18"),
                },
                ["zh"] = new Dictionary<string, (string, string, string)>
                {
                    ["gold"] = ("金", "Au", "79"),
                    ["iron"] = ("铁", "Fe", "26"),
                    ["helium"] = ("氦", "He", "2"),
                },
                ["ur"] = new Dictionary<string, (string, string, string)>
                {
                    ["gold"] = ("سونا", "Au", "79"),
                },
                // German shares "Gold" and "Argon" with English but not "Eisen" - which is exactly
                // the mix that made shared names misroute. See the detection tests below.
                ["de"] = new Dictionary<string, (string, string, string)>
                {
                    ["gold"] = ("Gold", "Au", "79"),
                    ["argon"] = ("Argon", "Ar", "18"),
                    ["iron"] = ("Eisen", "Fe", "26"),
                },
            };

        private static readonly IReadOnlyDictionary<string, IReadOnlyList<ElementAlias>> Aliases =
            EntityResolver.BuildAliases(Tables);

        private static EntityResolver Resolver(string language = "en") => new(Aliases, language);

        [Fact]
        public void ResolvesByEnglishName() =>
            Assert.Equal("gold", Resolver().Resolve("what is the density of gold")?.Key);

        [Fact]
        public void ResolvesBySymbol() =>
            Assert.Equal("gold", Resolver().Resolve("tell me about Au")?.Key);

        [Fact]
        public void ResolvesByAtomicNumber()
        {
            Assert.Equal("gold", Resolver().Resolve("element 79")?.Key);
            Assert.Equal("hydrogen", Resolver().Resolve("atomic number 1")?.Key);
        }

        [Fact]
        public void AtomicNumberOutOfRangeIsIgnored() =>
            Assert.Null(Resolver().Resolve("element 500")?.Key);

        [Fact]
        public void OutOfRangeNumberIsReportedSoTheAnswerCanSaySo()
        {
            Assert.Equal(119, Resolver().OutOfRangeElementNumber("tell me about element 119"));
            Assert.Null(Resolver().OutOfRangeElementNumber("tell me about element 79"));
            // A bare number with no framing is not a claim about the table.
            Assert.Null(Resolver().OutOfRangeElementNumber("what is 500 divided by 4"));
        }

        [Fact]
        public void ResolvesAcrossLanguages()
        {
            Assert.Equal("gold", Resolver("sv").Resolve("guld")?.Key);
            Assert.Equal("hydrogen", Resolver("sv").Resolve("väte")?.Key);
            Assert.Equal("gold", Resolver("zh").Resolve("金")?.Key);
            Assert.Equal("gold", Resolver("ur").Resolve("سونا")?.Key);
        }

        /// <summary>
        /// The collision cases the legacy agent guarded against. Swedish <c>är</c> normalises to
        /// <c>ar</c>, which is argon's symbol, so "är väte radioaktivt" must resolve to hydrogen,
        /// not argon.
        /// </summary>
        [Fact]
        public void SwedishArDoesNotResolveToArgon() =>
            Assert.Equal("hydrogen", Resolver("sv").Resolve("är väte radioaktivt")?.Key);

        [Fact]
        public void EnglishInDoesNotResolveToIndium()
        {
            var matches = Resolver().ResolveAll("what is in the air");
            Assert.DoesNotContain(matches, m => m.Key == "indium");
        }

        [Fact]
        public void ALoneSymbolStillResolves() =>
            // The guard only applies when the short token sits among other words.
            Assert.Equal("argon", Resolver("sv").Resolve("ar")?.Key);

        [Fact]
        public void ExplicitSymbolFramingOverridesTheGuard() =>
            Assert.Equal("indium", Resolver().Resolve("what element has the symbol In")?.Key);

        [Fact]
        public void ScientistNamesDoNotResolveToSymbols()
        {
            // "poisson" contains no standalone S, but a naive symbol scan used to match it.
            Assert.DoesNotContain(Resolver().ResolveAll("poisson ratio"), m => m.Key == "sulfur");
            Assert.DoesNotContain(Resolver().ResolveAll("planck constant"), m => m.Key == "sulfur");
        }

        /// <summary>
        /// Chinese element names are single characters that also occur inside ordinary words:
        /// 金 is gold, but 金属 means "metal". Resolving the compound to gold would be wrong.
        /// </summary>
        [Fact]
        public void SingleHanCharacterAbsorbedInACompoundIsNotAnElementMention()
        {
            Assert.Equal("gold", Resolver("zh").Resolve("金")?.Key);
            Assert.DoesNotContain(Resolver("zh").ResolveAll("金属的密度"), m => m.Key == "gold");
        }

        [Fact]
        public void ResolvesMultipleElementsForComparison()
        {
            var matches = Resolver().ResolveAll("compare gold and silver");
            Assert.Equal(["gold", "silver"], matches.Select(m => m.Key).OrderBy(k => k).ToArray());
        }

        [Fact]
        public void LongerNameWinsOverAContainedShorterOne() =>
            Assert.Equal("hydrogen", Resolver().ResolveAll("hydrogen")[0].Key);

        [Fact]
        public void TyposAreToleratedOnShortQueries()
        {
            Assert.Equal("gold", Resolver().Resolve("golde")?.Key);
            Assert.Equal("silver", Resolver().Resolve("silvor")?.Key);
        }

        [Fact]
        public void TypoToleranceDoesNotApplyToLongQueries() =>
            // A long sentence with no element named should resolve to nothing rather than guess.
            Assert.Null(Resolver().Resolve("please explain how chemical bonding works in general terms"));

        [Fact]
        public void UnrelatedQueryResolvesToNothing() =>
            Assert.Null(Resolver().Resolve("what is the weather like"));

        [Fact]
        public void NamesScoreHigherThanSymbols()
        {
            var byName = Resolver().Resolve("gold")!;
            var bySymbol = Resolver().Resolve("au")!;
            Assert.True(byName.Score > bySymbol.Score);
        }

        /// <summary>
        /// Not in the Kotlin suite. A mass number and a mole count are both bare integers that
        /// happen to be atomic numbers, and reading either as an element answers a different
        /// question entirely - "is iron-54 stable" came back about xenon.
        /// </summary>
        [Fact]
        public void MassNumbersAndMoleCountsAreNotAtomicNumbers()
        {
            Assert.DoesNotContain(Resolver().ResolveAll("is iron-54 stable"), m => m.Key == "xenon");
            Assert.DoesNotContain(
                Resolver().ResolveAll("if I have 12 grams of carbon-12 how many moles is that"),
                m => m.Key == "magnesium");
        }

        // ---- The exact cases asserted by the legacy reflection test --------------------------

        [Fact]
        public void LegacyCollisionCasesArePreserved()
        {
            Assert.True(EntityResolver.IsCommonWordCollision(
                "ar", "är väte radioaktivt", ["ar", "väte", "radioaktivt"], "sv"));
            Assert.False(EntityResolver.IsCommonWordCollision("ar", "ar", ["ar"], "sv"));
            Assert.True(EntityResolver.IsCommonWordCollision(
                "be", "to be or not to be", ["to", "be", "or", "not", "to", "be"], "en"));
            Assert.True(EntityResolver.IsCommonWordCollision(
                "se", "हाइड्रोजन से क्या होता है", ["हाइड्रोजन", "se", "क्या", "होता", "है"], "hi"));
            Assert.True(EntityResolver.IsCommonWordCollision(
                "ng", "ano ang gamit ng hydrogen", ["ano", "ang", "gamit", "ng", "hydrogen"], "fil"));
            Assert.True(EntityResolver.IsCommonWordCollision(
                "al", "aggiungi acqua al composto", ["aggiungi", "acqua", "al", "composto"], "it"));
            Assert.True(EntityResolver.IsCommonWordCollision(
                "as", "wat gebeur as dit warm word", ["wat", "gebeur", "as", "dit", "warm", "word"], "af"));
        }

        [Fact]
        public void TokensLongerThanTwoCharsAreNeverCollisions() =>
            Assert.False(EntityResolver.IsCommonWordCollision(
                "gold", "gold is dense", ["gold", "is", "dense"], "en"));

        // ---- Language detection -----------------------------------------------------------------

        /// <summary>
        /// A name only one language uses is real evidence, and outweighs the active language.
        /// </summary>
        [Fact]
        public void ADistinctiveNameSwitchesTheAnswerLanguage()
        {
            Assert.Equal("sv", Resolver("en").DetectLanguage("hur tung ar guld", "en"));
            Assert.Equal("de", Resolver("en").DetectLanguage("wie schwer ist eisen", "en"));
        }

        /// <summary>
        /// The defect this rule exists to prevent.
        ///
        /// <para>"Gold" is the English, German and Afrikaans name for the same element, so it says
        /// nothing about which language the user is writing in. The earlier rule scored all three
        /// and then deliberately discarded English on a tie, which answered "electron configuration
        /// of gold" in German - correct data, wrong language, and no way for the user to tell why.
        /// A shared name now contributes nothing and the query keeps the active language.</para>
        /// </summary>
        [Fact]
        public void ANameSeveralLanguagesShareIsNotEvidenceForAnyOfThem()
        {
            Assert.Equal("en", Resolver("en").DetectLanguage("electron configuration of gold", "en"));
            Assert.Equal("sv", Resolver("sv").DetectLanguage("electron configuration of gold", "sv"));
            Assert.Equal("sv", Resolver("sv").DetectLanguage("what is argon used for", "sv"));
        }

        /// <summary>A query naming no element at all cannot change the language.</summary>
        [Fact]
        public void AQueryWithNoElementKeepsTheActiveLanguage()
        {
            Assert.Equal("sv", Resolver("sv").DetectLanguage("which element has the highest density", "sv"));
            Assert.Equal("en", Resolver("en").DetectLanguage("vilket grundamne ar tyngst", "en"));
        }

        /// <summary>
        /// A script with no spaces has to be found by scanning, not by splitting - and the same
        /// distinctiveness rule has to apply on that path too.
        /// </summary>
        [Fact]
        public void ADistinctiveNameIsFoundInAScriptThatWritesNoSpaces() =>
            Assert.Equal("zh", Resolver("en").DetectLanguage("金的密度", "en"));

    }
}
