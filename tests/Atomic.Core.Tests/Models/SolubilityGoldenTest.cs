using System;
using System.IO;
using System.Linq;
using Atomic.Core.Models;
using Xunit;

namespace Atomic.Core.Tests.Models
{
    /// <summary>
    /// The solubility table against <c>shared/golden/solubility.tsv</c> (roadmap 0.8, D58), which
    /// Android generates from its <c>SolubilityModel.kt</c>. The two apps showed different tables until
    /// then; this keeps them one.
    /// </summary>
    public class SolubilityGoldenTest
    {
        private static string GoldenPath => Path.Combine(AppContext.BaseDirectory, "golden", "solubility.tsv");

        [Fact]
        public void TableMatchesTheSharedGoldenFile()
        {
            Assert.True(File.Exists(GoldenPath), $"{GoldenPath} missing - is the shared submodule checked out?");

            var lines = File.ReadAllLines(GoldenPath).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
            Assert.Equal(["cation", .. SolubilityData.Anions], lines[0].Split('\t'));

            var expected = lines.Skip(1).ToList();
            var actual = SolubilityData.Columns.Select(c => c.Cation + "\t" + string.Join('\t', c.Values)).ToList();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void EveryCellIsACode()
        {
            Assert.Equal(14, SolubilityData.Columns.Count);
            Assert.All(SolubilityData.Columns, c =>
            {
                Assert.Equal(SolubilityData.Anions.Count, c.Values.Count);
                Assert.All(c.Values, v => Assert.Contains(v, new[] { "S", "Ss", "I", "---" }));
            });
        }

        [Theory]
        [InlineData("SO4^2-", "SO₄²⁻")]
        [InlineData("NH4^+", "NH₄⁺")]
        [InlineData("CH3CO2^-", "CH₃CO₂⁻")]
        [InlineData("PO4^3-", "PO₄³⁻")]
        [InlineData("Fe^3+", "Fe³⁺")]
        [InlineData("H^+", "H⁺")]
        public void IonsArePrintedWithSubscriptsAndCharges(string ion, string label) =>
            Assert.Equal(label, SolubilityData.Label(ion));
    }
}
