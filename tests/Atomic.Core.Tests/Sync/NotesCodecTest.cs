using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Sync;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Sync
{
    public sealed class NotesCodecTest
    {
        /// <summary>
        /// The real code-to-number map, read from the shipped table exactly as the app builds it.
        /// </summary>
        private static (Dictionary<string, int> ToNumber, Dictionary<int, string> ToCode) Maps()
        {
            var toNumber = new Dictionary<string, int>(StringComparer.Ordinal);
            var toCode = new Dictionary<int, string>();

            foreach (var (_, row) in TestAssets.English.Rows)
            {
                if (!row.TryGetValue("element_code", out var codeValue)) continue;
                if (!row.TryGetValue("element_atomic_number", out var numberValue)) continue;

                var code = codeValue as string;
                if (string.IsNullOrWhiteSpace(code)) continue;
                if (!int.TryParse(
                        numberValue as string, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var atomicNumber)) continue;

                toNumber[code] = atomicNumber;
                toCode[atomicNumber] = code;
            }

            return (toNumber, toCode);
        }

        [Fact]
        public void TheShippedTableGivesAUsableMapForAllOneHundredAndEighteen()
        {
            var (toNumber, toCode) = Maps();

            Assert.Equal(118, toCode.Count);
            // A duplicate code would silently merge two elements' notes into one.
            Assert.Equal(118, toNumber.Count);
            Assert.Equal(Enumerable.Range(1, 118), toCode.Keys.OrderBy(k => k));
        }

        /// <summary>
        /// The round trip the plan asks for: a note on every element, out and back.
        /// </summary>
        [Fact]
        public void EveryElementSurvivesARoundTrip()
        {
            var (toNumber, toCode) = Maps();

            var original = Enumerable.Range(1, 118)
                .ToDictionary(z => z, z => $"note for element {z.ToString(CultureInfo.InvariantCulture)}");

            var blob = NotesCodec.Format(
                new NotesDocument(original, new Dictionary<string, string>()), toCode);
            var parsed = NotesCodec.Parse(blob, toNumber);

            Assert.Equal(original.Count, parsed.ByAtomicNumber.Count);
            Assert.Empty(parsed.Unmapped);
            foreach (var (z, text) in original)
                Assert.Equal(text, parsed.ByAtomicNumber[z]);
        }

        /// <summary>
        /// The case that decides whether this feature is safe to ship.
        ///
        /// <para>A code the local table does not contain is a note the user wrote on a phone
        /// running a newer build. Dropping it means a Windows sync deletes it. It has to come back
        /// out of <see cref="NotesCodec.Format"/> byte for byte.</para>
        /// </summary>
        [Fact]
        public void ANoteForAnUnknownElementIsPreservedVerbatim()
        {
            var (toNumber, toCode) = Maps();
            const string FutureCode = "AAzzzzFUTURE119zzZZ";

            var blob = $"<{toCode[79]}>gold note</{toCode[79]}><{FutureCode}>element 119 note</{FutureCode}>";

            var parsed = NotesCodec.Parse(blob, toNumber);

            Assert.Equal("gold note", parsed.ByAtomicNumber[79]);
            Assert.Equal("element 119 note", parsed.Unmapped[FutureCode]);

            var rewritten = NotesCodec.Parse(NotesCodec.Format(parsed, toCode), toNumber);

            Assert.Equal("gold note", rewritten.ByAtomicNumber[79]);
            Assert.Equal("element 119 note", rewritten.Unmapped[FutureCode]);
        }

        [Fact]
        public void MultilineNotesSurvive()
        {
            var (toNumber, toCode) = Maps();
            var text = "line one\nline two\r\nline three";

            var blob = NotesCodec.Format(
                new NotesDocument(new Dictionary<int, string> { [26] = text }, new Dictionary<string, string>()),
                toCode);

            Assert.Equal(text, NotesCodec.Parse(blob, toNumber).ByAtomicNumber[26]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a blob at all")]
        [InlineData("<unclosed>text")]
        public void NothingUsableParsesToAnEmptyDocument(string? blob)
        {
            var (toNumber, _) = Maps();
            Assert.True(NotesCodec.Parse(blob, toNumber).IsEmpty);
        }

        /// <summary>An empty note is not written, so a cleared note does not leave an empty tag.</summary>
        [Fact]
        public void EmptyNotesAreNotWritten()
        {
            var (_, toCode) = Maps();

            var blob = NotesCodec.Format(
                new NotesDocument(
                    new Dictionary<int, string> { [1] = "", [2] = "kept" },
                    new Dictionary<string, string>()),
                toCode);

            Assert.DoesNotContain(toCode[1], blob, StringComparison.Ordinal);
            Assert.Contains(toCode[2], blob, StringComparison.Ordinal);
        }

        /// <summary>
        /// A blob that is not byte-stable would make every sync look like a change, and provoke a
        /// write on every launch that changes nothing.
        /// </summary>
        [Fact]
        public void TheOutputIsStableAcrossReorderedInput()
        {
            var (_, toCode) = Maps();

            var forward = NotesCodec.Format(
                new NotesDocument(
                    new Dictionary<int, string> { [1] = "a", [50] = "b", [100] = "c" },
                    new Dictionary<string, string>()),
                toCode);

            var backward = NotesCodec.Format(
                new NotesDocument(
                    new Dictionary<int, string> { [100] = "c", [50] = "b", [1] = "a" },
                    new Dictionary<string, string>()),
                toCode);

            Assert.Equal(forward, backward);
        }

        // ---- Merge -----------------------------------------------------------------------------

        [Fact]
        public void MergeTakesTheLongerNotePerElement()
        {
            var local = new NotesDocument(
                new Dictionary<int, string> { [1] = "short", [2] = "local is longer here" },
                new Dictionary<string, string>());
            var cloud = new NotesDocument(
                new Dictionary<int, string> { [1] = "a much longer cloud note", [3] = "cloud only" },
                new Dictionary<string, string>());

            var merged = NotesCodec.Merge(local, cloud);

            Assert.Equal("a much longer cloud note", merged.ByAtomicNumber[1]);
            Assert.Equal("local is longer here", merged.ByAtomicNumber[2]);
            Assert.Equal("cloud only", merged.ByAtomicNumber[3]);
        }

        [Fact]
        public void MergeWithAnEmptySideKeepsEverything()
        {
            var populated = new NotesDocument(
                new Dictionary<int, string> { [1] = "kept" },
                new Dictionary<string, string> { ["XX"] = "also kept" });

            foreach (var merged in new[]
                     {
                         NotesCodec.Merge(populated, NotesDocument.Empty),
                         NotesCodec.Merge(NotesDocument.Empty, populated),
                     })
            {
                Assert.Equal("kept", merged.ByAtomicNumber[1]);
                Assert.Equal("also kept", merged.Unmapped["XX"]);
            }
        }

        /// <summary>A note for an unknown element must survive a merge too, not just a parse.</summary>
        [Fact]
        public void MergeKeepsUnmappedNotesFromBothSides()
        {
            var local = new NotesDocument(
                new Dictionary<int, string>(),
                new Dictionary<string, string> { ["AAA"] = "local", ["SHARED"] = "short" });
            var cloud = new NotesDocument(
                new Dictionary<int, string>(),
                new Dictionary<string, string> { ["BBB"] = "cloud", ["SHARED"] = "a longer one" });

            var merged = NotesCodec.Merge(local, cloud);

            Assert.Equal("local", merged.Unmapped["AAA"]);
            Assert.Equal("cloud", merged.Unmapped["BBB"]);
            Assert.Equal("a longer one", merged.Unmapped["SHARED"]);
        }

        /// <summary>
        /// The format carries no escaping, so a note containing its own closing tag truncates.
        /// Codes are opaque 19-character strings, so this is not a realistic typo - but pasting
        /// text copied out of a synced blob is, and a caller needs to be able to tell.
        /// </summary>
        [Fact]
        public void ANoteContainingItsOwnClosingTagIsReportedAsNotRoundTrippable()
        {
            var (_, toCode) = Maps();
            var code = toCode[79];

            Assert.True(NotesCodec.IsRoundTrippable(code, "an ordinary note"));
            Assert.True(NotesCodec.IsRoundTrippable(code, "mentions <b>markup</b>"));
            Assert.False(NotesCodec.IsRoundTrippable(code, $"trouble </{code}> here"));
        }
    }
}
