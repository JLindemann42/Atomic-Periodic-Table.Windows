using System.Linq;
using System.Text.Json.Nodes;
using Atomic.Core.Sync;
using Xunit;

namespace Atomic.Core.Tests.Sync
{
    /// <summary>
    /// Exactly what leaves the device when a user sends a declined question (roadmap 0.7). The
    /// dialog promises a list, the security rule accepts that list and nothing more, and this is
    /// where the two are held to it.
    /// </summary>
    public sealed class UnansweredQuestionDocumentTest
    {
        private static JsonObject Write(string question = "what is the colour of gold", string? reason = "no_match") =>
            UnansweredQuestionDocument.CommitBody("proj", "abc", question, "sv", reason, "5.1.30.0")["writes"]![0]!.AsObject();

        [Fact]
        public void SendsTheSixPromisedFieldsAndNothingElse()
        {
            var write = Write();
            var fields = write["update"]!["fields"]!.AsObject().Select(f => f.Key).OrderBy(k => k).ToList();

            Assert.Equal(["appVersion", "language", "platform", "reason", "text"], fields);
            Assert.Equal("createdAt", (string?)write["updateTransforms"]![0]!["fieldPath"]);
            Assert.Equal("REQUEST_TIME", (string?)write["updateTransforms"]![0]!["setToServerValue"]);
        }

        [Fact]
        public void IsACreateInTheSharedCollection()
        {
            var write = Write();

            Assert.Equal(
                "projects/proj/databases/(default)/documents/unanswered_questions/abc",
                (string?)write["update"]!["name"]);
            Assert.False((bool)write["currentDocument"]!["exists"]!);
        }

        [Fact]
        public void CarriesTheQuestionAndWhyItWasDeclined()
        {
            var fields = Write()["update"]!["fields"]!;

            Assert.Equal("what is the colour of gold", (string?)fields["text"]!["stringValue"]);
            Assert.Equal("sv", (string?)fields["language"]!["stringValue"]);
            Assert.Equal("no_match", (string?)fields["reason"]!["stringValue"]);
            Assert.Equal("windows", (string?)fields["platform"]!["stringValue"]);
        }

        [Fact]
        public void AMissingReasonIsSaidRatherThanLeftOut() =>
            Assert.Equal("unknown", (string?)Write(reason: null)["update"]!["fields"]!["reason"]!["stringValue"]);

        [Fact]
        public void ALongQuestionIsCutToWhatTheRuleAccepts()
        {
            var text = (string?)Write(new string('a', 900))["update"]!["fields"]!["text"]!["stringValue"];
            Assert.Equal(UnansweredQuestionDocument.MaxChars, text!.Length);
        }

        [Fact]
        public void TheCodecsDelimitersAreStripped() =>
            Assert.Equal("ab c", UnansweredQuestionDocument.Clean("  a\u001Db\u001E \u001Fc  "));
    }
}
