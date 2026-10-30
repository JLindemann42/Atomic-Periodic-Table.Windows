using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Atomic.Core.Sync
{
    /// <summary>One message in a synced conversation, in Android's shape.</summary>
    /// <param name="Timestamp">Epoch milliseconds, as Android's <c>System.currentTimeMillis()</c>.</param>
    public sealed record ChatHistoryMessage(string Id, string Text, bool IsFromUser, long Timestamp)
    {
        /// <summary>
        /// Windows' encoded source buttons, or null. Android neither writes nor reads it, and keeps
        /// it when it rewrites the array, so a conversation keeps its buttons on the way back.
        /// </summary>
        public string? Actions { get; init; }

        /// <summary>Windows' encoded chart card, or null. Carried the same way as <see cref="Actions"/>.</summary>
        public string? Cards { get; init; }

        /// <summary>
        /// The Firestore value this was read from, or null for a message written here. Kept so fields
        /// this build does not know survive a rewrite - see <see cref="ChatHistoryDocument"/>.
        /// </summary>
        public JsonNode? Raw { get; init; }
    }

    /// <summary>One conversation, as Android's <c>ChatHistoryManager</c> stores it.</summary>
    /// <param name="Timestamp">Epoch milliseconds of the last save; the newest conversation sorts first.</param>
    /// <param name="Language">The language the conversation was answered in, e.g. "sv".</param>
    public sealed record ChatHistorySession(
        string Id,
        string Title,
        long Timestamp,
        string Language,
        IReadOnlyList<ChatHistoryMessage> Messages)
    {
        /// <summary>The Firestore value this was read from, or null for a conversation started here.</summary>
        public JsonNode? Raw { get; init; }
    }

    /// <summary>
    /// <c>users/{uid}.chats</c>: the user's conversations, shared with Android.
    ///
    /// <para><b>Owned separately from progress.</b> Android's <c>ChatHistoryManager</c> owns this array
    /// and its <c>ProgressSyncManager</c> leaves it alone; this class plays the same role here, with its
    /// own PATCH that masks <c>chats</c> and nothing else. Progress sync used to claim the field and
    /// erased the array on every sync - see <see cref="ProgressDocument"/>.</para>
    ///
    /// <para><b>Nothing Android wrote is lost by a Windows write.</b> A conversation read from the cloud
    /// keeps its original Firestore value, and when Windows writes it back - unchanged, or with new
    /// messages - it starts from that value and overwrites only the fields it knows. A field Android
    /// adds next year rides through untouched, on the conversation and on each message. A conversation
    /// with no id cannot be matched, so it is carried through as it is rather than dropped.</para>
    ///
    /// <para>Pure, like <see cref="ProgressMerge"/>, for the same reason: this is a place where a bug
    /// deletes a user's data on their other device, so it is tested well away from the network.</para>
    /// </summary>
    public static class ChatHistoryDocument
    {
        public const string Chats = "chats";

        /// <summary>Android keeps its twenty newest conversations; so does this.</summary>
        public const int MaxSessions = 20;

        /// <summary>The title when a conversation has no user message, as Android writes it.</summary>
        public const string DefaultTitle = "New Chat";

        /// <summary>The only field a chat history PATCH may touch.</summary>
        public static readonly IReadOnlyList<string> FieldPaths = [Chats];

        public static string UpdateMaskQuery() =>
            "?updateMask.fieldPaths=" + Uri.EscapeDataString(Chats);

        /// <summary>
        /// A conversation's title, exactly as Android derives it: the first user message, cut to 37
        /// characters and an ellipsis when it is longer than 40.
        /// </summary>
        /// <remarks>
        /// Matching matters because both platforms rewrite the title on every save. Two rules would
        /// leave it changing with whichever device last touched the conversation.
        /// </remarks>
        public static string Title(string? firstUserMessage)
        {
            if (string.IsNullOrEmpty(firstUserMessage)) return DefaultTitle;
            return firstUserMessage.Length > 40 ? firstUserMessage[..37] + "..." : firstUserMessage;
        }

        // ---- Reading -----------------------------------------------------------------------------

        /// <summary>Every conversation in a document, newest first. Anything unreadable is skipped.</summary>
        public static IReadOnlyList<ChatHistorySession> FromDocument(JsonNode? document)
        {
            var fields = FirestoreValue.ReadDocument(document);
            if (!fields.TryGetValue(Chats, out var chats)) return [];

            return FirestoreValue.ReadArray(chats)
                .Select(ReadSession)
                .OfType<ChatHistorySession>()
                .OrderByDescending(s => s.Timestamp)
                .ToList();
        }

        private static ChatHistorySession? ReadSession(JsonNode? value)
        {
            var map = FirestoreValue.ReadMap(value);
            if (map.Count == 0) return null;

            var messages = FirestoreValue.ReadArray(Get(map, "messages"))
                .Select(ReadMessage)
                .OfType<ChatHistoryMessage>()
                .ToList();

            return new ChatHistorySession(
                FirestoreValue.ReadString(Get(map, "id")) ?? string.Empty,
                FirestoreValue.ReadString(Get(map, "title")) ?? string.Empty,
                ReadMillis(Get(map, "timestamp")),
                FirestoreValue.ReadString(Get(map, "language")) ?? "en",
                messages)
            {
                Raw = value?.DeepClone(),
            };
        }

        private static ChatHistoryMessage? ReadMessage(JsonNode? value)
        {
            var map = FirestoreValue.ReadMap(value);
            if (map.Count == 0) return null;

            return new ChatHistoryMessage(
                FirestoreValue.ReadString(Get(map, "id")) ?? string.Empty,
                FirestoreValue.ReadString(Get(map, "text")) ?? string.Empty,

                // Android's default when the field is missing: a message with no author is the
                // user's. Defaulting the other way would turn a damaged question into an answer.
                FirestoreValue.ReadBoolean(Get(map, "isFromUser")) ?? true,
                ReadMillis(Get(map, "timestamp")))
            {
                Actions = FirestoreValue.ReadString(Get(map, "actions")),
                Cards = FirestoreValue.ReadString(Get(map, "cards")),
                Raw = value?.DeepClone(),
            };
        }

        /// <summary>An integer, or a whole double - Firestore can narrow or widen either way.</summary>
        private static long ReadMillis(JsonNode? value) =>
            FirestoreValue.ReadInteger(value)
            ?? (FirestoreValue.ReadDouble(value) is { } d ? (long)d : 0L);

        private static JsonNode? Get(IReadOnlyDictionary<string, JsonNode?> map, string key) =>
            map.TryGetValue(key, out var value) ? value : null;

        // ---- Writing -----------------------------------------------------------------------------

        /// <summary>The PATCH body: a document carrying <c>chats</c> and nothing else.</summary>
        public static JsonObject ToDocument(IReadOnlyList<ChatHistorySession> sessions) =>
            FirestoreValue.Document(new Dictionary<string, JsonNode>(StringComparer.Ordinal)
            {
                [Chats] = FirestoreValue.Array(sessions.Select(SessionValue)),
            });

        private static JsonNode SessionValue(ChatHistorySession session)
        {
            // Start from what was read, so a field this build does not know is written back as it was.
            var fields = Fields(session.Raw);

            fields["id"] = FirestoreValue.String(session.Id);
            fields["title"] = FirestoreValue.String(session.Title);
            fields["timestamp"] = FirestoreValue.Integer(session.Timestamp);
            fields["language"] = FirestoreValue.String(session.Language);
            fields["messages"] = FirestoreValue.Array(session.Messages.Select(MessageValue));

            return FirestoreValue.Map(fields);
        }

        private static JsonNode MessageValue(ChatHistoryMessage message)
        {
            var fields = Fields(message.Raw);

            fields["id"] = FirestoreValue.String(message.Id);
            fields["text"] = FirestoreValue.String(message.Text);
            fields["isFromUser"] = FirestoreValue.Boolean(message.IsFromUser);
            fields["timestamp"] = FirestoreValue.Integer(message.Timestamp);

            // Only when there is something to say: Android's own messages have neither, and writing
            // an explicit null onto every one of them would change data that was not ours to change.
            if (message.Actions is { Length: > 0 } actions) fields["actions"] = FirestoreValue.String(actions);
            if (message.Cards is { Length: > 0 } cards) fields["cards"] = FirestoreValue.String(cards);

            return FirestoreValue.Map(fields);
        }

        /// <summary>A detached copy of a value's map fields, to be written into a new value.</summary>
        private static Dictionary<string, JsonNode> Fields(JsonNode? raw)
        {
            var fields = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            foreach (var (key, value) in FirestoreValue.ReadMap(raw))
            {
                if (value is not null) fields[key] = value.DeepClone();
            }
            return fields;
        }

        // ---- Merging -----------------------------------------------------------------------------

        /// <summary>
        /// Combine this device's conversations with the cloud's.
        /// </summary>
        /// <remarks>
        /// <para>The union, matched by id. Where both have a conversation the newer save wins - it is
        /// the one with the latest messages - and on a tie the one with more messages, then the
        /// cloud's, which carries the value Android wrote. The winner keeps the cloud's raw values for
        /// itself and for every message they share, so a Windows rewrite does not shed fields Android
        /// added.</para>
        ///
        /// <para>Newest first and capped at <see cref="MaxSessions"/>, as Android trims. A conversation
        /// with no id cannot be matched and is kept as it is. Like the progress merge, merging the
        /// result again changes nothing.</para>
        /// </remarks>
        public static IReadOnlyList<ChatHistorySession> Merge(
            IReadOnlyList<ChatHistorySession> local,
            IReadOnlyList<ChatHistorySession> cloud)
        {
            var byId = new Dictionary<string, ChatHistorySession>(StringComparer.Ordinal);
            var unmatched = new List<ChatHistorySession>();

            foreach (var theirs in cloud)
            {
                if (string.IsNullOrEmpty(theirs.Id)) unmatched.Add(theirs);
                else byId[theirs.Id] = theirs;
            }

            foreach (var mine in local)
            {
                if (string.IsNullOrEmpty(mine.Id))
                {
                    unmatched.Add(mine);
                    continue;
                }

                byId[mine.Id] = byId.TryGetValue(mine.Id, out var theirs)
                    ? Winner(mine, theirs)
                    : mine;
            }

            return byId.Values
                .Concat(unmatched)
                .OrderByDescending(s => s.Timestamp)
                .Take(MaxSessions)
                .ToList();
        }

        private static ChatHistorySession Winner(ChatHistorySession mine, ChatHistorySession theirs)
        {
            var mineWins = mine.Timestamp != theirs.Timestamp
                ? mine.Timestamp > theirs.Timestamp
                : mine.Messages.Count > theirs.Messages.Count;

            return mineWins ? CarryRaw(mine, theirs) : theirs;
        }

        /// <summary>Our content, on top of the values the cloud copy was read from.</summary>
        private static ChatHistorySession CarryRaw(ChatHistorySession mine, ChatHistorySession theirs)
        {
            var theirMessages = theirs.Messages
                .Where(m => !string.IsNullOrEmpty(m.Id))
                .GroupBy(m => m.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            return mine with
            {
                Raw = mine.Raw ?? theirs.Raw,
                Messages = mine.Messages
                    .Select(m => m.Raw is null && theirMessages.TryGetValue(m.Id, out var theirs2)
                        ? m with { Raw = theirs2.Raw }
                        : m)
                    .ToList(),
            };
        }

        /// <summary>
        /// Whether writing <paramref name="merged"/> would change what the cloud holds.
        /// </summary>
        /// <remarks>
        /// Compared on what identifies a save - id, timestamp and message count, in order - so opening
        /// the history does not cost a write when nothing new was said on this device.
        /// </remarks>
        public static bool DiffersFrom(
            IReadOnlyList<ChatHistorySession> merged,
            IReadOnlyList<ChatHistorySession> cloud) =>
            !merged.Select(Signature).SequenceEqual(
                cloud.OrderByDescending(s => s.Timestamp).Take(MaxSessions).Select(Signature),
                StringComparer.Ordinal);

        private static string Signature(ChatHistorySession s) =>
            string.Concat(
                s.Id, "|",
                s.Timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture), "|",
                s.Messages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
