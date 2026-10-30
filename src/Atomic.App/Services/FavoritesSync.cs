using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Atomic.Core.Sync;

namespace Atomic.App.Services
{
    /// <summary>
    /// Shares the tools' saved items between devices through <c>users/{uid}.favorites</c>.
    ///
    /// <para>The format and the merge are <see cref="FavoritesDocument"/> - pure and tested. What
    /// lives here is the round trip: read the document, merge each tool's list with this device's
    /// under the store's lock, and write back only what changed. The write masks the three lists
    /// alone, so progress, chats and anything else under <c>favorites</c> are untouched.</para>
    ///
    /// <para>Gated as saving them is: signed in, and PRO or PRO+. A free user cannot save favourites,
    /// so has nothing to sync.</para>
    /// </summary>
    public static class FavoritesSync
    {
        private static readonly SemaphoreSlim OneAtATime = new(1, 1);
        private static int _pending;

        /// <summary>Whether a sync could run now.</summary>
        public static bool CanSync => CloudSync.CanSync && SettingsStore.HasAnyPro;

        /// <summary>
        /// Sync after a save or removal. Changes made while a sync is running are picked up by one
        /// more pass rather than one pass each.
        /// </summary>
        public static async Task SyncSoonAsync()
        {
            if (!CanSync) return;
            if (Interlocked.Exchange(ref _pending, 1) == 1) return;

            await Task.Yield();
            await SyncAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Merge this device's saved items with the shared ones. Returns false when nothing could be
        /// synced - not entitled, offline, or the request failed - in which case nothing local changed.
        /// </summary>
        /// <remarks>
        /// A failed read must never be taken for an empty cloud: the merge would then write this
        /// device's lists over the other device's. So any failure stops before the merge.
        /// </remarks>
        public static async Task<bool> SyncAsync()
        {
            if (!CanSync)
            {
                Interlocked.Exchange(ref _pending, 0);
                return false;
            }

            await OneAtATime.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Exchange(ref _pending, 0);

                // Null is a user with no document yet, which really is empty; a failure throws.
                var cloud = FavoritesDocument.FromDocument(
                    await CloudSync.ReadDocumentAsync().ConfigureAwait(false));
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var merged = await FavoritesStore.MergeAsync(
                    (kind, local) => FavoritesDocument.Merge(local, cloud[kind], now)).ConfigureAwait(false);

                var differs = false;
                foreach (var (kind, list) in merged)
                    differs |= FavoritesDocument.Differs(list, cloud[kind]);

                if (differs)
                {
                    await CloudSync.WriteFieldsAsync(
                        FavoritesDocument.ToDocument(merged),
                        FavoritesDocument.UpdateMaskQuery()).ConfigureAwait(false);
                }
                return true;
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                return false;
            }
            finally
            {
                OneAtATime.Release();
            }
        }
    }
}
