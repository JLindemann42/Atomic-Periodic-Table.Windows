using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Atomic.Core.Sync;

namespace Atomic.App.Services
{
    /// <summary>
    /// Shares the conversations with Android through <c>users/{uid}.chats</c>.
    ///
    /// <para>The format and the merge are <see cref="ChatHistoryDocument"/> - pure and tested. What
    /// lives here is the conversion to and from the panel's own types and the round trip: read the
    /// document, merge with what this device has, and write back only when the merge says something
    /// changed. The write masks <c>chats</c> alone, so progress, and anything Android keeps in the
    /// same document, is untouched by it.</para>
    ///
    /// <para>Gated as Android gates it: signed in, and PRO or PRO+. Android saves no history for a
    /// free user, so syncing one from here would show the phone conversations it never offers to keep.</para>
    /// </summary>
    public static class ChatHistorySync
    {
        private static readonly SemaphoreSlim OneAtATime = new(1, 1);

        /// <summary>Whether the user's history is theirs to see: signed in, and PRO or PRO+.</summary>
        public static bool IsEntitled => CloudSync.IsSignedIn && SettingsStore.HasAnyPro;

        /// <summary>Whether a sync could run now: entitled, and this build has Firebase configured.</summary>
        public static bool CanSync => IsEntitled && CloudSync.CanSync;

        /// <summary>
        /// Merge <paramref name="local"/> with the shared history and return the result, or null when
        /// nothing could be synced - not entitled, offline, or the request failed.
        /// </summary>
        /// <remarks>
        /// <para>Null is the signal to keep what is on disk. A failure here must never be read as "the
        /// cloud is empty", which would be the same mistake that erased Android's history before.</para>
        ///
        /// <para>Single-flight: two syncs racing would each read, merge and write, and the second could
        /// write back a merge that does not include the first's conversation.</para>
        /// </remarks>
        public static async Task<List<StoredChatSession>?> SyncAsync(IReadOnlyList<StoredChatSession> local)
        {
            if (!CanSync) return null;

            await OneAtATime.WaitAsync().ConfigureAwait(true);
            try
            {
                var cloud = ChatHistoryDocument.FromDocument(
                    await CloudSync.ReadDocumentAsync().ConfigureAwait(true));

                var merged = ChatHistoryDocument.Merge(local.Select(ToShared).ToList(), cloud);

                if (ChatHistoryDocument.DiffersFrom(merged, cloud))
                {
                    await CloudSync.WriteFieldsAsync(
                        ChatHistoryDocument.ToDocument(merged),
                        ChatHistoryDocument.UpdateMaskQuery()).ConfigureAwait(true);
                }

                return merged.Select(ToLocal).ToList();
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                return null;
            }
            finally
            {
                OneAtATime.Release();
            }
        }

        // ---- Conversion ----------------------------------------------------------------------------

        private static ChatHistorySession ToShared(StoredChatSession session) =>
            new(session.Id,
                session.Title,
                session.Timestamp,
                session.Language,
                session.Messages
                    .Where(m => m.Role != ChatRole.Thinking)
                    .Select(m => new ChatHistoryMessage(
                        m.Id, m.Text, m.Role == ChatRole.User, m.Timestamp.ToUnixTimeMilliseconds())
                    {
                        Actions = m.Actions,
                        Cards = m.Cards,
                    })
                    .ToList());

        private static StoredChatSession ToLocal(ChatHistorySession session) => new()
        {
            Id = session.Id,
            Title = session.Title,
            Timestamp = session.Timestamp,
            Language = string.IsNullOrEmpty(session.Language) ? "en" : session.Language,
            Messages = session.Messages
                .Select(m => new ChatMessage(
                    m.IsFromUser ? ChatRole.User : ChatRole.Assistant, m.Text, m.Actions, m.Cards)
                {
                    // An Android message without an id is given one here, but only in this copy: the
                    // shared copy keeps the original, and the merge matches messages for nothing but
                    // carrying their raw values, so a new id costs nothing there.
                    Id = string.IsNullOrEmpty(m.Id) ? Guid.NewGuid().ToString() : m.Id,
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(m.Timestamp),
                })
                .ToList(),
        };
    }
}
