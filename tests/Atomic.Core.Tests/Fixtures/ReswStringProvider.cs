using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Atomic.Core.Localization;
using Atomic.Core.Text;

namespace Atomic.Core.Tests.Fixtures
{
    /// <summary>
    /// <see cref="IStringProvider"/> reading the shipped <c>.resw</c> files directly, so tests
    /// assert against the strings users actually see without needing an app host.
    ///
    /// <para>Mirrors <c>TestStrings.kt</c>, including the two behaviours that file documents as
    /// load-bearing.</para>
    /// </summary>
    public sealed class ReswStringProvider : IStringProvider
    {
        /// <summary>Returned for an unknown key when not falling back. Matches <c>TestStrings</c>.</summary>
        public const string MissingPrefix = "str:";

        private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Tables =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly IReadOnlyDictionary<string, string> _own;
        private readonly IReadOnlyDictionary<string, string> _fallback;

        /// <param name="language">Base language code, e.g. "sv".</param>
        /// <param name="fallBackToEnglish">
        /// Off by default, and the default is load-bearing in both directions:
        /// <list type="bullet">
        /// <item>Strict (false) returns the <c>str:key</c> sentinel, making a missing translation
        /// visible. A coverage test needs exactly that.</item>
        /// <item>Fallback (true) reproduces production, where a key absent from the Swedish
        /// resources is served from English and lands mid-sentence. Anything asserting *routing*
        /// in another language needs this, or every row fails on the sentinel before its real
        /// expectation is evaluated.</item>
        /// </list>
        /// </param>
        public ReswStringProvider(string language = "en", bool fallBackToEnglish = false)
        {
            Language = language;
            _own = Load(language);
            _fallback = fallBackToEnglish && !IsEnglish(language)
                ? Load("en")
                : new Dictionary<string, string>();
        }

        public string Language { get; }

        /// <summary>True when this language has its own value, rather than borrowing English.</summary>
        public bool HasOwn(string key) => _own.ContainsKey(key);

        public string Get(string key, params object?[] args)
        {
            if (!_own.TryGetValue(key, out var template) && !_fallback.TryGetValue(key, out template))
                return MissingPrefix + key;

            return args is { Length: > 0 } ? JavaFormat.Format(template, args) : template;
        }

        public IReadOnlyList<string> Array(string key)
        {
            var items = new List<string>();
            for (var i = 0; ; i++)
            {
                var indexed = $"{key}.{i}";
                if (!_own.TryGetValue(indexed, out var value) &&
                    !_fallback.TryGetValue(indexed, out value))
                    break;
                items.Add(value);
            }
            return items;
        }

        /// <summary>Language codes with a resource file present.</summary>
        public static IReadOnlyList<string> AvailableLanguages() =>
            !Directory.Exists(StringsRoot)
                ? System.Array.Empty<string>()
                : Directory.EnumerateDirectories(StringsRoot)
                    .Where(d => File.Exists(Path.Combine(d, "Resources.resw")))
                    .Select(d => Path.GetFileName(d)!)
                    .OrderBy(d => d, StringComparer.Ordinal)
                    .ToList();

        public static string StringsRoot => Path.Combine(AppContext.BaseDirectory, "strings");

        private static bool IsEnglish(string language) =>
            language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Both resource files for one language, merged.
        ///
        /// <para>App.resw is read last so it wins, matching <c>ResourceStringProvider</c>, which
        /// searches the App subtree before Resources precisely so Windows can override an Android
        /// string. A test reading only Resources.resw would report every Windows-only key as
        /// missing.</para>
        /// </summary>
        private static IReadOnlyDictionary<string, string> Load(string language) =>
            Tables.GetOrAdd(language, lang =>
            {
                var folder = ResolveFolder(lang);
                var table = new Dictionary<string, string>(StringComparer.Ordinal);
                if (folder is null)
                    return table;

                foreach (var file in new[] { "Resources.resw", "App.resw" })
                {
                    var path = Path.Combine(folder, file);
                    if (!File.Exists(path))
                        continue;

                    foreach (var data in XDocument.Load(path).Root!.Elements("data"))
                    {
                        if (data.Attribute("name") is { } name)
                            table[name.Value] = data.Element("value")?.Value ?? string.Empty;
                    }
                }
                return table;
            });

        /// <summary>
        /// Resolves a base code onto a resource folder. The folders are BCP-47, so several are
        /// region-qualified ("pt-BR", "zh-Hans") and cannot be derived from the base code alone.
        /// </summary>
        private static string? ResolveFolder(string language)
        {
            if (!Directory.Exists(StringsRoot))
                return null;

            var candidates = new[]
            {
                language,
                language switch
                {
                    "en" => "en-US",
                    "pt" => "pt-BR",
                    "zh" => "zh-Hans",
                    _ => language,
                },
            };

            foreach (var candidate in candidates)
            {
                var folder = Path.Combine(StringsRoot, candidate);
                if (File.Exists(Path.Combine(folder, "Resources.resw")))
                    return folder;
            }

            // Last resort: any folder whose language part matches, e.g. "sv" -> "sv-SE".
            var prefix = language + "-";
            return Directory.EnumerateDirectories(StringsRoot)
                .FirstOrDefault(d => Path.GetFileName(d)!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }
}
