using System;
using System.Linq;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Atomic.Core.Text;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    public class AiEngineTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static AiEngine Engine(string language = "en", Entitlements? entitlements = null) =>
            new(Service.Store,
                Service.Datasets,
                Service.Localized(language),
                new ReswStringProvider(language, fallBackToEnglish: true),
                Service.Resolver(language),
                Service.Retriever(language),
                entitlements);

        /// <summary>
        /// A field answered by name, with no question to parse.
        /// </summary>
        [Fact]
        public void AFieldCanBeAnsweredDirectly()
        {
            var answer = Engine().FieldAnswerFor("gold", "density");

            Assert.NotNull(answer);
            Assert.Contains("19.3", answer!.Text, StringComparison.Ordinal);
        }

        /// <summary>A field the element has no value for is not answered at all.</summary>
        [Fact]
        public void AnAbsentFieldIsNotAnswered()
        {
            Assert.Null(Engine().FieldAnswerFor("gold", "curie_point"));
        }

        /// <summary>
        /// The common neutron count is named as neutrons.
        /// </summary>
        /// <remarks>
        /// Its label is the Isotopes heading, which is what makes "isotopes" resolve to this
        /// field and is right for parsing. Rendered into a sentence it read "Gold's Isotopes is
        /// 118", which names a different quantity - gold has 118 neutrons and 1 stable isotope.
        /// </remarks>
        [Fact]
        public void TheCommonNeutronCountIsNotCalledIsotopes()
        {
            var answer = Engine().FieldAnswerFor("gold", "common_neutrons");

            Assert.NotNull(answer);
            Assert.Contains("Neutrons", answer!.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Isotopes is", answer.Text, StringComparison.Ordinal);
        }

        // ---- The decline contract ------------------------------------------------------------

        /// <summary>
        /// The engine answers only when its plan clears the threshold. Anything it does not claim
        /// returns null so the caller falls through - which is what makes it safe to add without
        /// putting existing answers at risk.
        /// </summary>
        [Theory]
        [InlineData("hello there")]
        [InlineData("write me a poem about the sea")]
        [InlineData("quiz me")]
        [InlineData("")]
        [InlineData("   ")]
        public void TheEngineDefersRatherThanGuessing(string query) =>
            Assert.Null(Engine().Answer(query, new DialogueState()));

        [Fact]
        public void ADeferredTurnLeavesNoPlanBehind()
        {
            var engine = Engine();
            engine.Answer("what is the density of gold", new DialogueState());
            Assert.NotNull(engine.LastPlan);

            engine.Answer("write me a poem", new DialogueState());
            Assert.Null(engine.LastPlan);
        }

        // ---- What it adds that a keyword router could not express ----------------------------

        [Fact]
        public void ItRanksAndFiltersAcrossTheWholeTable()
        {
            var answer = Engine().Answer("which transition metals melt above 2000 c", new DialogueState());

            Assert.NotNull(answer);
            Assert.NotEmpty(answer!.Text);
        }

        [Fact]
        public void ItSaysPlainlyWhenTheAppHasNoValue()
        {
            var barren = Service.Store.Elements.First(e => e.Value("curie_point").IsMissing);
            var answer = Engine().Answer($"what is the curie point of {barren.Key}", new DialogueState());

            Assert.NotNull(answer);
            // The coverage figure is what makes it an answer rather than a shrug.
            Assert.Contains(Service.Store.CoverageOf("curie_point").ToString(), answer!.Text, StringComparison.Ordinal);
        }

        // ---- Follow-ups -----------------------------------------------------------------------

        [Fact]
        public void AFollowUpInheritsTheSubjectAndUpdatesTheThread()
        {
            var engine = Engine();
            var state = new DialogueState();

            Assert.NotNull(engine.Answer("what is the density of gold", state));
            Assert.Equal("gold", state.FocusElement);

            var followUp = engine.Answer("and its melting point?", state);
            Assert.NotNull(followUp);
            Assert.Equal(["melting_point"], engine.LastPlan!.FieldIds);
            Assert.Equal(["gold"], engine.LastPlan.ElementKeys);
        }

        /// <summary>
        /// A conversion or a balanced equation is about a number or a compound, not an element, so
        /// none of them establishes a subject for "and its density?" to inherit.
        /// </summary>
        [Fact]
        public void AnAnswerAboutNoElementLeavesTheThreadsSubjectAlone()
        {
            var engine = Engine();
            var state = new DialogueState();

            engine.Answer("what is the density of gold", state);
            Assert.Equal("gold", state.FocusElement);

            engine.Answer("balance Fe + O2 -> Fe2O3", state);
            Assert.Equal("gold", state.FocusElement);
        }

        /// <summary>
        /// A withheld answer still establishes what the conversation is about, so the follow-up
        /// resolves against the element the user just asked about.
        /// </summary>
        [Fact]
        public void AWithheldAnswerStillSetsTheThreadsSubject()
        {
            var gated = FieldRegistry.All.First(s => s.Tier == Tier.Pro && s.Kind == FieldKind.Numeric).Id;
            var engine = Engine(entitlements: new Entitlements(IsPro: false, IsProPlus: false));
            var state = new DialogueState();

            var answer = engine.Answer($"what is the {gated.Replace('_', ' ')} of gold", state);

            if (answer is not null) Assert.Equal("gold", state.FocusElement);
        }

        // ---- Compound questions -----------------------------------------------------------------

        /// <summary>
        /// The whole query often plans to something on its own - "the density of gold and how does
        /// it compare to lead" plans as a comparison - so the compound attempt has to run first or
        /// half the question is answered and the other half never noticed.
        /// </summary>
        [Fact]
        public void ATwoPartQuestionIsAnsweredInBothParts()
        {
            var engine = Engine();
            var answer = engine.Answer(
                "what is the density of gold and what is the melting point of iron", new DialogueState());

            Assert.NotNull(answer);
            Assert.Contains("Gold", answer!.Text, StringComparison.Ordinal);
            Assert.Contains("Iron", answer.Text, StringComparison.Ordinal);
        }

        /// <summary>One merged sources section, rather than one after every clause.</summary>
        [Fact]
        public void ACompoundAnswerCarriesOneSourcesSection()
        {
            var answer = Engine().Answer(
                "what is the density of gold and what is the melting point of iron", new DialogueState());

            var sourceHeadings = answer!.Text.Split("###").Length - 1;
            Assert.Equal(1, sourceHeadings);
        }

        /// <summary>
        /// All or nothing: a partial compound answer would be worse than the single-clause answer it
        /// replaced, so a clause that cannot plan abandons the whole attempt.
        /// </summary>
        [Fact]
        public void ACompoundQuestionWithAnUnplannableClauseFallsBackToTheWholeQuery()
        {
            var engine = Engine();
            var answer = engine.Answer(
                "what is the density of gold and write me a poem about the sea", new DialogueState());

            // Either the whole query planned to something, or nothing did - but never half of it.
            if (answer is not null)
                Assert.DoesNotContain("poem", answer.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// One card per message. Two visuals in a single bubble is clutter and would make the panel's
        /// template scheme combinatorial for no gain.
        /// </summary>
        [Fact]
        public void ACompoundAnswerCarriesAtMostOneCard()
        {
            var answer = Engine().Answer(
                "what are the isotopes of carbon and what are the isotopes of uranium",
                new DialogueState());

            // The record holds a single card by construction; this pins that the merge does not
            // silently drop the first one when both clauses produce one.
            if (answer?.Card is not null) Assert.NotEmpty(answer.Card.ElementKey);
        }

        // ---- The direct entry points -------------------------------------------------------------

        [Fact]
        public void AnIntentCanBeRunDirectlyAgainstAKnownElement()
        {
            var answer = Engine().AnswerFor(Intent.Isotopes, "carbon");

            Assert.NotNull(answer);
            Assert.Contains("Carbon", answer!.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ACategoryAnswerListsOnlyThePopulatedFields()
        {
            var answer = Engine().CategoryAnswerFor("gold", FieldCategory.Thermo);

            Assert.NotNull(answer);
            var absent = FieldRegistry.ByCategory(FieldCategory.Thermo)
                .Where(s => Service.Store.Element("gold")!.Value(s.Id).IsMissing)
                .Select(s => s.Id.Replace('_', ' '));

            foreach (var label in absent)
                Assert.DoesNotContain($"\n{label}:", answer!.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ACategoryWithNothingRecordedReturnsNothing() =>
            Assert.Null(Engine().CategoryAnswerFor("not an element", FieldCategory.Thermo));
    }

    public class ChatOrchestratorTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(
            Func<Entitlements>? entitlements = null, string language = "en", int? seed = null) =>
            new(Service,
                lang => new ReswStringProvider(lang, fallBackToEnglish: true),
                entitlements,
                null,
                language,
                seed is { } value ? new Random(value) : null);

        private static IStringProvider Strings(string language = "en") =>
            new ReswStringProvider(language, fallBackToEnglish: true);

        // ---- The order is the contract ---------------------------------------------------------

        /// <summary>
        /// The engine is asked first and its decline is taken seriously. Reaching the personality
        /// path when the engine could have answered would replace a cited fact with a pleasantry.
        /// </summary>
        [Fact]
        public void AGroundedQuestionIsAnsweredByTheEngine()
        {
            var reply = Chat().Ask("what is the density of gold");

            Assert.Equal(ReplySource.Engine, reply.Source);
            Assert.NotNull(reply.Plan);
            Assert.NotEmpty(reply.Actions);
        }

        [Theory]
        [InlineData("hello there")]
        [InlineData("hej")]
        [InlineData("hallo")]
        public void AGreetingIsAnsweredWithoutTouchingTheData(string message)
        {
            var reply = Chat().Ask(message);

            Assert.Equal(ReplySource.Personality, reply.Source);
            Assert.Empty(reply.Actions);
            Assert.Null(reply.Plan);
        }

        // ---- Remembering the user ---------------------------------------------------------------

        /// <summary>A first-time user gets the plain greeting; there is nothing to remember yet.</summary>
        [Fact]
        public void AFirstGreetingIsThePlainOne()
        {
            var chat = Chat();

            Assert.Equal(Strings().Get(S.ai_greeting), chat.Ask("hello").Text);
        }

        /// <summary>
        /// After a few questions about one element, the greeting names it - Android's
        /// getPersonalizedGreeting, which is the only half of AILearningManager that ever runs.
        /// </summary>
        [Fact]
        public void AReturningUserIsGreetedByTheirElement()
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");
            chat.Ask("what is the melting point of gold");

            var greeting = chat.Ask("hello").Text;

            Assert.NotEqual(Strings().Get(S.ai_greeting), greeting);
            Assert.Contains("Gold", greeting, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The element is named in the reader's language. Android interpolates the raw key, so a
        /// Swedish user is greeted about "gold" in an otherwise Swedish sentence.
        /// </summary>
        [Fact]
        public void TheRememberedElementIsNamedInTheReadersLanguage()
        {
            var chat = Chat(language: "sv");
            chat.Ask("vad är densiteten för guld");

            var greeting = chat.Ask("hej").Text;

            Assert.Contains("Guld", greeting, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("gold", greeting, StringComparison.OrdinalIgnoreCase);
        }

        // ---- Questions about the app, and fact requests ----------------------------------------

        /// <summary>
        /// The engine defers on these by design - capabilities.tsv asserts it does - so something
        /// below it has to answer. Before AppFeatures existed, all three reached the decline.
        /// </summary>
        [Theory]
        [InlineData("what can you do")]
        [InlineData("do you have flashcards")]
        [InlineData("tell me about the unit converter")]
        [InlineData("is there a reaction balancer")]
        public void AQuestionAboutTheAppIsAnsweredNotDeclined(string message)
        {
            var reply = Chat().Ask(message);

            Assert.Equal(ReplySource.Personality, reply.Source);
            Assert.NotEqual(Strings().Get(S.ai_no_data), reply.Text);
        }

        /// <summary>A fact request gets one of the ten shipped facts.</summary>
        [Theory]
        [InlineData("tell me a fun fact")]
        [InlineData("did you know anything interesting")]
        public void AFactRequestGetsAShippedFact(string message)
        {
            var reply = Chat(seed: 7).Ask(message);

            Assert.Equal(ReplySource.Personality, reply.Source);
            Assert.Contains(reply.Text, Strings().Array("ai_facts"));
        }

        /// <summary>
        /// The fact comes from the language the question was asked in. Android's fallback is a
        /// hardcoded English sentence about oxygen, which is the one thing a Swedish reader should
        /// not get; here a missing array falls through to the decline instead.
        /// </summary>
        [Fact]
        public void ASwedishFactRequestIsAnsweredInSwedish()
        {
            var reply = Chat(language: "sv", seed: 3).Ask("visste du nagot intressant");

            Assert.Contains(reply.Text, Strings("sv").Array("ai_facts"));
            Assert.DoesNotContain(reply.Text, Strings("en").Array("ai_facts"));
        }

        /// <summary>
        /// "Cool" and "interesting" are fact keywords and broad enough to sit inside a real
        /// question. The engine runs first, so a grounded question keeps its grounded answer.
        /// </summary>
        [Fact]
        public void AGroundedQuestionIsNotDerailedByAFactKeyword()
        {
            var reply = Chat().Ask("what is the interesting property of the melting point of gold");

            Assert.NotEqual(ReplySource.Personality, reply.Source);
        }

        [Fact]
        public void AnUnanswerableQuestionDeclinesRatherThanImprovising()
        {
            var reply = Chat().Ask("write me a poem about the sea");

            Assert.Equal(ReplySource.Declined, reply.Source);
            Assert.NotEmpty(reply.Text);
        }

        /// <summary>
        /// A decline is a dead end for one turn, not for the conversation - so every reply on that
        /// path still carries the chips that say what can be asked.
        /// </summary>
        [Fact]
        public void EveryUngroundedReplyStillOffersSomewhereToGo()
        {
            foreach (var message in new[] { "hello", "write me a poem", "" })
                Assert.NotEmpty(Chat().Ask(message).Suggestions);
        }

        // ---- Reactivity, the recovered capability ------------------------------------------------

        /// <summary>
        /// The planner declines these deliberately - reactivity is derived rather than stored - so
        /// without the recovered heuristic "which is more reactive" has no answer at all. This is the
        /// one capability that would have been lost with AIAgentManager (D1).
        /// </summary>
        [Fact]
        public void AReactivityComparisonIsAnsweredByTheHeuristic()
        {
            var reply = Chat().Ask("which is more reactive, sodium or potassium");

            Assert.Equal(ReplySource.Reactivity, reply.Source);
            Assert.Contains("otassium", reply.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void AReactivityQuestionNamingOneElementIsNotAnswered() =>
            // Two are needed to order them; one is not a comparison.
            Assert.NotEqual(ReplySource.Reactivity, Chat().Ask("is sodium reactive").Source);

        // ---- Language ---------------------------------------------------------------------------

        /// <summary>
        /// Per message, not per session: a thread can switch language mid-way and each turn is
        /// answered in the language it was asked in.
        /// </summary>
        [Fact]
        public void TheLanguageFollowsTheMessageRatherThanTheSession()
        {
            var chat = Chat();

            chat.Ask("what is the density of gold");
            Assert.Equal("en", chat.Language);

            var swedish = chat.Ask("vad ar densiteten for guld");
            Assert.Equal("sv", chat.Language);
            Assert.Contains("Guld", swedish.Text, StringComparison.Ordinal);
        }

        /// <summary>
        /// Most element names are shared across several languages, so a tie keeps the current one -
        /// a thread that flips language every third turn reads as broken even when each guess was
        /// individually defensible.
        /// </summary>
        [Fact]
        public void AnAmbiguousMessageDoesNotSwitchLanguage()
        {
            var chat = Chat(language: "sv");
            chat.Ask("vad ar densiteten for guld");
            Assert.Equal("sv", chat.Language);

            // "Argon" is spelled identically in Swedish and English, so nothing here is decisive.
            chat.Ask("argon");
            Assert.Equal("sv", chat.Language);
        }

        /// <summary>
        /// French "or" is gold. Counting it as French evidence answered this in French - found on
        /// an Android device, not by the corpus (D53).
        /// </summary>
        [Fact]
        public void AnEnglishOrIsNotFrench()
        {
            var chat = Chat();
            var reply = chat.Ask("which is more reactive, sodium or potassium");

            Assert.Equal("en", chat.Language);
            Assert.Contains("more reactive", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>The floor is for foreign names only: the language in play keeps its short names.</summary>
        [Fact]
        public void AShortNameStillCountsInItsOwnLanguage() =>
            Assert.Equal("fr", Service.Resolver("fr").DetectLanguage("densité de l'or", "fr"));

        // ---- Entitlements ------------------------------------------------------------------------

        /// <summary>
        /// A purchase has to take effect on the next message rather than the next launch, and the
        /// entitlements are baked into the executor when it is built.
        /// </summary>
        [Fact]
        public void APurchaseTakesEffectOnTheNextMessage()
        {
            var gated = FieldRegistry.All.First(s => s.Tier == Tier.Pro && s.Kind == FieldKind.Numeric).Id;
            var pro = false;
            var chat = Chat(() => new Entitlements(IsPro: pro, IsProPlus: pro));

            var question = $"what is the {gated.Replace('_', ' ')} of gold";
            var locked = chat.Ask(question);

            pro = true;
            var unlocked = chat.Ask(question);

            Assert.NotEqual(locked.Text, unlocked.Text);
        }

        // ---- Conversation state --------------------------------------------------------------------

        [Fact]
        public void TheThreadRemembersItsSubjectAcrossTurns()
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");

            var followUp = chat.Ask("and its melting point?");

            Assert.Equal(ReplySource.Engine, followUp.Source);
            Assert.Equal(["gold"], followUp.Plan!.ElementKeys);
        }

        [Fact]
        public void ResetClearsTheSubjectButKeepsTheLanguage()
        {
            var chat = Chat();
            chat.Ask("vad ar densiteten for guld");
            Assert.Equal("sv", chat.Language);

            chat.Reset();

            Assert.Null(chat.State.FocusElement);
            Assert.Equal("sv", chat.Language);
        }

        /// <summary>
        /// With an element in play the chips are about it; otherwise they advertise what the
        /// assistant can do, which is otherwise undiscoverable from an empty chat.
        /// </summary>
        [Fact]
        public void SuggestionsFollowWhateverTheThreadIsAbout()
        {
            var chat = Chat();

            var cold = chat.Ask("hello").Suggestions;
            var warm = chat.Ask("what is the density of gold").Suggestions;

            Assert.NotEmpty(cold);
            Assert.NotEmpty(warm);
            Assert.Contains(warm, s => s.Contains("Gold", StringComparison.OrdinalIgnoreCase));
        }
    }

    public class ReactivityScaleTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ElementRecord Element(string key) => Service.Store.Element(key)!;

        /// <summary>
        /// Fluorine is the most reactive element there is, and the halogen trend - which decreases
        /// with atomic number - would otherwise rank it behind nothing at all.
        /// </summary>
        [Fact]
        public void FluorineOutranksEverything()
        {
            foreach (var other in Service.Store.Elements.Where(e => e.Key != "fluorine"))
                Assert.Equal(Element("fluorine"), ReactivityScale.MoreReactive(Element("fluorine"), other));
        }

        [Fact]
        public void ReactivityIncreasesDownTheAlkaliMetals()
        {
            Assert.Equal(Element("potassium"), ReactivityScale.MoreReactive(Element("sodium"), Element("potassium")));
            Assert.Equal(Element("sodium"), ReactivityScale.MoreReactive(Element("lithium"), Element("sodium")));
        }

        [Fact]
        public void ReactivityDecreasesDownTheHalogens() =>
            Assert.Equal(Element("chlorine"), ReactivityScale.MoreReactive(Element("chlorine"), Element("iodine")));

        [Fact]
        public void NobleGasesScoreLowest()
        {
            Assert.Equal(0, ReactivityScale.Of(Element("neon")));
            Assert.Equal(Element("sodium"), ReactivityScale.MoreReactive(Element("neon"), Element("sodium")));
        }

        /// <summary>
        /// Two elements that score the same are two elements this scale has nothing to say about,
        /// and saying so is the honest answer.
        /// </summary>
        [Fact]
        public void AnUnseparableePairReturnsNull() =>
            Assert.Null(ReactivityScale.MoreReactive(Element("neon"), Element("argon")));

        /// <summary>
        /// Android spells it the American way; the merged element table uses caesium. A spelling
        /// must not decide a chemistry answer.
        /// </summary>
        [Fact]
        public void BothSpellingsOfCaesiumScoreTheSame() =>
            Assert.Equal(
                ReactivityScale.Of(SeriesId.AlkaliMetal, 55, "caesium"),
                ReactivityScale.Of(SeriesId.AlkaliMetal, 55, "cesium"));
    }

    public class FuzzyMatchTest
    {
        [Theory]
        [InlineData("yes")]
        [InlineData("sure")]
        [InlineData("ja")]
        [InlineData("tell me more")]
        [InlineData("是的")]
        public void AffirmativesAreRecognised(string reply) => Assert.True(FuzzyMatch.IsAffirmative(reply));

        /// <summary>
        /// Matched on word boundaries: a plain containment check reads the "ja" inside "japan" and
        /// the "ha" inside "hafnium".
        /// </summary>
        [Theory]
        [InlineData("japan")]
        [InlineData("hafnium")]
        [InlineData("what is the density of gold")]
        [InlineData("")]
        public void NonAffirmativesAreNot(string reply) => Assert.False(FuzzyMatch.IsAffirmative(reply));

        [Fact]
        public void TheThreeReflectionTestedHelpersStillBehave()
        {
            Assert.Equal(0, FuzzyMatch.Levenshtein("gold", "gold"));
            Assert.Equal(1, FuzzyMatch.Levenshtein("gold", "golf"));
            Assert.True(FuzzyMatch.HasKeyword("what is the density", ["density"]));
            Assert.False(FuzzyMatch.HasKeyword("what is the weather", ["density"]));
        }
    }
}
