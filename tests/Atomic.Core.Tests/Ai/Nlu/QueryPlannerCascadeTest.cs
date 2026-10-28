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
    /// The planning cascade.
    ///
    /// <para>Every case here is an <i>ordering</i> case: the query it names matches a later branch
    /// as well, and the later branch would answer a different question. Reordering the cascade does
    /// not fail loudly - it makes an intent unreachable - so these are the tripwires.</para>
    /// </summary>
    public class QueryPlannerCascadeTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static QueryPlanner Planner(string language = "en") => new(
            Service.Store,
            Service.Datasets,
            Service.Resolver(language),
            new FieldResolver(new ReswStringProvider(language, fallBackToEnglish: true)),
            Service.Retriever(language));

        private static QueryPlan Plan(string query, DialogueState? state = null) =>
            Planner().Plan(query, state ?? new DialogueState());

        // ---- The order itself ---------------------------------------------------------------

        /// <summary>
        /// The declared order is documentation with teeth: a future Android sync that moves a guard
        /// has to change this list, which makes the change visible in review rather than silent in
        /// behaviour.
        /// </summary>
        [Fact]
        public void TheCascadeOrderIsDeclaredAndUnique()
        {
            Assert.Equal(QueryPlanner.CascadeOrder.Count,
                         QueryPlanner.CascadeOrder.Distinct(StringComparer.Ordinal).Count());

            // The four early declines come first, and the dataset fallback is last - those two ends
            // are what the whole shape rests on.
            Assert.Equal("defer-to-personality", QueryPlanner.CascadeOrder[0]);
            Assert.Equal("calculation", QueryPlanner.CascadeOrder[4]);
            Assert.Equal("dataset-row", QueryPlanner.CascadeOrder[^1]);
        }

        // ---- Early declines -----------------------------------------------------------------

        /// <summary>
        /// "quiz me" scored 0.70 against the constants table and was answered with the mass of an
        /// electron. Retrieval cannot tell that a query is not a question.
        /// </summary>
        [Theory]
        [InlineData("hello there")]
        [InlineData("quiz me")]
        [InlineData("thanks")]
        public void ConversationIsDeclinedBeforeRetrievalCanAnswerIt(string query) =>
            Assert.Equal(Intent.Unknown, Plan(query).Intent);

        /// <summary>
        /// "tell me about element 119" produced "Electron mass". An element number past 118 has a
        /// definite answer - there is no such element - and the worst thing to do is guess.
        /// </summary>
        [Fact]
        public void AnElementNumberPastTheTableIsDeclinedRatherThanGuessed() =>
            Assert.Equal(Intent.Unknown, Plan("tell me about element 119").Intent);

        /// <summary>
        /// "समान" is Hindi for "similar" and also opens "the same group", so the unbacked-concept
        /// guard declined every Hindi structural question until it was exempted.
        /// </summary>
        [Fact]
        public void TheSameGroupExemptionSurvivesTheUnbackedConceptGuard()
        {
            var plan = Plan("which elements are in the same group as carbon");
            Assert.Equal(Intent.FilterList, plan.Intent);
            // The set read off the named element (roadmap 1.7).
            Assert.Contains(plan.Evidence, e => e.StartsWith("group of carbon", StringComparison.Ordinal));
        }

        /// <summary>
        /// An equation carries a reaction word, and the unbacked-concept guard declines every
        /// sentence that does - but the arrow is stronger evidence than the noun beside it.
        /// </summary>
        [Fact]
        public void AnArrowBeatsTheUnbackedConceptGuard() =>
            Assert.Equal(Intent.BalanceEquation,
                         Plan("balance this reaction: Fe + O2 -> Fe2O3").Intent);

        /// <summary>
        /// Chinese asks for every statistic with 多少, so a median question matched the count word,
        /// the word "element" and no subset, and came back as the size of the periodic table.
        /// </summary>
        [Fact]
        public void CountOfAllElementsRequiresTheAggregationToActuallyBeACount()
        {
            var count = Plan("how many elements are there");
            Assert.Equal(Intent.Aggregate, count.Intent);
            Assert.Equal(Aggregation.Count, count.Aggregation);

            var median = Planner("zh").Plan("镧系元素的中位原子量是多少", new DialogueState());
            Assert.NotEqual(Aggregation.Count, median.Aggregation);
        }

        // ---- Narrative and etymology ---------------------------------------------------------

        /// <summary>
        /// The worst defect reported: the query names an element, resolves no field, and
        /// inheritance hands it the previous turn's field. "Vad används titan till" was answered
        /// with titanium's atomic number that way.
        /// </summary>
        [Fact]
        public void ANarrativeQuestionIsDeclinedRatherThanInheritingAField()
        {
            var state = new DialogueState();
            state.NoteAnswer(new QueryPlan(Intent.PropertyLookup, FieldIds: ["atomic_number"]), ["gold"]);

            var plan = Planner("sv").Plan("vad anvands titan till", state);
            Assert.Equal(Intent.Unknown, plan.Intent);
        }

        /// <summary>
        /// Etymology declines even with a field in hand: "where does that symbol come from"
        /// resolves `symbol`, and answering with a value mistakes what the sentence is about.
        /// </summary>
        [Fact]
        public void EtymologyDeclinesEvenWhenAFieldResolves() =>
            Assert.Equal(Intent.Unknown, Plan("where does the symbol for gold come from").Intent);

        // ---- Explanations ---------------------------------------------------------------------

        /// <summary>
        /// "why does radius decrease left to right, despite adding electrons" came back
        /// "Potassium's Electrons is 19". Saying nothing is a far better answer than saying that.
        /// </summary>
        [Fact]
        public void AMechanismQuestionNeverAnswersWithAFieldItMentionedInPassing()
        {
            var plan = Plan("why does atomic radius decrease left to right despite adding electrons");
            Assert.NotEqual(Intent.PropertyLookup, plan.Intent);
        }

        /// <summary>
        /// "What is the average electronegativity of the halogens" opens with a definition frame and
        /// names a topic the dictionary has, so it was answered with the definition - the word
        /// "average" was never reached, because the explanation check runs before aggregation.
        /// </summary>
        [Fact]
        public void AnOperatorDisqualifiesTheDefinitionalReading()
        {
            var plan = Plan("what is the average electronegativity of the halogens");
            Assert.Equal(Intent.Aggregate, plan.Intent);
            Assert.Equal(Aggregation.Avg, plan.Aggregation);
        }

        /// <summary>
        /// A subset on its own is deliberately not disqualifying: "what is a halogen" names a family
        /// and wants the definition of it, not a list of its members.
        /// </summary>
        [Fact]
        public void ASubsetAloneDoesNotDisqualifyTheDefinition() =>
            Assert.Equal(Intent.DatasetLookup, Plan("what is a halogen").Intent);

        // ---- Inheritance ----------------------------------------------------------------------

        [Fact]
        public void AFollowUpInheritsTheSubjectItDoesNotName()
        {
            var state = new DialogueState();
            state.NoteAnswer(new QueryPlan(Intent.PropertyLookup, FieldIds: ["density"]), ["gold"]);

            var plan = Plan("and its melting point?", state);

            Assert.Equal(Intent.PropertyLookup, plan.Intent);
            Assert.Equal(["gold"], plan.ElementKeys);
            Assert.Equal(["melting_point"], plan.FieldIds);
        }

        [Fact]
        public void AFollowUpInheritsTheFieldItDoesNotName()
        {
            var state = new DialogueState();
            state.NoteAnswer(new QueryPlan(Intent.PropertyLookup, FieldIds: ["density"]), ["gold"]);

            var plan = Plan("what about silver", state);

            Assert.Equal(["silver"], plan.ElementKeys);
            Assert.Equal(["density"], plan.FieldIds);
        }

        /// <summary>
        /// Without the ranges-over-table guard, "which element has the highest melting point" asked
        /// three turns into a thread about tungsten inherits tungsten and collapses into a plain
        /// property lookup - the opposite failure to a stale field, and just as wrong.
        /// </summary>
        [Fact]
        public void AQuestionRangingOverTheTableDoesNotInheritTheThreadsSubject()
        {
            var state = new DialogueState();
            state.NoteAnswer(new QueryPlan(Intent.PropertyLookup, FieldIds: ["melting_point"]), ["tungsten"]);

            var plan = Plan("which element has the highest melting point", state);

            Assert.Equal(Intent.Superlative, plan.Intent);
            Assert.Empty(plan.ElementKeys);
        }

        /// <summary>
        /// "What's the weather today" is short enough to read as a follow-up, and then inherited
        /// iron and answered "Iron's Electrical Type is Conductor".
        /// </summary>
        [Fact]
        public void AShortQueryAboutSomethingElseDoesNotInheritTheSubject()
        {
            var state = new DialogueState();
            state.NoteAnswer(new QueryPlan(Intent.PropertyLookup, FieldIds: ["electrical_type"]), ["iron"]);

            var plan = Plan("what is the weather today", state);
            Assert.NotEqual(Intent.PropertyLookup, plan.Intent);
        }

        /// <summary>
        /// "And their melting points" after comparing gold and silver is a two-element question;
        /// taking only the focus answered half of it with no sign that half was dropped.
        /// </summary>
        [Fact]
        public void APluralAnaphorInheritsTheWholeSetNotJustTheFocus()
        {
            var state = new DialogueState();
            state.NoteAnswer(new QueryPlan(Intent.Comparison, FieldIds: ["density"]), ["gold", "silver"]);

            var plan = Plan("and their melting points", state);
            Assert.Equal(2, plan.ElementKeys.Count);
        }

        // ---- Comparatives ---------------------------------------------------------------------

        [Fact]
        public void TwoElementsAndADegreeWordCompareThosePairRatherThanRankingTheTable()
        {
            var plan = Plan("is gold denser than lead");

            Assert.Equal(Intent.Comparative, plan.Intent);
            Assert.Equal(["gold", "lead"], plan.ElementKeys);
        }

        /// <summary>
        /// "l'oro è più denso del piombo" uses the same word for comparative and superlative, so
        /// without the periphrastic branch above the superlative one it ranked the whole table.
        /// </summary>
        [Fact]
        public void APeriphrasticComparativeIsNotReadAsASuperlative()
        {
            var plan = Planner("it").Plan("l oro e piu denso del piombo", new DialogueState());
            Assert.NotEqual(Intent.Superlative, plan.Intent);
        }

        /// <summary>
        /// "which is denser" names neither element; both come from the previous turn, which is the
        /// only place they exist.
        /// </summary>
        [Fact]
        public void AnEllipticalComparativeTakesBothSubjectsFromTheThread()
        {
            var state = new DialogueState();
            state.NoteAnswer(new QueryPlan(Intent.Comparison, FieldIds: ["density"]), ["gold", "silver"]);

            var plan = Plan("which is denser", state);

            Assert.Equal(Intent.Comparative, plan.Intent);
            Assert.Equal(2, plan.ElementKeys.Count);
        }

        /// <summary>
        /// Naming the word "element" is what says the question ranges over the table rather than
        /// over the pair. Without that half, three explicit superlatives collapsed into comparisons.
        /// </summary>
        [Fact]
        public void NamingTheWordElementKeepsASuperlativeASuperlative()
        {
            var state = new DialogueState();
            state.NoteAnswer(new QueryPlan(Intent.Comparison, FieldIds: ["density"]), ["gold", "silver"]);

            var plan = Plan("which element is the densest", state);
            Assert.Equal(Intent.Superlative, plan.Intent);
        }

        /// <summary>"what are its isotopes" was being answered as an elliptical comparison.</summary>
        [Fact]
        public void AnIsotopeQuestionIsNotAComparisonHoweverElliptical()
        {
            var state = new DialogueState();
            state.NoteAnswer(new QueryPlan(Intent.Comparison, FieldIds: ["density"]), ["gold", "silver"]);

            var plan = Plan("what are its isotopes", state);
            Assert.Equal(Intent.Isotopes, plan.Intent);
        }

        // ---- Lists, superlatives and thresholds ------------------------------------------------

        [Fact]
        public void AMultiHopQuestionBecomesASubsetPlusAThreshold()
        {
            var plan = Plan("which transition metals melt above 2000 c");

            Assert.Equal(Intent.FilterList, plan.Intent);
            Assert.Contains(plan.Filters, f => f is Filter.InSeries);
            Assert.Contains(plan.Filters, f => f is Filter.FieldCompare { Op: Op.Gt });
        }

        /// <summary>
        /// "which metals are lighter than aluminium" is a subset question that also carries a
        /// threshold; dropping the threshold here would list all metals.
        /// </summary>
        [Fact]
        public void ASubsetListKeepsItsThreshold()
        {
            var plan = Plan("which metals are lighter than aluminium");

            Assert.Equal(Intent.FilterList, plan.Intent);
            Assert.Contains(plan.Filters, f => f is Filter.FieldCompare);
        }

        /// <summary>
        /// "which element has the lowest X" is a superlative, not a list, even though it opens with
        /// "which".
        /// </summary>
        [Fact]
        public void AWhichQuestionWithASuperlativeAsksForOneRow()
        {
            var plan = Plan("which element has the lowest melting point");

            Assert.Equal(Intent.Superlative, plan.Intent);
            Assert.Equal(1, plan.Limit);
            Assert.False(plan.SortDescending);
        }

        [Fact]
        public void ARankOrdinalAsksForOneRowFurtherDown()
        {
            var plan = Plan("the third densest element");

            Assert.Equal(Intent.Superlative, plan.Intent);
            Assert.Equal(3, plan.FieldOrdinal);
            Assert.Equal(1, plan.Limit);
        }

        /// <summary>A bare family name is the request as well as the subject.</summary>
        [Fact]
        public void ABareFamilyNameIsARequestForItsMembers()
        {
            var plan = Plan("the noble gases");

            Assert.Equal(Intent.FilterList, plan.Intent);
            Assert.Contains(plan.Filters, f => f is Filter.InSeries);
        }

        // ---- Element-shaped intents -------------------------------------------------------------

        [Fact]
        public void NeighbourQuestionsResolveBothEnds()
        {
            var plan = Plan("what comes after carbon");

            Assert.Equal(Intent.Neighbour, plan.Intent);
            Assert.Equal(["carbon", "nitrogen"], plan.ElementKeys);
            Assert.Equal(1, plan.Limit);
        }

        /// <summary>
        /// "what does the emission spectrum of iron look like" matched `appearance` and answered
        /// with the metal's colour, so the spectrum check has to run before field resolution.
        /// </summary>
        [Fact]
        public void EmissionSpectrumBeatsTheAppearanceField() =>
            Assert.Equal(Intent.EmissionSpectrum,
                         Plan("what does the emission spectrum of iron look like").Intent);

        [Fact]
        public void IsotopesAndSafetyBeatTheFieldsTheirWordsResolve()
        {
            Assert.Equal(Intent.Isotopes, Plan("what are the isotopes of carbon").Intent);
            Assert.Equal(Intent.Safety, Plan("is mercury dangerous").Intent);
        }

        /// <summary>
        /// An alloy is not an element, so a side-by-side property table is not available - unless
        /// the query names an element and a field, in which case it is an ordinary lookup.
        /// </summary>
        [Fact]
        public void AnAlloyQuestionGoesToTheAlloyTableUnlessAFieldIsNamed()
        {
            Assert.Equal(Intent.DatasetLookup, Plan("what is the difference between iron and steel").Intent);
            Assert.Equal(Intent.PropertyLookup, Plan("what is the density of iron").Intent);
        }

        /// <summary>
        /// A specific field wins over a family word: "thermal conductivity" contains "thermal", and
        /// because the category branch sits above property lookup, asking for one figure returned
        /// the whole family instead.
        /// </summary>
        [Fact]
        public void ASpecificFieldBeatsTheCategoryItsNameContains()
        {
            Assert.Equal(Intent.PropertyLookup, Plan("thermal conductivity of gold").Intent);
            Assert.Equal(Intent.CategoryLookup, Plan("the thermal properties of gold").Intent);
        }

        /// <summary>
        /// Abundance is the exception: all nine fields carry "abundance" in their labels, so it
        /// keeps the family reading unless the query narrows to one reservoir.
        /// </summary>
        [Fact]
        public void AbundanceKeepsItsFamilyReadingUnlessAReservoirIsNamed()
        {
            Assert.Equal(Intent.CategoryLookup, Plan("what is the abundance of gold").Intent);
            Assert.Equal(Intent.PropertyLookup, Plan("how much gold is in sea water").Intent);
        }

        /// <summary>
        /// A bare "tell me about gold" is narrative and belongs to the personality layer - but only
        /// when no field was actually named, because "what is the density of gold" opens the same way.
        /// </summary>
        [Fact]
        public void AnOverviewRequestIsNotAPropertyLookup()
        {
            Assert.NotEqual(Intent.PropertyLookup, Plan("tell me about gold").Intent);
            Assert.Equal(Intent.PropertyLookup, Plan("what is the density of gold").Intent);
        }

        // ---- Dataset fallback --------------------------------------------------------------------

        /// <summary>
        /// "sure" retrieved sulfur and "hey" retrieved a dictionary row, both above the fallback's
        /// confidence floor. Anything that short and unrecognised is conversational.
        /// </summary>
        [Theory]
        [InlineData("sure")]
        [InlineData("hey")]
        [InlineData("42")]
        public void AVeryShortQueryNeverReachesTheDatasetFallback(string query) =>
            Assert.Equal(Intent.Unknown, Plan(query).Intent);

        /// <summary>
        /// Naming a table topic is evidence of the same kind an element is, pointing the other way:
        /// only the electrode table holds standard potentials.
        /// </summary>
        [Fact]
        public void ATableTopicLowersTheBarEvenWhenAnElementIsNamed()
        {
            var plan = Plan("standard electrode potential of zinc");
            Assert.Equal(Intent.DatasetLookup, plan.Intent);
        }

        /// <summary>
        /// The reference tables are English and BM25 is lexical, so a Swedish query matched nothing
        /// until the search string was glossed.
        /// </summary>
        [Fact]
        public void ADatasetSearchIsGlossedIntoEnglish()
        {
            var plan = Planner("sv").Plan("vad ar stal gjort av", new DialogueState());
            Assert.Equal(Intent.DatasetLookup, plan.Intent);
        }

        /// <summary>
        /// The pair of tests that separates a real question from chatter: unknown words appear in
        /// perfectly good questions, so absence alone is not enough - the retrieved row also has to
        /// be about what was asked.
        /// </summary>
        [Fact]
        public void OffTopicChatterIsDeclinedWhileARealDefinitionIsAnswered()
        {
            Assert.Equal(Intent.Unknown, Plan("write me a poem about the sea").Intent);
            Assert.Equal(Intent.DatasetLookup, Plan("define molar mass").Intent);
        }

        // ---- The contract ------------------------------------------------------------------------

        /// <summary>
        /// The planner declines by default. A query it does not claim behaves exactly as it did
        /// before, which is what makes it safe alongside the existing router.
        /// </summary>
        [Fact]
        public void EveryDeclinedPlanCarriesZeroConfidenceAndTheOriginalQuery()
        {
            var plan = Plan("write me a poem about the sea");

            Assert.Equal(Intent.Unknown, plan.Intent);
            Assert.Equal(0.0, plan.Confidence);
            Assert.Equal("write me a poem about the sea", plan.RawQuery);
            Assert.True(plan.Confidence < Planner().Threshold);
        }

        [Fact]
        public void EveryClaimedPlanClearsTheThreshold()
        {
            string[] claimed =
            [
                "what is the density of gold",
                "which transition metals melt above 2000 c",
                "is gold denser than lead",
                "what are the isotopes of carbon",
                "molar mass of H2SO4",
                "how many elements are there",
            ];

            foreach (var query in claimed)
            {
                var plan = Plan(query);
                Assert.NotEqual(Intent.Unknown, plan.Intent);
                Assert.True(plan.Confidence >= Planner().Threshold,
                            $"{query} planned {plan.Intent} at {plan.Confidence}");
            }
        }
    }
}
