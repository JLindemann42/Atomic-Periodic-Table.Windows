using System.Linq;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Data;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Cards
{
    public sealed class ElectronShellsTest
    {
        [Theory]
        [InlineData("K1 L0 M0 N0 O0 P0 Q0 R0", new[] { 1 })]
        [InlineData("K2 L8 M18 N32 O18 P1 Q0 R0", new[] { 2, 8, 18, 32, 18, 1 })]
        [InlineData("K2 L8 M14 N2 O0 P0 Q0 R0", new[] { 2, 8, 14, 2 })]
        [InlineData("K2 L8 M18 N32 O21 P9 Q2 R0", new[] { 2, 8, 18, 32, 21, 9, 2 })]
        public void TheAuthoredFormParsesToShellCounts(string authored, int[] expected) =>
            Assert.Equal(expected, ElectronShells.Parse(authored));

        /// <summary>
        /// The letters are decoration - the position carries the shell. A parser that depended on
        /// them would break on the 41 elements whose letters were corrupted before P1 repaired
        /// them, and on any future authoring that drops one.
        /// </summary>
        [Fact]
        public void TheShellLettersAreNotRequired() =>
            Assert.Equal([2, 8, 18], ElectronShells.Parse("2 8 18"));

        [Fact]
        public void TrailingZeroPaddingIsNotAShell() =>
            Assert.Equal([2, 8], ElectronShells.Parse("K2 L8 M0 N0 O0 P0 Q0 R0"));

        /// <summary>
        /// Documents the one divergence from Android, which filters every zero rather than only the
        /// trailing run. No shipped element has an interior zero - shells fill in order - so the two
        /// agree everywhere today. This pins which behaviour is intended if one ever appears.
        /// </summary>
        [Fact]
        public void AnInteriorZeroIsKeptRatherThanRenumberingTheShellsOutsideIt() =>
            Assert.Equal([2, 0, 18], ElectronShells.Parse("K2 L0 M18 N0 O0 P0 Q0 R0"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("K0 L0 M0")]
        [InlineData("---")]
        public void NothingUsableParsesToNoShells(string? authored) =>
            Assert.Empty(ElectronShells.Parse(authored));

        /// <summary>
        /// The real data, all 118. A chart drawn from a mis-parse looks plausible, so this asserts
        /// against the file that ships rather than against examples chosen to pass.
        /// </summary>
        [Fact]
        public void EveryShippedElementParsesToAPlausibleShellStructure()
        {
            var store = KnowledgeStore.Build(TestAssets.English);
            var problems = new System.Collections.Generic.List<string>();

            foreach (var element in store.Elements)
            {
                var authored = (element.Value("electron_shells") as FieldValue.Text)?.Raw;
                var shells = ElectronShells.Parse(authored);

                if (shells.Count == 0)
                {
                    problems.Add($"{element.Key}: no shells from {Quote(authored)}");
                    continue;
                }

                // The innermost shell is K and holds at most two electrons; more than that means the
                // string was read in the wrong order or the letters leaked into the numbers.
                if (shells[0] is < 1 or > 2)
                    problems.Add($"{element.Key}: first shell {shells[0]} from {Quote(authored)}");

                // Shell count matches the period - that correspondence is the whole point of the
                // diagram - with exactly one exception, and it is a real one rather than a data
                // fault. Palladium is [Kr] 4d10: its 5s shell is empty in the ground state, so it
                // is the only element in the table whose outermost shell holds no electrons. Its
                // diagram therefore has four rings where every other period-5 element has five.
                var expectedShells = element.Key == "palladium" ? 4 : element.Period;
                if (shells.Count != expectedShells)
                    problems.Add($"{element.Key}: {shells.Count} shells but period {element.Period}");

                if (shells.Sum() != element.AtomicNumber)
                    problems.Add($"{element.Key}: electrons {shells.Sum()} != Z {element.AtomicNumber}");
            }

            Assert.Empty(problems);
        }

        private static string Quote(string? value) => value is null ? "null" : $"\"{value}\"";
    }
}
