using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Atomic.Core.Sync;
using Xunit;

namespace Atomic.Core.Tests.Sync
{
    public sealed class FirestoreValueTest
    {
        /// <summary>
        /// The trap the class exists for: Firestore's REST API sends and expects a JSON
        /// <b>string</b> here. Writing a number is rejected by the service.
        /// </summary>
        [Fact]
        public void IntegersAreWrittenAsJsonStrings()
        {
            var json = FirestoreValue.Integer(42).ToJsonString();

            Assert.Equal("{\"integerValue\":\"42\"}", json);
            Assert.DoesNotContain(":42", json, StringComparison.Ordinal);
        }

        /// <summary>
        /// Tolerant on read, because a document written by the Admin SDK or the console can carry a
        /// number. A reader that only handled one form would throw on the other client's data.
        /// </summary>
        [Fact]
        public void IntegersReadBackFromEitherForm()
        {
            Assert.Equal(42, FirestoreValue.ReadInteger(JsonNode.Parse("{\"integerValue\":\"42\"}")));
            Assert.Equal(42, FirestoreValue.ReadInteger(JsonNode.Parse("{\"integerValue\":42}")));
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(-1L)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        public void IntegersRoundTrip(long value) =>
            Assert.Equal(value, FirestoreValue.ReadInteger(FirestoreValue.Integer(value)));

        /// <summary>
        /// The second trap: RFC3339, UTC, six fractional digits. .NET's round-trip format emits
        /// seven, which Firestore rejects.
        /// </summary>
        [Fact]
        public void TimestampsAreRfc3339UtcWithMicroseconds()
        {
            var moment = new DateTimeOffset(2026, 8, 14, 9, 30, 15, 123, TimeSpan.FromHours(2));

            var written = FirestoreValue.Timestamp(moment)["timestampValue"]!.GetValue<string>();

            // Converted to UTC, so the +02:00 offset is folded in rather than carried.
            Assert.Equal("2026-08-14T07:30:15.123000Z", written);
            Assert.EndsWith("Z", written, StringComparison.Ordinal);
            Assert.Equal(6, written.Split('.')[1].TrimEnd('Z').Length);
        }

        [Fact]
        public void TimestampsRoundTripToTheSameInstant()
        {
            var moment = new DateTimeOffset(2026, 8, 14, 9, 30, 15, 123, TimeSpan.FromHours(2));

            var read = FirestoreValue.ReadTimestamp(FirestoreValue.Timestamp(moment));

            Assert.NotNull(read);
            Assert.Equal(moment.ToUniversalTime(), read!.Value.ToUniversalTime());
        }

        /// <summary>
        /// Under ur-PK an ambient parse reads a Hijri calendar - the same hazard StreakMath
        /// documents, and it would put a synced timestamp in the wrong year.
        /// </summary>
        [Theory]
        [InlineData("ur-PK")]
        [InlineData("ar-SA")]
        [InlineData("th-TH")]
        public void TimestampsAreCultureIndependent(string culture)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                var hostile = new CultureInfo(culture);
                CultureInfo.CurrentCulture = hostile;
                Thread.CurrentThread.CurrentCulture = hostile;

                var moment = new DateTimeOffset(2026, 8, 14, 7, 30, 15, TimeSpan.Zero);

                Assert.Equal(
                    "2026-08-14T07:30:15.000000Z",
                    FirestoreValue.Timestamp(moment)["timestampValue"]!.GetValue<string>());

                Assert.Equal(moment, FirestoreValue.ReadTimestamp(FirestoreValue.Timestamp(moment)));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        /// <summary>
        /// A decimal separator that is a comma would produce invalid JSON, so this is checked under
        /// a comma-decimal culture rather than only the default one.
        /// </summary>
        [Fact]
        public void DoublesAreInvariantUnderACommaDecimalCulture()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                var swedish = new CultureInfo("sv-SE");
                CultureInfo.CurrentCulture = swedish;
                Thread.CurrentThread.CurrentCulture = swedish;

                var json = FirestoreValue.Double(1.5).ToJsonString();

                Assert.Contains("1.5", json, StringComparison.Ordinal);
                Assert.DoesNotContain(",5", json, StringComparison.Ordinal);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        /// <summary>
        /// Firestore narrows a whole double to an integer on write, so a field that went out as a
        /// double comes back as one. Reading only doubleValue silently loses every round figure.
        /// </summary>
        [Fact]
        public void ADoubleThatCameBackAsAnIntegerStillReads() =>
            Assert.Equal(3.0, FirestoreValue.ReadDouble(JsonNode.Parse("{\"integerValue\":\"3\"}")));

        [Fact]
        public void StringsAndBooleansRoundTrip()
        {
            Assert.Equal("hej", FirestoreValue.ReadString(FirestoreValue.String("hej")));
            Assert.Equal(string.Empty, FirestoreValue.ReadString(FirestoreValue.String(string.Empty)));
            Assert.True(FirestoreValue.ReadBoolean(FirestoreValue.Boolean(true)));
            Assert.False(FirestoreValue.ReadBoolean(FirestoreValue.Boolean(false)));
        }

        /// <summary>A null string becomes nullValue, not the text "null".</summary>
        [Fact]
        public void ANullStringIsANullValue()
        {
            var json = FirestoreValue.String(null).ToJsonString();

            Assert.Contains("nullValue", json, StringComparison.Ordinal);
            Assert.Null(FirestoreValue.ReadString(FirestoreValue.String(null)));
        }

        [Fact]
        public void ArraysRoundTrip()
        {
            var node = FirestoreValue.Array([
                FirestoreValue.String("a"),
                FirestoreValue.Integer(2),
            ]);

            var read = FirestoreValue.ReadArray(node);

            Assert.Equal(2, read.Count);
            Assert.Equal("a", FirestoreValue.ReadString(read[0]));
            Assert.Equal(2, FirestoreValue.ReadInteger(read[1]));
        }

        [Fact]
        public void MapsRoundTrip()
        {
            var node = FirestoreValue.Map(new Dictionary<string, JsonNode>
            {
                ["xp"] = FirestoreValue.Integer(4200),
                ["note"] = FirestoreValue.String("hi"),
            });

            var read = FirestoreValue.ReadMap(node);

            Assert.Equal(4200, FirestoreValue.ReadInteger(read["xp"]));
            Assert.Equal("hi", FirestoreValue.ReadString(read["note"]));
        }

        [Fact]
        public void ADocumentRoundTripsThroughItsFields()
        {
            var document = FirestoreValue.Document(new Dictionary<string, JsonNode>
            {
                ["xp"] = FirestoreValue.Integer(4200),
                ["streak"] = FirestoreValue.Integer(7),
            });

            var fields = FirestoreValue.ReadDocument(JsonNode.Parse(document.ToJsonString()));

            Assert.Equal(4200, FirestoreValue.ReadInteger(fields["xp"]));
            Assert.Equal(7, FirestoreValue.ReadInteger(fields["streak"]));
        }

        /// <summary>
        /// Reading the wrong type, or a missing field, returns null rather than throwing. A sync
        /// runs against a document co-owned with another platform, so an unexpected shape is a
        /// thing to skip, not a thing to crash on.
        /// </summary>
        [Fact]
        public void AWrongOrMissingTypeReadsAsNullRatherThanThrowing()
        {
            var text = FirestoreValue.String("not a number");

            Assert.Null(FirestoreValue.ReadInteger(text));
            Assert.Null(FirestoreValue.ReadTimestamp(text));
            Assert.Null(FirestoreValue.ReadBoolean(text));
            Assert.Null(FirestoreValue.ReadString(FirestoreValue.Integer(1)));
            Assert.Null(FirestoreValue.ReadInteger(null));
            Assert.Empty(FirestoreValue.ReadArray(text));
            Assert.Empty(FirestoreValue.ReadMap(text));
            Assert.Null(FirestoreValue.ReadInteger(JsonNode.Parse("{\"integerValue\":\"not a number\"}")));
        }
    }
}
