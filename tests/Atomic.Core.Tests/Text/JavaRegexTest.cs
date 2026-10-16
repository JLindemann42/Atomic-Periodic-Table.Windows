using System.Text.RegularExpressions;
using Atomic.Core.Text;
using Xunit;

namespace Atomic.Core.Tests.Text
{
    /// <summary>
    /// The Kotlin sources have a WordBoundaryTest because this class of bug already bit once.
    /// Each test here pairs the translated pattern against the raw .NET one, so the assertion
    /// shows the divergence rather than merely asserting the fix.
    /// </summary>
    public class JavaRegexTest
    {
        [Fact]
        public void DigitIsAsciiOnly()
        {
            // .NET's \d matches Arabic-Indic and Devanagari digits; Java's does not. The value,
            // isotope and quantity parsers all use \d and all run against Hindi and Urdu text, so
            // this decides whether a number parses at all - and if it does, into what.
            var translated = JavaRegex.Compile(@"^\d+$");
            var dotnet = new Regex(@"^\d+$");

            Assert.True(translated.IsMatch("123"));
            Assert.False(translated.IsMatch("٩٨٧"));   // Arabic-Indic
            Assert.False(translated.IsMatch("१२३"));   // Devanagari

            Assert.True(dotnet.IsMatch("٩٨٧"));        // the behaviour being corrected
        }

        [Fact]
        public void WordBoundaryIsAsciiOnly()
        {
            // ChemistryMath.MOLE_QUANTITY looks like this. For "5 molé": Java sees é as a non-word
            // character, finds a boundary, and matches. .NET sees é as a word character, finds no
            // boundary, and does not match - with no error.
            var translated = JavaRegex.Compile(@"([\d.]+)\s*mol(?:e|es|i|s)?\b");
            var dotnet = new Regex(@"([\d.]+)\s*mol(?:e|es|i|s)?\b");

            Assert.True(translated.IsMatch("5 molé"));
            Assert.False(dotnet.IsMatch("5 molé"));

            // Still behaves for the ordinary cases.
            Assert.True(translated.IsMatch("2 mol"));
            Assert.True(translated.IsMatch("0.5 moles"));
            Assert.False(translated.IsMatch("2 molybdenum"));
        }

        [Fact]
        public void WordCharacterIsAsciiOnly()
        {
            var translated = JavaRegex.Compile(@"^\w+$");
            Assert.True(translated.IsMatch("iron_2"));
            Assert.False(translated.IsMatch("café"));
            Assert.False(translated.IsMatch("铁"));
        }

        [Fact]
        public void WhitespaceIsAsciiOnly()
        {
            var translated = JavaRegex.Compile(@"^\s$");
            Assert.True(translated.IsMatch(" "));
            Assert.True(translated.IsMatch("\t"));
            // U+00A0 no-break space is whitespace to .NET but not to Java.
            Assert.False(translated.IsMatch(" "));
            Assert.True(new Regex(@"^\s$").IsMatch(" "));
        }

        [Fact]
        public void ShorthandsInsideCharacterClassesExpandToMembers()
        {
            var translated = JavaRegex.Compile(@"^[\d.]+$");
            Assert.True(translated.IsMatch("7.87"));
            Assert.False(translated.IsMatch("٧.٨"));
            Assert.DoesNotContain("[[", JavaRegex.Translate(@"[\d.]"));
        }

        [Fact]
        public void NegatedShorthandsSurvive()
        {
            var translated = JavaRegex.Compile(@"^\D+$");
            Assert.True(translated.IsMatch("abc"));
            Assert.False(translated.IsMatch("a1c"));
            // Java's \D is "not an ASCII digit", so a Devanagari digit satisfies it.
            Assert.True(translated.IsMatch("१"));
        }

        [Fact]
        public void UnicodePropertyEscapesAreLeftAlone()
        {
            // .NET agrees with Java on \p{L} and \p{M}, so translating them would only break them.
            Assert.Equal(@"\p{L}+\p{M}", JavaRegex.Translate(@"\p{L}+\p{M}"));
            Assert.True(JavaRegex.Compile(@"^\p{L}+$").IsMatch("铁"));
        }

        [Fact]
        public void EscapedBackslashIsNotMistakenForAShorthand()
        {
            // "\\d" is a literal backslash followed by d, not a digit class.
            var translated = JavaRegex.Compile(@"^\\d$");
            Assert.True(translated.IsMatch(@"\d"));
            Assert.False(translated.IsMatch("5"));
        }

        [Fact]
        public void MatchEntireRequiresTheWholeInput()
        {
            // Kotlin's matchEntire is not Regex.Match: the latter succeeds on a partial match.
            var r = JavaRegex.Compile(@"\d+");
            Assert.True(r.IsMatch("abc 123"));
            Assert.Null(r.MatchEntire("abc 123"));
            Assert.NotNull(r.MatchEntire("123"));
        }

        [Fact]
        public void CaseInsensitiveMatchingIsCultureInvariant()
        {
            // Under tr-TR, "I".ToLower() is a dotless ı. A culture-sensitive IgnoreCase would stop
            // matching ASCII identifiers in exactly the places the engine relies on them.
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture =
                    new System.Globalization.CultureInfo("tr-TR");
                var r = JavaRegex.Compile("^IRON$", RegexOptions.IgnoreCase);
                Assert.True(r.IsMatch("iron"));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }
    }
}
