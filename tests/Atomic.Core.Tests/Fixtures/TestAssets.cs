using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Atomic.Core.Ai.Data;

namespace Atomic.Core.Tests.Fixtures
{
    /// <summary>
    /// The real shipped element tables, loaded from the copy the csproj links out of
    /// <c>shared/elements</c>, the shared-data submodule the Android app reads too. Mirrors
    /// <c>TestAssets.kt</c>.
    ///
    /// <para>Tests run against the actual data rather than a fixture on purpose: most of what can
    /// go wrong here is a mismatch between the parser and how the data is really authored, and a
    /// hand-written fixture would encode the assumption instead of testing it.</para>
    /// </summary>
    public static class TestAssets
    {
        public static readonly string AssetDirectory =
            Path.Combine(AppContext.BaseDirectory, "assets");

        private static readonly Lazy<JsonElementSource> Source = new(() =>
        {
            var languages = JsonElementSource.LanguagesFrom(
                Directory.Exists(AssetDirectory)
                    ? Directory.EnumerateFiles(AssetDirectory, "elements_*.json")
                    : Enumerable.Empty<string>());

            return new JsonElementSource(OpenLanguage, languages, OpenFile);
        });

        public static JsonElementSource ElementSource => Source.Value;

        public static IReadOnlyList<string> Languages => ElementSource.AvailableLanguages();

        public static ElementTable English =>
            ElementSource.Load("en") ?? throw new InvalidOperationException(
                $"elements_en.json not found under {AssetDirectory}. " +
                "It is linked from shared/elements by the test csproj; " +
                "if that directory is empty, run `git submodule update --init`.");

        private static Stream? OpenLanguage(string language)
        {
            var path = Path.Combine(AssetDirectory, $"elements_{language}.json");
            return File.Exists(path) ? File.OpenRead(path) : null;
        }

        /// <summary>The other shared files beside the tables, such as <c>titles_sv.tsv</c>.</summary>
        private static Stream? OpenFile(string name)
        {
            var path = Path.Combine(AssetDirectory, Path.GetFileName(name));
            return File.Exists(path) ? File.OpenRead(path) : null;
        }
    }
}
