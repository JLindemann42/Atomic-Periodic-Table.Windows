using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// Loading and shape of the merged element tables. These are the first tests over the ported
    /// data layer, and they double as a check on the P1 data migration itself.
    /// </summary>
    public class JsonElementSourceTest
    {
        [Fact]
        public void ShipsTwelveLanguages()
        {
            Assert.Equal(
                new[] { "af", "de", "en", "es", "fil", "fr", "hi", "it", "pt", "sv", "ur", "zh" },
                TestAssets.Languages);
        }

        [Theory]
        [InlineData("af")]
        [InlineData("de")]
        [InlineData("en")]
        [InlineData("es")]
        [InlineData("fil")]
        [InlineData("fr")]
        [InlineData("hi")]
        [InlineData("it")]
        [InlineData("pt")]
        [InlineData("sv")]
        [InlineData("ur")]
        [InlineData("zh")]
        public void EveryLanguageHasAll118Elements(string language)
        {
            var table = TestAssets.ElementSource.Load(language);
            Assert.NotNull(table);
            Assert.Equal(118, table!.Count);
        }

        [Fact]
        public void UnknownLanguageLoadsAsNull()
        {
            Assert.Null(TestAssets.ElementSource.Load("xx"));
        }

        [Fact]
        public void CaesiumIsKeyedTheBritishWay()
        {
            // The Windows app filed this element as cesium.json; the merged data follows Android.
            // A key-based join that still expects "cesium" breaks on exactly this one element.
            Assert.NotNull(TestAssets.English["caesium"]);
            Assert.Null(TestAssets.English["cesium"]);
        }

        [Fact]
        public void NfpaFieldsStayIntegers()
        {
            // The three hazard fields are the only ones authored as bare numbers, and the hazard
            // UI indexes straight into them. If the parser stringified numbers wholesale these
            // would silently arrive as "1" and every comparison would fail.
            var oxygen = TestAssets.English["oxygen"];
            Assert.NotNull(oxygen);
            Assert.IsType<int>(oxygen!["health"]);
            Assert.IsType<int>(oxygen["flammability"]);
            Assert.IsType<int>(oxygen["instability"]);
        }

        [Fact]
        public void NestedFieldsBecomeStringMaps()
        {
            var iron = TestAssets.English["iron"]!;

            var lattice = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
                iron["lattice_constants"]);
            Assert.True(lattice.ContainsKey("a"));

            var debye = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
                iron["debye_temperature"]);
            Assert.True(debye.ContainsKey("room_temperature"));
        }

        [Fact]
        public void EverythingElseIsAStringOrNull()
        {
            var strays = new List<string>();
            foreach (var (key, row) in TestAssets.English.Rows)
            {
                foreach (var (field, value) in row)
                {
                    if (value is null or string or int) continue;
                    if (value is IReadOnlyDictionary<string, string>) continue;
                    strays.Add($"{key}.{field} = {value.GetType().Name}");
                }
            }
            Assert.Empty(strays);
        }

        [Fact]
        public void TablesAreCachedNotReparsed()
        {
            Assert.Same(TestAssets.ElementSource.Load("en"), TestAssets.ElementSource.Load("en"));
        }

        [Fact]
        public void LanguagesFromIgnoresUnrelatedFiles()
        {
            var codes = JsonElementSource.LanguagesFrom(new[]
            {
                "/x/elements_en.json", "/x/elements_zh.json",
                "/x/readme.md", "/x/elements_.json", "/x/other_en.json", "/x/elements_de.txt",
            });
            Assert.Equal(new[] { "en", "zh" }, codes);
        }
    }
}
