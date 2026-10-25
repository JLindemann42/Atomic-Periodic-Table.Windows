using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Atomic.Core.Ai.Data;
using Atomic.Core.Models;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    public class DatasetIndexTest
    {
        private static readonly DatasetIndex Index = DatasetIndex.Build();

        [Fact]
        public void EveryDatasetIsReachable()
        {
            // The point of the index is that all eleven datasets become queryable. A dataset that
            // builds to zero rows is silently unreachable to the agent - no error, just questions
            // that can never be answered.
            var empty = DatasetIndex.Datasets.All.Where(d => Index.InDataset(d).Count == 0).ToList();
            Assert.Empty(empty);
        }

        [Theory]
        [InlineData(DatasetIndex.Datasets.Dictionary, 150)]
        [InlineData(DatasetIndex.Datasets.Equation, 77)]
        [InlineData(DatasetIndex.Datasets.Constant, 58)]
        [InlineData(DatasetIndex.Datasets.Poisson, 52)]
        [InlineData(DatasetIndex.Datasets.Geology, 25)]
        [InlineData(DatasetIndex.Datasets.Ion, 118)]
        [InlineData(DatasetIndex.Datasets.Indicator, 4)]
        [InlineData(DatasetIndex.Datasets.Electrode, 33)]
        [InlineData(DatasetIndex.Datasets.Solubility, 13)]
        [InlineData(DatasetIndex.Datasets.Alloy, 15)]
        [InlineData(DatasetIndex.Datasets.Unit, 65)]
        public void RowCountsMatchTheSourceTables(string dataset, int expected) =>
            Assert.Equal(expected, Index.InDataset(dataset).Count);

        /// <summary>
        /// Row() returns the first match, so a duplicate id makes the second row uncitable. This
        /// is the check that caught Android's doubled "Elementary charge" and the two Basalt rows.
        /// </summary>
        [Fact]
        public void IdsAreUniqueWithinEachDataset()
        {
            var collisions = new List<string>();
            foreach (var dataset in DatasetIndex.Datasets.All)
            {
                var ids = Index.InDataset(dataset).Select(r => r.Id).ToList();
                collisions.AddRange(ids
                    .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"{dataset}/{g.Key}"));
            }
            Assert.Empty(collisions);
        }

        [Fact]
        public void BothBasaltRowsSurviveAndAreDistinguishable()
        {
            var basalt = Index.InDataset(DatasetIndex.Datasets.Poisson)
                .Where(r => r.Title == "Basalt").ToList();

            Assert.Equal(2, basalt.Count);
            Assert.NotEqual(basalt[0].Id, basalt[1].Id);
            Assert.NotNull(Index.Row(DatasetIndex.Datasets.Poisson, basalt[1].Id));
        }

        [Fact]
        public void EveryRowHasSomethingToSearchAndSomethingToShow()
        {
            foreach (var row in Index.Rows)
            {
                Assert.False(string.IsNullOrWhiteSpace(row.Title), $"{row.Dataset}/{row.Id} title");
                Assert.False(string.IsNullOrWhiteSpace(row.Body), $"{row.Dataset}/{row.Id} body");
                Assert.False(string.IsNullOrWhiteSpace(row.Detail), $"{row.Dataset}/{row.Id} detail");
            }
        }

        [Fact]
        public void LookupIsCaseInsensitive()
        {
            Assert.NotNull(Index.Row(DatasetIndex.Datasets.Dictionary, "density"));
            Assert.NotNull(Index.Row(DatasetIndex.Datasets.Dictionary, "DENSITY"));
            Assert.Null(Index.Row(DatasetIndex.Datasets.Dictionary, "not a term"));
            Assert.Null(Index.Row("no such dataset", "density"));
        }

        [Fact]
        public void ConstantsThatAreNumbersParseAndTheRestDoNot()
        {
            var pi = Index.Row(DatasetIndex.Datasets.Constant, "Pi (π)");
            Assert.NotNull(pi);
            Assert.NotNull(pi!.NumericValue);
            Assert.Equal(Math.PI, pi.NumericValue!.Value, 1e-12);

            // "-" is the placeholder for "this constant is dimensionless", not a unit.
            Assert.Null(pi.Unit);
        }

        [Fact]
        public void ElectrodePotentialsCarryTheirSignAndUnit()
        {
            var lithium = Index.Row(DatasetIndex.Datasets.Electrode, "Li");
            Assert.NotNull(lithium);
            Assert.Equal(-3.04, lithium!.NumericValue!.Value, 1e-9);
            Assert.Equal("V", lithium.Unit);

            var fluorine = Index.Row(DatasetIndex.Datasets.Electrode, "F");
            Assert.True(fluorine!.NumericValue > 0, "fluorine is the positive end of the series");
        }

        /// <summary>
        /// The series stores element keys, not names, so that the title can be localized. Passing
        /// no resolver must still produce a readable English title rather than "lithium".
        /// </summary>
        [Fact]
        public void SeriesTitlesUseTheResolverWhenGivenOneAndCapitaliseOtherwise()
        {
            Assert.Equal("Lithium", Index.Row(DatasetIndex.Datasets.Electrode, "Li")!.Title);

            var swedish = DatasetIndex.Build(key => key == "lithium" ? "Litium" : null);
            Assert.Equal("Litium", swedish.Row(DatasetIndex.Datasets.Electrode, "Li")!.Title);

            // A resolver that does not know a key must not blank the title.
            Assert.Equal("Gold", swedish.Row(DatasetIndex.Datasets.Electrode, "Au")!.Title);
        }

        [Fact]
        public void SeriesUsesTheElementTableSpellingOfCaesium()
        {
            // "cesium" is the American spelling and is not a key in the merged element table;
            // resolving it would return null and the title would fall back to "Cesium", which is
            // right by accident. The key itself must be caesium or the deep link misses.
            Assert.Contains(SeriesModel.All, s => s.ElementKey == "caesium");
            Assert.DoesNotContain(SeriesModel.All, s => s.ElementKey == "cesium");
        }

        [Fact]
        public void SolubilityDetailNamesTheAnionAndTheWord()
        {
            var silver = Index.Row(DatasetIndex.Datasets.Solubility, "Ag^+");
            Assert.NotNull(silver);
            Assert.Contains("Cl^- insoluble", silver!.Detail, StringComparison.Ordinal);

            // "---" means no stable compound, and must not be rendered as a solubility claim.
            Assert.DoesNotContain("---", silver.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public void AlloysDeepLinkToAnElementBecauseTheyHaveNoScreen() =>
            Assert.All(Index.InDataset(DatasetIndex.Datasets.Alloy),
                       r => Assert.Equal(DeepLinkTarget.ElementInfo, r.DeepLink));

        [Fact]
        public void UnitIdsAreCategoryQualifiedBecauseNamesRepeat()
        {
            var meter = Index.Row(DatasetIndex.Datasets.Unit, "Length/Meter");
            Assert.NotNull(meter);
            Assert.Equal(1.0, meter!.NumericValue);

            // Temperature is affine, so its detail must not claim a multiplicative factor.
            var celsius = Index.Row(DatasetIndex.Datasets.Unit, "Temperature/Celsius");
            Assert.Equal("Temperature", celsius!.Detail);
        }

        /// <summary>
        /// Numbers reach the user inside answer text, so they must not follow the ambient culture.
        /// Under sv-SE an uncontrolled format writes "-3,04", which reads as a list in English.
        /// </summary>
        [Fact]
        public void NumbersAreInvariantUnderAHostileCulture()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");
                var swedish = DatasetIndex.Build();
                Assert.Contains("-3.04", swedish.Row(DatasetIndex.Datasets.Electrode, "Li")!.Detail,
                                StringComparison.Ordinal);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }

    public class UnitCatalogTest
    {
        [Fact]
        public void CatalogueMatchesAndroid()
        {
            Assert.Equal(14, UnitCatalog.Categories.Count);
            Assert.Equal(65, UnitCatalog.Categories.Sum(c => c.Value.Count));
        }

        [Theory]
        [InlineData("Length", "Kilometer", "Meter", 1.0, 1000.0)]
        [InlineData("Mass", "Gram", "Kilogram", 2500.0, 2.5)]
        [InlineData("Pressure", "Atmosphere", "Pascal", 1.0, 101325.0)]
        public void MultiplicativeCategoriesConvertThroughTheirBaseUnit(
            string category, string from, string to, double value, double expected) =>
            Assert.Equal(expected, UnitCatalog.Convert(category, from, to, value)!.Value, 1e-6);

        [Theory]
        [InlineData("Celsius", "Fahrenheit", 100.0, 212.0)]
        [InlineData("Fahrenheit", "Celsius", 32.0, 0.0)]
        [InlineData("Celsius", "Kelvin", 0.0, 273.15)]
        [InlineData("Kelvin", "Celsius", 0.0, -273.15)]
        [InlineData("Fahrenheit", "Kelvin", 32.0, 273.15)]
        public void TemperatureIsAffineNotMultiplicative(
            string from, string to, double value, double expected) =>
            Assert.Equal(expected, UnitCatalog.Convert(UnitCatalog.Temperature, from, to, value)!.Value, 1e-9);

        [Fact]
        public void UnknownCategoryOrUnitReturnsNullRatherThanZero()
        {
            Assert.Null(UnitCatalog.Convert("Luminosity", "Lumen", "Lux", 1.0));
            Assert.Null(UnitCatalog.Convert("Length", "Furlong", "Meter", 1.0));
            Assert.Null(UnitCatalog.Convert("Length", "Meter", "Furlong", 1.0));
        }

        [Fact]
        public void CategoryOfFindsAUnitAndReportsNullForAnUnknownOne()
        {
            Assert.Equal("Pressure", UnitCatalog.CategoryOf("Bar"));
            Assert.Null(UnitCatalog.CategoryOf("Furlong"));
        }

        [Fact]
        public void EveryNonTemperatureCategoryHasExactlyOneBaseUnit()
        {
            foreach (var (category, units) in UnitCatalog.Categories)
            {
                if (category == UnitCatalog.Temperature) continue;
                Assert.Equal(1, units.Count(u => u.Factor == 1.0));
            }
        }
    }
}
