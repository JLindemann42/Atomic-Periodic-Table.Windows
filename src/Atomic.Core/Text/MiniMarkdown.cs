using System;
using System.Collections.Generic;

namespace Atomic.Core.Text
{
    /// <summary>A run of text within a line, either plain or bold.</summary>
    public sealed record MarkdownSpan(string Text, bool Bold);

    /// <summary>What a line is.</summary>
    public enum MarkdownBlock
    {
        /// <summary>Ordinary prose.</summary>
        Paragraph,

        /// <summary>A <c>###</c> heading. The marker is not part of the spans.</summary>
        Heading,

        /// <summary>A <c>•</c> bullet. The glyph is not part of the spans.</summary>
        Bullet,

        /// <summary>An empty line, carrying vertical space and nothing else.</summary>
        Blank,
    }

    /// <summary>One line of composed answer text.</summary>
    public sealed record MarkdownLine(MarkdownBlock Block, IReadOnlyList<MarkdownSpan> Spans);

    /// <summary>
    /// The three-token grammar <see cref="Ai.Compose.AnswerComposer"/> emits, and nothing else.
    ///
    /// <para>A heading is <c>### </c> at the start of a line, a bullet is <c>• </c> at the start of
    /// a line, and <c>**bold**</c> is inline. That is the whole vocabulary - there are no links,
    /// no italics, no code spans and no nesting, because the composer never produces them. A
    /// Markdown package for this would be several hundred kilobytes to parse three tokens, and would
    /// bring a much larger grammar that turns incidental punctuation in 12 languages into
    /// formatting.</para>
    ///
    /// <para>Parsing lives here rather than in the WinUI control because the failure modes are all
    /// in the parsing: an unclosed <c>**</c> that swallows the rest of an answer, a marker inside a
    /// word, a heading with no text after it. Those are worth tests, and building
    /// <c>Inline</c> objects is not.</para>
    ///
    /// <para><b>Unmatched markers stay literal.</b> A composed answer is built from translated
    /// strings in twelve languages and from element data, so a stray asterisk is a question of when,
    /// not if. Dropping it would silently delete a character from a user's answer; swallowing to end
    /// of line would silently re-style the rest of it. Showing it is the only option that cannot
    /// misrepresent the text.</para>
    /// </summary>
    public static class MiniMarkdown
    {
        private const string HeadingMarker = "### ";
        private const string BulletMarker = "• ";
        private const string BoldMarker = "**";

        /// <summary>Parse composed answer text into lines of spans.</summary>
        public static IReadOnlyList<MarkdownLine> Parse(string? text)
        {
            if (string.IsNullOrEmpty(text)) return [];

            var lines = new List<MarkdownLine>();
            // \r\n and \r both normalise to \n first, so a line never keeps a stray carriage return
            // that would render as a box glyph.
            foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal)
                                    .Replace('\r', '\n')
                                    .Split('\n'))
            {
                var line = raw.TrimEnd();

                if (line.Length == 0)
                {
                    lines.Add(new MarkdownLine(MarkdownBlock.Blank, []));
                    continue;
                }

                var block = MarkdownBlock.Paragraph;
                var content = line;

                if (content.StartsWith(HeadingMarker, StringComparison.Ordinal))
                {
                    block = MarkdownBlock.Heading;
                    content = content[HeadingMarker.Length..].TrimStart();
                }
                else if (content.StartsWith(BulletMarker, StringComparison.Ordinal))
                {
                    block = MarkdownBlock.Bullet;
                    content = content[BulletMarker.Length..].TrimStart();
                }

                lines.Add(new MarkdownLine(block, Spans(content)));
            }

            return lines;
        }

        /// <summary>Split one line's text on its bold markers.</summary>
        private static IReadOnlyList<MarkdownSpan> Spans(string line)
        {
            if (line.Length == 0) return [];

            var spans = new List<MarkdownSpan>();
            var plain = new System.Text.StringBuilder();
            var index = 0;

            void FlushPlain()
            {
                if (plain.Length == 0) return;
                spans.Add(new MarkdownSpan(plain.ToString(), Bold: false));
                plain.Clear();
            }

            while (index < line.Length)
            {
                var open = line.IndexOf(BoldMarker, index, StringComparison.Ordinal);
                if (open < 0) break;

                var close = line.IndexOf(BoldMarker, open + BoldMarker.Length, StringComparison.Ordinal);
                if (close < 0)
                {
                    // No closing marker on this line. Everything from here is literal, including the
                    // marker itself - see the note on the class.
                    break;
                }

                var inner = line[(open + BoldMarker.Length)..close];
                plain.Append(line, index, open - index);

                if (inner.Length == 0)
                {
                    // "****" is an empty emphasis. Bolding nothing would render as nothing, quietly
                    // deleting four characters, so it stays literal too.
                    plain.Append(BoldMarker).Append(BoldMarker);
                }
                else
                {
                    FlushPlain();
                    spans.Add(new MarkdownSpan(inner, Bold: true));
                }

                index = close + BoldMarker.Length;
            }

            if (index < line.Length) plain.Append(line, index, line.Length - index);
            FlushPlain();

            return spans;
        }
    }
}
