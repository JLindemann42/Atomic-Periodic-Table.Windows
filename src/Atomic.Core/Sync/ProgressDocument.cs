using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Atomic.Core.Sync
{
    /// <summary>
    /// Maps a <see cref="ProgressSnapshot"/> to and from the Firestore document at
    /// <c>users/{uid}</c>.
    ///
    /// <para><b>The document is co-owned with Android.</b> That single fact drives the design:
    /// this build must write only the fields it understands and leave everything else alone,
    /// because a bare PATCH replaces the whole document. A Windows client that has never heard of a
    /// field Android added would delete it from the user's phone.</para>
    ///
    /// <para><see cref="FieldPaths"/> is therefore the exhaustive list of what this build owns, and
    /// it must be sent as <c>updateMask.fieldPaths</c> on every write. Adding a field here without
    /// also writing it, or writing one without listing it, are both bugs the tests catch.</para>
    /// </summary>
    public static class ProgressDocument
    {
        // Field names as Android writes them. These are wire identifiers, not display strings -
        // renaming one silently forks the two apps' data.
        public const string Xp = "xp";
        public const string Level = "level";
        public const string Streak = "streak";
        public const string StreakLastPlay = "streakLastPlay";
        public const string Achievements = "achievements";
        public const string Statistics = "statistics";
        public const string Notes = "notes";
        public const string Chats = "chats";
        public const string LastUpdated = "lastUpdated";

        /// <summary>
        /// The per-platform entitlement map: <c>entitlements.windows</c>, <c>entitlements.android</c>,
        /// <c>entitlements.ultimate</c>.
        /// </summary>
        /// <remarks>
        /// A map rather than three top-level fields so the platforms stay visibly parallel, and so
        /// a third one costs a key rather than a schema change.
        /// </remarks>
        public const string Entitlements = "entitlements";

        public const string WindowsTierPath = Entitlements + ".windows";
        public const string AndroidTierPath = Entitlements + ".android";
        public const string UltimatePath = Entitlements + ".ultimate";

        /// <summary>
        /// Every field this build owns, and nothing else.
        ///
        /// <para>Sent as the update mask so a PATCH touches exactly these. Anything Android writes
        /// that is absent from this list survives a Windows sync untouched, which is the whole
        /// point.</para>
        /// </summary>
        public static readonly IReadOnlyList<string> FieldPaths =
        [
            Xp, Level, Streak, StreakLastPlay, Achievements, Statistics, Notes, Chats, LastUpdated,

            // Nested paths, not the bare "entitlements" map. Masking the whole map would delete
            // entitlements.android on every Windows sync - the same co-ownership rule the mask
            // exists to enforce, one level further down.
            WindowsTierPath, UltimatePath,
        ];

        /// <summary>Build the document body for a PATCH.</summary>
        public static JsonObject ToDocument(ProgressSnapshot snapshot, DateTimeOffset now)
        {
            var fields = new Dictionary<string, JsonNode>(StringComparer.Ordinal)
            {
                [Xp] = FirestoreValue.Integer(snapshot.Xp),
                [Level] = FirestoreValue.Integer(snapshot.Level),
                [Streak] = FirestoreValue.Integer(snapshot.Streak),
                [StreakLastPlay] = FirestoreValue.String(snapshot.StreakLastPlay),
                [Notes] = FirestoreValue.String(snapshot.Notes),
                [LastUpdated] = FirestoreValue.Timestamp(now),

                [Achievements] = FirestoreValue.Array(
                    snapshot.Achievements.Select(a => FirestoreValue.Map(
                        new Dictionary<string, JsonNode>(StringComparer.Ordinal)
                        {
                            // An integerValue, because that is what Android writes. It reads a
                            // string id too, but writing one back would leave the field's type
                            // flip-flopping with whichever device synced last.
                            ["id"] = FirestoreValue.Integer(ParseId(a.Id)),
                            ["title"] = FirestoreValue.String(a.Title),
                            ["progress"] = FirestoreValue.Integer(a.Progress),
                            ["maxProgress"] = FirestoreValue.Integer(a.MaxProgress),
                        }))),

                [Statistics] = FirestoreValue.Map(
                    snapshot.Statistics.ToDictionary(
                        e => e.Key,
                        e => (JsonNode)FirestoreValue.Integer(e.Value),
                        StringComparer.Ordinal)),

                [Chats] = FirestoreValue.Array(snapshot.Chats.Select(FirestoreValue.String)),

                // Only the two sub-fields this platform owns are written. The android entry is
                // read and carried in the snapshot but never sent back, which is what keeps a
                // Windows sync from speaking for a device it knows nothing about.
                [Entitlements] = FirestoreValue.Map(
                    new Dictionary<string, JsonNode>(StringComparer.Ordinal)
                    {
                        ["windows"] = FirestoreValue.String(snapshot.WindowsTier),
                        ["ultimate"] = FirestoreValue.Boolean(snapshot.Ultimate),
                    }),
            };

            return FirestoreValue.Document(fields);
        }

        /// <summary>
        /// Read a document into a snapshot.
        /// </summary>
        /// <remarks>
        /// Every field is optional. A document written by an older build, or by a platform that has
        /// not shipped a feature yet, is a normal thing to meet - so a missing or oddly-typed field
        /// becomes a default rather than an exception.
        /// </remarks>
        public static ProgressSnapshot FromDocument(JsonNode? document)
        {
            var fields = FirestoreValue.ReadDocument(document);

            return new ProgressSnapshot
            {
                Xp = (int)(FirestoreValue.ReadInteger(Field(Xp)) ?? 0),
                Level = (int)(FirestoreValue.ReadInteger(Field(Level)) ?? 0),
                Streak = (int)(FirestoreValue.ReadInteger(Field(Streak)) ?? 0),
                StreakLastPlay = FirestoreValue.ReadString(Field(StreakLastPlay)),
                Notes = FirestoreValue.ReadString(Field(Notes)),

                Achievements = FirestoreValue.ReadArray(Field(Achievements))
                    .Select(FirestoreValue.ReadMap)
                    .Select(map => new AchievementProgress(
                        ReadId(map.GetValueOrDefault("id")),
                        (int)(FirestoreValue.ReadInteger(map.GetValueOrDefault("progress")) ?? 0),
                        (int)(FirestoreValue.ReadInteger(map.GetValueOrDefault("maxProgress")) ?? 0))
                    {
                        Title = FirestoreValue.ReadString(map.GetValueOrDefault("title")) ?? string.Empty,
                    })
                    // An achievement with no id cannot be merged against anything, so it is dropped
                    // rather than carried as a phantom entry keyed on empty string.
                    .Where(a => a.Id.Length > 0)
                    .ToList(),

                Statistics = FirestoreValue.ReadMap(Field(Statistics))
                    .Select(e => (e.Key, Value: FirestoreValue.ReadInteger(e.Value)))
                    .Where(e => e.Value is not null)
                    .ToDictionary(e => e.Key, e => (int)e.Value!.Value, StringComparer.Ordinal),

                Chats = FirestoreValue.ReadArray(Field(Chats))
                    .Select(FirestoreValue.ReadString)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c!)
                    .ToList(),

                WindowsTier = FirestoreValue.ReadString(Entitlement("windows")),
                AndroidTier = FirestoreValue.ReadString(Entitlement("android")),
                Ultimate = FirestoreValue.ReadBoolean(Entitlement("ultimate")) ?? false,
            };

            JsonNode? Entitlement(string platform) =>
                FirestoreValue.ReadMap(Field(Entitlements)).GetValueOrDefault(platform);

            JsonNode? Field(string name) => fields.GetValueOrDefault(name);
        }

        /// <summary>
        /// An achievement id, whichever type the writer used.
        /// </summary>
        /// <remarks>
        /// <para><b>Android writes it as a number.</b> Reading only <c>stringValue</c> therefore
        /// matched nothing the phone had written, and every achievement it sent was dropped by the
        /// empty-id guard below - silently, because a document with no achievements this build
        /// recognises is indistinguishable from a user who has earned none.</para>
        ///
        /// <para>Android is symmetric about this, accepting a number or a string, which is why the
        /// mismatch only broke one direction.</para>
        /// </remarks>
        private static string ReadId(JsonNode? value) =>
            FirestoreValue.ReadString(value)
            ?? FirestoreValue.ReadInteger(value)?.ToString(CultureInfo.InvariantCulture)
            ?? string.Empty;

        /// <summary>
        /// The id as a number for the wire. Non-numeric ids cannot round-trip through Android and
        /// are written as zero, which the id guard on read then discards.
        /// </summary>
        private static long ParseId(string id) =>
            long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

        /// <summary>The query string carrying the update mask, including the leading <c>?</c>.</summary>
        public static string UpdateMaskQuery() =>
            "?" + string.Join(
                "&",
                FieldPaths.Select(p => "updateMask.fieldPaths=" + Uri.EscapeDataString(p)));
    }
}
