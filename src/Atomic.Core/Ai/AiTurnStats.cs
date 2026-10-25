using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Engine;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// What happened during one turn of the assistant, recorded for analytics. Ported from Android's
    /// <c>AiTurnStats</c> (D45, roadmap 0.7).
    ///
    /// <para>Carries no user text. A question is free-form input that may contain anything, so only
    /// its shape travels: how long it was, and whether an element came out of it.</para>
    ///
    /// <para>Every name and value is Android's - <c>ai_response</c>, <c>path</c>, <c>PROPERTY_LOOKUP</c>,
    /// <c>no_match</c> - so one report covers both platforms.</para>
    /// </summary>
    public sealed record AiTurnStats(
        string Path,
        string Language,
        string? Intent = null,
        double? Confidence = null,
        bool LanguageSwitched = false,
        bool ElementResolved = false,
        string? CardKind = null,
        int ActionCount = 0,
        int QueryChars = 0,
        int QueryWords = 0,
        long LatencyMs = 0,
        string? DeclineReason = null,
        int? DailyLimit = null)
    {
        public const string ResponseEvent = "ai_response";
        public const string RateLimitedEvent = "ai_rate_limited";

        /// <summary>The user confirmed sending a declined question. Carries no text.</summary>
        public const string QuestionSentEvent = "ai_question_sent";

        /// <summary>
        /// Coarse buckets mirroring <see cref="ReplySource"/>, with Android's values.
        /// </summary>
        public static class Paths
        {
            public const string Engine = "engine";
            public const string Personality = "personality";
            public const string Reactivity = "reactivity";
            public const string Fallback = "fallback";
            public const string RateLimited = "rate_limited";
            public const string ContextLost = "context_lost";

            public static string Of(ReplySource source) => source switch
            {
                ReplySource.Engine => Engine,
                ReplySource.Personality => Personality,
                ReplySource.Reactivity => Reactivity,
                ReplySource.Declined => Fallback,
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
            };
        }

        /// <summary>The turn behind a reply, from its shape alone.</summary>
        public static AiTurnStats From(
            ChatReply reply, string question, string language, bool languageSwitched, bool elementResolved,
            int actionCount, long latencyMs) =>
            new(
                Paths.Of(reply.Source),
                language,
                reply.Plan is { } plan ? AndroidName(plan.Intent) : null,
                reply.Plan?.Confidence,
                languageSwitched,
                elementResolved,
                reply.Card is { } card ? AndroidName(card.Kind) : null,
                actionCount,
                QueryChars: question.Trim().Length,
                QueryWords: Words(question),
                LatencyMs: latencyMs,
                DeclineReason: reply.DeclineReason?.WireName());

        /// <summary>Confidence as a dimension rather than a raw number, so it groups in the console.</summary>
        public string? ConfidenceBucket => Confidence switch
        {
            null => null,
            < 0.5 => "lt_50",
            < 0.7 => "50_70",
            < 0.85 => "70_85",
            _ => "gte_85",
        };

        /// <summary>The <c>ai_response</c> parameters, under Android's names. Nulls are dropped by the sink.</summary>
        public IReadOnlyList<KeyValuePair<string, object?>> Parameters(string host) =>
        [
            new("host", host),
            new("path", Path),
            new("intent", Intent),
            new("confidence", ConfidenceBucket),
            new("latency_ms", LatencyMs),
            new("language", Language),
            new("language_switched", LanguageSwitched),
            new("element_resolved", ElementResolved),
            new("has_card", CardKind is not null),
            new("card_kind", CardKind),
            new("action_count", ActionCount),
            new("query_chars", QueryChars),
            new("query_words", QueryWords),
            new("decline_reason", DeclineReason),
        ];

        public static int Words(string question)
        {
            var trimmed = question.Trim();
            return trimmed.Length == 0
                ? 0
                : trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        /// <summary>
        /// A C# enum member as Kotlin names it: <c>PropertyLookup</c> → <c>PROPERTY_LOOKUP</c>.
        /// </summary>
        public static string AndroidName(Enum value)
        {
            var name = value.ToString();
            var out_ = new StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i])) out_.Append('_');
                out_.Append(char.ToUpperInvariant(name[i]));
            }
            return out_.ToString();
        }
    }
}
