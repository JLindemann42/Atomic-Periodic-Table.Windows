using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// Ported from <c>EquationBalancerTest.kt</c>. Symbols come from the shipped element table, so
    /// these exercise the same recognition path the app uses.
    /// </summary>
    public class EquationBalancerTest
    {
        private static readonly HashSet<string> Symbols = BuildSymbols();

        private static HashSet<string> BuildSymbols()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in TestAssets.English.Rows.Values)
                if (row.TryGetValue("short", out var v) && v is string s && s.Length > 0)
                    set.Add(s);
            return set;
        }

        private static bool Known(string symbol) => Symbols.Contains(symbol);

        private static EquationBalancer.Result.Balanced Balance(string equation)
        {
            var parsed = Assert.IsType<EquationBalancer.ParseOutcome.Sides>(
                EquationBalancer.Parse(equation, Known));
            var result = EquationBalancer.Balance(parsed.Reactants, parsed.Products);
            return Assert.IsType<EquationBalancer.Result.Balanced>(result);
        }

        private static string Render(EquationBalancer.Result.Balanced b)
        {
            static string Side(IReadOnlyList<Term> terms) => string.Join(" + ",
                terms.Select(t => (t.Coefficient == 1 ? "" : t.Coefficient.ToString()) + t.Formula));
            return Side(b.Reactants) + " -> " + Side(b.Products);
        }

        // ---- the three examples the tool offers ------------------------------------------------

        [Fact]
        public void IronRusting() =>
            Assert.Equal("4Fe + 3O2 -> 2Fe2O3", Render(Balance("Fe + O2 -> Fe2O3")));

        [Fact]
        public void PropaneCombustion() =>
            Assert.Equal("C3H8 + 5O2 -> 3CO2 + 4H2O", Render(Balance("C3H8 + O2 -> CO2 + H2O")));

        [Fact]
        public void PermanganateWithHydrochloricAcid() =>
            Assert.Equal(
                "2KMnO4 + 16HCl -> 2KCl + 2MnCl2 + 8H2O + 5Cl2",
                Render(Balance("KMnO4 + HCl -> KCl + MnCl2 + H2O + Cl2")));

        [Fact]
        public void AlreadyBalancedEquationsStayThatWay() =>
            Assert.Equal("2H2 + O2 -> 2H2O", Render(Balance("2H2 + O2 -> 2H2O")));

        [Fact]
        public void UserSuppliedCoefficientsAreRederivedNotTrusted() =>
            Assert.Equal("4Fe + 3O2 -> 2Fe2O3", Render(Balance("7Fe + 99O2 -> 3Fe2O3")));

        [Fact]
        public void GroupsAndNestedCountsResolve() =>
            Assert.Equal(
                "Fe2(SO4)3 + 6NaOH -> 2Fe(OH)3 + 3Na2SO4",
                Render(Balance("Fe2(SO4)3 + NaOH -> Fe(OH)3 + Na2SO4")));

        [Fact]
        public void EveryBalanceCarriesItsProof()
        {
            var balanced = Balance("C3H8 + O2 -> CO2 + H2O");
            Assert.NotEmpty(balanced.Tally);
            Assert.All(balanced.Tally, t => Assert.Equal(t.Left, t.Right));
            Assert.Contains(balanced.Tally, t => t.Symbol == "C" && t.Left == 3);
        }

        // ---- failure modes, each of which owes the user a different sentence --------------------

        [Fact]
        public void AnElementOnOneSideOnlyIsNamed()
        {
            var parsed = (EquationBalancer.ParseOutcome.Sides)
                EquationBalancer.Parse("Fe + O2 -> Fe2O3 + Na", Known);
            var failed = Assert.IsType<EquationBalancer.Result.Failed>(
                EquationBalancer.Balance(parsed.Reactants, parsed.Products));

            Assert.Equal(EquationBalancer.Reason.OneSidedElement, failed.Reason);
            // Naming the symbol is the difference between a useful message and "no solution".
            Assert.Equal("Na", failed.Detail);
        }

        [Fact]
        public void TwoReactionsWrittenAsOneAreReportedNotGuessedAt()
        {
            // Picking a basis vector out of a two-dimensional null space would present one
            // arbitrary answer as the answer.
            var parsed = (EquationBalancer.ParseOutcome.Sides)
                EquationBalancer.Parse("CO + O2 + H2 -> CO2 + H2O", Known);
            var failed = Assert.IsType<EquationBalancer.Result.Failed>(
                EquationBalancer.Balance(parsed.Reactants, parsed.Products));

            Assert.Equal(EquationBalancer.Reason.Underdetermined, failed.Reason);
        }

        [Fact]
        public void AnEquationWithNoSolutionSaysSo()
        {
            var parsed = (EquationBalancer.ParseOutcome.Sides)
                EquationBalancer.Parse("H2 + O2 -> H2O2 + H2O", Known);
            var result = EquationBalancer.Balance(parsed.Reactants, parsed.Products);
            // Either it is underdetermined or it has no solution; what it must never be is a
            // confident wrong answer.
            var failed = Assert.IsType<EquationBalancer.Result.Failed>(result);
            Assert.Contains(failed.Reason,
                new[] { EquationBalancer.Reason.NoSolution, EquationBalancer.Reason.Underdetermined });
        }

        // ---- parse outcomes: three situations that used to be one null --------------------------

        [Fact]
        public void NoArrowIsNotAnError()
        {
            // What half a typed equation looks like, and what an ordinary sentence looks like.
            Assert.IsType<EquationBalancer.ParseOutcome.NoArrowOutcome>(
                EquationBalancer.Parse("Fe + O2", Known));
            Assert.IsType<EquationBalancer.ParseOutcome.NoArrowOutcome>(
                EquationBalancer.Parse("what is the density of iron", Known));
        }

        [Fact]
        public void AnArrowOverProseIsNotAnEquation() =>
            Assert.IsType<EquationBalancer.ParseOutcome.NotAnEquationOutcome>(
                EquationBalancer.Parse("breakfast -> lunch", Known));

        [Fact]
        public void TooManySpeciesIsAVerdictNotAParseFailure()
        {
            var many = string.Join(" + ", Enumerable.Repeat("H2", 8)) + " -> " +
                       string.Join(" + ", Enumerable.Repeat("H2", 8));
            Assert.IsType<EquationBalancer.ParseOutcome.TooManySpeciesOutcome>(
                EquationBalancer.Parse(many, Known));
        }

        // ---- the arrow pattern ------------------------------------------------------------------

        [Theory]
        [InlineData("Fe + O2 -> Fe2O3")]
        [InlineData("Fe + O2 --> Fe2O3")]
        [InlineData("Fe + O2 => Fe2O3")]
        [InlineData("Fe + O2 → Fe2O3")]
        [InlineData("Fe + O2 ⇌ Fe2O3")]
        [InlineData("Fe + O2 = Fe2O3")]
        public void EveryArrowSpellingIsAccepted(string equation) =>
            Assert.IsType<EquationBalancer.ParseOutcome.Sides>(EquationBalancer.Parse(equation, Known));

        [Theory]
        [InlineData("H2 == O2")]
        [InlineData("mass <= 10")]
        [InlineData("mass >= 10")]
        [InlineData("a != b")]
        public void ComparisonOperatorsAreNotArrows(string text)
        {
            // The Kotlin file once claimed this in a comment while testing raw.contains("="), which
            // is true of all four - so the planner declined these and the tool balanced them.
            Assert.IsType<EquationBalancer.ParseOutcome.NoArrowOutcome>(
                EquationBalancer.Parse(text, Known));
        }

        [Fact]
        public void TheEarliestArrowIsTheSplitPoint()
        {
            // Scanning a list of literals cut "A = B -> C" at the arrow rather than the equals.
            var parsed = Assert.IsType<EquationBalancer.ParseOutcome.Sides>(
                EquationBalancer.Parse("H2 = O2 -> H2O", Known));
            Assert.Single(parsed.Reactants);
            Assert.Equal("H2", parsed.Reactants[0].Formula);
        }

        [Fact]
        public void DoubleArrowIsNotReadAsMinusThenArrow() =>
            Assert.Equal("4Fe + 3O2 -> 2Fe2O3", Render(Balance("Fe + O2 --> Fe2O3")));

        // ---- tolerating the way people actually type ---------------------------------------------

        [Fact]
        public void StateAnnotationsAreIgnored() =>
            Assert.Equal("2H2 + O2 -> 2H2O", Render(Balance("H2(g) + O2(g) -> H2O(l)")));

        [Fact]
        public void StructuralParenthesesAreNotMistakenForStates()
        {
            // (OH) and (SO4) are structure. Stripping them would change what the formula means.
            var counts = ChemistryMath.ElementCounts("Fe(OH)3", Known)!;
            Assert.Equal(3, counts["O"]);
            Assert.Equal(3, counts["H"]);
        }

        [Fact]
        public void ProseAroundTheEquationIsDropped() =>
            Assert.Equal("4Fe + 3O2 -> 2Fe2O3",
                Render(Balance("balance this reaction: Fe + O2 -> Fe2O3")));

        [Fact]
        public void SubscriptDigitsArePastedAllTheTime()
        {
            // The rendered formula echoes what the user typed - the tool shows their equation back
            // with coefficients derived, not a normalised rewrite of it - so the assertion is on
            // the coefficients.
            var b = Balance("H₂ + O₂ -> H₂O");
            Assert.Equal([2, 1], b.Reactants.Select(t => t.Coefficient));
            Assert.Equal([2], b.Products.Select(t => t.Coefficient));
        }

        [Fact]
        public void LowerCaseFormulasAreRecapitalised()
        {
            // Users type "h2so4" far more often than "H2SO4". Same echo rule as above.
            var b = Balance("h2 + o2 -> h2o");
            Assert.Equal([2, 1], b.Reactants.Select(t => t.Coefficient));
            Assert.Equal([2], b.Products.Select(t => t.Coefficient));

            // And the counts really were resolved, not merely accepted.
            Assert.All(b.Tally, t => Assert.Equal(t.Left, t.Right));
            Assert.Contains(b.Tally, t => t.Symbol == "H" && t.Left == 4);
        }

        [Fact]
        public void ChargesAreNotSpeciesSeparators()
        {
            // "Na+" is a charge; splitting on it would leave a dangling empty species.
            var parsed = EquationBalancer.Parse("Na+ + Cl- -> NaCl", Known);
            Assert.IsNotType<EquationBalancer.ParseOutcome.NotAnEquationOutcome>(parsed);
        }
    }
}
