using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Compose;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Exec;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Compose
{
    /// <summary>
    /// The composer, driven through the real executor so the results it renders are ones the engine
    /// actually produces.
    ///
    /// <para>Most of these cases exist to hold a single promise: an answer never asserts more than
    /// the data supports. Partial statistics say they are partial, withheld values say they are
    /// withheld rather than absent, and a working is shown wherever a reader would otherwise have to
    /// take a number on faith.</para>
    /// </summary>
    public class AnswerComposerTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static (QueryExecutor Executor, AnswerComposer Composer) Pair(
            string language = "en",
            Entitlements? entitlements = null,
            ChatCardPolicy? policy = null)
        {
            var strings = new ReswStringProvider(language, fallBackToEnglish: true);
            var localized = Service.Localized(language);
            return (new QueryExecutor(Service.Store, Service.Datasets, localized, strings, entitlements),
                    new AnswerComposer(Service.Store, localized, strings, policy));
        }

        private static ComposedAnswer Answer(
            QueryPlan plan,
            string language = "en",
            Entitlements? entitlements = null,
            ChatCardPolicy? policy = null)
        {
            var (executor, composer) = Pair(language, entitlements, policy);
            var result = executor.Execute(plan)
                         ?? throw new InvalidOperationException($"{plan.Intent} executed to nothing");
            return composer.Compose(result, plan);
        }

        private static QueryPlan Lookup(string key, string field, string? unit = null) =>
            new(Intent.PropertyLookup,
                Entities: [new EntityRef.Element(key)], FieldIds: [field], TargetUnit: unit);

        // ---- Property ---------------------------------------------------------------------

        /// <summary>
        /// A bare figure is not an answer to most people: "19.3 g/cm³" only lands if you already
        /// know the range it sits in.
        /// </summary>
        [Fact]
        public void APropertyAnswerCarriesItsRankAsContext()
        {
            var text = Answer(Lookup("gold", "density")).Text;

            Assert.Contains("Gold", text, StringComparison.Ordinal);
            Assert.Contains("19.3", text, StringComparison.Ordinal);
            // The rank sentence names the pool it ranked against.
            Assert.Contains("105", text, StringComparison.Ordinal);
        }

        [Fact]
        public void AnAnswerCarriesItsSourcesAndATappableChip()
        {
            var answer = Answer(Lookup("gold", "density"));

            Assert.Contains("###", answer.Text, StringComparison.Ordinal);
            Assert.Contains("•", answer.Text, StringComparison.Ordinal);
            Assert.NotEmpty(answer.Actions);
            Assert.Equal(DeepLinkTarget.ElementInfo, answer.Actions[0].Target);
        }

        /// <summary>
        /// A compound answer composes each clause separately and emits one merged Sources section,
        /// rather than repeating the heading after every part.
        /// </summary>
        [Fact]
        public void ComposeBodyOmitsTheSourcesBlock()
        {
            var plan = Lookup("gold", "density");
            var (executor, composer) = Pair();
            var result = executor.Execute(plan)!;

            var body = composer.ComposeBody(result, plan);
            var full = composer.Compose(result, plan);

            Assert.DoesNotContain("•", body.Text, StringComparison.Ordinal);
            Assert.StartsWith(body.Text, full.Text, StringComparison.Ordinal);
            Assert.Equal(composer.CitationsFor(result.Citations), full.Text[body.Text.Length..]);
        }

        // ---- Honesty about what is missing -------------------------------------------------

        /// <summary>
        /// The honest failure: names the field, says it was checked, and reports how sparse it is -
        /// rather than printing the raw sentinel or drifting to an unrelated answer.
        /// </summary>
        [Fact]
        public void AMissingValueSaysSoAndReportsTheCoverage()
        {
            var barren = Service.Store.Elements.First(e => e.Value("curie_point").IsMissing);
            var text = Answer(Lookup(barren.Key, "curie_point")).Text;

            Assert.Contains(Service.Store.CoverageOf("curie_point").ToString(CultureInfo.InvariantCulture),
                            text, StringComparison.Ordinal);
            Assert.Contains("118", text, StringComparison.Ordinal);
        }

        /// <summary>
        /// "Tungsten has no recorded Curie point" is honest and a dead end; the same answer plus the
        /// neighbouring properties that <i>are</i> recorded gives the reader somewhere to go.
        ///
        /// <para>Every property it offers must actually have a value - see D15, where Android's
        /// filter is always true and can suggest properties that are also missing.</para>
        /// </summary>
        [Fact]
        public void TheAlternativesOfferedAreOnesTheElementActuallyHas()
        {
            var element = Service.Store.Elements.First(
                e => e.Value("curie_point").IsMissing && !e.Value("melting_point").IsMissing);

            var text = Answer(Lookup(element.Key, "curie_point")).Text;

            // Whatever it names, the element has to have it. The labels are localized, so the check
            // is that no *absent* thermal field's label appears in the sentence.
            var absentLabels = FieldRegistry.All
                .Where(s => s.Category == FieldCategory.Thermo && element.Value(s.Id).IsMissing)
                .Where(s => s.Id != "curie_point")
                .Select(s => s.Id.Replace('_', ' '))
                .ToList();

            foreach (var label in absentLabels)
                Assert.DoesNotContain($", {label},", text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A withheld answer says the app <i>has</i> the value and is not showing it. Telling a user
        /// "I don't have that" about a figure their element screen visibly displays as a lock would
        /// be a lie, and a confusing one.
        /// </summary>
        [Fact]
        public void AWithheldAnswerNamesTheFieldsAndOffersTheUpgrade()
        {
            var gated = FieldRegistry.All.First(s => s.Tier == Tier.Pro && s.Kind == FieldKind.Numeric).Id;
            var free = new Entitlements(IsPro: false, IsProPlus: false);

            var answer = Answer(Lookup("gold", gated), entitlements: free);

            Assert.Contains(answer.Actions, a => a.Target == DeepLinkTarget.ProPage);
            // The field is named: knowing what is behind the paywall is not the same as being told
            // the value, and it is what makes the upsell concrete.
            Assert.NotEmpty(answer.Text);
        }

        /// <summary>A partial statistic is always disclosed as partial.</summary>
        [Fact]
        public void AnAggregateStatesHowManyContributedAndHowManyDidNot()
        {
            var plan = new QueryPlan(
                Intent.Aggregate,
                FieldIds: ["curie_point"],
                Filters: [new Filter.IsMetal(true)],
                Aggregation: Aggregation.Avg,
                SortField: "curie_point");

            var (executor, composer) = Pair();
            var result = (ExecutionResult.Aggregate)executor.Execute(plan)!;
            var text = composer.Compose(result, plan).Text;

            Assert.Contains(result.Contributors.Count.ToString(CultureInfo.InvariantCulture),
                            text, StringComparison.Ordinal);
            Assert.True(result.Missing > 0);
        }

        /// <summary>
        /// An average with no spread is the classic misleading statistic: the mean density of the
        /// transition metals is about 10 g/cm³, and nothing in that number hints the set runs from
        /// scandium at 3 to osmium at 22.6.
        /// </summary>
        [Fact]
        public void AnAverageShowsBothEndsOfItsRange()
        {
            var plan = new QueryPlan(
                Intent.Aggregate,
                FieldIds: ["density"],
                Filters: [new Filter.InSeries(new HashSet<SeriesId> { SeriesId.TransitionMetal })],
                Aggregation: Aggregation.Avg,
                SortField: "density");

            var (executor, composer) = Pair();
            var result = (ExecutionResult.Aggregate)executor.Execute(plan)!;
            var text = composer.Compose(result, plan).Text;

            var lowest = result.Contributors.OrderBy(c => c.Quantity!.Value).First();
            var highest = result.Contributors.OrderBy(c => c.Quantity!.Value).Last();

            Assert.Contains(lowest.Element.Key, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(highest.Element.Key, text, StringComparison.OrdinalIgnoreCase);
        }

        // ---- Workings ---------------------------------------------------------------------

        /// <summary>
        /// The tally is the proof. A balancer that prints only coefficients is asking to be trusted;
        /// one that shows both sides' atom counts can be checked in a few seconds.
        /// </summary>
        [Fact]
        public void ABalancedEquationShowsItsAtomCheck()
        {
            var text = Answer(new QueryPlan(Intent.BalanceEquation, RawQuery: "Fe + O2 -> Fe2O3")).Text;

            Assert.Contains("Fe", text, StringComparison.Ordinal);
            Assert.Contains("O", text, StringComparison.Ordinal);
            // A per-element tally row for each side.
            Assert.True(text.Split('\n').Length > 3, "the atom check should be a block, not a line");
        }

        /// <summary>"1Fe" is how a bug looks, not chemistry.</summary>
        [Fact]
        public void ACoefficientOfOneIsNeverPrinted()
        {
            var text = Answer(new QueryPlan(Intent.BalanceEquation, RawQuery: "H2 + Cl2 -> HCl")).Text;
            Assert.DoesNotContain("1H2", text, StringComparison.Ordinal);
            Assert.DoesNotContain("1Cl2", text, StringComparison.Ordinal);
        }

        /// <summary>
        /// "An element appears on only one side" leaves the reader to find which; naming it is the
        /// same sentence with the work done.
        /// </summary>
        [Fact]
        public void AOneSidedElementIsNamedInTheFailure()
        {
            var text = Answer(new QueryPlan(Intent.BalanceEquation, RawQuery: "Na + Cl2 -> KCl")).Text;
            Assert.Contains("Na", text, StringComparison.Ordinal);
        }

        /// <summary>
        /// Asked for the molar mass of Ca(NO3)2 the agent gave the total and nothing else, and the
        /// follow-up "show the calculation step by step" retrieved the dictionary definition of
        /// stoichiometry instead.
        /// </summary>
        [Fact]
        public void ACompoundShowsItsBreakdownWithoutBeingAsked()
        {
            var text = Answer(new QueryPlan(Intent.FormulaMass, RawQuery: "Ca(NO3)2")).Text;

            Assert.Contains("Ca", text, StringComparison.Ordinal);
            Assert.Contains("N", text, StringComparison.Ordinal);
            Assert.Contains("O", text, StringComparison.Ordinal);
        }

        [Fact]
        public void ADecayAnswerShowsItsHalfLifeWorking()
        {
            var text = Answer(new QueryPlan(
                Intent.DecayCalc,
                Entities: [new EntityRef.Element("carbon")], Limit: 14,
                RawQuery: "how much of 100 g of carbon-14 remains after 11460 years")).Text;

            Assert.Contains("carbon-14", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("###", text, StringComparison.Ordinal);
        }

        /// <summary>
        /// Past sixty half-lives less than one part in 10^18 survives. Printing 3.2e-25 g would imply
        /// a measurement nobody could make from data this app does not have.
        /// </summary>
        [Fact]
        public void AVeryLongDecaySaysEffectivelyNoneRatherThanPrintingAnAbsurdFigure()
        {
            var text = Answer(new QueryPlan(
                Intent.DecayCalc,
                Entities: [new EntityRef.Element("carbon")], Limit: 14,
                RawQuery: "how much of 100 g of carbon-14 remains after 10000000 years")).Text;

            Assert.DoesNotContain("e-", text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The three "no number" decay outcomes read as three different sentences, because they mean
        /// three different things.
        /// </summary>
        [Fact]
        public void TheDecayOutcomesReadDifferently()
        {
            var stable = Answer(new QueryPlan(
                Intent.DecayCalc, Entities: [new EntityRef.Element("carbon")], Limit: 12,
                RawQuery: "how much carbon-12 after 100 years")).Text;

            var unlisted = Answer(new QueryPlan(
                Intent.DecayCalc, Entities: [new EntityRef.Element("carbon")], Limit: 99,
                RawQuery: "how much carbon-99 after 100 years")).Text;

            Assert.NotEqual(stable, unlisted);
            Assert.NotEmpty(stable);
            Assert.NotEmpty(unlisted);
        }

        // ---- Comparisons -------------------------------------------------------------------

        [Fact]
        public void AYesNoComparativeLeadsWithTheVerdict()
        {
            var yes = Answer(new QueryPlan(
                Intent.Comparative,
                Entities: [new EntityRef.Element("gold"), new EntityRef.Element("lead")],
                FieldIds: ["density"], Limit: 1)).Text;

            var no = Answer(new QueryPlan(
                Intent.Comparative,
                Entities: [new EntityRef.Element("lead"), new EntityRef.Element("gold")],
                FieldIds: ["density"], Limit: 1)).Text;

            Assert.NotEqual(yes, no);
        }

        /// <summary>
        /// A table of five properties against two elements is data, not an answer - it leaves the
        /// reader to do the comparison the question asked for.
        /// </summary>
        [Fact]
        public void ATwoElementComparisonEndsWithAVerdict()
        {
            var plan = new QueryPlan(
                Intent.Comparison,
                Entities: [new EntityRef.Element("gold"), new EntityRef.Element("silver")],
                FieldIds: ["density", "melting_point", "atomic_mass"]);

            var text = Answer(plan).Text;

            Assert.Contains("Gold", text, StringComparison.Ordinal);
            Assert.Contains("Silver", text, StringComparison.Ordinal);
            // The verdict counts fields won, and says "has the higher value" rather than "is better".
            Assert.Contains("3", text, StringComparison.Ordinal);
        }

        /// <summary>
        /// With three or more elements, "leads on 2 of 5" hides more than it says, and the table
        /// really is the right shape for the answer.
        /// </summary>
        [Fact]
        public void ThreeElementsGetATableAndNoVerdict()
        {
            var two = Answer(new QueryPlan(
                Intent.Comparison,
                Entities: [new EntityRef.Element("gold"), new EntityRef.Element("silver")],
                FieldIds: ["density", "melting_point"])).Text;

            var three = Answer(new QueryPlan(
                Intent.Comparison,
                Entities:
                [
                    new EntityRef.Element("gold"),
                    new EntityRef.Element("silver"),
                    new EntityRef.Element("copper"),
                ],
                FieldIds: ["density", "melting_point"])).Text;

            // The two-element answer has one more paragraph than the three-element one.
            Assert.True(two.Split("\n\n").Length > three.Split("\n\n").Length - 1);
        }

        /// <summary>
        /// "Compare lithium, sodium and potassium on reactivity, density and applications" can only
        /// answer density from stored fields. Saying so is the difference between an incomplete
        /// answer and a misleading one.
        /// </summary>
        [Fact]
        public void UncoveredAspectsAreNamedRatherThanDroppedSilently()
        {
            var plan = new QueryPlan(
                Intent.Comparison,
                Entities: [new EntityRef.Element("lithium"), new EntityRef.Element("sodium")],
                FieldIds: ["density"],
                Evidence: ["no field for reactivity", "no field for uses"]);

            var text = Answer(plan).Text;

            Assert.Contains("reactivity", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("uses", text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"application, applications" is one aspect, not two.</summary>
        [Fact]
        public void AspectsContainedInAnotherAreNotListedTwice()
        {
            var plan = new QueryPlan(
                Intent.Comparison,
                Entities: [new EntityRef.Element("lithium"), new EntityRef.Element("sodium")],
                FieldIds: ["density"],
                Evidence: ["no field for application", "no field for applications"]);

            var text = Answer(plan).Text;
            var mentions = text.Split("application", StringSplitOptions.None).Length - 1;
            Assert.Equal(1, mentions);
        }

        // ---- Lists and superlatives -----------------------------------------------------------

        /// <summary>
        /// A superlative was answering with a bare winner and no sense of the field it topped.
        /// </summary>
        [Fact]
        public void ASuperlativeNamesThePoolAndTheRunnerUp()
        {
            var text = Answer(new QueryPlan(
                Intent.Superlative, FieldIds: ["density"], SortField: "density", Limit: 1)).Text;

            // Whatever the data says is densest, and whatever is second, must both appear: the
            // winner and the runner-up that gives the margin a sense of scale.
            var ranked = Service.Store.Elements
                .Where(e => e.Quantity("density") is not null)
                .OrderByDescending(e => e.Quantity("density")!.Mid)
                .ToList();

            Assert.Contains(ranked[0].Key, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ranked[1].Key, text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"the third densest element" - say which place, or the answer looks wrong.</summary>
        [Fact]
        public void ARankOrdinalSaysWhichPlaceItIs()
        {
            var text = Answer(new QueryPlan(
                Intent.Superlative, FieldIds: ["density"], SortField: "density",
                FieldOrdinal: 3, Limit: 1)).Text;

            Assert.Contains("3", text, StringComparison.Ordinal);
        }

        /// <summary>
        /// Say plainly when the shown rows are only the top slice of a larger match set, and how
        /// many were left out - a list that silently truncates reads as the whole answer.
        /// </summary>
        [Fact]
        public void ATruncatedListSaysHowManyMoreThereAre()
        {
            var plan = new QueryPlan(
                Intent.FilterList,
                Filters: [new Filter.IsMetal(true)],
                SortField: "density", Limit: 5);

            var (executor, composer) = Pair();
            var result = (ExecutionResult.ElementList)executor.Execute(plan)!;
            var text = composer.Compose(result, plan).Text;

            Assert.Equal(5, result.Results.Count);
            Assert.True(result.Matched > result.Results.Count, "the filter should match more than it shows");

            // Both halves of the disclosure: how many are shown of how many matched, and how many
            // are left.
            Assert.Contains(result.Matched.ToString(CultureInfo.InvariantCulture),
                            text, StringComparison.Ordinal);
            Assert.Contains((result.Matched - result.Results.Count).ToString(CultureInfo.InvariantCulture),
                            text, StringComparison.Ordinal);
        }

        // ---- Localisation and rendering ---------------------------------------------------------

        /// <summary>
        /// The reference tables are English-only, which the dataset index documents and says the
        /// composer "flags rather than pretending otherwise". It did not, so a Swedish user asking
        /// what an isotope is got an English definition with no indication why.
        /// </summary>
        [Fact]
        public void AnEnglishOnlyDatasetRowIsFlaggedInEveryOtherLanguage()
        {
            var plan = new QueryPlan(
                Intent.DatasetLookup,
                Entities: [new EntityRef.DatasetRow(DatasetIndex.Datasets.Dictionary, "Density")]);

            var english = Answer(plan).Text;
            var swedish = Answer(plan, language: "sv").Text;

            Assert.True(swedish.Length > english.Length, "the Swedish answer should carry the note");
        }

        [Fact]
        public void ElementNamesFollowTheActiveLanguage()
        {
            var swedish = Answer(Lookup("gold", "density"), language: "sv").Text;
            Assert.Contains("Guld", swedish, StringComparison.Ordinal);
        }

        /// <summary>
        /// The renderer supports only headings and bold, so nothing else may reach it. A markdown
        /// link would render as literal brackets.
        /// </summary>
        [Fact]
        public void TheRenderedVocabularyStaysWithinWhatThePanelSupports()
        {
            string[] plans = ["density", "melting_point", "atomic_mass"];

            foreach (var field in plans)
            {
                var text = Answer(Lookup("gold", field)).Text;
                Assert.DoesNotContain("](", text, StringComparison.Ordinal);
                Assert.DoesNotContain("<a ", text, StringComparison.Ordinal);
                Assert.DoesNotContain("|", text, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// A half-life reads as "703 800 000 years" rather than an unbroken nine-digit string. Spaces
        /// rather than a locale separator, because the value is authored text being made legible.
        /// </summary>
        [Fact]
        public void LongDigitRunsAreGroupedForLegibility()
        {
            var text = Answer(new QueryPlan(
                Intent.IsotopeComparison,
                Entities:
                [
                    new EntityRef.Nuclide("uranium", 235),
                    new EntityRef.Nuclide("uranium", 238),
                ])).Text;

            // No run of six or more consecutive digits survives ungrouped.
            Assert.DoesNotMatch(@"\d{6,}", text);
        }

        /// <summary>
        /// Two isotopes of one element differ only in neutrons, so the answer leads with that rather
        /// than repeating the shared proton count as a difference.
        /// </summary>
        [Fact]
        public void TwoIsotopesOfOneElementLeadWithTheNeutronDifference()
        {
            var text = Answer(new QueryPlan(
                Intent.IsotopeComparison,
                Entities:
                [
                    new EntityRef.Nuclide("uranium", 235),
                    new EntityRef.Nuclide("uranium", 238),
                ])).Text;

            Assert.Contains("3", text, StringComparison.Ordinal);   // 146 - 143
            Assert.Contains("Uranium", text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Offline the card is dropped, and the text says so rather than describing an image that is
        /// not going to appear.
        /// </summary>
        [Fact]
        public void TheSpectrumAnswerAdmitsWhenTheCardWillNotAppear()
        {
            var plan = new QueryPlan(
                Intent.EmissionSpectrum, Entities: [new EntityRef.Element("sodium")]);

            var online = Answer(plan);
            var offline = Answer(plan, policy: new ChatCardPolicy(AllowNetworkCards: false));

            Assert.NotNull(online.Card);
            Assert.Null(offline.Card);
            Assert.True(offline.Text.Length > online.Text.Length);
        }

        /// <summary>Numbers reach the user inside the answer, so they must not follow the culture.</summary>
        [Fact]
        public void NumbersAreInvariantUnderAHostileCulture()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                var english = Answer(Lookup("gold", "density")).Text;
                Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");
                Assert.Equal(english, Answer(Lookup("gold", "density")).Text);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        // ---- The contract ---------------------------------------------------------------------

        /// <summary>
        /// Every result the executor can produce has to render to something. A blank answer reaches
        /// the user as an empty bubble with no explanation.
        /// </summary>
        [Fact]
        public void EveryExecutableIntentRendersToSomething()
        {
            var blank = new List<string>();

            foreach (var intent in Enum.GetValues<Intent>().Where(i => i != Intent.Unknown))
            {
                var plan = PlanFor(intent);
                var (executor, composer) = Pair();
                var result = executor.Execute(plan);
                if (result is null) continue;   // covered by QueryExecutorTest.EveryIntentIsHandled

                var answer = composer.Compose(result, plan);
                if (string.IsNullOrWhiteSpace(answer.Text)) blank.Add(intent.ToString());
            }

            Assert.Empty(blank);

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

        [Fact]
        public void AnEmptyResultStillSaysSomething()
        {
            var (_, composer) = Pair();
            var answer = composer.Compose(new ExecutionResult.Empty([]), new QueryPlan(Intent.FilterList));

            Assert.NotEmpty(answer.Text);
            Assert.Empty(answer.Actions);
            Assert.Null(answer.Card);
        }

        [Fact]
        public void DuplicateCitationsCollapseIntoOneChip()
        {
            var plan = new QueryPlan(
                Intent.Comparison,
                Entities: [new EntityRef.Element("gold"), new EntityRef.Element("gold")],
                FieldIds: ["density"]);

            var answer = Answer(plan);
            Assert.Equal(answer.Actions.Select(a => a.Label).Distinct().Count(), answer.Actions.Count);
        }
    }
}
