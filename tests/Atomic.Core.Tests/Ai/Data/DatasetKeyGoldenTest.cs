using System;
using System.IO;
using System.Linq;
using Atomic.Core.Ai.Data;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// Every dataset row's canonical key, against <c>shared/golden/dataset_keys.tsv</c> (roadmap 0.6).
    ///
    /// <para>The file is generated from Android's Kotlin <c>DatasetIndex</c>, which is the source. A
    /// corpus row's <c>expect_row</c> names one of these keys, so a key that differs here means the
    /// same corpus row asserts a different row on each platform.</para>
    /// </summary>
    public class DatasetKeyGoldenTest
    {
        private static readonly DatasetIndex Index = DatasetIndex.Build();

        private static string GoldenPath => Path.Combine(AppContext.BaseDirectory, "golden", "dataset_keys.tsv");

        [Fact]
        public void KeysMatchTheSharedGoldenFile()
        {
            Assert.True(File.Exists(GoldenPath), $"{GoldenPath} missing - is the shared submodule checked out?");

            var expected = File.ReadAllLines(GoldenPath)
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Skip(1) // header
                .Select(l => l.Split('\t'))
                .Select(c => $"{c[0]}\t{c[1]}\t{c[2]}")
                .ToList();

            Assert.True(expected.Count > 500, "golden file is suspiciously short");

            var actual = Index.Rows
                .Select(r => $"{r.Key}\t{r.Dataset}\t{r.Title.Replace('\t', ' ').Replace('\n', ' ')}")
                .ToList();

            var missing = expected.Except(actual).ToList();
            var extra = actual.Except(expected).ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                "dataset keys differ from Android's.\n  only in golden: " + string.Join("\n    ", missing) +
                "\n  only here: " + string.Join("\n    ", extra));
            Assert.Equal(expected, actual); // and in the same order, since repeat numbers depend on it
        }

        [Fact]
        public void EveryKeyIsUnique() =>
            Assert.Equal(Index.Rows.Count, Index.Rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());

        [Theory]
        [InlineData("Planck's constant", "planck-s-constant")]
        [InlineData("H₂O", "h2o")]
        [InlineData("Café au lait", "cafe-au-lait")]
        [InlineData("—", "row")]
        public void SlugsFoldToLowercaseAsciiWords(string text, string slug) =>
            Assert.Equal(slug, DatasetIndex.Slug(text));
    }
}
