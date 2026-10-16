using Atomic.Core.Links;
using Xunit;

namespace Atomic.Core.Tests.Links
{
    public sealed class AppLinksTest
    {
        [Theory]
        [InlineData("atomic://flashcards", "FlashcardsPage")]
        [InlineData("atomic://account", "AccountPage")]
        [InlineData("atomic://balancer", "EquationBalancerPage")]
        [InlineData("atomic://dictionary", "DictionaryPage")]
        [InlineData("atomic://table", AppLinks.HomeRoute)]
        public void AKnownHostResolvesToItsRoute(string url, string expected) =>
            Assert.Equal(expected, AppLinks.Parse(url).RouteTag);

        /// <summary>A scheme is case-insensitive, and Windows does not normalise it for us.</summary>
        [Theory]
        [InlineData("ATOMIC://flashcards")]
        [InlineData("Atomic://FLASHCARDS")]
        public void TheSchemeAndHostAreCaseInsensitive(string url) =>
            Assert.Equal("FlashcardsPage", AppLinks.Parse(url).RouteTag);

        [Fact]
        public void AnAskLinkCarriesTheQuestionAndNamesNoPage()
        {
            var link = AppLinks.Parse("atomic://ask?q=what%20is%20the%20density%20of%20gold");

            Assert.Null(link.RouteTag);
            Assert.Equal("what is the density of gold", link.Query);
        }

        /// <summary>
        /// A widget text box sends spaces as '+', which <c>UnescapeDataString</c> leaves alone - so
        /// the question would arrive with literal plus signs in it.
        /// </summary>
        [Fact]
        public void PlusEncodedSpacesBecomeSpaces() =>
            Assert.Equal(
                "melting point of iron",
                AppLinks.Parse("atomic://ask?q=melting+point+of+iron").Query);

        [Fact]
        public void AnElementLinkCarriesTheKey()
        {
            var link = AppLinks.Parse("atomic://element?key=gold");

            Assert.Equal("ElementDetailsPage", link.RouteTag);
            Assert.Equal("gold", link.ElementKey);
        }

        /// <summary>
        /// Activation runs before any window exists, so an unknown or malformed link must land
        /// somewhere rather than throw - a throw there kills the app on launch with no UI to say
        /// why, which is the worst possible place for one.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("atomic://nonsense")]
        [InlineData("atomic://ask")]
        [InlineData("atomic://ask?q=")]
        [InlineData("atomic://ask?q=%20%20")]
        [InlineData("atomic://element")]
        [InlineData("atomic://element?key=")]
        [InlineData("atomic:")]
        [InlineData("https://example.com/atomic")]
        [InlineData("not a uri at all")]
        [InlineData("atomic://ask?malformed&&&=")]
        public void AnythingUnrecognisedLandsOnThePeriodicTable(string? url)
        {
            var link = AppLinks.Parse(url);

            // Either it named the home route, or it is an ask link with nothing to ask - both of
            // which the host opens at the table.
            Assert.True(
                link.RouteTag == AppLinks.HomeRoute || (link.RouteTag is null && link.Query is null),
                $"'{url}' produced route '{link.RouteTag}' with query '{link.Query}'");
        }

        /// <summary>A query value containing an '=' must not be truncated at it.</summary>
        [Fact]
        public void AQuestionContainingAnEqualsSignSurvives() =>
            Assert.Equal("is a=b", AppLinks.Parse("atomic://ask?q=is%20a=b").Query);
    }
}
