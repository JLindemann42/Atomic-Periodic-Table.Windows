using System;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Nlu
{
    /// <summary>
    /// The planner's sub-planners, each exercised on its own.
    ///
    /// <para>Android has no test for any of these - they are private behind an 876-line cascade and
    /// covered only by the corpus, which tells you a query came out wrong rather than which of
    /// eight ordered branches claimed it. That is why they are internal here.</para>
    /// </summary>
    public class QueryPlannerSupportTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static readonly QueryPlanner Planner = new(
            Service.Store,
            Service.Datasets,
            Service.Resolver("en"),
            new FieldResolver(new ReswStringProvider("en", fallBackToEnglish: true)),
            Service.Retriever("en"));

        private static string Norm(string q) => TextMatching.NormalizeForLookup(q);

        private static QueryPlan? Calculate(string query) => Planner.CalculationPlan(query, Norm(query));

        // ---- The ordering that makes the cascade a specification ---------------------------

        /// <summary>
        /// The whole reason CalculationPlan is ordered the way it is. Each of these three queries
        /// matches a *later* branch as well, and the later branch would answer a different question.
        /// </summary>
        [Theory]
        // MoleQuantity matches the "0.5 mol" inside "0.5 mol/L", so below the solution branch this
        // is answered as a particle count.
        [InlineData("how many grams of NaCl for 250 mL of 0.5 mol/L", Intent.SolutionCalc)]
        // ISOTOPE_WORDS holds "decay", "half life" and "how long does", so below the isotope branch
        // this is answered with the isotope table instead of the arithmetic asked for.
        [InlineData("how much of 100 g of carbon-14 remains after 11460 years", Intent.DecayCalc)]
        // The arrow is the most specific syntax any of these carry, and it is read on the raw query
        // because normalisation lowercases every formula in the sentence.
        [InlineData("balance this: Fe + O2 -> Fe2O3", Intent.BalanceEquation)]
        public void EachGuardBeatsTheBranchThatWouldOtherwiseSwallowIt(string query, Intent expected) =>
            Assert.Equal(expected, Calculate(query)?.Intent);

        [Fact]
        public void AnEquationIsClaimedEvenWhenItIsTooLargeToSolve()
        {
            // Falling through would answer a readable equation by recommending the balancer the
            // user is already effectively using.
            var many = string.Join(" + ", Enumerable.Range(0, 12).Select(_ => "H2O")) + " -> " +
                       string.Join(" + ", Enumerable.Range(0, 12).Select(_ => "H2"));
            var plan = Calculate(many);
            Assert.Equal(Intent.BalanceEquation, plan?.Intent);
        }

        [Fact]
        public void ProseContainingAnArrowIsNotAnEquation() =>
            // The plan is produced only when the equation actually parses.
            Assert.NotEqual(Intent.BalanceEquation, Calculate("does x -> y mean implication")?.Intent);

        // ---- Nuclides ----------------------------------------------------------------------

        [Fact]
        public void TwoNuclidesAreAComparisonRatherThanTwoDecayQuestions()
        {
            var plan = Calculate("uranium-235 vs uranium-238");

            Assert.Equal(Intent.IsotopeComparison, plan?.Intent);
            Assert.Equal(2, plan!.Entities.Count);
            Assert.All(plan.Entities, e => Assert.IsType<EntityRef.Nuclide>(e));
        }

        /// <summary>
        /// The nuclide pattern is "word gap digits", so "of 100" matches before the sentence
        /// reaches carbon-14. Taking the first match declined the question on the word "of".
        /// </summary>
        [Fact]
        public void ANumberEarlierInTheSentenceDoesNotMaskTheNuclide()
        {
            var plan = Calculate("how much of 100 g of carbon-14 remains after 11460 years");

            Assert.Equal(Intent.DecayCalc, plan?.Intent);
            Assert.Equal("carbon", ((EntityRef.Element)plan!.Entities[0]).Key);
            Assert.Equal(14, plan.Limit);
        }

        /// <summary>
        /// No nuclide has fewer nucleons than protons, and all three nuclide branches reject one
        /// that does - which is what stops the loose "word gap digits" pattern claiming an
        /// accidental match.
        ///
        /// <para><b>Android guards only two of the three.</b> Its neutron-count branch claims
        /// "how many neutrons in uranium-3" and reaches the executor with a mass number below the
        /// atomic number, where the subtraction gives -89 neutrons. This test pinned that asymmetry
        /// while the corpus was still being ported, because tightening a guard can move rows and the
        /// corpus is the specification for this phase. P4h is green and the fix is corpus-neutral -
        /// 2,793 of 2,793 either way - so the guard is now applied on all three. See D14.</para>
        /// </summary>
        [Fact]
        public void EveryNuclideBranchRejectsAnImpossibleMassNumber()
        {
            Assert.NotEqual(Intent.IsotopeComparison, Calculate("uranium-3 vs uranium-5")?.Intent);
            Assert.NotEqual(Intent.Isotopes, Calculate("half life of uranium-3")?.Intent);
            Assert.NotEqual(Intent.NuclideCount, Calculate("how many neutrons in uranium-3")?.Intent);

            // The guard is on the mass number, not on the shape: a real nuclide still routes.
            Assert.Equal(Intent.NuclideCount, Calculate("how many neutrons in uranium-238")?.Intent);
        }

        [Fact]
        public void NeutronCountsCarryTheMassNumberInLimit()
        {
            var plan = Calculate("how many neutrons does uranium-238 have");

            Assert.Equal(Intent.NuclideCount, plan?.Intent);
            Assert.Equal(238, plan!.Limit);
        }

        /// <summary>
        /// The store is keyed on lowercase English names, so a Swedish or German nuclide resolved
        /// nothing and fell through to a property lookup.
        /// </summary>
        [Fact]
        public void ANuclideResolvesInAnyShippedLanguage()
        {
            var plan = Calculate("hur många neutroner har uran-238");
            Assert.Equal(Intent.NuclideCount, plan?.Intent);
        }

        // ---- Moles and formulas ------------------------------------------------------------

        [Fact]
        public void MoleQuestionsAreRecognisedInBothDirections()
        {
            Assert.Equal(Intent.MoleConversion, Calculate("how many atoms are in 2.5 moles of iron")?.Intent);

            // "If I have 12 grams of carbon-12, how many moles is that" used to resolve magnesium
            // from the 12 and answer with a two-element list.
            var grams = Calculate("if I have 12 grams of carbon, how many moles is that");
            Assert.Equal(Intent.MoleConversion, grams?.Intent);
            Assert.Contains(grams!.Entities, e => e is EntityRef.Element { Key: "carbon" });
        }

        [Fact]
        public void FormulaMassAndCompositionAreDistinguished()
        {
            var mass = Calculate("what is the molar mass of H2SO4");
            Assert.Equal(Intent.FormulaMass, mass?.Intent);
            Assert.Empty(mass!.FieldIds);

            var composition = Calculate("percentage composition of H2SO4");
            Assert.Equal(Intent.FormulaMass, composition?.Intent);
            Assert.Equal(["composition"], composition!.FieldIds);
        }

        /// <summary>
        /// A plain Contains found "salt" inside "basalt", so "what is the composition of basalt"
        /// was answered with the formula mass of NaCl.
        /// </summary>
        [Fact]
        public void ACompoundNameIsMatchedOnWordBoundaries()
        {
            Assert.Equal("NaCl", Planner.CompoundIn("composition of table salt", Norm("composition of table salt")));
            Assert.Null(Planner.CompoundIn("composition of basalt", Norm("composition of basalt")));
        }

        [Fact]
        public void LowercaseFormulasStillParse() =>
            Assert.Equal(Intent.FormulaMass, Calculate("molar mass of h2so4")?.Intent);

        [Fact]
        public void AQuestionWithNoArithmeticIsNotClaimed()
        {
            Assert.Null(Calculate("what is the density of gold"));
            Assert.Null(Calculate("which elements are radioactive"));
        }

        // ---- Unit conversion ---------------------------------------------------------------

        /// <summary>
        /// The guard that made this branch work: a unit token resolves a field all by itself, so
        /// asking "did a field resolve" declined "convert 5 GPa to MPa" on the strength of the very
        /// units being converted.
        /// </summary>
        [Fact]
        public void AStandaloneConversionIsClaimed()
        {
            var plan = Planner.UnitConvertPlan("convert 5 GPa to MPa", new Operators());

            Assert.Equal(Intent.UnitConvert, plan?.Intent);
            Assert.Equal(UnitConverter.KnownUnit("MPa"), plan!.TargetUnit);
        }

        /// <summary>
        /// In "convert 5 GPa to MPa" the GPa has "to" on its right, so OperatorExtractor's reading
        /// picks it as the target and the conversion collapses to a no-op. The scanner's reading
        /// has to win.
        /// </summary>
        [Fact]
        public void TheTargetIsNeverTheUnitAttachedToTheNumber()
        {
            var plan = Planner.UnitConvertPlan("2 eV in kJ/mol", new Operators());
            Assert.Equal(UnitConverter.KnownUnit("kJ/mol"), plan?.TargetUnit);
        }

        [Fact]
        public void APropertyLookupCarryingAUnitIsNotAConversion() =>
            // "density of gold in kg/m³" names a field and an element outside the unit expression.
            Assert.Null(Planner.UnitConvertPlan("density of gold in kg/m3", new Operators()));

        [Fact]
        public void ADimensionalMismatchDeclinesRatherThanInventingARelationship() =>
            Assert.Null(Planner.UnitConvertPlan("convert 500 K to litres", new Operators()));

        [Fact]
        public void RankingAndFilteringCarryUnitsTooAndAreNotConversions()
        {
            var ranking = new Operators(SuperlativeDescending: true);
            Assert.Null(Planner.UnitConvertPlan("densest element above 5 g/cm3", ranking));

            var filtering = new Operators(
                Comparators: [(Op.Gt, new Quantity(5.0, Unit: "g/cm3"))]);
            Assert.Null(Planner.UnitConvertPlan("elements denser than 5 g/cm3", filtering));
        }

        // ---- Comparatives and thresholds ---------------------------------------------------

        [Fact]
        public void TheThreeComparativeShapesAreDistinguishedByLimit()
        {
            // Limit doubles as the question shape: 1 yes/no, 2 which-of, 3 by-how-much.
            Assert.Equal(1, Compare("is gold denser than lead")!.Limit);
            Assert.Equal(2, Compare("which is heavier, gold or silver")!.Limit);
            Assert.Equal(3, Compare("how much denser is gold than aluminium")!.Limit);

            static QueryPlan? Compare(string q) =>
                QueryPlanner.ComparativePlan(q, Norm(q), ["gold", "lead"], []);
        }

        /// <summary>The adjective usually is the property: "denser" means density.</summary>
        [Fact]
        public void TheComparativeAdjectiveSuppliesTheFieldAndTheDirection()
        {
            var denser = QueryPlanner.ComparativePlan(
                "is gold denser than lead", Norm("is gold denser than lead"), ["gold", "lead"], []);
            Assert.Equal(["density"], denser!.FieldIds);
            Assert.True(denser.SortDescending);

            var lighter = QueryPlanner.ComparativePlan(
                "is gold lighter than lead", Norm("is gold lighter than lead"), ["gold", "lead"], []);
            Assert.False(lighter!.SortDescending);
        }

        [Fact]
        public void AnUnframedPairIsNotAComparative() =>
            Assert.Null(QueryPlanner.ComparativePlan(
                "gold and lead", Norm("gold and lead"), ["gold", "lead"], ["density"]));

        /// <summary>
        /// "denser than iron" takes iron's own density as the bound, so the user does not have to
        /// know the figure.
        /// </summary>
        [Fact]
        public void AThresholdCanBeNamedAsAnElement()
        {
            var filter = Planner.ElementThreshold(Norm("denser than iron"), ["iron"], []);

            Assert.NotNull(filter);
            Assert.Equal("density", filter!.FieldId);
            Assert.Equal(Op.Gt, filter.Op);
            Assert.Equal(Service.Store.Element("iron")!.Quantity("density")!.Value, filter.Value.Value);
        }

        /// <summary>
        /// The Romance languages build "denser than gold" out of three words, so neither a
        /// comparative adjective nor a GREATER phrase is present to find.
        /// </summary>
        [Fact]
        public void APeriphrasticComparativeStillProducesAThreshold()
        {
            var filter = Planner.ElementThreshold(
                Norm("plus denses que l or"), ["gold"], ["density"]);

            Assert.NotNull(filter);
            Assert.Equal(Op.Gt, filter!.Op);
        }

        [Fact]
        public void AThresholdNeedsAFieldAndAReferenceValue()
        {
            Assert.Null(Planner.ElementThreshold(Norm("denser than iron"), [], []));
            Assert.Null(Planner.ElementThreshold(Norm("about iron"), ["iron"], ["density"]));
        }

        // ---- Small lookups ------------------------------------------------------------------

        [Fact]
        public void NeighbourDirectionReadsBothWays()
        {
            Assert.Equal(1, QueryPlanner.NeighbourDirection(Norm("what comes after gold")));
            Assert.Equal(-1, QueryPlanner.NeighbourDirection(Norm("what comes before gold")));
            Assert.Null(QueryPlanner.NeighbourDirection(Norm("what is gold")));
        }

        /// <summary>
        /// Longest name first, or lexical retrieval ranks "Stainless steel" above "Steel" for a
        /// question about steel.
        /// </summary>
        [Fact]
        public void AlloysMatchLongestNameFirst()
        {
            Assert.Equal("Stainless steel", Planner.AlloyIn(Norm("what is stainless steel made of"))?.Title);
            Assert.Equal("Steel", Planner.AlloyIn(Norm("what is steel made of"))?.Title);
        }

        /// <summary>The alloy table is English, so a Swedish query has to be glossed first.</summary>
        [Fact]
        public void AlloysResolveThroughTheDatasetGloss() =>
            Assert.Equal("Steel", Planner.AlloyIn(Norm("vad ar stal gjort av"))?.Title);

        /// <summary>
        /// A plain Contains found the Swedish "stål" inside "crystallographic", so that query was
        /// glossed with "steel" and answered from the alloy table.
        /// </summary>
        [Fact]
        public void TheGlossMatchesOnWordBoundaries() =>
            Assert.Null(Planner.AlloyIn(Norm("crystallographic data for iron")));

        [Fact]
        public void ImplicitSuperlativesNameTheirOwnField()
        {
            Assert.Equal("density", QueryPlanner.InferSortField("densest element"));
            Assert.Equal("atomic_mass", QueryPlanner.InferSortField("heaviest element"));
            // "quale elemento è il più denso" resolved no field at all and was declined outright.
            Assert.Equal("density", QueryPlanner.InferSortField("quale elemento è il più denso"));
            Assert.Null(QueryPlanner.InferSortField("what is gold"));
        }

        [Fact]
        public void UnsupportedAspectsAreNamedSoTheAnswerCanSaySo()
        {
            var aspects = QueryPlanner.UnsupportedAspects(
                Norm("compare lithium and sodium on reactivity and uses"));

            Assert.Contains("reactivity", aspects);
            Assert.Contains("uses", aspects);
            Assert.Empty(QueryPlanner.UnsupportedAspects(Norm("compare density and melting point")));
        }

        [Fact]
        public void CategoryWordsMatchLongestPhraseFirst()
        {
            Assert.Equal(FieldCategory.Thermo, QueryPlanner.CategoryIn(Norm("thermal properties of gold")));
            Assert.Null(QueryPlanner.CategoryIn(Norm("what is gold")));
        }

        [Fact]
        public void ThresholdIsTheDocumentedConfidenceFloor() => Assert.Equal(0.7, Planner.Threshold);
    }

}
