using System.Globalization;
using Atomic.Core.Text;
using Xunit;

namespace Atomic.Core.Tests.Text
{
    public class JavaFormatTest
    {
        [Theory]
        // The forms that actually occur in the resource set, by frequency.
        [InlineData("**%1$s**: %2$s", new object[] { "Density", "7.87 g/cm3" }, "**Density**: 7.87 g/cm3")]
        [InlineData("Uses of %1$s", new object[] { "iron" }, "Uses of iron")]
        [InlineData("Lost %d life", new object[] { 1 }, "Lost 1 life")]
        [InlineData("%s has %d isotopes", new object[] { "Tin", 10 }, "Tin has 10 isotopes")]
        [InlineData("100%% pure", new object[0], "100% pure")]
        [InlineData("no specifiers here", new object[0], "no specifiers here")]
        public void FormatsTheShapesTheResourcesUse(string template, object[] args, string expected) =>
            Assert.Equal(expected, JavaFormat.Format(template, args));

        [Fact]
        public void ExplicitIndexesMayRepeatAndReorder()
        {
            // This is the whole reason the specifiers are kept verbatim: translators reorder them.
            Assert.Equal("b then a", JavaFormat.Format("%2$s then %1$s", "a", "b"));
            Assert.Equal("a a a", JavaFormat.Format("%1$s %1$s %1$s", "a"));
        }

        [Fact]
        public void ExplicitIndexDoesNotConsumeAnImplicitOne()
        {
            // Java's rule: %1$s and %s both read the first argument.
            Assert.Equal("a a", JavaFormat.Format("%1$s %s", "a", "b"));
        }

        [Fact]
        public void MissingArgumentsLeaveTheSpecifierVisible()
        {
            // A hole you can see beats a sentence that silently lost a value.
            Assert.Equal("Uses of %1$s", JavaFormat.Format("Uses of %1$s"));
            Assert.Equal("a and %s", JavaFormat.Format("%s and %s", "a"));
        }

        [Fact]
        public void MalformedTemplateReturnsItselfRatherThanThrowing()
        {
            Assert.Equal("trailing %", JavaFormat.Format("trailing %"));
            Assert.Equal("half %1$", JavaFormat.Format("half %1$"));
        }

        [Fact]
        public void NumbersAreFormattedInvariantlyByDefault()
        {
            Assert.Equal("3.14", JavaFormat.Format("%.2f", 3.14159));
            Assert.Equal("1234567", JavaFormat.Format("%d", 1234567));
            Assert.Equal("1,234,567", JavaFormat.Format("%,d", 1234567));
        }

        [Fact]
        public void AmbientCultureCannotChangeTheResult()
        {
            // Under a comma-decimal culture, a naive implementation renders 3,14 - which in a
            // chemistry value is not a cosmetic difference. The engine must be culture-proof.
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
                Assert.Equal("3.14", JavaFormat.Format("%.2f", 3.14159));
                Assert.Equal("1,234.50", JavaFormat.Format("%,.2f", 1234.5));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void TurkishCultureCannotChangeTheResult()
        {
            // Turkish lowercases 'I' to a dotless 'ı'. Any casing done with the ambient culture
            // corrupts every ASCII lookup key in the engine.
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                Assert.Equal("IRON", JavaFormat.Format("%S", "iron"));
                Assert.Equal("42", JavaFormat.Format("%d", 42));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void WidthAndAlignment()
        {
            Assert.Equal("   ab", JavaFormat.Format("%5s", "ab"));
            Assert.Equal("ab   ", JavaFormat.Format("%-5s", "ab"));
            Assert.Equal("00042", JavaFormat.Format("%05d", 42));
        }

        [Fact]
        public void NullArgumentRendersAsNullLikeJava() =>
            Assert.Equal("value: null", JavaFormat.Format("value: %s", new object?[] { null }));

        [Fact]
        public void NonLatinTemplatesSurviveIntact()
        {
            Assert.Equal("लोहा का घनत्व", JavaFormat.Format("%1$s का घनत्व", "लोहा"));
            Assert.Equal("کثافت آئرن", JavaFormat.Format("کثافت %1$s", "آئرن"));
            Assert.Equal("铁的密度", JavaFormat.Format("%1$s的密度", "铁"));
        }
    }
}
