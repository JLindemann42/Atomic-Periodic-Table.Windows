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

        /// <summary>The entitlements the cached engines were built with, so a purchase invalidates them.</summary>
        private Entitlements _builtWith;

        public ChatOrchestrator(
            RetrievalService retrieval,
            Func<string, IStringProvider> stringsFor,
            Func<Entitlements>? entitlements = null,
            ChatCardPolicy? cardPolicy = null,
            string language = "en")
        {
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

            // ---- The engine, first ----------------------------------------------------------
            if (engine.Answer(message, State) is { } answered)
            {
                return new ChatReply(
                    answered.Text, ReplySource.Engine, answered.Actions, answered.Card,
                    Suggestions(strings, engine.LastPlan), engine.LastPlan);
            }

            // ---- Reactivity, the one capability the engine cannot cover ----------------------
            // Checked before the personality path because it is a real chemistry question with a
            // real answer; leaving it to the greeting responder would decline it.
            if (Reactivity(message, strings) is { } reactivity) return reactivity;

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
        /// A greeting, a thank-you, or an honest decline.
        ///
        /// <para>Deliberately not a keyword cascade. Every branch answers something with no grounded
        /// answer at all, so the only decision is which of three sentences to say.</para>
        ///
        /// <para>There is no separate "what can you do" reply, because there is no shipped string
        /// for one. The suggestion chips <i>are</i> that answer - they name what the assistant can
        /// do, in twelve languages, and they are attached to every reply on this path.</para>
        /// </summary>
        private ChatReply Personality(string message, IStringProvider strings, string language)
        {
            var normalized = TextMatching.NormalizeForLookup(message);

            if (Mentions(normalized, Greetings))
                return new ChatReply(
                    strings.Get(S.ai_greeting), ReplySource.Personality, [], null,
                    Suggestions(strings, null));

            if (Mentions(normalized, Thanks))
                return new ChatReply(
                    strings.Get(S.ai_encouragement), ReplySource.Personality, [], null,
                    Suggestions(strings, null));

            return Decline(language);
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

        private const int MaxSuggestions = 3;
    }
}
