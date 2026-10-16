using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Atomic.Core.Ai.Data;

namespace Atomic.App.Services
{
    /// <summary>
    /// The app's single door to element data.
    ///
    /// <para>Replaces reading <c>Elements/&lt;name&gt;.json</c> from disk at each call site. There
    /// were eight of those, none cached: opening one element page re-read and re-parsed a file,
    /// switching the table's display mode re-read all 118, and the nuclide chart read all 118
    /// asynchronously to draw a single picture. Data now lives in twelve
    /// <c>Assets/Elements/elements_{lang}.json</c> files, parsed once per language and held.</para>
    ///
    /// <para>Two shapes are served from the same parse:
    /// <see cref="Get"/> materialises the <see cref="Element"/> POCO the pages bind to, and
    /// <see cref="Raw"/> hands back the underlying <see cref="JsonElement"/> for the few places
    /// that look up a field by name at runtime.</para>
    /// </summary>
    public static class ElementRepository
    {
        private const string DefaultLanguage = "en";

        /// <summary>
        /// Language used when a call does not name one. Phase 2 points this at the app's language
        /// setting; until then everything is English.
        /// </summary>
        public static string CurrentLanguage { get; set; } = DefaultLanguage;

        private static readonly ConcurrentDictionary<string, JsonDocument?> Documents = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, Element?> ElementCache = new(StringComparer.Ordinal);

        /// <summary>
        /// The engine's view of the same files. Wired up when the Ask Atomic port lands; exposed
        /// here so there is one place that knows where the element assets live.
        /// </summary>
        public static IElementSource Source { get; } = new JsonElementSource(
            OpenLanguageAsset,
            new[] { "af", "de", "en", "es", "fil", "fr", "hi", "it", "pt", "sv", "ur", "zh" });

        // -- lookup ----------------------------------------------------------------------------

        /// <summary>
        /// One element by its lowercase English name, which is the key the JSON is organised by
        /// (for example "iron", "caesium"). Returns null when the element is unknown.
        /// </summary>
        public static Element? Get(string key, string? language = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            var lang = language ?? CurrentLanguage;
            var normalised = Normalise(key);
            var cacheKey = lang + "/" + normalised;

            return ElementCache.GetOrAdd(cacheKey, _ =>
            {
                var raw = Raw(normalised, lang);
                if (raw is null)
                    return null;

                var element = JsonSerializer.Deserialize(raw.Value.GetRawText(), ElementJsonContext.Default.Element);
                if (element is not null)
                {
                    // OriginalName is not a JSON field - it is the map key, capitalised. Pages use
                    // it to look the element back up, so it must round-trip through Normalise.
                    element.OriginalName = Capitalise(normalised);
                }
                return element;
            });
        }

        /// <summary>
        /// Every element key (lowercase English name) for a language, in file order. For callers
        /// that want to sweep the whole table without materialising 118 <see cref="Element"/>
        /// objects.
        /// </summary>
        public static IReadOnlyList<string> Keys(string? language = null)
        {
            var doc = Document(language ?? CurrentLanguage);
            return doc is null
                ? Array.Empty<string>()
                : doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        }

        /// <summary>Every element for a language, ordered by atomic number.</summary>
        public static IReadOnlyList<Element> All(string? language = null)
        {
            var lang = language ?? CurrentLanguage;
            var doc = Document(lang);
            if (doc is null)
                return Array.Empty<Element>();

            return doc.RootElement.EnumerateObject()
                .Select(p => Get(p.Name, lang))
                .Where(e => e is not null)
                .Select(e => e!)
                .OrderBy(e => e.AtomicNumber)
                .ToList();
        }

        /// <summary>
        /// The raw JSON object for an element, for callers that read a field chosen at runtime.
        /// The returned <see cref="JsonElement"/> stays valid for the life of the app - the
        /// backing document is cached deliberately and never disposed.
        /// </summary>
        public static JsonElement? Raw(string key, string? language = null)
        {
            var doc = Document(language ?? CurrentLanguage);
            if (doc is null)
                return null;

            return doc.RootElement.TryGetProperty(Normalise(key), out var element)
                ? element
                : null;
        }

        // -- keys ------------------------------------------------------------------------------

        /// <summary>
        /// Maps a name onto the JSON key: lowercase, plus the spellings that differ from the key.
        ///
        /// <para>"cesium" is the one that matters - Windows filed that element as cesium.json and
        /// the merged data follows Android's "caesium", so without this every lookup of it returns
        /// null. The other two cost nothing and let the search box accept either spelling; the
        /// keys are already "aluminium" and "sulfur".</para>
        /// </summary>
        public static string Normalise(string key) =>
            key.Trim().ToLowerInvariant() switch
            {
                "cesium" => "caesium",
                "aluminum" => "aluminium",
                "sulphur" => "sulfur",
                var other => other,
            };

        private static string Capitalise(string key) =>
            key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..];

        // -- loading ---------------------------------------------------------------------------

        private static JsonDocument? Document(string language) =>
            Documents.GetOrAdd(language, lang =>
            {
                using var stream = OpenLanguageAsset(lang)
                                   ?? (lang == DefaultLanguage ? null : OpenLanguageAsset(DefaultLanguage));
                return stream is null ? null : JsonDocument.Parse(stream);
            });

        private static Stream? OpenLanguageAsset(string language)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Elements", $"elements_{language}.json");
            return File.Exists(path) ? File.OpenRead(path) : null;
        }
    }
}
