using System.Linq;
using Atomic.Core.Ai.Data;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// The element photo URL, which must be byte-identical to the one Android asks for - the files
    /// are named on a server neither app controls, so a difference is a 404 rather than a fallback.
    /// </summary>
    public class ElementImagesTest
    {
        [Fact]
        public void MatchesTheAndroidUrlExactly() =>
            Assert.Equal("https://jlindemann.se/atomic/element_images/Aluminium.jpg",
                         ElementImages.UrlFor("Aluminium"));

        /// <summary>
        /// A few rows in elements_en.json are authored lower case. Android capitalises defensively
        /// for exactly this reason, and the files on the server are capitalised.
        /// </summary>
        [Theory]
        [InlineData("tennessine", "Tennessine")]
        [InlineData("gold", "Gold")]
        [InlineData("Oganesson", "Oganesson")]
        public void TheFirstLetterIsCapitalised(string name, string expectedFile) =>
            Assert.Equal(ElementImages.BaseUrl + expectedFile + ElementImages.Extension,
                         ElementImages.UrlFor(name));

        /// <summary>Only the first letter - "Ununseptium" must not become "UNUNSEPTIUM".</summary>
        [Fact]
        public void TheRestOfTheNameIsUntouched() =>
            Assert.EndsWith("/Ununseptium.jpg", ElementImages.UrlFor("ununseptium"), System.StringComparison.Ordinal);

        [Fact]
        public void SurroundingSpaceIsTrimmed() =>
            Assert.Equal(ElementImages.UrlFor("Carbon"), ElementImages.UrlFor("  Carbon  "));

        /// <summary>
        /// "---" is the element table's blank marker. Asking the server for "---.jpg" is a
        /// guaranteed 404, and the placeholder is the honest answer.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("---")]
        public void ThereIsNoUrlWithoutAName(string? name) => Assert.Null(ElementImages.UrlFor(name));

        /// <summary>
        /// Every real element name produces a well-formed absolute URL under the expected host.
        /// </summary>
        [Theory]
        [InlineData("Hydrogen")]
        [InlineData("Caesium")]
        [InlineData("Roentgenium")]
        public void EveryNameGivesAnAbsoluteHttpsUrl(string name)
        {
            var url = ElementImages.UrlFor(name);

            Assert.NotNull(url);
            Assert.True(System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri));
            Assert.Equal("https", uri!.Scheme);
            Assert.Equal("jlindemann.se", uri.Host);
        }

        /// <summary>
        /// A name with a space would break the URL silently. No element has one today, so this
        /// pins the assumption rather than the behaviour.
        /// </summary>
        [Fact]
        public void ElementNamesAreSingleWords() =>
            Assert.DoesNotContain(' ', ElementImages.UrlFor("Praseodymium")!.Split('/').Last());
    }
}
