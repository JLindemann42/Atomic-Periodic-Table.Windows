using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// Reads element tables from <c>elements_{lang}.json</c>.
    ///
    /// <para>Replaces <c>ai/data/AssetElementSource.kt</c>. That class took an Android
    /// <c>AssetManager</c>; this one takes a delegate that opens a named asset, so
    /// <c>Atomic.Core</c> keeps no dependency on how the host stores its files. The app passes an
    /// opener backed by the package; tests pass one backed by the filesystem.</para>
    ///
    /// <para>Parsed tables are cached per language. The file is roughly 1 MB and the parse is not
    /// cheap, and both the UI and the engine ask for the same language - Android caches for the
    /// same reason in <c>ElementDataLoader</c>. <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// because the engine reads off the UI thread while the UI reads on it.</para>
    /// </summary>
    public sealed class JsonElementSource : IElementSource
    {
        private readonly Func<string, Stream?> _open;
        private readonly Func<string, Stream?>? _openFile;
        private readonly IReadOnlyList<string> _languages;
        private readonly ConcurrentDictionary<string, ElementTable?> _cache = new(StringComparer.Ordinal);

        /// <param name="open">
        /// Opens the asset for a language code and returns a readable stream, or null when the
        /// language has no file. The caller owns nothing; this class disposes the stream.
        /// </param>
        /// <param name="languages">Language codes to advertise from <see cref="AvailableLanguages"/>.</param>
        /// <param name="openFile">
        /// Opens another shipped file by name, such as <c>titles_sv.tsv</c>, or returns null when
        /// there is none; disposed here too. Without it <see cref="Text"/> has nothing to read.
        /// </param>
        public JsonElementSource(Func<string, Stream?> open, IReadOnlyList<string> languages,
            Func<string, Stream?>? openFile = null)
        {
            _open = open ?? throw new ArgumentNullException(nameof(open));
            _languages = languages ?? throw new ArgumentNullException(nameof(languages));
            _openFile = openFile;
        }

        public IReadOnlyList<string> AvailableLanguages() => _languages;

        public ElementTable? Load(string language) => _cache.GetOrAdd(language, Parse);

        public string? Text(string name)
        {
            using var stream = _openFile?.Invoke(name);
            if (stream is null) return null;
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private ElementTable? Parse(string language)
        {
            using var stream = _open(language);
            if (stream is null)
                return null;

            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var rows = new Dictionary<string, ElementRow>(StringComparer.Ordinal);
            foreach (var element in doc.RootElement.EnumerateObject())
            {
                if (element.Value.ValueKind == JsonValueKind.Object)
                    rows[element.Name] = ToRow(element.Value);
            }
            return new ElementTable(rows);
        }

        /// <summary>
        /// Mirrors <c>AssetElementSource.toRow</c>. The type mapping is load-bearing: downstream
        /// parsers branch on whether a value is an <see cref="int"/> or a <see cref="string"/>,
        /// so integral numbers must stay integers and everything else must become text.
        /// </summary>
        private static ElementRow ToRow(JsonElement obj)
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var field in obj.EnumerateObject())
            {
                row[field.Name] = field.Value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    JsonValueKind.Object => ToStringMap(field.Value),
                    // Only the NFPA triple is authored as a bare number; keep those as int so the
                    // hazard code can use them directly. Anything else numeric becomes text,
                    // because the values carry units and ranges the parsers handle themselves.
                    JsonValueKind.Number when field.Value.TryGetInt32(out var i) => i,
                    JsonValueKind.Number => field.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Array => field.Value.GetRawText(),
                    _ => field.Value.GetString(),
                };
            }
            return row;
        }

        private static IReadOnlyDictionary<string, string> ToStringMap(JsonElement obj)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in obj.EnumerateObject())
            {
                var value = field.Value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    JsonValueKind.String => field.Value.GetString(),
                    _ => field.Value.GetRawText(),
                };
                if (value is not null)
                    map[field.Name] = value;
            }
            return map;
        }

        /// <summary>
        /// Discovers language codes from filenames matching <c>elements_{lang}.json</c>.
        /// Equivalent to <c>ElementDataLoader.getAvailableLanguages</c>, which scans the asset
        /// directory rather than hardcoding the list.
        /// </summary>
        public static IReadOnlyList<string> LanguagesFrom(IEnumerable<string> fileNames) =>
            fileNames
                .Select(Path.GetFileName)
                .Where(n => n is not null
                            && n.StartsWith("elements_", StringComparison.Ordinal)
                            && n.EndsWith(".json", StringComparison.Ordinal))
                .Select(n => n!["elements_".Length..^".json".Length])
                .Where(code => code.Length > 0)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToList();
    }
}
