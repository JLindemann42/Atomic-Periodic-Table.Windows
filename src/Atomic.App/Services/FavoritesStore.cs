using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Atomic.App.Tools;
using Windows.Storage;

namespace Atomic.App.Services
{
    /// <summary>
    /// Reads and writes the tools' saved favourites.
    ///
    /// <para>One JSON file per tool in the local folder, matching the convention the calculator and
    /// unit converter already use. Android stores these as a SharedPreferences StringSet with
    /// fields separated by U+001F; that encoding exists because a StringSet cannot hold structure,
    /// and there is no reason to reproduce it here - see docs/porting-notes.md D7.</para>
    ///
    /// <para>All serialization goes through <see cref="ElementJsonContext"/>. A reflection-based
    /// overload returns an empty list under trimming and then overwrites the file with it, which
    /// silently destroys the one thing in the app the user actually created.</para>
    /// </summary>
    public static class FavoritesStore
    {
        private const string EquationsFile = "equation_favorites.json";
        private const string IdealGasFile = "ideal_gas_favorites.json";

        public static Task<List<SavedEquation>> LoadEquationsAsync() =>
            LoadAsync(EquationsFile, ElementJsonContext.Default.ListSavedEquation);

        public static Task SaveEquationsAsync(IEnumerable<SavedEquation> items) =>
            SaveAsync(EquationsFile, new List<SavedEquation>(items), ElementJsonContext.Default.ListSavedEquation);

        public static Task<List<SavedIdealGas>> LoadIdealGasAsync() =>
            LoadAsync(IdealGasFile, ElementJsonContext.Default.ListSavedIdealGas);

        public static Task SaveIdealGasAsync(IEnumerable<SavedIdealGas> items) =>
            SaveAsync(IdealGasFile, new List<SavedIdealGas>(items), ElementJsonContext.Default.ListSavedIdealGas);

        private static async Task<List<T>> LoadAsync<T>(
            string fileName, System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> typeInfo)
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

        private static async Task SaveAsync<T>(
            string fileName, List<T> items,
            System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> typeInfo)
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
