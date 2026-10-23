using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Atomic.Core.Sync;
using Xunit;

namespace Atomic.Core.Tests.Sync
{
    /// <summary>
    /// The tools' saved items: <c>users/{uid}.favorites</c>.
    ///
    /// <para>The promises: a removal on one device stays removed on the other, saving again beats an
    /// older removal, and a write sheds nothing it does not know - neither a field on an item nor a
    /// sibling list under <c>favorites</c>.</para>
    /// </summary>
    public class FavoritesDocumentTest
    {
        private const long Now = 1_760_000_000_000;
        private const long Day = 86_400_000;

        /// <summary>One of each, plus an unknown field on an item and an unknown list beside them.</summary>
        private const string CloudDocument = """
            {"fields":{
              "xp":{"integerValue":"500"},
              "favorites":{"mapValue":{"fields":{
                "compounds":{"arrayValue":{"values":[
                  {"mapValue":{"fields":{
                    "formula":{"stringValue":"H2O"},
                    "molarMass":{"stringValue":"18.015"},
                    "updatedAt":{"integerValue":"1759000000000"},
                    "deleted":{"booleanValue":false},
                    "note":{"stringValue":"from the phone"}}}}
                ]}},
                "equations":{"arrayValue":{"values":[
                  {"mapValue":{"fields":{
                    "input":{"stringValue":"H2 + O2 = H2O"},
                    "balanced":{"stringValue":"2H2 + O2 → 2H2O"},
                    "updatedAt":{"integerValue":"1759000000000"}}}}
                ]}},
                "idealGas":{"arrayValue":{"values":[
                  {"mapValue":{"fields":{
                    "summary":{"stringValue":"P = 1 atm, V = 22.4 L, T = 273 K"},
                    "result":{"stringValue":"n = 1.00 mol"},
                    "updatedAt":{"integerValue":"1759000000000"},
                    "deleted":{"booleanValue":true}}}}
                ]}},
                "units":{"arrayValue":{"values":[{"stringValue":"km"}]}}
              }}}}}
            """;

        private static IReadOnlyDictionary<FavoriteKind, IReadOnlyList<SyncedFavorite>> Cloud() =>
            FavoritesDocument.FromDocument(JsonNode.Parse(CloudDocument));

        private static SyncedFavorite Item(string key, long at, bool deleted = false) => new(key, "v", at, deleted);

        [Fact]
        public void ReadsEachToolsItems()
        {
            var cloud = Cloud();

            Assert.Equal(new SyncedFavorite("H2O", "18.015", 1759000000000), cloud[FavoriteKind.Compound].Single() with { Raw = null });
            Assert.Equal("2H2 + O2 → 2H2O", cloud[FavoriteKind.Equation].Single().Value);
            Assert.False(cloud[FavoriteKind.Equation].Single().Deleted);
            Assert.True(cloud[FavoriteKind.IdealGas].Single().Deleted);
        }

        [Fact]
        public void AMissingDocumentReadsAsEmptyLists()
        {
            var empty = FavoritesDocument.FromDocument(JsonNode.Parse("""{"fields":{}}"""));

            Assert.All(empty.Values, Assert.Empty);
            Assert.Equal(3, empty.Count);
        }

        [Fact]
        public void AWriteKeepsFieldsItDoesNotKnow()
        {
            var body = FavoritesDocument.ToDocument(Cloud());
            var compound = FavoritesDocument.FromDocument(body)[FavoriteKind.Compound].Single();

            Assert.Equal("from the phone", FirestoreValue.ReadString(FirestoreValue.ReadMap(compound.Raw)["note"]));
        }

        /// <summary>The write names its three lists, so "units" - and "xp" - are left alone by Firestore.</summary>
        [Fact]
        public void TheMaskNamesOnlyTheThreeLists()
        {
            Assert.Equal(
                "?updateMask.fieldPaths=favorites.compounds&updateMask.fieldPaths=favorites.equations&updateMask.fieldPaths=favorites.idealGas",
                FavoritesDocument.UpdateMaskQuery());

            var favorites = FirestoreValue.ReadMap(FavoritesDocument.ToDocument(Cloud())["fields"]!["favorites"]);
            Assert.Equal(["compounds", "equations", "idealGas"], favorites.Keys.Order(StringComparer.Ordinal));
        }

        [Fact]
        public void ARemovalBeatsAnOlderSave()
        {
            var merged = FavoritesDocument.Merge([Item("H2O", Now, deleted: true)], [Item("H2O", Now - Day)], Now);

            Assert.True(merged.Single().Deleted);
        }

        /// <summary>The reason removals are kept: without the record, the other side's copy comes back.</summary>
        [Fact]
        public void ARemovalIsNotUndoneByTheOtherDevice()
        {
            var merged = FavoritesDocument.Merge([Item("H2O", Now - Day)], [Item("H2O", Now, deleted: true)], Now);

            Assert.True(merged.Single().Deleted);
        }

        [Fact]
        public void SavingAgainBeatsAnOlderRemoval()
        {
            var merged = FavoritesDocument.Merge([Item("H2O", Now)], [Item("H2O", Now - Day, deleted: true)], Now);

            Assert.False(merged.Single().Deleted);
        }

        [Fact]
        public void BothSidesItemsAreKept()
        {
            var merged = FavoritesDocument.Merge([Item("NaCl", Now)], [Item("H2O", Now - Day)], Now);

            Assert.Equal(["NaCl", "H2O"], merged.Select(m => m.Key));
        }

        /// <summary>On a tie this device wins, and still carries what Android stored on the item.</summary>
        [Fact]
        public void ATieKeepsTheCloudsUnknownFields()
        {
            var theirs = Cloud()[FavoriteKind.Compound].Single();
            var merged = FavoritesDocument.Merge([new SyncedFavorite("H2O", "18.02", theirs.UpdatedAt)], [theirs], Now).Single();

            Assert.Equal("18.02", merged.Value);
            Assert.Same(theirs.Raw, merged.Raw);
        }

        [Fact]
        public void OldRemovalsExpire()
        {
            var old = Now - (long)FavoritesDocument.TombstoneLifetime.TotalMilliseconds - 1;
            var merged = FavoritesDocument.Merge([], [Item("H2O", old, deleted: true), Item("NaCl", Now - Day, deleted: true)], Now);

            Assert.Equal(["NaCl"], merged.Select(m => m.Key));
        }

        [Fact]
        public void SavedItemsAreCappedNewestFirst()
        {
            var many = Enumerable.Range(0, FavoritesDocument.MaxPerKind + 5).Select(i => Item("k" + i, Now - i)).ToList();
            var merged = FavoritesDocument.Merge(many, [], Now);

            Assert.Equal(FavoritesDocument.MaxPerKind, merged.Count);
            Assert.Equal("k0", merged[0].Key);
            Assert.DoesNotContain(merged, m => m.Key == "k" + FavoritesDocument.MaxPerKind);
        }

        /// <summary>A keyless item the cloud holds is carried, once - not doubled by every sync after.</summary>
        [Fact]
        public void MergingAgainChangesNothing()
        {
            IReadOnlyList<SyncedFavorite> cloud = [Item("H2O", Now - Day), Item("", Now - Day), Item("NaCl", Now, deleted: true)];
            var once = FavoritesDocument.Merge([Item("CO2", Now)], cloud, Now);
            var twice = FavoritesDocument.Merge(once, once, Now);

            Assert.Equal(4, once.Count);
            Assert.False(FavoritesDocument.Differs(once, twice));
        }

        [Fact]
        public void DiffersIgnoresOrderButNotRemoval()
        {
            Assert.False(FavoritesDocument.Differs([Item("a", 1), Item("b", 2)], [Item("b", 2), Item("a", 1)]));
            Assert.True(FavoritesDocument.Differs([Item("a", 1)], [Item("a", 1, deleted: true)]));
        }
    }
}
