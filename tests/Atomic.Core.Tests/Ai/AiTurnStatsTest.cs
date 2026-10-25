using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Analytics;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// Windows' turn telemetry (D45, roadmap 0.7). The point of it is that one report covers both
    /// platforms, so what is tested is that the names and values are Android's - and that nothing the
    /// user typed is among them.
    /// </summary>
    public sealed class AiTurnStatsTest
    {
        private static ChatReply Declined(DeclineReason reason) =>
            new("no data", ReplySource.Declined, [], DeclineReason: reason);

        [Fact]
        public void EnumsAreNamedAsKotlinNamesThem()
        {
            Assert.Equal("PROPERTY_LOOKUP", AiTurnStats.AndroidName(Intent.PropertyLookup));
            Assert.Equal("UNKNOWN", AiTurnStats.AndroidName(Intent.Unknown));
            Assert.Equal("NFPA_DIAMOND", AiTurnStats.AndroidName(ChatCardKind.NfpaDiamond));
        }

        [Fact]
        public void ADeclineIsAFallbackWithItsReason()
        {
            var stats = AiTurnStats.From(
                Declined(DeclineReason.NoSuchElement), "tell me about element 119", "en",
                languageSwitched: false, elementResolved: false, actionCount: 1, latencyMs: 12);

            Assert.Equal("fallback", stats.Path);
            Assert.Equal("no_such_element", stats.DeclineReason);
            Assert.Null(stats.Intent);
            Assert.Equal(25, stats.QueryChars);
            Assert.Equal(5, stats.QueryWords);
        }

        [Fact]
        public void AnEngineAnswerCarriesItsIntentAndBucket()
        {
            var plan = new QueryPlan(Intent.PropertyLookup, Confidence: 0.9);
            var stats = AiTurnStats.From(
                new ChatReply("19.3 g/cm³", ReplySource.Engine, [], Plan: plan), "density of gold", "sv",
                languageSwitched: true, elementResolved: true, actionCount: 2, latencyMs: 40);

            Assert.Equal("engine", stats.Path);
            Assert.Equal("PROPERTY_LOOKUP", stats.Intent);
            Assert.Equal("gte_85", stats.ConfidenceBucket);
            Assert.Null(stats.DeclineReason);
        }

        [Fact]
        public void TheParametersAreAndroidsAndCarryNoText()
        {
            var question = "what is the colour of unobtainium";
            var stats = AiTurnStats.From(
                Declined(DeclineReason.NoMatch), question, "en",
                languageSwitched: false, elementResolved: false, actionCount: 1, latencyMs: 5);
            var parameters = stats.Parameters("chat_panel");

            Assert.Equal(
                ["host", "path", "intent", "confidence", "latency_ms", "language", "language_switched",
                 "element_resolved", "has_card", "card_kind", "action_count", "query_chars", "query_words",
                 "decline_reason"],
                parameters.Select(p => p.Key));
            Assert.DoesNotContain(parameters, p => p.Value is string s && s.Contains("unobtainium"));
        }

        [Fact]
        public void TheBodyDropsNullsAndSendsBooleansAsNumbers()
        {
            var body = MeasurementProtocol.Body("cid", "ai_response", new List<KeyValuePair<string, object?>>
            {
                new("intent", null),
                new("has_card", true),
                new("element_resolved", false),
                new("query_chars", 25),
                new("path", "fallback"),
            });
            var parameters = body["events"]![0]!["params"]!.AsObject();

            Assert.Equal("cid", (string?)body["client_id"]);
            Assert.Equal("ai_response", (string?)body["events"]![0]!["name"]);
            Assert.False(parameters.ContainsKey("intent"));
            Assert.Equal(1, (int)parameters["has_card"]!);
            Assert.Equal(0, (int)parameters["element_resolved"]!);
            Assert.Equal(25, (int)parameters["query_chars"]!);
            Assert.Equal("fallback", (string?)parameters["path"]);
        }

        [Fact]
        public void ALongValueIsCutToWhatGa4Accepts()
        {
            var body = MeasurementProtocol.Body("cid", "e", [new("error", new string('x', 300))]);
            Assert.Equal(MeasurementProtocol.MaxValueLength, ((string?)body["events"]![0]!["params"]!["error"])!.Length);
        }

        [Fact]
        public void TheSecretTravelsOnlyInTheUrl()
        {
            var url = MeasurementProtocol.CollectUrl("G-ABC", "s3cret");
            Assert.Equal("https://www.google-analytics.com/mp/collect?measurement_id=G-ABC&api_secret=s3cret", url);
            Assert.DoesNotContain("s3cret", MeasurementProtocol.Body("cid", "e", []).ToJsonString());
        }
    }
}
