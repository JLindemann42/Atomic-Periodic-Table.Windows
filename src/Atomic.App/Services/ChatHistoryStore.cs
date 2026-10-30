using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Atomic.Core.Sync;
using Windows.Storage;

namespace Atomic.App.Services
{
    /// <summary>
    /// One saved conversation: the local form of an entry in <c>users/{uid}.chats</c>.
    /// </summary>
    /// <remarks>
    /// Shaped after Android's <c>ChatSession</c> - id, title, timestamp, language, messages - so a
    /// conversation converts to and from the shared document without losing anything either platform
    /// needs. The messages are the panel's own <see cref="ChatMessage"/>s, so a conversation reopened
    /// here keeps its source buttons and chart card.
    /// </remarks>
    public sealed class StoredChatSession
    {
        /// <summary>
        /// Shared with Android, which matches conversations on it. May be empty for a conversation
        /// some other client wrote without one; it is kept as it is rather than given a new id, which
        /// would make it a second conversation on the next sync.
        /// </summary>
        public string Id { get; init; } = Guid.NewGuid().ToString();

        public string Title { get; set; } = ChatHistoryDocument.DefaultTitle;

        /// <summary>Epoch milliseconds of the last save, as Android stores it.</summary>
        public long Timestamp { get; set; }

        public string Language { get; set; } = "en";

        public List<ChatMessage> Messages { get; set; } = [];

        /// <summary>When it was last saved, in the user's own format, for the history list.</summary>
        [JsonIgnore]
        public string WhenDisplay =>
            DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).ToLocalTime()
                .ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// The saved conversations, in the local folder.
    ///
    /// <para>Kept locally whether or not the user is signed in, so the conversation on screen survives
    /// a restart and the list opens instantly; <see cref="ChatHistorySync"/> merges it with the copy
    /// shared with Android. Newest first and capped at Android's twenty, so neither side holds more
    /// than the other will keep.</para>
    ///
    /// <para>Serialization goes through <c>ElementJsonContext</c> for the reason recorded there - a
    /// trimmed build using the reflection overloads would read back an empty list and then overwrite
    /// the file with it.</para>
    /// </summary>
    public static class ChatHistoryStore
    {
        private const string FileName = "chat_sessions.json";

        /// <summary>
        /// The single thread earlier builds kept. Read once, turned into the first conversation, and
        /// removed, so upgrading does not lose what the user was in the middle of.
        /// </summary>
        private const string LegacyFileName = "chat_history.json";

        public static async Task<List<StoredChatSession>> LoadAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;

                if (await folder.TryGetItemAsync(FileName) is StorageFile file)
                {
                    using var stream = await file.OpenStreamForReadAsync();
                    if (stream.Length == 0) return [];

                    var sessions = await JsonSerializer.DeserializeAsync(
                        stream, ElementJsonContext.Default.ListStoredChatSession);

                    return Clean(sessions ?? []);
                }

                return await MigrateLegacyAsync(folder);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                // A corrupt history must not stop the panel opening. Returning empty is safe: nothing
                // overwrites the file until the user finishes another answer.
                return [];
            }
        }

        public static async Task SaveAsync(IEnumerable<StoredChatSession> sessions)
        {
            var keep = Clean(sessions)
                .OrderByDescending(s => s.Timestamp)
                .Take(ChatHistoryDocument.MaxSessions)
                .ToList();

            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    FileName, CreationCollisionOption.ReplaceExisting);
                using var stream = await file.OpenStreamForWriteAsync();
                await JsonSerializer.SerializeAsync(
                    stream, keep, ElementJsonContext.Default.ListStoredChatSession);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Nothing useful to do; the history stays correct in memory for this session.
            }
        }

        /// <summary>
        /// A thinking bubble must never come back from disk - it would restore a conversation that is
        /// permanently waiting for an answer that already arrived.
        /// </summary>
        private static List<StoredChatSession> Clean(IEnumerable<StoredChatSession> sessions)
        {
            var list = sessions.ToList();
            foreach (var session in list)
                session.Messages = session.Messages.Where(m => m.Role != ChatRole.Thinking).ToList();
            return list;
        }

        private static async Task<List<StoredChatSession>> MigrateLegacyAsync(StorageFolder folder)
        {
            if (await folder.TryGetItemAsync(LegacyFileName) is not StorageFile legacy) return [];

            List<ChatMessage>? messages;
            using (var stream = await legacy.OpenStreamForReadAsync())
            {
                messages = stream.Length == 0
                    ? []
                    : await JsonSerializer.DeserializeAsync(stream, ElementJsonContext.Default.ListChatMessage);
            }

            var thread = (messages ?? []).Where(m => m.Role != ChatRole.Thinking).ToList();
            var sessions = new List<StoredChatSession>();

            if (thread.Count > 0)
            {
                sessions.Add(new StoredChatSession
                {
                    Title = ChatHistoryDocument.Title(thread.FirstOrDefault(m => m.IsUser)?.Text),
                    Timestamp = thread[^1].Timestamp.ToUnixTimeMilliseconds(),
                    Language = AppLanguage.BaseCode(AppLanguage.Current),
                    Messages = thread,
                });
            }

            await SaveAsync(sessions);

            // Only after the new file is written, so a failure between the two cannot lose the thread.
            if (await folder.TryGetItemAsync(FileName) is StorageFile) await legacy.DeleteAsync();

            return sessions;
        }
    }
}
