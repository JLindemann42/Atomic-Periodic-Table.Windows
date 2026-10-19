using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Compose;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Text;

namespace Atomic.Core.Ai
{
    /// <summary>Why a turn produced the reply it did, for analytics and for tests.</summary>
    public enum ReplySource
    {
        /// <summary>Answered from the app's own data by the engine.</summary>
        Engine,

        /// <summary>A greeting, a thank-you, or a question about the app rather than about chemistry.</summary>
        Personality,

        /// <summary>Two elements the reactivity heuristic can order, which no stored field can.</summary>
        Reactivity,

        /// <summary>Nothing could be said honestly.</summary>
        Declined,
    }

    /// <summary>One turn's reply.</summary>
    /// <param name="Suggestions">Chips offered under the reply, to make the next question discoverable.</param>
    public sealed record ChatReply(
        string Text,
        ReplySource Source,
        IReadOnlyList<ChatAction> Actions,
        ChatCard? Card = null,
        IReadOnlyList<string>? Suggestions = null,
        QueryPlan? Plan = null)
    {
        public IReadOnlyList<ChatAction> Actions { get; init; } = Actions;

        public IReadOnlyList<string> Suggestions { get; init; } = Suggestions ?? [];
    }

    /// <summary>
    /// Owns one conversation: detects the language of each message, asks the engine, and handles
    /// everything the engine deliberately declines.
    ///
    /// <para><b>This replaces Android's 2,664-line <c>AIAgentManager</c> (D1).</b> That file is a
    /// keyword cascade the acceptance corpus never touches - <c>CorpusHarness</c> wires
    /// <c>AiEngine</c> directly - so porting it would have added 2,664 lines with zero test
    /// coverage. What it actually owed the user is defined by two corpus files:
    /// <c>must_defer.tsv</c> and <c>capabilities.tsv</c>, which between them describe greetings,
    /// "what can you do", and small talk in twelve languages. That is a personality responder, not a
    /// router.</para>
    ///
    /// <para>One capability would have been lost with it, so it is recovered explicitly: reactivity
    /// comparisons, which have a real answer and no field behind them. See
    /// <see cref="ReactivityScale"/>.</para>
    ///
    /// <para>Stateful by design - it holds the <see cref="DialogueState"/> that makes "and its
    /// density?" work. One instance per conversation.</para>
    /// </summary>
    public sealed class ChatOrchestrator
    {
        private readonly KnowledgeStore _store;
        private readonly RetrievalService _retrieval;
        private readonly Func<string, IStringProvider> _stringsFor;
        private readonly Func<Entitlements> _entitlements;
        private readonly ChatCardPolicy _cardPolicy;

        /// <summary>
        /// Engines are per-language and built on demand.
        ///
        /// <para>Each one carries a field resolver built from that language's labels, so switching
        /// language mid-thread cannot be done by swapping a string - the resolver has to be rebuilt.
        /// Cached because that build walks the whole field registry.</para>
        /// </summary>
        private readonly Dictionary<string, AiEngine> _engines = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Picks among the shipped facts. Per-orchestrator rather than <c>Random.Shared</c> so a
        /// test can seed one and get a reply it can name.
        /// </summary>
        private readonly Random _random;

        /// <summary>The entitlements the cached engines were built with, so a purchase invalidates them.</summary>
        private Entitlements _builtWith;

        public ChatOrchestrator(
            RetrievalService retrieval,
            Func<string, IStringProvider> stringsFor,
            Func<Entitlements>? entitlements = null,
            ChatCardPolicy? cardPolicy = null,
            string language = "en",
            Random? random = null)
        {
            _random = random ?? new Random();
            _retrieval = retrieval;
            _store = retrieval.Store;
            _stringsFor = stringsFor;
            _entitlements = entitlements ?? (() => Entitlements.Full);
            _cardPolicy = cardPolicy ?? ChatCardPolicy.Default;
            _builtWith = _entitlements();

            State = new DialogueState { ActiveLanguage = language };
        }

        /// <summary>What the conversation remembers. Public so a host can persist and restore a thread.</summary>
        public DialogueState State { get; }

        /// <summary>
        /// Which elements this user keeps asking about. Public for the same reason
        /// <see cref="State"/> is: the host saves and restores it.
        /// </summary>
        public InterestProfile Profile { get; } = new();

        /// <summary>The language the last message was answered in.</summary>
        public string Language => State.ActiveLanguage;

        /// <summary>
        /// Answer one message.
        ///
        /// <para>The order is the contract. The engine is asked first and its decline is taken
        /// seriously: everything below is for questions that have no grounded answer, and reaching
        /// them when the engine could have answered would replace a cited fact with a pleasantry.</para>
        /// </summary>
        public ChatReply Ask(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return Decline("en");

            // Per-message, not per-session. A thread can switch language mid-way and each turn is
            // answered in the language it was asked in - which is why the engine is keyed by
            // language rather than constructed once.
            State.ActiveLanguage = _retrieval.Resolver(State.ActiveLanguage)
                .DetectLanguage(message, State.ActiveLanguage);

            var language = State.ActiveLanguage;
            var strings = _stringsFor(language);
            var engine = EngineFor(language);

            // ---- A quiz question already on the table --------------------------------------
            // Before the engine: the expected reply is an element name, and the engine would
            // answer it as a lookup rather than marking it right or wrong.
            if (Quiz(message, strings) is { } quiz) return quiz;

            // ---- The engine, first ----------------------------------------------------------
            if (engine.Answer(message, State) is { } answered)
            {
                // The element the answer was about, which is the one worth remembering. Taken from
                // the plan rather than the message so "what about its density" counts towards the
                // element in focus rather than towards nothing.
                Profile.Track(engine.LastPlan?.ElementKeys.FirstOrDefault() ?? State.FocusElement);

                return new ChatReply(
                    answered.Text, ReplySource.Engine, answered.Actions, answered.Card,
                    Suggestions(strings, engine.LastPlan), engine.LastPlan);
            }

            // ---- Reactivity, the one capability the engine cannot cover ----------------------
            // Checked before the personality path because it is a real chemistry question with a
            // real answer; leaving it to the greeting responder would decline it.
            if (Reactivity(message, strings) is { } reactivity) return reactivity;

            // ---- "Tell me more" --------------------------------------------------------------
            // Before the overview, as on Android: "tell me more about gold" names an element, and
            // the overview would answer it by repeating the opening the user has just read.
            if (MoreInfo(message, strings, language) is { } more) return more;

            // ---- "Gold", "tell me about gold", "element 79" ----------------------------------
            // must_defer.tsv requires the engine to decline all three; this is what answers them.
            if (Overview(message, strings, language) is { } overview) return overview;

            // ---- "What happens when sodium reacts with water" -------------------------------
            if (Reactions(message, strings) is { } reaction) return reaction;

            // ---- "Which element is similar to gold" -----------------------------------------
            if (Similarity(message, strings) is { } similar) return similar;

            // ---- Answers that live in the element's prose, not in a field --------------------
            // Usage, biological role and etymology. must_defer.tsv requires the engine to decline
            // these; declining is only right if something picks them up, and this is it.
            if (Narrative(message, strings, language) is { } narrative) return narrative;

            // ---- Everything with no grounded answer -----------------------------------------
            return Personality(message, strings, language);
        }

        /// <summary>
        /// Two elements the reactivity heuristic can order.
        ///
        /// <para>The planner declines these deliberately - reactivity is derived from group and
        /// position rather than stored, so planning over it would answer the wrong question. The
        /// heuristic is the only thing that can answer, and it says so in the same breath.</para>
        /// </summary>
        private ChatReply? Reactivity(string message, IStringProvider strings)
        {
            var normalized = TextMatching.NormalizeForLookup(message);
            if (!ReactivityWords.Any(w => TextMatching.ContainsWord(normalized, w))) return null;

            var named = _retrieval.Resolver(State.ActiveLanguage).ResolveAll(message, limit: 2);
            if (named.Count < 2) return null;

            var left = _store.Element(named[0].Key);
            var right = _store.Element(named[1].Key);
            if (left is null || right is null) return null;

            var winner = ReactivityScale.MoreReactive(left, right);
            var text = winner is null
                ? strings.Get(S.ai_reactivity_similar, Name(left), Name(right))
                : strings.Get(
                    S.ai_reactivity_more,
                    Name(winner),
                    Name(ReferenceEquals(winner, left) ? right : left));

            return new ChatReply(text, ReplySource.Reactivity, []);

            string Name(ElementRecord e) =>
                _retrieval.Localized(State.ActiveLanguage)?.Name(e.Key) is { Length: > 0 } localized
                    ? localized
                    : char.ToUpperInvariant(e.Key[0]) + e.Key[1..];
        }

        /// <summary>
        /// Ask a quiz question, or judge the answer to the one outstanding.
        /// </summary>
        private ChatReply? Quiz(string message, IStringProvider strings)
        {
            var normalized = TextMatching.NormalizeForLookup(message);
            var pending = State.PendingQuizAnswer;

            // A short reply while a question is open is the answer to it - but only if it names
            // an element, or says the answer outright.
            //
            // Android judges any reply of four words or fewer, which means an unrelated request
            // is consumed as a wrong guess: with a question open, "tell me more" came back "Not
            // quite! The answer was Vanadium". Requiring an element keeps every real guess,
            // including a misspelling the resolver still matches, and lets anything else through
            // to the handler it was meant for.
            if (ElementQuiz.LooksLikeAnAnswer(normalized, pending) && !ElementQuiz.Asks(normalized))
            {
                var guess = _retrieval.Resolver(State.ActiveLanguage).ResolveAll(message, limit: 1);
                var namesTheAnswer = normalized.Contains(pending!, StringComparison.OrdinalIgnoreCase);

                if (guess.Count == 0 && !namesTheAnswer) return null;

                var answer = _store.Element(pending!);

                var verdict = ElementQuiz.Judge(
                    normalized,
                    pending!,
                    guess.Count > 0 ? guess[0].Key : null,
                    answer is null ? pending! : Display(answer, pending!),
                    strings);

                State.PendingQuizAnswer = null;
                return new ChatReply(verdict, ReplySource.Personality, [], null, Suggestions(strings, null));
            }

            if (!ElementQuiz.Asks(normalized)) return null;

            if (ElementQuiz.Ask(_store.Elements, _random, strings, e => Display(e, e.Key)) is not { } question)
                return new ChatReply(strings.Get(S.ai_quiz_no_start), ReplySource.Personality, [],
                                     null, Suggestions(strings, null));

            State.PendingQuizAnswer = question.About.Key;

            // Deliberately not set as the focus: the user is being asked to name this element, and
            // putting it in focus would let the next question answer itself.
            return new ChatReply(question.Text, ReplySource.Personality, [], null,
                                 Suggestions(strings, null));
        }

        /// <summary>
        /// What an element does when it meets water, oxygen, an acid, or another element.
        /// </summary>
        private ChatReply? Reactions(string message, IStringProvider strings)
        {
            var normalized = TextMatching.NormalizeForLookup(message);
            if (!ElementReactions.Asks(normalized)) return null;

            // Two at most: a reaction is between a pair, and the third name in a sentence is
            // usually the product rather than a reactant.
            var named = _retrieval.Resolver(State.ActiveLanguage).ResolveAll(message, limit: 2);

            // In the order the question names them, so "sodium and chlorine" is not answered
            // back as "Chlorine and Sodium" - the resolver orders by score, not by position.
            var records = named
                .OrderBy(m => normalized.IndexOf(
                    TextMatching.NormalizeForLookup(m.Matched), StringComparison.Ordinal))
                .Select(m => _store.Element(m.Key))
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList();

            // "What does it react with" after discussing an element is about that element.
            if (records.Count == 0 && State.FocusElement is { } focus && _store.Element(focus) is { } inFocus)
                records.Add(inFocus);

            var text = ElementReactions.Answer(records, normalized, strings, e => Display(e, e.Key));
            if (text is null) return null;

            State.FocusElement = records[0].Key;
            Profile.Track(records[0].Key);

            return new ChatReply(text, ReplySource.Personality, [], null, Suggestions(strings, null));
        }

        /// <summary>
        /// What an element resembles - the elements sharing its group.
        /// </summary>
        private ChatReply? Similarity(string message, IStringProvider strings)
        {
            var normalized = TextMatching.NormalizeForLookup(message);
            if (!ElementSimilarity.Asks(normalized)) return null;

            var named = _retrieval.Resolver(State.ActiveLanguage).ResolveAll(message, limit: 1);
            var key = named.Count > 0 ? named[0].Key : State.FocusElement;

            // No element named and none in focus: say so rather than guessing one.
            var target = key is null ? null : _store.Element(key);

            var text = ElementSimilarity.Answer(
                target, _store.Elements, strings, e => Display(e, e.Key));

            if (target is not null)
            {
                State.FocusElement = target.Key;
                Profile.Track(target.Key);
            }

            return new ChatReply(text, ReplySource.Personality, [], null, Suggestions(strings, null));
        }

        /// <summary>
        /// An element named with nothing asked about it.
        ///
        /// <para>Also resolves "element 79" by atomic number, which Android does inside
        /// <c>findElementKeyByQuery</c>: a bare number is an element reference when the query says
        /// "element" or "atomic number", or is short enough to be nothing else.</para>
        /// </summary>
        private ChatReply? Overview(string message, IStringProvider strings, string language)
        {
            var normalized = TextMatching.NormalizeForLookup(message);

            // A question that asks something specific is not a request for an overview; it has
            // already been through the engine and been declined for its own reasons.
            if (ElementNarrative.Detect(normalized) is not null) return null;

            // The resolver already reads "element 79" as gold, so there is no separate numeric
            // path to maintain here.
            var named = _retrieval.Resolver(State.ActiveLanguage).ResolveAll(message, limit: 2);
            if (named.Count != 1) return null;            // none, or a comparison the engine owns

            var key = named[0].Key;

            // Only when the element is essentially the whole question. "Gold" and "tell me about
            // gold" qualify; a real question that happens to name gold does not, and would have
            // been answered by the engine anyway.
            if (!ReadsAsAnOpening(normalized, named[0].Matched)) return null;

            if (_store.Element(key) is not { } element) return null;

            // "Tell me everything about gold" asks for every section; anything else gets a selection,
            // varied by the orchestrator's random so a second "gold" is not the same reply.
            var text = ElementOverview.Of(
                element, Display(element, key), language, strings, _retrieval.Localized(language),
                new OverviewOptions(ElementOverview.AsksForEverything(normalized), _random, _entitlements()));
            if (text is null) return null;

            State.FocusElement = key;
            Profile.Track(key);

            // The overview states the atomic mass, so "tell me more" must not offer it as news.
            // Android seeds its equivalent set with the labels the overview used, for the same
            // reason.
            State.NoteShared(key, ["atomic_mass"]);

            return new ChatReply(text, ReplySource.Personality, [], null, Suggestions(strings, null));
        }

        /// <summary>
        /// Whether the element name is effectively the whole message - the shape that means
        /// "tell me about this" rather than a question with an element in it.
        /// </summary>
        private static bool ReadsAsAnOpening(string normalized, string surface)
        {
            var withoutElement = normalized
                .Replace(TextMatching.NormalizeForLookup(surface), " ", StringComparison.Ordinal);

            return TextMatching.SplitQueryTokens(withoutElement)
                .Count(t => t.Trim().Length > 0
                            && !OpeningFrames.Contains(t, StringComparer.Ordinal)
                            && !ElementOverview.IsFullWord(t)) == 0;
        }

        /// <summary>Words that carry no question of their own - "tell me about <i>gold</i>".</summary>
        /// <remarks>
        /// "Element" is here so that "element 79" reads as an opening: the resolver already maps
        /// the number to gold, leaving the bare word behind, and without it the overview declined
        /// the very query it exists to answer.
        /// </remarks>
        private static readonly string[] OpeningFrames =
        [
            "tell", "me", "about", "what", "is", "a", "an", "the", "info", "information", "on",
            "element", "grundamne", "amne", "elemento", "elemente", "element79",
            "beratta", "om", "vad", "ar", "erzahl", "mir", "uber", "was", "ist", "parle", "moi", "de",
            "que", "es", "cuentame", "sobre", "dimmi", "cosa", "conte", "me", "fale",
        ];

        /// <summary>
        /// "Tell me more" - the next thing about the element in focus that has not been said.
        ///
        /// <para>The element comes from the message, or from the conversation. With neither there
        /// is nothing to say more <i>about</i>, so this declines rather than picking an element at
        /// random - a confident fact about something the user never mentioned reads as a non
        /// sequitur.</para>
        ///
        /// <para>The fact itself is rendered by the engine, so it arrives with the units, the
        /// citation and the entitlement gate of an ordinary question.</para>
        /// </summary>
        private ChatReply? MoreInfo(string message, IStringProvider strings, string language)
        {
            var normalized = TextMatching.NormalizeForLookup(message);

            // "Yes" continues a tour that has already started, which is the other way into this
            // on Android. The requirement that something has been said already is theirs too, and
            // it is what stops a bare "sure" volunteering a fact about nothing in particular.
            var goOn = FuzzyMatch.IsAffirmative(message) && State.SharedFieldIds.Count > 0;

            if (!ElementMoreInfo.Asks(normalized) && !goOn) return null;

            // An affirmative is never an element, so it is not resolved as one. "Sure" is close
            // enough to "sulfur" for the fuzzy resolver to take it, which moved the tour to a
            // different element on the strength of the user saying yes.
            string? key;
            if (goOn)
            {
                key = State.FocusElement;
            }
            else
            {
                var named = _retrieval.Resolver(State.ActiveLanguage).ResolveAll(message, limit: 1);
                key = named.Count > 0 ? named[0].Key : State.FocusElement;
            }

            if (key is null) return null;

            if (_store.Element(key) is not { } element) return null;

            var candidates = ElementMoreInfo.CandidatesFor(element);
            var next = ElementMoreInfo.Next(
                candidates,
                id => State.HasShared(key, id),
                State.SharedFieldIds.LastOrDefault(),
                _random);

            // Everything this element has to offer has been said. Say so, rather than starting
            // over - repeating the density as though it were new is worse than admitting the tour
            // is finished.
            if (next is null)
            {
                var done = strings.Get(S.ai_covered_all, Display(element, key));
                if (Fact(strings) is { } extra) done = done + "\n\n" + extra;

                return new ChatReply(
                    strings.Get(S.ai_encouragement) + " " + done,
                    ReplySource.Personality, [], null, Suggestions(strings, null));
            }

            if (EngineFor(language).FieldAnswerFor(key, next) is not { } answer) return null;

            State.FocusElement = key;
            State.NoteShared(key, [next]);
            Profile.Track(key);

            return new ChatReply(
                answer.Text, ReplySource.Engine, answer.Actions, answer.Card,
                Suggestions(strings, null));
        }

        /// <summary>
        /// What an element is used for, what it does in the body, where its name comes from.
        ///
        /// <para>Read out of the element's own description, because no stored field holds any of
        /// it. The element comes from the question, or from the one already in focus - "what is it
        /// used for" after asking about gold is about gold.</para>
        /// </summary>
        private ChatReply? Narrative(string message, IStringProvider strings, string language)
        {
            var normalized = TextMatching.NormalizeForLookup(message);
            if (ElementNarrative.Detect(normalized) is not { } topic) return null;

            var named = _retrieval.Resolver(State.ActiveLanguage).ResolveAll(message, limit: 1);
            var key = named.Count > 0 ? named[0].Key : State.FocusElement;
            if (key is null) return null;

            if (_store.Element(key) is not { } element) return null;

            var text = ElementNarrative.Answer(
                topic,
                (element.Value("description") as Atomic.Core.Ai.Data.FieldValue.Text)?.Raw,
                Display(element, key),
                language,
                strings,
                (element.Value("element_discovered_name") as Atomic.Core.Ai.Data.FieldValue.Text)?.Raw,
                (element.Value("element_year") as Atomic.Core.Ai.Data.FieldValue.Text)?.Raw);

            if (text is null) return null;

            // The element is now what the conversation is about, as it would be after any answer.
            State.FocusElement = key;
            Profile.Track(key);

            return new ChatReply(text, ReplySource.Personality, [], null, Suggestions(strings, null));
        }

        /// <summary>An element's name in the reader's language.</summary>
        private string Display(ElementRecord element, string key) =>
            _retrieval.Localized(State.ActiveLanguage)?.Name(key) is { Length: > 0 } localized
                ? localized
                : char.ToUpperInvariant(key[0]) + key[1..];

        /// <summary>
        /// A greeting, a thank-you, a fun fact, an answer about the app itself, or an honest
        /// decline - everything with no grounded answer behind it.
        ///
        /// <para>The order is Android's: greeting, then facts, then the app's own features. Each
        /// branch is reachable only because the one above it did not match, so a question matching
        /// two is answered the same way on both platforms.</para>
        /// </summary>
        private ChatReply Personality(string message, IStringProvider strings, string language)
        {
            var normalized = TextMatching.NormalizeForLookup(message);

            if (Mentions(normalized, Greetings))
                return new ChatReply(
                    PersonalGreeting(strings) ?? strings.Get(S.ai_greeting),
                    ReplySource.Personality, [], null,
                    Suggestions(strings, null));

            if (Mentions(normalized, Thanks))
                return new ChatReply(
                    strings.Get(S.ai_encouragement), ReplySource.Personality, [], null,
                    Suggestions(strings, null));

            // "Tell me something interesting." Android checks this between the greeting and its
            // local knowledge, so it is checked here in the same place.
            if (Mentions(normalized, FactWords) && Fact(strings) is { } fact)
                return new ChatReply(
                    fact, ReplySource.Personality, [], null,
                    Suggestions(strings, null));

            // Questions about the app itself. After the greetings, as on Android, where the
            // greeting branch is tested before local knowledge is consulted - "help me" is a
            // capabilities keyword and a greeting is the better answer to it.
            if (AppFeatures.Resolve(normalized) is { } feature)
                return new ChatReply(
                    strings.Get(feature), ReplySource.Personality, [], null,
                    Suggestions(strings, null));

            return Decline(language);
        }

        /// <summary>
        /// "I remember you were checking out Gold recently", or null for a first-time user.
        /// </summary>
        /// <remarks>
        /// The element is named in the reader's language. Android interpolates the raw key, so a
        /// Swedish user is greeted about "gold" in an otherwise Swedish sentence - the same class
        /// of gap as D41, and fixed here rather than reproduced.
        /// </remarks>
        private string? PersonalGreeting(IStringProvider strings)
        {
            if (Profile.Favourite is not { } key) return null;
            if (!strings.Has(S.ai_personalized_greeting_fav)) return null;

            var localized = _retrieval.Localized(State.ActiveLanguage)?.Name(key);
            var name = localized is { Length: > 0 }
                ? localized
                : char.ToUpperInvariant(key[0]) + key[1..];

            return strings.Get(S.ai_personalized_greeting_fav, name);
        }

        /// <summary>
        /// One of the ten shipped facts, at random.
        ///
        /// <para>Null when the array is missing rather than inventing an English sentence: Android's
        /// catch falls back to a hardcoded "Oxygen is the most abundant element in Earth's crust",
        /// which is the one outcome a Swedish reader should not get. Falling through to the decline
        /// keeps the reply in the right language.</para>
        /// </summary>
        private string? Fact(IStringProvider strings)
        {
            var facts = strings.Array(Facts);
            return facts.Count == 0 ? null : facts[_random.Next(facts.Count)];
        }

        /// <summary>
        /// The honest failure: nothing here could be said from the app's data.
        ///
        /// <para>Still carries the suggestion chips, so a decline is a dead end for one turn rather
        /// than for the conversation.</para>
        /// </summary>
        private ChatReply Decline(string language)
        {
            var strings = _stringsFor(language);
            return new ChatReply(
                strings.Get(S.ai_no_data), ReplySource.Declined, [], null,
                Suggestions(strings, null));
        }

        /// <summary>
        /// The chips offered under a reply.
        ///
        /// <para>Read from the shipped string arrays rather than written here, so all twelve
        /// languages get them. Android's hand-written <c>when (language)</c> covered Swedish, German
        /// and English only - the other nine were offered English chips in an otherwise translated
        /// conversation.</para>
        /// </summary>
        private IReadOnlyList<string> Suggestions(IStringProvider strings, QueryPlan? plan)
        {
            // With an element in play the follow-ups are about it; otherwise there is room to
            // advertise what the assistant can do beyond lookups.
            var subject = plan?.ElementKeys.FirstOrDefault() ?? State.FocusElement;

            if (subject is not null)
            {
                var name = _retrieval.Localized(State.ActiveLanguage)?.Name(subject)
                           ?? char.ToUpperInvariant(subject[0]) + subject[1..];

                return strings.Array(SuggestElement)
                    .Select(template =>
                        Try.OrNull(() => JavaFormat.Format(CultureInfo.InvariantCulture, template, name))
                        ?? template)
                    .Take(MaxSuggestions)
                    .ToList();
            }

            return strings.Array(SuggestGeneral)
                .Concat(strings.Array(SuggestTools).Take(1))
                .Take(MaxSuggestions)
                .ToList();
        }

        /// <summary>
        /// The engine for a language, rebuilt when the user's entitlements change.
        ///
        /// <para>A purchase has to take effect on the next message rather than the next launch, and
        /// the entitlements are baked into the executor when it is constructed - so the cache is
        /// dropped wholesale rather than tracked per engine.</para>
        /// </summary>
        private AiEngine EngineFor(string language)
        {
            var current = _entitlements();
            if (current != _builtWith)
            {
                _engines.Clear();
                _builtWith = current;
            }

            if (_engines.TryGetValue(language, out var cached)) return cached;

            var engine = new AiEngine(
                _store,
                _retrieval.Datasets,
                _retrieval.Localized(language),
                _stringsFor(language),
                _retrieval.Resolver(language),
                _retrieval.Retriever(language),
                current,
                _cardPolicy);

            _engines[language] = engine;
            return engine;
        }

        /// <summary>Start a new thread, keeping the language the user is writing in.</summary>
        public void Reset() => State.Clear();

        private static bool Mentions(string normalized, IReadOnlyList<string> phrases) =>
            phrases.Any(p => p.Contains(' ', StringComparison.Ordinal) || p.Any(c => c > 0x2000)
                ? normalized.Contains(p, StringComparison.Ordinal)
                : TextMatching.ContainsWord(normalized, p));

        /// <summary>
        /// Words that make a query a reactivity question.
        ///
        /// <para>Folded, like every vocabulary compared against normalised text.</para>
        /// </summary>
        private static readonly string[] ReactivityWords =
        [
            "reactive", "reactivity",
            "reaktiv", "reaktivitet",       // sv, de
            "reactivo", "reactividad",      // es
            "reactif", "reactivite",        // fr
            "reattivo", "reattivita",       // it
            "reativo", "reatividade",       // pt
            "reaktief",                     // af
            "reaktibo",                     // fil
            "क्रियाशील", "فعال", "活泼", "活性",
        ];

        /// <summary>
        /// The base names of the shipped suggestion arrays.
        ///
        /// <para>Literals rather than S constants: the generator emits a constant per <i>entry</i>
        /// (ai_suggest_general_0 and so on), and an array's base name is not itself an entry.
        /// IStringProvider.Array appends the index, so the base name is what it needs.</para>
        /// </summary>
        private const string SuggestElement = "ai_suggest_element";
        private const string SuggestGeneral = "ai_suggest_general";
        private const string SuggestTools = "ai_suggest_tools";

        /// <summary>
        /// Greetings and thanks, folded.
        ///
        /// <para>A subset of Lexicon.DEFER_TO_PERSONALITY, which is one undifferentiated list
        /// covering quizzes, greetings and farewells together - enough for the planner, which only
        /// needs to know it should decline, but not enough to choose between two replies.</para>
        /// </summary>
        private static readonly string[] Greetings =
        [
            "hello", "hi there", "hey", "good morning", "good afternoon", "good evening",
            "hej", "hejsan", "god morgon", "hallo", "guten morgen", "guten tag",
            "bonjour", "hola", "ciao", "ola", "namaste", "salam", "kumusta", "你好", "早上好",
        ];

        private static readonly string[] Thanks =
        [
            "thanks", "thank you", "tack", "danke", "merci", "gracias", "grazie", "obrigado",
            "dankie", "salamat", "shukriya", "dhanyavaad", "谢谢", "多谢",
        ];

        /// <summary>
        /// Words that make a message a request for a fact rather than a question.
        ///
        /// <para>Android's list, verbatim, including that "interesting" and "cool" are on it. They
        /// are broad enough to catch a real question - but only reach here once the engine has
        /// declined, and the same words reach the same branch on Android.</para>
        /// </summary>
        private static readonly string[] FactWords =
        [
            "fact", "fun fact", "did you know", "interesting", "cool",
            "fakta", "kul fakta", "visste du", "intressant", "haftigt",
            "faktum", "interessant", "fait", "curiosite", "dato curioso", "curiosidad",
            "curiosita", "interessante", "fatti", "curiosidade", "curiosidades", "fatos",
            "तथ्य", "क्या आप जानते हैं", "दिलचस्प",
            "事实", "你知道吗", "有趣", "shishi", "haqiqat", "feit", "katotohanan",

            // The word each language's own "Tell me a fun fact" chip uses. German says "Fakt"
            // where only "Faktum" was listed, French "anecdote", Urdu "دلچسپ" where only a
            // romanisation was listed, and Filipino "kawili-wiling".
            "fakt", "anecdote", "دلچسپ", "kawili-wili", "kawili-wiling",
        ];

        /// <summary>The shipped fact array - ai_facts.0 through .9.</summary>
        private const string Facts = "ai_facts";

        private const int MaxSuggestions = 3;
    }
}
