using Atomic.Core.Localization;
using Xunit;

namespace Atomic.Core.Tests.Localization
{
    /// <summary>
    /// The plural rule for each shipped language.
    ///
    /// <para>These exist because <c>count == 1</c> is wrong in three of the twelve, and wrong
    /// invisibly: "0 vie perdue" is correct French and "0 vies perdues" is not, and nobody reading
    /// the English build will ever notice.</para>
    /// </summary>
    public class PluralRulesTest
    {
        [Theory]
        [InlineData("en")]
        [InlineData("sv")]
        [InlineData("de")]
        [InlineData("es")]
        [InlineData("it")]
        [InlineData("af")]
        [InlineData("ur")]
        [InlineData("fil")]
        public void MostLanguagesUseTheSingularOnlyForOne(string language)
        {
            Assert.False(PluralRules.IsOne(language, 0));
            Assert.True(PluralRules.IsOne(language, 1));
            Assert.False(PluralRules.IsOne(language, 2));
            Assert.False(PluralRules.IsOne(language, 11));
        }

        /// <summary>French, Portuguese and Hindi put zero in the singular alongside one.</summary>
        [Theory]
        [InlineData("fr")]
        [InlineData("pt")]
        [InlineData("hi")]
        public void ThreeLanguagesPutZeroInTheSingular(string language)
        {
            Assert.True(PluralRules.IsOne(language, 0));
            Assert.True(PluralRules.IsOne(language, 1));
            Assert.False(PluralRules.IsOne(language, 2));
        }

        /// <summary>
        /// Chinese has no singular form, which is why its <c>lives_lost_result.One</c> does not
        /// exist in the shipped resources.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        public void ChineseNeverTakesTheSingular(int count) =>
            Assert.False(PluralRules.IsOne("zh-Hans", count));

        [Fact]
        public void ARegionIsIgnored()
        {
            Assert.True(PluralRules.IsOne("pt-BR", 0));
            Assert.True(PluralRules.IsOne("es-MX", 1));
            Assert.False(PluralRules.IsOne("es-MX", 0));
        }

        [Fact]
        public void CaseDoesNotMatter() => Assert.True(PluralRules.IsOne("FR", 0));

        /// <summary>
        /// An unknown language must not throw - it arrives from a settings value or a detected
        /// chat language, neither of which this table controls.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("xx")]
        public void AnUnknownLanguageFallsBackToTheCommonRule(string? language)
        {
            Assert.True(PluralRules.IsOne(language, 1));
            Assert.False(PluralRules.IsOne(language, 2));
        }

        [Fact]
        public void SuffixesMatchTheResourceNames()
        {
            Assert.Equal(".One", PluralRules.SuffixFor("en", 1));
            Assert.Equal(".Other", PluralRules.SuffixFor("en", 2));
            Assert.Equal(".Other", PluralRules.SuffixFor("zh-Hans", 1));
        }

        /// <summary>A negative count is a bug upstream, but it must still pick a form.</summary>
        [Fact]
        public void ANegativeCountTakesThePlural() => Assert.False(PluralRules.IsOne("en", -1));
    }
}
