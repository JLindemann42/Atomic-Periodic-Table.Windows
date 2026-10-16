using Atomic.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Atomic.App.Controls
{
    /// <summary>
    /// Renders the composer's three-token markup into a <see cref="TextBlock"/>'s inlines.
    ///
    /// <para>Used as an attached property so a chat bubble stays a plain <c>TextBlock</c> and keeps
    /// selection, wrapping and theming for free:</para>
    /// <code>controls:MarkdownText.Source="{Binding Text}"</code>
    ///
    /// <para>The grammar lives in <see cref="MiniMarkdown"/> over in Atomic.Core, where it is
    /// tested; this file only turns spans into <see cref="Run"/>s. There is no Markdown package
    /// here on purpose - see that class for why a three-token grammar wants a three-token
    /// parser.</para>
    /// </summary>
    public static class MarkdownText
    {
        /// <summary>How much larger a <c>###</c> heading is than the body around it.</summary>
        private const double HeadingSizeStep = 2.0;

        /// <summary>
        /// The gap a blank line leaves, as a fraction of the line height.
        ///
        /// <para>A blank line renders as a <see cref="LineBreak"/> with a small font size rather
        /// than a full empty line: the composer separates every section with <c>\n\n</c>, so full
        /// blank lines would make a four-section answer scroll for no reason.</para>
        /// </summary>
        private const double BlankLineScale = 0.5;

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.RegisterAttached(
                "Source", typeof(string), typeof(MarkdownText),
                new PropertyMetadata(null, OnSourceChanged));

        public static string? GetSource(DependencyObject element) =>
            (string?)element.GetValue(SourceProperty);

        public static void SetSource(DependencyObject element, string? value) =>
            element.SetValue(SourceProperty, value);

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock block) return;

            block.Inlines.Clear();
            Render(block, e.NewValue as string);
        }

        private static void Render(TextBlock block, string? text)
        {
            var lines = MiniMarkdown.Parse(text);
            var bodySize = block.FontSize;
            var first = true;

            foreach (var line in lines)
            {
                if (!first) block.Inlines.Add(new LineBreak());
                first = false;

                if (line.Block == MarkdownBlock.Blank)
                {
                    // A blank line becomes a short spacer rather than a full empty line: the
                    // composer separates every section with "\n\n", so full-height gaps would make
                    // a four-section answer scroll for no reason. The zero-width space is what
                    // gives the line a height at all - an empty Run has none to reduce.
                    block.Inlines.Add(new Run
                    {
                        Text = "​",
                        FontSize = bodySize * BlankLineScale,
                    });
                    continue;
                }

                if (line.Block == MarkdownBlock.Bullet)
                {
                    // A non-breaking space, so the glyph can never be left alone at the end of a
                    // wrapped line with its text on the next one.
                    block.Inlines.Add(new Run { Text = "• " });
                }

                foreach (var span in line.Spans)
                {
                    var run = new Run { Text = span.Text };

                    if (line.Block == MarkdownBlock.Heading)
                    {
                        run.FontWeight = FontWeights.SemiBold;
                        run.FontSize = bodySize + HeadingSizeStep;
                    }
                    else if (span.Bold)
                    {
                        run.FontWeight = FontWeights.SemiBold;
                    }

                    block.Inlines.Add(run);
                }
            }
        }
    }
}
