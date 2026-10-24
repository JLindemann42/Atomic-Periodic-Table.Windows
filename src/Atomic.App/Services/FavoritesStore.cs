using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Atomic.App.Tools;
using Atomic.Core.Sync;
using Windows.Storage;

namespace Atomic.App.Services
{
    /// <summary>The record that a saved item was removed, kept so the removal can sync.</summary>
    public sealed class RemovedFavorite
    {
        public FavoriteKind Kind { get; set; }
        public string Key { get; set; } = string.Empty;
        public long RemovedAt { get; set; }
    }

    /// <summary>
    /// Reads and writes the tools' saved favourites: the calculator's compounds, the equation
    /// balancer's equations and the ideal gas calculator's results.
    ///
    /// <para>One JSON file per tool in the local folder. Android stores these as a SharedPreferences
    /// StringSet with fields separated by U+001F; that encoding exists because a StringSet cannot hold
    /// structure, and there is no reason to reproduce it here - see docs/porting-notes.md D7.</para>
    ///
    /// <para><b>Every change is a read-modify-write of the file, under one lock.</b> The pages used to
    /// write their whole on-screen list back. With sync that loses data: a sync that lands items from
    /// the other device between a page's load and its next save would see them overwritten by the
    /// page's older list. So the pages ask for one item to be saved or removed, and get the new list
    /// back.</para>
    ///
    /// <para>Each item carries the time it was saved, and a removal leaves a <see cref="RemovedFavorite"/>,
    /// which is what <see cref="FavoritesSync"/> needs to tell "removed here" from "not seen here yet".</para>
    ///
    /// <para>All serialization goes through <see cref="ElementJsonContext"/>. A reflection-based
    /// overload returns an empty list under trimming and then overwrites the file with it, which
    /// silently destroys the one thing in the app the user actually created.</para>
    /// </summary>
    public static class FavoritesStore
    {
        private const string CompoundsFile = "favorites.json";
        private const string EquationsFile = "equation_favorites.json";
        private const string IdealGasFile = "ideal_gas_favorites.json";
        private const string RemovedFile = "favorites_removed.json";

        private static readonly SemaphoreSlim Lock = new(1, 1);

        /// <summary>
        /// How one tool's items map onto the shared form. The calculator lists oldest first, as it
        /// always has; the other two newest first.
        /// </summary>
        private sealed record Tool<T>(
            FavoriteKind Kind,
            string File,
            JsonTypeInfo<List<T>> TypeInfo,
            Func<T, string> Key,
            Func<T, string> Value,
            Func<T, long> SavedAt,
            Action<T, long> Stamp,
            Func<SyncedFavorite, T?> FromShared,
            bool NewestFirst) where T : class;

        private static readonly Tool<FavoriteCompound> Compounds = new(
            FavoriteKind.Compound, CompoundsFile, ElementJsonContext.Default.ListFavoriteCompound,
            c => c.Formula ?? string.Empty,
            c => c.MolarMass.ToString("R", CultureInfo.InvariantCulture),
            c => c.SavedAt, (c, at) => c.SavedAt = at,
            s => LeadingNumber(s.Value) is { } mass
                ? new FavoriteCompound { Formula = s.Key, MolarMass = mass, SavedAt = s.UpdatedAt }
                : null,
            NewestFirst: false);

        private static readonly Tool<SavedEquation> Equations = new(
            FavoriteKind.Equation, EquationsFile, ElementJsonContext.Default.ListSavedEquation,
            e => e.Input, e => e.Balanced,
            e => e.SavedAt, (e, at) => e.SavedAt = at,
            s => new SavedEquation { Input = s.Key, Balanced = s.Value, SavedAt = s.UpdatedAt },
            NewestFirst: true);

        private static readonly Tool<SavedIdealGas> IdealGas = new(
            FavoriteKind.IdealGas, IdealGasFile, ElementJsonContext.Default.ListSavedIdealGas,
            g => g.Summary, g => g.Result,
            g => g.SavedAt, (g, at) => g.SavedAt = at,
            s => new SavedIdealGas { Summary = s.Key, Result = s.Value, SavedAt = s.UpdatedAt },
            NewestFirst: true);

        /// <summary>Raised after a sync changed what is saved here, so an open page can reload. Any thread.</summary>
        public static event EventHandler<FavoriteKind>? Changed;

        // ---- The pages' API --------------------------------------------------------------------

        public static Task<List<FavoriteCompound>> LoadCompoundsAsync() => LoadLockedAsync(Compounds);
        public static Task<List<SavedEquation>> LoadEquationsAsync() => LoadLockedAsync(Equations);
        public static Task<List<SavedIdealGas>> LoadIdealGasAsync() => LoadLockedAsync(IdealGas);

        public static Task<List<FavoriteCompound>> SaveCompoundAsync(FavoriteCompound item) => SaveAsync(Compounds, item);
        public static Task<List<SavedEquation>> SaveEquationAsync(SavedEquation item) => SaveAsync(Equations, item);
        public static Task<List<SavedIdealGas>> SaveIdealGasAsync(SavedIdealGas item) => SaveAsync(IdealGas, item);

        public static Task<List<FavoriteCompound>> RemoveCompoundAsync(string formula) => RemoveAsync(Compounds, formula);
        public static Task<List<SavedEquation>> RemoveEquationAsync(string input) => RemoveAsync(Equations, input);
        public static Task<List<SavedIdealGas>> RemoveIdealGasAsync(string summary) => RemoveAsync(IdealGas, summary);

        private static async Task<List<T>> LoadLockedAsync<T>(Tool<T> list) where T : class
        {
            await Lock.WaitAsync().ConfigureAwait(true);
            try
            {
                return await LoadAsync(list).ConfigureAwait(true);
            }
            finally
            {
                Lock.Release();
            }
        }

        /// <summary>Save one item, replacing any with the same key, and return the new list.</summary>
        private static async Task<List<T>> SaveAsync<T>(Tool<T> list, T item) where T : class
        {
            await Lock.WaitAsync().ConfigureAwait(true);
            try
            {
                var key = list.Key(item);
                var items = await LoadAsync(list).ConfigureAwait(true);
                items.RemoveAll(i => list.Key(i) == key);

                list.Stamp(item, Now());
                if (list.NewestFirst) items.Insert(0, item);
                else items.Add(item);

                await WriteAsync(list.File, items, list.TypeInfo).ConfigureAwait(true);
                _ = FavoritesSync.SyncSoonAsync();
                return items;
            }
            finally
            {
                Lock.Release();
            }
        }

        /// <summary>Remove the item with this key, remembering that it was removed, and return the new list.</summary>
        private static async Task<List<T>> RemoveAsync<T>(Tool<T> list, string key) where T : class
        {
            await Lock.WaitAsync().ConfigureAwait(true);
            try
            {
                var items = await LoadAsync(list).ConfigureAwait(true);
                if (items.RemoveAll(i => list.Key(i) == key) == 0) return items;

                await WriteAsync(list.File, items, list.TypeInfo).ConfigureAwait(true);

                var removed = await ReadAsync(RemovedFile, ElementJsonContext.Default.ListRemovedFavorite).ConfigureAwait(true);
                removed.RemoveAll(r => r.Kind == list.Kind && r.Key == key);
                removed.Add(new RemovedFavorite { Kind = list.Kind, Key = key, RemovedAt = Now() });
                await WriteAsync(RemovedFile, removed, ElementJsonContext.Default.ListRemovedFavorite).ConfigureAwait(true);

                _ = FavoritesSync.SyncSoonAsync();
                return items;
            }
            finally
            {
                Lock.Release();
            }
        }

        // ---- The sync's API --------------------------------------------------------------------

        /// <summary>
        /// Run <paramref name="merge"/> over everything saved here, as the shared form, and store what
        /// it returns - all under the lock, so a save cannot slip in between the read and the write.
        /// </summary>
        /// <returns>The merged lists, to write to the cloud.</returns>
        internal static async Task<IReadOnlyDictionary<FavoriteKind, IReadOnlyList<SyncedFavorite>>> MergeAsync(
            Func<FavoriteKind, IReadOnlyList<SyncedFavorite>, IReadOnlyList<SyncedFavorite>> merge)
        {
            await Lock.WaitAsync().ConfigureAwait(true);
            try
            {
                // Loading the ideal gas list first lets any old-format summaries be rewritten, and their
                // removal records written, before those records are read below.
                await LoadAsync(IdealGas).ConfigureAwait(true);

                var removed = await ReadAsync(RemovedFile, ElementJsonContext.Default.ListRemovedFavorite).ConfigureAwait(true);
                var result = new Dictionary<FavoriteKind, IReadOnlyList<SyncedFavorite>>();
                var changed = new List<FavoriteKind>();

                foreach (var (kind, merged, didChange) in new[]
                {
                    await MergeOneAsync(Compounds, removed, merge).ConfigureAwait(true),
                    await MergeOneAsync(Equations, removed, merge).ConfigureAwait(true),
                    await MergeOneAsync(IdealGas, removed, merge).ConfigureAwait(true),
                })
                {
                    result[kind] = merged;
                    if (didChange) changed.Add(kind);
                }

                var stillRemoved = result
                    .SelectMany(r => r.Value
                        .Where(f => f.Deleted && !string.IsNullOrEmpty(f.Key))
                        .Select(f => new RemovedFavorite { Kind = r.Key, Key = f.Key, RemovedAt = f.UpdatedAt }))
                    .ToList();
                await WriteAsync(RemovedFile, stillRemoved, ElementJsonContext.Default.ListRemovedFavorite).ConfigureAwait(true);

                foreach (var kind in changed) Changed?.Invoke(null, kind);
                return result;
            }
            finally
            {
                Lock.Release();
            }
        }

        private static async Task<(FavoriteKind, IReadOnlyList<SyncedFavorite>, bool)> MergeOneAsync<T>(
            Tool<T> list, IReadOnlyList<RemovedFavorite> removed,
            Func<FavoriteKind, IReadOnlyList<SyncedFavorite>, IReadOnlyList<SyncedFavorite>> merge) where T : class
        {
            var items = await LoadAsync(list).ConfigureAwait(true);

            // A removal and a later save of the same key can both be on disk; the newer is what happened.
            var local = items
                .Select(i => new SyncedFavorite(list.Key(i), list.Value(i), list.SavedAt(i)))
                .Concat(removed.Where(r => r.Kind == list.Kind)
                    .Select(r => new SyncedFavorite(r.Key, string.Empty, r.RemovedAt, Deleted: true)))
                .GroupBy(f => f.Key, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(f => f.UpdatedAt).First())
                .ToList();

            var merged = merge(list.Kind, local);

            var live = merged.Where(f => !f.Deleted && !string.IsNullOrEmpty(f.Key))
                .Select(list.FromShared)
                .OfType<T>();
            var ordered = (list.NewestFirst
                    ? live.OrderByDescending(list.SavedAt)
                    : live.OrderBy(list.SavedAt))
                .ToList();

            var changed = !ordered.Select(list.Key).SequenceEqual(items.Select(list.Key), StringComparer.Ordinal)
                || !ordered.Select(list.Value).SequenceEqual(items.Select(list.Value), StringComparer.Ordinal);
            if (changed) await WriteAsync(list.File, ordered, list.TypeInfo).ConfigureAwait(true);

            return (list.Kind, merged, changed);
        }

        // ---- Files -----------------------------------------------------------------------------

        /// <summary>
        /// The tool's items. One saved before items carried a time is given one now, spaced a
        /// millisecond apart in list order, so it keeps its place and does not lose to everything.
        /// </summary>
        private static async Task<List<T>> LoadAsync<T>(Tool<T> list) where T : class
        {
            var items = await ReadAsync(list.File, list.TypeInfo).ConfigureAwait(true);
            if (items is List<SavedIdealGas> gases) await RewriteOldSummariesAsync(gases).ConfigureAwait(true);
            if (items.All(i => list.SavedAt(i) != 0)) return items;

            var now = Now();
            for (var i = 0; i < items.Count; i++)
            {
                if (list.SavedAt(items[i]) == 0)
                    list.Stamp(items[i], list.NewestFirst ? now - i : now - (items.Count - i));
            }
            await WriteAsync(list.File, items, list.TypeInfo).ConfigureAwait(true);
            return items;
        }

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static readonly System.Text.RegularExpressions.Regex OldSummarySymbol = new(
            @"(?<=^|, )([PVnT]) = ", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// Rewrite ideal gas summaries saved as "P = 1 atm" to Android's "P=1 atm", so a calculation
        /// saved on both platforms is one favourite. The old text is recorded as removed, so a copy
        /// of it already synced is taken out of the cloud instead of standing beside the new one.
        /// Caller holds the lock.
        /// </summary>
        private static async Task RewriteOldSummariesAsync(List<SavedIdealGas> gases)
        {
            var renamed = new List<string>();
            foreach (var gas in gases)
            {
                var rewritten = OldSummarySymbol.Replace(gas.Summary, "$1=");
                if (rewritten == gas.Summary) continue;
                renamed.Add(gas.Summary);
                gas.Summary = rewritten;
            }
            if (renamed.Count == 0) return;

            // Two old entries can now read the same; keep the newer.
            var deduped = gases
                .GroupBy(g => g.Summary, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(x => x.SavedAt).First())
                .ToList();
            gases.Clear();
            gases.AddRange(deduped);
            await WriteAsync(IdealGasFile, gases, ElementJsonContext.Default.ListSavedIdealGas).ConfigureAwait(true);

            var removed = await ReadAsync(RemovedFile, ElementJsonContext.Default.ListRemovedFavorite).ConfigureAwait(true);
            var now = Now();
            foreach (var old in renamed)
            {
                removed.RemoveAll(r => r.Kind == FavoriteKind.IdealGas && r.Key == old);
                removed.Add(new RemovedFavorite { Kind = FavoriteKind.IdealGas, Key = old, RemovedAt = now });
            }
            await WriteAsync(RemovedFile, removed, ElementJsonContext.Default.ListRemovedFavorite).ConfigureAwait(true);
        }

        private static readonly System.Text.RegularExpressions.Regex LeadingNumberPattern = new(
            @"^\s*[-+]?[0-9]+(?:\.[0-9]+)?(?:[eE][-+]?[0-9]+)?",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// The molar mass at the start of a shared value. Both apps write the bare number, but Android
        /// shows its masses as "18.015 (g/mol)", and one written that way should still be read.
        /// </summary>
        private static double? LeadingNumber(string value) =>
            LeadingNumberPattern.Match(value) is { Success: true } match &&
            double.TryParse(match.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        private static async Task<List<T>> ReadAsync<T>(
            string fileName, JsonTypeInfo<List<T>> typeInfo)
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var item = await folder.TryGetItemAsync(fileName);
                if (item is not StorageFile file) return [];

                using var stream = await file.OpenStreamForReadAsync();
                if (stream.Length == 0) return [];

                return await JsonSerializer.DeserializeAsync(stream, typeInfo) ?? [];
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                // A corrupt or unreadable favourites file must not stop the tool opening. Returning
                // empty is safe: nothing overwrites the file until the user saves something.
                return [];
            }
        }

        private static async Task WriteAsync<T>(
            string fileName, List<T> items,
            JsonTypeInfo<List<T>> typeInfo)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    fileName, CreationCollisionOption.ReplaceExisting);
                using var stream = await file.OpenStreamForWriteAsync();
                await JsonSerializer.SerializeAsync(stream, items, typeInfo);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Nothing useful to do here; the list stays correct in memory for this session.
            }
        }
    }
}
