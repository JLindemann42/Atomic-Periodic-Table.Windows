using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Atomic.Core.Sync
{
    /// <summary>The three tools whose saved items sync.</summary>
    public enum FavoriteKind
    {
        /// <summary>The molar mass calculator: a formula and its molar mass.</summary>
        Compound,

        /// <summary>The equation balancer: what was typed and what it balanced to.</summary>
        Equation,

        /// <summary>The ideal gas calculator: the inputs and the result.</summary>
        IdealGas,
    }

    /// <summary>
    /// One saved item, or the record that it was removed.
    /// </summary>
    /// <param name="Key">What identifies it within its tool: the formula, the typed equation, the inputs.</param>
    /// <param name="Value">What it saved: the molar mass, the balanced equation, the result.</param>
    /// <param name="UpdatedAt">Epoch milliseconds of the last save or removal; the newer one wins.</param>
    /// <param name="Deleted">A removal. Kept, so the removal reaches the other devices instead of being undone by them.</param>
    public sealed record SyncedFavorite(string Key, string Value, long UpdatedAt, bool Deleted = false)
    {
        /// <summary>The Firestore value this was read from, so fields this build does not know survive a write.</summary>
        public JsonNode? Raw { get; init; }
    }

    /// <summary>
    /// <c>users/{uid}.favorites</c>: the calculator's, equation balancer's and ideal gas calculator's saved
    /// items, synced for PRO users.
    ///
    /// <para><b>Defined here first.</b> Android keeps these on the device only, as delimited strings in
    /// SharedPreferences ("formula:mass"), which break on a value that contains the delimiter. The shared
    /// form is structured - one map per item with named fields - so Android can adopt it by mapping its
    /// strings onto it, without either side needing a migration.</para>
    ///
    /// <para><b>Removals are records, not absences.</b> Merging two lists by union - as chat history does,
    /// where nothing is ever deleted - would bring a removed favourite straight back from the other device
    /// on the next sync. So a removal is kept as a <see cref="SyncedFavorite.Deleted"/> record with the time
    /// it happened, and per item the newer of the two sides wins: removing beats an older save, and saving
    /// again beats an older removal. Removal records expire after <see cref="TombstoneLifetime"/>.</para>
    ///
    /// <para>The write masks the three nested paths and nothing else, so another field under
    /// <c>favorites</c> - Android adding its unit converter's list, say - survives a Windows sync. Pure, like
    /// the other merges, because this is a place where a bug deletes a user's data on their other
    /// device.</para>
    /// </summary>
    public static class FavoritesDocument
    {
        public const string Favorites = "favorites";

        /// <summary>The most saved items kept per tool, newest first.</summary>
        public const int MaxPerKind = 200;

        /// <summary>
        /// How long a removal is remembered. A device that stays offline for longer than this and still
        /// holds the item will bring it back - the price of not keeping every removal forever.
        /// </summary>
        public static readonly TimeSpan TombstoneLifetime = TimeSpan.FromDays(180);

        private static readonly FavoriteKind[] Kinds = [FavoriteKind.Compound, FavoriteKind.Equation, FavoriteKind.IdealGas];

        /// <summary>The wire names: the array under <c>favorites</c>, and the item's two fields.</summary>
        private static (string Array, string KeyField, string ValueField) Wire(FavoriteKind kind) => kind switch
        {
            FavoriteKind.Compound => ("compounds", "formula", "molarMass"),
            FavoriteKind.Equation => ("equations", "input", "balanced"),
            FavoriteKind.IdealGas => ("idealGas", "summary", "result"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        /// <summary>The only paths a favourites PATCH may touch.</summary>
        public static readonly IReadOnlyList<string> FieldPaths =
            Kinds.Select(k => Favorites + "." + Wire(k).Array).ToList();

        public static string UpdateMaskQuery() =>
            "?" + string.Join("&", FieldPaths.Select(p => "updateMask.fieldPaths=" + Uri.EscapeDataString(p)));

        // ---- Reading ---------------------------------------------------------------------------------

        public static IReadOnlyDictionary<FavoriteKind, IReadOnlyList<SyncedFavorite>> FromDocument(JsonNode? document)
        {
            var fields = FirestoreValue.ReadDocument(document);
            var favorites = fields.TryGetValue(Favorites, out var map) ? FirestoreValue.ReadMap(map) : null;

            var result = new Dictionary<FavoriteKind, IReadOnlyList<SyncedFavorite>>();
            foreach (var kind in Kinds)
            {
                var (array, keyField, valueField) = Wire(kind);
                var values = favorites is not null && favorites.TryGetValue(array, out var list)
                    ? FirestoreValue.ReadArray(list)
                    : [];

                result[kind] = values
                    .Select(v => Read(v, keyField, valueField))
                    .OfType<SyncedFavorite>()
                    .ToList();
            }
            return result;
        }

        private static SyncedFavorite? Read(JsonNode? value, string keyField, string valueField)
        {
            var map = FirestoreValue.ReadMap(value);
            if (map.Count == 0) return null;

            return new SyncedFavorite(
                FirestoreValue.ReadString(Get(map, keyField)) ?? string.Empty,
                FirestoreValue.ReadString(Get(map, valueField)) ?? string.Empty,
                FirestoreValue.ReadInteger(Get(map, "updatedAt")) ?? 0,
                FirestoreValue.ReadBoolean(Get(map, "deleted")) ?? false)
            {
                Raw = value?.DeepClone(),
            };
        }

        private static JsonNode? Get(IReadOnlyDictionary<string, JsonNode?> map, string key) =>
            map.TryGetValue(key, out var value) ? value : null;

        // ---- Writing ---------------------------------------------------------------------------------

        /// <summary>The PATCH body: <c>favorites</c> with the three arrays, and nothing else.</summary>
        public static JsonObject ToDocument(IReadOnlyDictionary<FavoriteKind, IReadOnlyList<SyncedFavorite>> all)
        {
            var arrays = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            foreach (var kind in Kinds)
            {
                var (array, keyField, valueField) = Wire(kind);
                var items = all.TryGetValue(kind, out var list) ? list : [];
                arrays[array] = FirestoreValue.Array(items.Select(i => Value(i, keyField, valueField)));
            }

            return FirestoreValue.Document(new Dictionary<string, JsonNode>(StringComparer.Ordinal)
            {
                [Favorites] = FirestoreValue.Map(arrays),
            });
        }

        private static JsonNode Value(SyncedFavorite item, string keyField, string valueField)
        {
            var fields = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            foreach (var (key, value) in FirestoreValue.ReadMap(item.Raw))
            {
                if (value is not null) fields[key] = value.DeepClone();
            }

            fields[keyField] = FirestoreValue.String(item.Key);
            fields[valueField] = FirestoreValue.String(item.Value);
            fields["updatedAt"] = FirestoreValue.Integer(item.UpdatedAt);
            fields["deleted"] = FirestoreValue.Boolean(item.Deleted);

            return FirestoreValue.Map(fields);
        }

        // ---- Merging ---------------------------------------------------------------------------------

        /// <summary>
        /// Combine one tool's items from this device and the cloud.
        /// </summary>
        /// <remarks>
        /// <para>Matched by key. Per item the later change wins - a save or a removal - and on a tie this
        /// device's, which keeps the cloud's raw value under it so a Windows write sheds nothing Android put
        /// there. An item with no key cannot be matched, so the cloud's is carried as it is.</para>
        /// <para>Removals older than <see cref="TombstoneLifetime"/> are dropped; saved items are kept
        /// newest first up to <see cref="MaxPerKind"/>. Merging the result again changes nothing.</para>
        /// </remarks>
        public static IReadOnlyList<SyncedFavorite> Merge(
            IReadOnlyList<SyncedFavorite> local, IReadOnlyList<SyncedFavorite> cloud, long nowMillis)
        {
            var byKey = new Dictionary<string, SyncedFavorite>(StringComparer.Ordinal);
            var unmatched = new List<SyncedFavorite>();

            foreach (var theirs in cloud)
            {
                if (string.IsNullOrEmpty(theirs.Key)) unmatched.Add(theirs);
                else byKey[theirs.Key] = theirs;
            }

            foreach (var mine in local)
            {
                // A keyless item here can only be one the cloud handed over last time, and the cloud's copy
                // is already carried; taking both would double it on every sync.
                if (string.IsNullOrEmpty(mine.Key)) continue;

                byKey[mine.Key] = byKey.TryGetValue(mine.Key, out var theirs)
                    ? mine.UpdatedAt >= theirs.UpdatedAt ? mine with { Raw = mine.Raw ?? theirs.Raw } : theirs
                    : mine;
            }

            var oldest = nowMillis - (long)TombstoneLifetime.TotalMilliseconds;
            var all = byKey.Values.Concat(unmatched).ToList();

            var live = all.Where(i => !i.Deleted)
                .OrderByDescending(i => i.UpdatedAt)
                .ThenBy(i => i.Key, StringComparer.Ordinal)
                .Take(MaxPerKind);

            var removed = all.Where(i => i.Deleted && i.UpdatedAt >= oldest)
                .OrderByDescending(i => i.UpdatedAt)
                .ThenBy(i => i.Key, StringComparer.Ordinal);

            return live.Concat(removed).ToList();
        }

        /// <summary>Whether two lists hold different changes, compared on key, time and removal - not order.</summary>
        public static bool Differs(IReadOnlyList<SyncedFavorite> a, IReadOnlyList<SyncedFavorite> b) =>
            !a.Select(Signature).Order(StringComparer.Ordinal)
                .SequenceEqual(b.Select(Signature).Order(StringComparer.Ordinal), StringComparer.Ordinal);

        private static string Signature(SyncedFavorite f) =>
            string.Concat(f.Key, "\u001F", f.Value, "\u001F",
                f.UpdatedAt.ToString(CultureInfo.InvariantCulture), f.Deleted ? "\u001Fx" : string.Empty);
    }
}
