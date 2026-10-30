using System.Linq;
using Atomic.Core.Text;
using Xunit;

namespace Atomic.Core.Tests.Text
{
    public sealed class MiniMarkdownTest
    {
        private static string Flat(MarkdownLine line) =>
            string.Concat(line.Spans.Select(s => s.Text));

        [Fact]
        public void PlainTextIsOneParagraphSpan()
        {
            var lines = MiniMarkdown.Parse("Gold melts at 1064 °C.");

            var line = Assert.Single(lines);
            Assert.Equal(MarkdownBlock.Paragraph, line.Block);
            var span = Assert.Single(line.Spans);
            Assert.Equal("Gold melts at 1064 °C.", span.Text);
            Assert.False(span.Bold);
        }

        [Fact]
        public void AHeadingDropsItsMarker()
        {
            var line = Assert.Single(MiniMarkdown.Parse("### Density"));

            Assert.Equal(MarkdownBlock.Heading, line.Block);
            Assert.Equal("Density", Flat(line));
        }

        [Fact]
        public void ABulletDropsItsGlyph()
        {
            var line = Assert.Single(MiniMarkdown.Parse("• Periodic table"));

            Assert.Equal(MarkdownBlock.Bullet, line.Block);
            Assert.Equal("Periodic table", Flat(line));
        }

        /// <summary>
        /// The marker is only a marker at the start of a line. Chemistry prose contains bullets and
        /// hashes mid-sentence, and a heading that could begin anywhere would re-style them.
        /// </summary>
        [Fact]
        public void AMarkerMidLineIsOrdinaryText()
        {
            var line = Assert.Single(MiniMarkdown.Parse("Item ### not a heading • not a bullet"));

            Assert.Equal(MarkdownBlock.Paragraph, line.Block);
            Assert.Equal("Item ### not a heading • not a bullet", Flat(line));
        }

        [Fact]
        public void BoldSplitsIntoThreeSpans()
        {
            var line = Assert.Single(MiniMarkdown.Parse("the **densest** element"));

            Assert.Equal(["the ", "densest", " element"], line.Spans.Select(s => s.Text));
            Assert.Equal([false, true, false], line.Spans.Select(s => s.Bold));
        }

        [Fact]
        public void TwoBoldRunsOnOneLineBothResolve()
        {
            var line = Assert.Single(MiniMarkdown.Parse("**Osmium** beats **iridium**"));

            Assert.Equal(["Osmium", " beats ", "iridium"], line.Spans.Select(s => s.Text));
            Assert.Equal([true, false, true], line.Spans.Select(s => s.Bold));
        }

        /// <summary>
        /// The defect this parser exists to not have.
        ///
        /// <para>A composed answer is assembled from translated strings in twelve languages and from
        /// element data, so a stray asterisk arrives eventually. Swallowing to end of line would
        /// silently re-style the rest of the answer and drop two characters; this keeps every
        /// character the user was meant to see.</para>
        /// </summary>
        [Fact]
        public void AnUnclosedMarkerStaysLiteral()
        {
            var line = Assert.Single(MiniMarkdown.Parse("a **b c"));

            Assert.Equal("a **b c", Flat(line));
            Assert.DoesNotContain(line.Spans, s => s.Bold);
        }

        [Fact]
        public void AnUnclosedMarkerAfterAClosedOneKeepsTheFirst()
        {
            var line = Assert.Single(MiniMarkdown.Parse("**bold** then **dangling"));

            Assert.Equal("bold", line.Spans[0].Text);
            Assert.True(line.Spans[0].Bold);
            Assert.Equal(" then **dangling", Flat(line)[4..]);
        }

        /// <summary>Bolding nothing renders as nothing, which would delete four characters.</summary>
        [Fact]
        public void AnEmptyEmphasisStaysLiteral()
        {
            var line = Assert.Single(MiniMarkdown.Parse("a ****b"));

            Assert.Equal("a ****b", Flat(line));
            Assert.DoesNotContain(line.Spans, s => s.Bold);
        }

        [Fact]
        public void AHeadingCanContainBold()
        {
            var line = Assert.Single(MiniMarkdown.Parse("### The **densest** element"));

            Assert.Equal(MarkdownBlock.Heading, line.Block);
            Assert.Equal("The **densest** element".Replace("**", ""), Flat(line));
            Assert.Contains(line.Spans, s => s.Bold && s.Text == "densest");
        }

        [Fact]
        public void BlankLinesArePreservedAsSpacing()
        {
            var lines = MiniMarkdown.Parse("a\n\nb");

            Assert.Equal(3, lines.Count);
            Assert.Equal(MarkdownBlock.Blank, lines[1].Block);
            Assert.Empty(lines[1].Spans);
        }

        /// <summary>
        /// A lone <c>\r</c> would otherwise survive into a Run and render as a box glyph, which is
        /// the kind of defect that only shows up on one machine.
        /// </summary>
        [Theory]
        [InlineData("a\r\nb")]
        [InlineData("a\rb")]
        [InlineData("a\nb")]
        public void EveryLineEndingSplitsTheSameWay(string text)
        {
            var lines = MiniMarkdown.Parse(text);

            Assert.Equal(2, lines.Count);
            Assert.Equal("a", Flat(lines[0]));
            Assert.Equal("b", Flat(lines[1]));
        }

        [Fact]
        public void EmptyInputProducesNoLines()
        {
            Assert.Empty(MiniMarkdown.Parse(null));
            Assert.Empty(MiniMarkdown.Parse(""));
        }

        /// <summary>
        /// A heading with nothing after it is degenerate but must not crash or produce a phantom
        /// span - the composer builds headings from resource strings, one of which could be empty.
        /// </summary>
        [Fact]
        public void AHeadingWithNoTextHasNoSpans()
        {
            var line = Assert.Single(MiniMarkdown.Parse("###"));

            // "###" without the trailing space is not a heading marker at all.
            Assert.Equal(MarkdownBlock.Paragraph, line.Block);
            Assert.Equal("###", Flat(line));
        }

        /// <summary>
        /// The parser must be lossless outside its three markers: every character of the input
        /// reaches a span, or the user is shown less than the composer wrote.
        /// </summary>
        [Theory]
        [InlineData("### Sources\n• Periodic table\n• Nuclide chart")]
        [InlineData("**Väte** är **lättast**")]
        [InlineData("no markup at all")]
        [InlineData("*single* asterisks are not bold")]
        [InlineData("100% * 2 ** 3")]
        public void EveryCharacterOutsideAMarkerSurvives(string text)
        {
            var rebuilt = string.Join("\n", MiniMarkdown.Parse(text).Select(line => line.Block switch
            {
                MarkdownBlock.Heading => "### " + Flat(line),
                MarkdownBlock.Bullet => "• " + Flat(line),
                _ => Flat(line),
            }));

            Assert.Equal(text.Replace("**", ""), rebuilt.Replace("**", ""));
        }
    }
}
