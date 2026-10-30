using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Atomic.Core.Sync
{
    /// <summary>
    /// A parsed notes blob.
    /// </summary>
    /// <param name="ByAtomicNumber">Notes for elements this build knows, keyed the Windows way.</param>
    /// <param name="Unmapped">
    /// Notes whose element code is not in this build's table, kept verbatim.
    ///
    /// <para>This field is the whole reason the type exists. Android keys notes by an opaque
    /// <c>element_code</c>; if a future Android release adds an element, its code arrives here with
    /// no local counterpart. Dropping it would mean a Windows sync silently deletes notes the user
    /// wrote on their phone - so it is carried through and written back untouched.</para>
    /// </param>
    public sealed record NotesDocument(
        IReadOnlyDictionary<int, string> ByAtomicNumber,
        IReadOnlyDictionary<string, string> Unmapped)
    {
        public static readonly NotesDocument Empty =
            new(new Dictionary<int, string>(), new Dictionary<string, string>(StringComparer.Ordinal));

        public bool IsEmpty => ByAtomicNumber.Count == 0 && Unmapped.Count == 0;
    }

    /// <summary>
    /// Translates between Android's notes blob and Windows' per-element settings.
    ///
    /// <para><b>The two apps key notes differently.</b> Android stores one string,
    /// <c>&lt;code&gt;text&lt;/code&gt;&lt;code&gt;text&lt;/code&gt;…</c>, keyed by the opaque
    /// <c>element_code</c> in the element JSON. Windows stores a separate
    /// <c>ElementNotes_{Z}</c> setting per atomic number. Getting the mapping wrong does not fail
    /// loudly - it puts every note on the wrong element.</para>
    ///
    /// <para>The map is supplied by the caller rather than read here, because
    /// <c>Atomic.Core</c> owns no element data source; the app builds it from the shipped
    /// table.</para>
    /// </summary>
    public static class NotesCodec
    {
        /// <summary>
        /// <c>&lt;code&gt;content&lt;/code&gt;</c>, with the close tag tied to the open tag by
        /// backreference and <c>.</c> matching newlines - the same shape Android parses with, so a
        /// blob written by either app reads on the other.
        /// </summary>
        private static readonly Regex Entry = new(
            @"<([^>]+)>(.*?)</\1>",
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Read a blob into notes this build can address, plus whatever it cannot.
        /// </summary>
        /// <param name="codeToAtomicNumber">Element code to atomic number, from the element table.</param>
        public static NotesDocument Parse(
            string? blob, IReadOnlyDictionary<string, int> codeToAtomicNumber)
        {
            if (string.IsNullOrEmpty(blob)) return NotesDocument.Empty;

            var byNumber = new Dictionary<int, string>();
            var unmapped = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (Match match in Entry.Matches(blob))
            {
                var code = match.Groups[1].Value;
                var content = match.Groups[2].Value;

                if (codeToAtomicNumber.TryGetValue(code, out var atomicNumber))
                    byNumber[atomicNumber] = content;
                else
                    unmapped[code] = content;
            }

            return new NotesDocument(byNumber, unmapped);
        }

        /// <summary>
        /// Write a blob Android can read.
        /// </summary>
        /// <remarks>
        /// Unmapped entries are written back exactly as they arrived. Ordering is by code so the
        /// output is stable - an unstable blob would make every sync look like a change and trigger
        /// a write that changes nothing.
        /// </remarks>
        public static string Format(
            NotesDocument document, IReadOnlyDictionary<int, string> atomicNumberToCode)
        {
            var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (var (atomicNumber, content) in document.ByAtomicNumber)
            {
                if (string.IsNullOrEmpty(content)) continue;
                if (!atomicNumberToCode.TryGetValue(atomicNumber, out var code)) continue;
                entries[code] = content;
            }

            foreach (var (code, content) in document.Unmapped)
            {
                if (string.IsNullOrEmpty(content)) continue;
                // Local notes win over a preserved entry for the same code, which can only happen
                // if the map gained that code since the blob was parsed.
                if (!entries.ContainsKey(code)) entries[code] = content;
            }

            var builder = new StringBuilder();
            foreach (var (code, content) in entries)
                builder.Append('<').Append(code).Append('>')
                       .Append(content)
                       .Append("</").Append(code).Append('>');

            return builder.ToString();
        }

        /// <summary>
        /// Merge two documents, taking the longer note per element.
        /// </summary>
        /// <remarks>
        /// <para>Longer wins, matching Android's rule: more content means more editing, and there
        /// is no timestamp per note to order them by. It is not a perfect rule - a deliberate
        /// deletion loses to the old text - but it is the one both platforms apply, and a
        /// disagreement here would make notes flip back and forth between devices.</para>
        ///
        /// <para>Unmapped entries merge on the same rule, so a note for an element this build does
        /// not know still survives a round trip through it.</para>
        /// </remarks>
        public static NotesDocument Merge(NotesDocument local, NotesDocument cloud)
        {
            var byNumber = new Dictionary<int, string>(local.ByAtomicNumber);
            foreach (var (atomicNumber, cloudNote) in cloud.ByAtomicNumber)
            {
                if (!byNumber.TryGetValue(atomicNumber, out var localNote) ||
                    cloudNote.Length > localNote.Length)
                {
                    byNumber[atomicNumber] = cloudNote;
                }
            }

            var unmapped = new Dictionary<string, string>(local.Unmapped, StringComparer.Ordinal);
            foreach (var (code, cloudNote) in cloud.Unmapped)
            {
                if (!unmapped.TryGetValue(code, out var localNote) ||
                    cloudNote.Length > localNote.Length)
                {
                    unmapped[code] = cloudNote;
                }
            }

            return new NotesDocument(byNumber, unmapped);
        }

        /// <summary>
        /// Whether a note would survive the round trip through the blob format.
        /// </summary>
        /// <remarks>
        /// <para>The format has no escaping. A note containing its <i>own</i> closing tag would be
        /// truncated there, because the regex stops at the first backreferenced close. Element
        /// codes are opaque 19-character strings, so a user typing one is not a realistic accident
        /// - but pasting text copied out of a synced blob is, and this is what lets a caller refuse
        /// rather than corrupt.</para>
        ///
        /// <para>Not fixed by escaping: the format is co-owned with Android, and an escape this
        /// side invents would read as literal backslashes there.</para>
        /// </remarks>
        public static bool IsRoundTrippable(string code, string? content) =>
            content is null || !content.Contains("</" + code + ">", StringComparison.Ordinal);
    }
}
