using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace Atomic.App.Services
{
    /// <summary>
    /// Persists the chat thread to the local folder.
    ///
    /// <para>This is the local half only. The Firestore half arrives in P9 and merges against the
    /// same records, which is why <see cref="ChatMessage.Actions"/> and
    /// <see cref="ChatMessage.Cards"/> stay codec strings: Android stores them that way, so a synced
    /// thread reads on both platforms without a conversion step that could only ever lose
    /// information.</para>
    ///
    /// <para>Serialization goes through <c>ElementJsonContext</c> for the reason recorded there - a
    /// trimmed build using the reflection overloads would read back an empty thread and then
    /// overwrite the file with it.</para>
    /// </summary>
    public static class ChatHistoryStore
    {
        private const string FileName = "chat_history.json";

        /// <summary>
        /// How many turns are kept.
        ///
        /// <para>Matches the cap Android's <c>ProgressSyncManager</c> applies to <c>chats[]</c>, so
        /// neither platform can push a thread the other would truncate on read. Trimming here rather
        /// than at sync time also keeps the file small enough that loading it is never felt.</para>
        /// </summary>
        public const int MaxMessages = 20;

        public static async Task<List<ChatMessage>> LoadAsync()
        {
            try
            {
                var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync(FileName);
                if (item is not StorageFile file) return [];

                using var stream = await file.OpenStreamForReadAsync();
                if (stream.Length == 0) return [];

                var messages = await JsonSerializer.DeserializeAsync(
                    stream, ElementJsonContext.Default.ListChatMessage);

                // A thinking bubble must never come back from disk - it would restore a
                // conversation that is permanently waiting for an answer that already arrived.
                return messages?.Where(m => m.Role != ChatRole.Thinking).ToList() ?? [];
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                // A corrupt history must not stop the panel opening. Returning empty is safe:
                // nothing overwrites the file until the user sends another message.
                return [];
            }
        }

        public static async Task SaveAsync(IEnumerable<ChatMessage> messages)
        {
            var keep = messages
                .Where(m => m.Role != ChatRole.Thinking)
                .TakeLast(MaxMessages)
                .ToList();

            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    FileName, CreationCollisionOption.ReplaceExisting);
                using var stream = await file.OpenStreamForWriteAsync();
                await JsonSerializer.SerializeAsync(
                    stream, keep, ElementJsonContext.Default.ListChatMessage);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Nothing useful to do; the thread stays correct in memory for this session.
            }
        }

        public static async Task ClearAsync()
        {
            try
            {
                var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync(FileName);
                if (item is StorageFile file) await file.DeleteAsync();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
