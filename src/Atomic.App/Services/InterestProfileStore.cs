using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using Atomic.Core.Ai;
using Windows.Storage;

namespace Atomic.App.Services
{
    /// <summary>
    /// Persists which elements the user keeps asking about, so the assistant still recognises them
    /// after a restart.
    ///
    /// <para>Counts only, never question text - see <see cref="InterestProfile"/>. Kept in its own
    /// small file rather than in the chat history, because clearing a conversation should not make
    /// the assistant forget who it is talking to, and because the history is capped at twenty turns
    /// while the counts are not.</para>
    /// </summary>
    public static class InterestProfileStore
    {
        private const string FileName = "ai_interests.json";

        /// <summary>Restore the profile in place. Silent on a missing or unreadable file.</summary>
        public static async Task LoadAsync(InterestProfile profile)
        {
            try
            {
                if (await ApplicationData.Current.LocalFolder.TryGetItemAsync(FileName) is not StorageFile file)
                    return;

                using var stream = await file.OpenStreamForReadAsync();
                var counts = await JsonSerializer.DeserializeAsync(
                    stream, ElementJsonContext.Default.DictionaryStringInt32);

                profile.Load(counts);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // A profile is a convenience. Losing it costs a personalized greeting, so it is not
                // worth failing a launch over.
            }
        }

        /// <summary>Save the profile.</summary>
        public static async Task SaveAsync(InterestProfile profile)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    FileName, CreationCollisionOption.ReplaceExisting);

                using var stream = await file.OpenStreamForWriteAsync();
                stream.SetLength(0);

                await JsonSerializer.SerializeAsync(
                    stream,
                    new Dictionary<string, int>(profile.Elements, StringComparer.Ordinal),
                    ElementJsonContext.Default.DictionaryStringInt32);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}