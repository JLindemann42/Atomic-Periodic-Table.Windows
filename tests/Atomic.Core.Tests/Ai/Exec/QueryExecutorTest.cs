using System;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Exec;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Exec
{
    /// <summary>
    /// The executor, against the real element table.
    ///
    /// <para>Two rules run through the whole file and most of these cases exist to hold them:
    /// absent data is reported rather than guessed, and an aggregate always discloses what it
    /// missed.</para>
    /// </summary>
    public class QueryExecutorTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static QueryExecutor Executor(Entitlements? entitlements = null, string language = "en") =>
            new(Service.Store,
                Service.Datasets,
                Service.Localized(language),
                new ReswStringProvider(language, fallBackToEnglish: true),
                entitlements);

        private static readonly QueryExecutor Full = Executor();

        private static QueryPlan Lookup(string key, string field, string? unit = null) =>
            new(Intent.PropertyLookup,
                Entities: [new EntityRef.Element(key)],
                FieldIds: [field],
                TargetUnit: unit);

        // ---- Absent data is reported, never guessed -----------------------------------------

        /// <summary>
        /// The rule the whole file rests on. A missing value yields NoData carrying the field's
        /// coverage, so the answer can say how sparse the data is rather than printing a sentinel
        /// or drifting to another topic.
        /// </summary>
        [Fact]
        public void AMissingValueBecomesNoDataCarryingItsCoverage()
        {
            // curie_point is one of the sparse fields; most elements have none.
            var barren = Service.Store.Elements.First(e => e.Value("curie_point").IsMissing);

            var result = Full.Execute(Lookup(barren.Key, "curie_point"));

            var noData = Assert.IsType<ExecutionResult.NoData>(result);
            Assert.Equal("curie_point", noData.FieldId);
            Assert.Equal(barren.Key, noData.Element!.Key);
            Assert.Equal(Service.Store.CoverageOf("curie_point"), noData.Coverage);
            Assert.True(noData.Coverage is > 0 and < 118, "the coverage figure is what makes the answer honest");
        }

        [Fact]
        public void APresentValueIsReturnedWithItsCitation()
        {
            var result = Full.Execute(Lookup("gold", "density"));

            var property = Assert.IsType<ExecutionResult.Property>(result);
            Assert.Equal("gold", property.Element.Key);
            Assert.NotNull(property.Quantity);
            Assert.NotEmpty(property.Display);
            Assert.Single(property.Citations);
            Assert.Equal("Gold", property.Citations[0].Source);
        }

        // ---- Ranking --------------------------------------------------------------------------

        /// <summary>
        /// 19.3 g/cm³ says nothing unless you already know the range; "the 6th densest of 105" does.
        /// </summary>
        [Fact]
        public void ANumericValueCarriesItsRank()
        {
            var property = Assert.IsType<ExecutionResult.Property>(Full.Execute(Lookup("gold", "density")));

            Assert.NotNull(property.Rank);
            Assert.InRange(property.Rank!.Value, 1, property.RankedOutOf);
            Assert.True(property.RankedOutOf >= 20);
        }

        /// <summary>
        /// Ranking atomic numbers or NFPA codes would be noise, not context.
        ///
        /// <para>Sodium rather than gold: valence_electrons is derived only for the s and p blocks,
        /// so a d-block element answers NoData and never reaches the ranking code at all.</para>
        /// </summary>
        [Theory]
        [InlineData("atomic_number")]
        [InlineData("period")]
        [InlineData("group_number")]
        [InlineData("valence_electrons")]
        public void FieldsWhoseOrderingMeansNothingAreNotRanked(string field)
        {
            var property = Assert.IsType<ExecutionResult.Property>(Full.Execute(Lookup("sodium", field)));
            Assert.Null(property.Rank);
        }

        // ---- The paywall ----------------------------------------------------------------------

        private static readonly Entitlements FreeUser = new(IsPro: false, IsProPlus: false);

        /// <summary>
        /// Gating in the executor rather than the composer is what covers every shape at once: a
        /// ranking over a gated column is the column, and an average over it is the column
        /// summarised.
        /// </summary>
        [Fact]
        public void AGatedFieldIsLockedWhicheverShapeAsksForIt()
        {
            var gated = FieldRegistry.All.First(s => s.Tier == Tier.Pro && s.Kind == FieldKind.Numeric).Id;
            var free = Executor(FreeUser);

            Assert.IsType<ExecutionResult.Locked>(free.Execute(Lookup("gold", gated)));

            Assert.IsType<ExecutionResult.Locked>(free.Execute(new QueryPlan(
                Intent.Superlative, FieldIds: [gated], SortField: gated)));

            Assert.IsType<ExecutionResult.Locked>(free.Execute(new QueryPlan(
                Intent.Aggregate, FieldIds: [gated], SortField: gated, Aggregation: Aggregation.Avg)));
        }

        /// <summary>
        /// The deliberate exception. "The mechanical properties of gold" holds free and PRO fields,
        /// and withholding the free ones because a gated one shares the family would be gating more
        /// than the app does.
        /// </summary>
        [Fact]
        public void ACategoryAnswersWithTheFreeFieldsRatherThanLockingTheFamily()
        {
            var mechanical = FieldRegistry.ByCategory(FieldCategory.Mechanical).Select(s => s.Id).ToList();
            var plan = new QueryPlan(
                Intent.CategoryLookup,
                Entities: [new EntityRef.Element("gold")],
                FieldIds: mechanical);

            var result = Executor(FreeUser).Execute(plan);

            // Gold has free mechanical fields, so the family answers rather than locking.
            if (result is ExecutionResult.Comparison comparison)
            {
                Assert.All(comparison.FieldIds, f => Assert.True(FreeUser.AllowsField(f)));
            }
            else
            {
                Assert.IsType<ExecutionResult.Locked>(result);
            }
        }

        [Fact]
        public void SafetyBalancingAndSolutionsMirrorTheScreensTheyCameFrom()
        {
            var free = Executor(FreeUser);

            // Hazard ratings are PRO on the element screen.
            Assert.IsType<ExecutionResult.Locked>(free.Execute(new QueryPlan(
                Intent.Safety, Entities: [new EntityRef.Element("mercury")])));

            // Balancing and solutions are compound analysis, gated at PRO+ like molar mass.
            Assert.IsType<ExecutionResult.Locked>(free.Execute(new QueryPlan(
                Intent.BalanceEquation, RawQuery: "Fe + O2 -> Fe2O3")));
            Assert.IsType<ExecutionResult.Locked>(free.Execute(new QueryPlan(
                Intent.FormulaMass, RawQuery: "H2SO4")));
        }

        /// <summary>
        /// Unit conversion and decay arithmetic are free on purpose: both are already free elsewhere
        /// in the app, and gating them here would be a new paywall rather than a mirror of one.
        /// </summary>
        [Fact]
        public void ConversionAndDecayStayFree()
        {
            var free = Executor(FreeUser);

            var converted = free.Execute(new QueryPlan(
                Intent.UnitConvert, RawQuery: "convert 500 K to celsius", TargetUnit: "°C"));
            Assert.IsType<ExecutionResult.UnitConversion>(converted);

            var decayed = free.Execute(new QueryPlan(
                Intent.DecayCalc,
                Entities: [new EntityRef.Element("carbon")],
                Limit: 14,
                RawQuery: "how much of 100 g of carbon-14 remains after 11460 years"));
            Assert.IsType<ExecutionResult.Decay>(decayed);
        }

        // ---- Aggregates disclose what they missed ---------------------------------------------

        /// <summary>
        /// A mean over the halogens has to say how many of them actually had a value, rather than
        /// quietly averaging whatever was present.
        /// </summary>
        [Fact]
        public void AnAggregateReportsHowManyContributorsWereMissing()
        {
            var plan = new QueryPlan(
                Intent.Aggregate,
                FieldIds: ["curie_point"],
                Filters: [new Filter.IsMetal(true)],
                Aggregation: Aggregation.Avg,
                SortField: "curie_point");

            var result = Full.Execute(plan);

            var aggregate = Assert.IsType<ExecutionResult.Aggregate>(result);
            Assert.True(aggregate.Missing > 0, "curie_point is sparse, so most metals contribute nothing");
            Assert.Equal(aggregate.Contributors.Count + aggregate.Missing,
                         plan.Candidates(Service.Store).Count);
        }

        [Theory]
        [InlineData(Aggregation.Min)]
        [InlineData(Aggregation.Max)]
        [InlineData(Aggregation.Avg)]
        [InlineData(Aggregation.Median)]
        [InlineData(Aggregation.Sum)]
        [InlineData(Aggregation.Range)]
        public void EveryStatisticIsComputedOverTheSortedValues(Aggregation aggregation)
        {
            var plan = new QueryPlan(
                Intent.Aggregate,
                FieldIds: ["density"],
                Filters: [new Filter.InSeries(new HashSet<SeriesId> { SeriesId.NobleGas })],
                Aggregation: aggregation,
                SortField: "density");

            var result = Assert.IsType<ExecutionResult.Aggregate>(Full.Execute(plan));
            var values = result.Contributors.Select(c => c.Quantity!.Mid).OrderBy(v => v).ToList();

            var expected = aggregation switch
            {
                Aggregation.Min => values[0],
                Aggregation.Max => values[^1],
                Aggregation.Avg => values.Average(),
                Aggregation.Sum => values.Sum(),
                Aggregation.Range => values[^1] - values[0],
                Aggregation.Median => values.Count % 2 == 1
                    ? values[values.Count / 2]
                    : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2.0,
                _ => throw new InvalidOperationException(),
            };

            Assert.Equal(expected, result.Value, 1e-9);
        }

        [Fact]
        public void ACountNeedsNoFieldAtAll()
        {
            var result = Full.Execute(new QueryPlan(Intent.Aggregate, Aggregation: Aggregation.Count));

            var aggregate = Assert.IsType<ExecutionResult.Aggregate>(result);
            Assert.Equal(118, aggregate.Value);
            Assert.Equal(0, aggregate.Missing);
        }

        // ---- Lists and superlatives -------------------------------------------------------------

        /// <summary>
        /// A value comparison already drops the elements with no value, so counting against the
        /// post-filter set would always report zero and the answer could never disclose the gap.
        /// </summary>
        [Fact]
        public void AFilteredListCountsWhatItExcludedForHavingNoValue()
        {
            var plan = new QueryPlan(
                Intent.FilterList,
                Filters: [new Filter.IsMetal(true)],
                SortField: "curie_point",
                Limit: 10);

            var list = Assert.IsType<ExecutionResult.ElementList>(Full.Execute(plan));
            Assert.True(list.Missing > 0);
        }

        /// <summary>"the third densest element" asks for one element, three down the ranking.</summary>
        [Fact]
        public void ARankOrdinalSkipsDownTheRanking()
        {
            var top = Assert.IsType<ExecutionResult.ElementList>(Full.Execute(new QueryPlan(
                Intent.Superlative, FieldIds: ["density"], SortField: "density", Limit: 1)));

            var third = Assert.IsType<ExecutionResult.ElementList>(Full.Execute(new QueryPlan(
                Intent.Superlative, FieldIds: ["density"], SortField: "density",
                FieldOrdinal: 3, Limit: 1)));

            Assert.Equal(2, third.RankOffset);
            Assert.NotEqual(top.Results[0].Element.Key, third.Results[0].Element.Key);
        }

        /// <summary>A superlative needs something to be measured against.</summary>
        [Fact]
        public void ASingleRowSuperlativeCarriesItsRunnerUp()
        {
            var list = Assert.IsType<ExecutionResult.ElementList>(Full.Execute(new QueryPlan(
                Intent.Superlative, FieldIds: ["density"], SortField: "density", Limit: 1)));

            Assert.NotNull(list.RunnerUp);
            Assert.True(list.Results[0].Quantity!.Mid > list.RunnerUp!.Quantity!.Mid);
        }

        [Fact]
        public void ASubsetWithNoPropertyJustListsWhatMatched()
        {
            var list = Assert.IsType<ExecutionResult.ElementList>(Full.Execute(new QueryPlan(
                Intent.FilterList,
                Filters: [new Filter.InSeries(new HashSet<SeriesId> { SeriesId.NobleGas })],
                Limit: 10)));

            Assert.Null(list.FieldId);
            Assert.Equal(0, list.Missing);
            Assert.NotEmpty(list.Results);
        }

        [Fact]
        public void FiltersThatMatchNothingProduceEmptyRatherThanAnEmptyList()
        {
            var plan = new QueryPlan(
                Intent.FilterList,
                Filters:
                [
                    new Filter.InSeries(new HashSet<SeriesId> { SeriesId.NobleGas }),
                    new Filter.IsMetal(true),
                ],
                SortField: "density",
                Limit: 10);

            Assert.IsType<ExecutionResult.Empty>(Full.Execute(plan));
        }

        // ---- Comparatives -----------------------------------------------------------------------

        /// <summary>
        /// The subject is the element named first: "is gold denser than lead" holds when gold is on
        /// the winning side of the comparison the adjective describes.
        /// </summary>
        [Fact]
        public void AYesNoComparativeAnswersAboutTheFirstElement()
        {
            var holds = Assert.IsType<ExecutionResult.Comparative>(Full.Execute(new QueryPlan(
                Intent.Comparative,
                Entities: [new EntityRef.Element("gold"), new EntityRef.Element("lead")],
                FieldIds: ["density"], Limit: 1)));

            Assert.True(holds.ClaimHolds);
            Assert.Equal("gold", holds.Winner.Key);

            var fails = Assert.IsType<ExecutionResult.Comparative>(Full.Execute(new QueryPlan(
                Intent.Comparative,
                Entities: [new EntityRef.Element("lead"), new EntityRef.Element("gold")],
                FieldIds: ["density"], Limit: 1)));

            Assert.False(fails.ClaimHolds);
            Assert.Equal("gold", fails.Winner.Key);
        }

        [Fact]
        public void OnlyAByHowMuchQuestionCarriesTheRatio()
        {
            var whichOf = Assert.IsType<ExecutionResult.Comparative>(Full.Execute(new QueryPlan(
                Intent.Comparative,
                Entities: [new EntityRef.Element("gold"), new EntityRef.Element("aluminium")],
                FieldIds: ["density"], Limit: 2)));
            Assert.Null(whichOf.Ratio);
            Assert.Null(whichOf.ClaimHolds);

            var byHowMuch = Assert.IsType<ExecutionResult.Comparative>(Full.Execute(new QueryPlan(
                Intent.Comparative,
                Entities: [new EntityRef.Element("gold"), new EntityRef.Element("aluminium")],
                FieldIds: ["density"], Limit: 3)));
            Assert.NotNull(byHowMuch.Ratio);
            Assert.True(byHowMuch.Ratio > 1.0);
        }

        [Fact]
        public void AComparativeWithAMissingValueReportsWhichElementLacksIt()
        {
            var barren = Service.Store.Elements.First(e => e.Value("curie_point").IsMissing);

            var result = Full.Execute(new QueryPlan(
                Intent.Comparative,
                Entities: [new EntityRef.Element(barren.Key), new EntityRef.Element("iron")],
                FieldIds: ["curie_point"], Limit: 1));

            var noData = Assert.IsType<ExecutionResult.NoData>(result);
            Assert.Equal(barren.Key, noData.Element!.Key);
        }

        // ---- Nuclides, isotopes and decay --------------------------------------------------------

        [Fact]
        public void NeutronCountsAreDerivedFromTheMassNumber()
        {
            var result = Full.Execute(new QueryPlan(
                Intent.NuclideCount, Entities: [new EntityRef.Element("uranium")], Limit: 238));

            var nuclide = Assert.IsType<ExecutionResult.Nuclide>(result);
            Assert.Equal(92, nuclide.Protons);
            Assert.Equal(146, nuclide.Neutrons);
        }

        [Fact]
        public void IsotopesComeBackLongestLivedFirstWithStableOnesAhead()
        {
            var result = Full.Execute(new QueryPlan(
                Intent.Isotopes, Entities: [new EntityRef.Element("carbon")], Limit: 8));

            var isotopes = Assert.IsType<ExecutionResult.Isotopes>(result);
            Assert.NotEmpty(isotopes.Shown);
            Assert.True(isotopes.Total >= isotopes.Shown.Count);

            // Every stable isotope precedes every unstable one.
            var lastStable = isotopes.Shown.Select((iso, i) => (iso, i)).Where(x => x.iso.Stable)
                .Select(x => x.i).DefaultIfEmpty(-1).Max();
            var firstUnstable = isotopes.Shown.Select((iso, i) => (iso, i)).Where(x => !x.iso.Stable)
                .Select(x => x.i).DefaultIfEmpty(int.MaxValue).Min();
            Assert.True(lastStable < firstUnstable);
        }

        /// <summary>
        /// The three ways a decay question can have no number are kept apart. "Stable" is a positive
        /// statement - nothing decays - while "not listed" and "listed with no half-life" are two
        /// different gaps.
        /// </summary>
        [Fact]
        public void TheThreeNoNumberDecayOutcomesAreDistinct()
        {
            var stable = Assert.IsType<ExecutionResult.Decay>(Full.Execute(new QueryPlan(
                Intent.DecayCalc, Entities: [new EntityRef.Element("carbon")], Limit: 12,
                RawQuery: "how much carbon-12 is left after 100 years")));
            Assert.Equal(ExecutionResult.DecayMode.Stable, stable.Mode);
            Assert.True(stable.Stable);

            var absent = Assert.IsType<ExecutionResult.Decay>(Full.Execute(new QueryPlan(
                Intent.DecayCalc, Entities: [new EntityRef.Element("carbon")], Limit: 99,
                RawQuery: "how much carbon-99 is left after 100 years")));
            Assert.Equal(ExecutionResult.DecayMode.NoHalfLife, absent.Mode);
            Assert.False(absent.Stable);
        }

        [Fact]
        public void ADecayWithAStartingAmountAnswersHowMuchIsLeft()
        {
            var decay = Assert.IsType<ExecutionResult.Decay>(Full.Execute(new QueryPlan(
                Intent.DecayCalc, Entities: [new EntityRef.Element("carbon")], Limit: 14,
                RawQuery: "how much of 100 g of carbon-14 remains after 11460 years")));

            Assert.Equal(ExecutionResult.DecayMode.Remaining, decay.Mode);
            Assert.Equal(100.0, decay.InitialAmount);
            // Two half-lives of carbon-14 is about 11460 years, so roughly a quarter survives.
            Assert.InRange(decay.FinalAmount!.Value, 20.0, 30.0);
        }

        /// <summary>
        /// Without a starting amount, all that can honestly be said is how many half-lives went by
        /// and what fraction survives.
        /// </summary>
        [Fact]
        public void ADecayWithNoStartingAmountReportsHalfLivesRatherThanAMass()
        {
            var decay = Assert.IsType<ExecutionResult.Decay>(Full.Execute(new QueryPlan(
                Intent.DecayCalc, Entities: [new EntityRef.Element("carbon")], Limit: 14,
                RawQuery: "how much carbon-14 is left after 11460 years")));

            Assert.Equal(ExecutionResult.DecayMode.HalfLives, decay.Mode);
            Assert.Null(decay.InitialAmount);
            Assert.NotNull(decay.HalfLivesElapsed);
        }

        // ---- Balancing ----------------------------------------------------------------------------

        /// <summary>
        /// A failure is a result, not a null. Declining would drop the query back to the router,
        /// which answers it by mentioning that the app has a balancer - advice, where the user asked
        /// a question.
        /// </summary>
        [Fact]
        public void AnUnbalanceableEquationIsAnAnswerRatherThanADecline()
        {
            var result = Full.Execute(new QueryPlan(
                Intent.BalanceEquation, RawQuery: "Na + Cl2 -> KCl"));

            var balanced = Assert.IsType<ExecutionResult.BalancedEquation>(result);
            Assert.NotNull(balanced.Reason);
            Assert.NotEmpty(balanced.Citations);
        }

        [Fact]
        public void ABalancedEquationCarriesItsProof()
        {
            var result = Full.Execute(new QueryPlan(
                Intent.BalanceEquation, RawQuery: "Fe + O2 -> Fe2O3"));

            var balanced = Assert.IsType<ExecutionResult.BalancedEquation>(result);
            Assert.Null(balanced.Reason);
            Assert.NotEmpty(balanced.Tally);
            // The tally is the proof: both sides agree, element by element.
            Assert.All(balanced.Tally, t => Assert.Equal(t.Left, t.Right));
        }

        /// <summary>
        /// The chip used to open an empty input box, asking the user to retype the equation they had
        /// just been given the answer to.
        /// </summary>
        [Fact]
        public void TheBalancerChipCarriesTheEquationTheSolverRead()
        {
            var result = Assert.IsType<ExecutionResult.BalancedEquation>(Full.Execute(new QueryPlan(
                Intent.BalanceEquation, RawQuery: "balance this reaction: Fe + O2 -> Fe2O3")));

            var args = result.Citations[0].Args;
            Assert.True(args.ContainsKey(QueryExecutor.ArgEquation));
            // The equation, not the sentence around it.
            Assert.DoesNotContain("balance this", args[QueryExecutor.ArgEquation], StringComparison.OrdinalIgnoreCase);
        }

        // ---- Rendering -----------------------------------------------------------------------------

        /// <summary>
        /// A value read straight from the data prints exactly as authored, so nothing is lost to
        /// rounding on a plain lookup; only a converted value is reformatted.
        /// </summary>
        [Fact]
        public void AnUnconvertedValuePrintsAsAuthored()
        {
            var property = Assert.IsType<ExecutionResult.Property>(Full.Execute(Lookup("gold", "density")));
            Assert.Equal(Service.Store.Element("gold")!.Quantity("density")!.Display, property.Display);
        }

        [Fact]
        public void AConvertedValueIsReformattedWithItsNewUnit()
        {
            var property = Assert.IsType<ExecutionResult.Property>(
                Full.Execute(Lookup("gold", "melting_point", "K")));

            Assert.Contains("K", property.Display, StringComparison.Ordinal);
        }

        /// <summary>
        /// A prose field is served from the active language's table. The numeric index is parsed
        /// from English, which is right for measured values and wrong for these.
        /// </summary>
        [Fact]
        public void AProseFieldIsRenderedInTheActiveLanguage()
        {
            var swedish = Assert.IsType<ExecutionResult.Property>(
                Executor(language: "sv").Execute(Lookup("gold", "phase")));

            Assert.Equal("Fast", swedish.Display);
        }

        [Fact]
        public void TheDisplayNameFollowsTheActiveLanguage()
        {
            Assert.Equal("Gold", Full.DisplayName(Service.Store.Element("gold")!));
            Assert.Equal("Guld", Executor(language: "sv").DisplayName(Service.Store.Element("gold")!));
        }

        // ---- The contract ----------------------------------------------------------------------------

        [Fact]
        public void AnUnknownPlanExecutesToNothing() =>
            Assert.Null(Full.Execute(new QueryPlan(Intent.Unknown)));

        [Fact]
        public void EveryIntentIsHandled()
        {
            // A plan the executor silently returns null for is an intent the planner can produce and
            // nothing can answer - which reaches the user as a decline with no explanation.
            var unhandled = Enum.GetValues<Intent>()
                .Where(intent => intent != Intent.Unknown)
                .Where(intent => Full.Execute(PlanFor(intent)) is null)
                .ToList();

            // The intents that legitimately return null do so because the *plan* was incomplete,
            // not because the intent is unhandled - they need entities this synthetic plan has no
            // way to supply. Named so the set cannot grow unnoticed. An explanation needs its key and
            // the explanations it names, which ExplanationTest supplies (roadmap 2.4); the gas law
            // its quantities, pH its formula and molarity, and an ion its "symbol:charge", which the
            // calculators corpus supplies (roadmap 2.8).
            Assert.Equal(
                [Intent.Explanation, Intent.GasLaw, Intent.IonConfig, Intent.PhCalc, Intent.SolutionCalc, Intent.UnitConvert],
                unhandled.OrderBy(i => i.ToString(), StringComparer.Ordinal));

            static QueryPlan PlanFor(Intent intent) => intent switch
            {
                Intent.IsotopeComparison => new QueryPlan(intent, Entities:
                [
                    new EntityRef.Nuclide("uranium", 235),
                    new EntityRef.Nuclide("uranium", 238),
                ]),
                Intent.Comparative or Intent.Comparison or Intent.Neighbour => new QueryPlan(
                    intent,
                    Entities: [new EntityRef.Element("gold"), new EntityRef.Element("lead")],
                    FieldIds: ["density"], Limit: 1),
                Intent.DatasetLookup => new QueryPlan(intent, Entities:
                    [new EntityRef.DatasetRow(DatasetIndex.Datasets.Dictionary, "Density")]),
                Intent.FormulaMass => new QueryPlan(intent, RawQuery: "H2SO4"),
                Intent.BalanceEquation => new QueryPlan(intent, RawQuery: "Fe + O2 -> Fe2O3"),
                Intent.MoleConversion => new QueryPlan(intent, RawQuery: "2 moles of iron"),
                Intent.NuclideCount or Intent.DecayCalc => new QueryPlan(
                    intent, Entities: [new EntityRef.Element("uranium")], Limit: 238,
                    RawQuery: "uranium-238 after 100 years"),
                Intent.Aggregate => new QueryPlan(intent, Aggregation: Aggregation.Count),
                Intent.Superlative or Intent.FilterList => new QueryPlan(
                    intent, FieldIds: ["density"], SortField: "density", Limit: 1),
                Intent.CategoryLookup => new QueryPlan(
                    intent, Entities: [new EntityRef.Element("gold")],
                    FieldIds: FieldRegistry.ByCategory(FieldCategory.Thermo).Select(s => s.Id).ToList()),
                _ => new QueryPlan(
                    intent, Entities: [new EntityRef.Element("gold")], FieldIds: ["density"], Limit: 8),
            };
        }
    }
}
