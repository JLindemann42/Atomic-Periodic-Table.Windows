using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Localization
{
    /// <summary>
    /// <see cref="IStringProvider.Plural"/> against the resources that actually ship.
    /// </summary>
    public class PluralLookupTest
    {
        private static IStringProvider For(string language) => new ReswStringProvider(language);

        [Fact]
        public void EnglishPicksSingularAndPlural()
        {
            var english = For("en-US");

            Assert.Equal("Lost 1 life", english.Plural(S.lives_lost_result, 1));
            Assert.Equal("Lost 3 lives", english.Plural(S.lives_lost_result, 3));
        }

        /// <summary>
        /// The case the ternary at the old call site could not express: French inflects the noun
        /// *and* the participle, and puts zero in the singular.
        /// </summary>
        [Fact]
        public void FrenchInflectsAndTreatsZeroAsSingular()
        {
            var french = For("fr");

            Assert.Equal("0 vie perdue", french.Plural(S.lives_lost_result, 0));
            Assert.Equal("1 vie perdue", french.Plural(S.lives_lost_result, 1));
            Assert.Equal("2 vies perdues", french.Plural(S.lives_lost_result, 2));
        }

        /// <summary>
        /// Chinese has no <c>.One</c> resource at all. The rule answers "other" before the lookup
        /// is attempted, so this must not fall through to the English singular.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public void ChineseAlwaysUsesTheOtherForm(int count)
        {
            var chinese = For("zh-Hans");
            var text = chinese.Plural(S.lives_lost_result, count);

            Assert.DoesNotContain("Lost", text, System.StringComparison.Ordinal);
            Assert.Contains(count.ToString(System.Globalization.CultureInfo.InvariantCulture), text,
                            System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Swedish spells both forms the same. That is a real translation, not a missing one, and
        /// the count still has to reach the string.
        /// </summary>
        [Fact]
        public void SwedishUsesOneWordForBoth()
        {
            var swedish = For("sv");

            Assert.Equal("Förlorade 1 liv", swedish.Plural(S.lives_lost_result, 1));
            Assert.Equal("Förlorade 2 liv", swedish.Plural(S.lives_lost_result, 2));
        }

        [Fact]
        public void HasAnswersForBothForms()
        {
            var english = For("en-US");

            Assert.True(english.Has(S.lives_lost_result_One));
            Assert.True(english.Has(S.lives_lost_result_Other));
            Assert.False(english.Has("lives_lost_result.NoSuchForm"));
        }

        /// <summary>Every shipped language must produce something that contains the count.</summary>
        [Theory]
        [InlineData("en-US")]
        [InlineData("sv")]
        [InlineData("de")]
        [InlineData("fr")]
        [InlineData("es")]
        [InlineData("it")]
        [InlineData("pt-BR")]
        [InlineData("af")]
        [InlineData("fil")]
        [InlineData("hi")]
        [InlineData("ur")]
        [InlineData("zh-Hans")]
        public void EveryLanguageResolvesAndKeepsTheCount(string language)
        {
            var strings = For(language);

            foreach (var count in new[] { 0, 1, 2 })
            {
                var text = strings.Plural(S.lives_lost_result, count);

                Assert.DoesNotContain(ReswStringProvider.MissingPrefix, text, System.StringComparison.Ordinal);
                Assert.Contains(count.ToString(System.Globalization.CultureInfo.InvariantCulture), text,
                                System.StringComparison.Ordinal);
            }
        }
    }
}
