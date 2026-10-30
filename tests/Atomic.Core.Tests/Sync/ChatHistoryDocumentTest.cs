using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Atomic.Core.Sync;
using Xunit;

namespace Atomic.Core.Tests.Sync
{
    /// <summary>
    /// The chat history shared with Android: <c>users/{uid}.chats</c>.
    ///
    /// <para>The document below is the shape Android's <c>ChatHistoryManager</c> writes, field for
    /// field. Everything here is about two promises: Windows reads what the phone wrote, and a Windows
    /// write never takes away anything the phone put there.</para>
    /// </summary>
    public class ChatHistoryDocumentTest
    {
        /// <summary>Two conversations as Android saves them, plus a field Android might add later.</summary>
        private const string AndroidDocument = """
            {"fields":{
              "xp":{"integerValue":"500"},
              "chats":{"arrayValue":{"values":[
                {"mapValue":{"fields":{
                  "id":{"stringValue":"older"},
                  "title":{"stringValue":"Vad är guld?"},
                  "timestamp":{"integerValue":"1760000000000"},
                  "language":{"stringValue":"sv"},
                  "pinned":{"booleanValue":true},
                  "messages":{"arrayValue":{"values":[
                    {"mapValue":{"fields":{
                      "id":{"stringValue":"m1"},
                      "text":{"stringValue":"Vad är guld?"},
                      "isFromUser":{"booleanValue":true},
                      "timestamp":{"integerValue":"1759999999000"},
                      "rating":{"integerValue":"5"}}}},
                    {"mapValue":{"fields":{
                      "id":{"stringValue":"m2"},
                      "text":{"stringValue":"Guld är ett grundämne."},
                      "isFromUser":{"booleanValue":false},
                      "timestamp":{"integerValue":"1760000000000"}}}}
                  ]}}}}},
                {"mapValue":{"fields":{
                  "id":{"stringValue":"newer"},
                  "title":{"stringValue":"Tell me about iron"},
                  "timestamp":{"integerValue":"1760500000000"},
                  "language":{"stringValue":"en"},
                  "messages":{"arrayValue":{"values":[]}}}}}
              ]}}}}
            """;

        private static IReadOnlyList<ChatHistorySession> Read(string json) =>
            ChatHistoryDocument.FromDocument(JsonNode.Parse(json));

        private static ChatHistorySession Session(string id, long timestamp, int messages = 1) =>
            new(id, "t", timestamp, "en",
                Enumerable.Range(0, messages)
                    .Select(i => new ChatHistoryMessage($"{id}-{i}", "x", i % 2 == 0, timestamp))
                    .ToList());

        // ---- Reading what the phone wrote ---------------------------------------------------------

        [Fact]
        public void AndroidsConversationsAreRead()
        {
            var sessions = Read(AndroidDocument);

            Assert.Equal(2, sessions.Count);

            // Newest first, as Android lists them.
            Assert.Equal("newer", sessions[0].Id);

            var older = sessions[1];
            Assert.Equal("Vad är guld?", older.Title);
            Assert.Equal("sv", older.Language);
            Assert.Equal(1760000000000, older.Timestamp);
            Assert.Equal(2, older.Messages.Count);
            Assert.True(older.Messages[0].IsFromUser);
            Assert.False(older.Messages[1].IsFromUser);
            Assert.Equal("Guld är ett grundämne.", older.Messages[1].Text);
        }

        [Fact]
        public void ADocumentWithNoChatsHasNoHistory()
        {
            Assert.Empty(ChatHistoryDocument.FromDocument(null));
            Assert.Empty(Read("""{"fields":{"xp":{"integerValue":"1"}}}"""));
        }

        /// <summary>
        /// The array progress sync used to write - a list of strings, or nothing - reads as no
        /// conversations rather than as an error, so a document it damaged still opens.
        /// </summary>
        [Fact]
        public void AnArrayOfStringsReadsAsNothing()
        {
            Assert.Empty(Read("""{"fields":{"chats":{"arrayValue":{"values":[{"stringValue":"old"}]}}}}"""));
            Assert.Empty(Read("""{"fields":{"chats":{"arrayValue":{}}}}"""));
        }

        /// <summary>A message with no author is the user's, as Android defaults it.</summary>
        [Fact]
        public void AMessageWithNoAuthorIsTheUsers()
        {
            var sessions = Read("""
                {"fields":{"chats":{"arrayValue":{"values":[{"mapValue":{"fields":{
                  "id":{"stringValue":"a"},
                  "messages":{"arrayValue":{"values":[{"mapValue":{"fields":{
                    "text":{"stringValue":"hi"}}}}]}}}}}]}}}}
                """);

            Assert.True(sessions.Single().Messages.Single().IsFromUser);
        }

        // ---- Writing it back ---------------------------------------------------------------------

        /// <summary>Integers go out as JSON strings, which is what the REST API requires.</summary>
        [Fact]
        public void TheWrittenShapeIsAndroids()
        {
            var body = ChatHistoryDocument.ToDocument(
            [
                new ChatHistorySession("s", "Title", 1760000000000, "fil",
                [
                    new ChatHistoryMessage("m", "Kumusta", true, 1760000000001),
                ]),
            ]);

            var session = body["fields"]!["chats"]!["arrayValue"]!["values"]![0]!["mapValue"]!["fields"]!;
            Assert.Equal("s", session["id"]!["stringValue"]!.GetValue<string>());
            Assert.Equal("1760000000000", session["timestamp"]!["integerValue"]!.GetValue<string>());
            Assert.Equal("fil", session["language"]!["stringValue"]!.GetValue<string>());

            var message = session["messages"]!["arrayValue"]!["values"]![0]!["mapValue"]!["fields"]!;
            Assert.True(message["isFromUser"]!["booleanValue"]!.GetValue<bool>());
            Assert.Equal("1760000000001", message["timestamp"]!["integerValue"]!.GetValue<string>());

            // Nothing invented: no actions or cards on a message that had none.
            Assert.Null(message["actions"]);
            Assert.Null(message["cards"]);
        }

        /// <summary>The PATCH touches chats and nothing else, so progress and Android's fields survive it.</summary>
        [Fact]
        public void ThePatchMasksChatsAlone()
        {
            Assert.Equal(["chats"], ChatHistoryDocument.FieldPaths);
            Assert.Equal("?updateMask.fieldPaths=chats", ChatHistoryDocument.UpdateMaskQuery());

            var body = ChatHistoryDocument.ToDocument([]);
            Assert.Equal(["chats"], body["fields"]!.AsObject().Select(p => p.Key));
        }

        /// <summary>
        /// The promise that matters most. A field Android writes that this build has never heard of -
        /// on a conversation, on a message - is still there after Windows writes the array back.
        /// </summary>
        [Fact]
        public void AFieldAndroidAddedSurvivesAWindowsWrite()
        {
            var written = ChatHistoryDocument.ToDocument(Read(AndroidDocument));
            var older = written["fields"]!["chats"]!["arrayValue"]!["values"]!
                .AsArray()
                .Single(v => v!["mapValue"]!["fields"]!["id"]!["stringValue"]!.GetValue<string>() == "older")!
                ["mapValue"]!["fields"]!;

            Assert.True(older["pinned"]!["booleanValue"]!.GetValue<bool>());
            Assert.Equal("5",
                older["messages"]!["arrayValue"]!["values"]![0]!["mapValue"]!["fields"]!["rating"]!["integerValue"]!
                    .GetValue<string>());
        }

        /// <summary>
        /// And it survives when Windows changed the conversation, not only when it passed it through:
        /// the newer local copy is written on top of the values the cloud copy was read from.
        /// </summary>
        [Fact]
        public void AFieldAndroidAddedSurvivesAWindowsEditToThatConversation()
        {
            var cloud = Read(AndroidDocument);
            var older = cloud.Single(s => s.Id == "older");

            // This device has the same conversation, one message longer and saved later - with no raw
            // values of its own, as a conversation loaded from local storage has.
            var local = new ChatHistorySession(older.Id, older.Title, older.Timestamp + 1000, "sv",
                [.. older.Messages.Select(m => m with { Raw = null }),
                 new ChatHistoryMessage("m3", "Och silver?", true, older.Timestamp + 1000)]);

            var merged = ChatHistoryDocument.Merge([local], cloud);
            var written = ChatHistoryDocument.ToDocument(merged);
            var session = written["fields"]!["chats"]!["arrayValue"]!["values"]!
                .AsArray()
                .Single(v => v!["mapValue"]!["fields"]!["id"]!["stringValue"]!.GetValue<string>() == "older")!
                ["mapValue"]!["fields"]!;

            Assert.Equal(3, session["messages"]!["arrayValue"]!["values"]!.AsArray().Count);
            Assert.True(session["pinned"]!["booleanValue"]!.GetValue<bool>());
            Assert.Equal("5",
                session["messages"]!["arrayValue"]!["values"]![0]!["mapValue"]!["fields"]!["rating"]!["integerValue"]!
                    .GetValue<string>());
        }

        /// <summary>Windows' source buttons and chart card travel with a message and come back.</summary>
        [Fact]
        public void WindowsExtrasRoundTrip()
        {
            var session = new ChatHistorySession("s", "t", 1, "en",
            [
                new ChatHistoryMessage("m", "Gold's density is 19.3", false, 1) { Actions = "A", Cards = "C" },
            ]);

            var read = ChatHistoryDocument.FromDocument(
                JsonNode.Parse(ChatHistoryDocument.ToDocument([session]).ToJsonString()));

            var message = read.Single().Messages.Single();
            Assert.Equal("A", message.Actions);
            Assert.Equal("C", message.Cards);
        }

        // ---- Titles ------------------------------------------------------------------------------

        /// <summary>Android's rule, exactly, or the title flips with whichever device saved last.</summary>
        [Theory]
        [InlineData(null, "New Chat")]
        [InlineData("", "New Chat")]
        [InlineData("What is gold?", "What is gold?")]
        [InlineData("1234567890123456789012345678901234567890", "1234567890123456789012345678901234567890")]
        [InlineData("12345678901234567890123456789012345678901", "1234567890123456789012345678901234567...")]
        public void TheTitleIsAndroids(string? firstUserMessage, string expected) =>
            Assert.Equal(expected, ChatHistoryDocument.Title(firstUserMessage));

        // ---- Merging -----------------------------------------------------------------------------

        [Fact]
        public void ConversationsFromBothDevicesAreKept()
        {
            var merged = ChatHistoryDocument.Merge([Session("windows", 2)], [Session("android", 1)]);

            Assert.Equal(["windows", "android"], merged.Select(s => s.Id));
        }

        [Fact]
        public void TheNewerSaveOfOneConversationWins()
        {
            var merged = ChatHistoryDocument.Merge(
                [Session("same", 100, messages: 2)],
                [Session("same", 200, messages: 4)]);

            Assert.Equal(200, merged.Single().Timestamp);
            Assert.Equal(4, merged.Single().Messages.Count);
        }

        [Fact]
        public void OnATieTheLongerConversationWins()
        {
            var merged = ChatHistoryDocument.Merge(
                [Session("same", 100, messages: 5)],
                [Session("same", 100, messages: 3)]);

            Assert.Equal(5, merged.Single().Messages.Count);
        }

        /// <summary>Android keeps its twenty newest, and so must a Windows write.</summary>
        [Fact]
        public void TheListIsCappedAtTwentyNewestFirst()
        {
            var local = Enumerable.Range(0, 15).Select(i => Session($"w{i}", 1000 + i)).ToList();
            var cloud = Enumerable.Range(0, 15).Select(i => Session($"a{i}", 2000 + i)).ToList();

            var merged = ChatHistoryDocument.Merge(local, cloud);

            Assert.Equal(ChatHistoryDocument.MaxSessions, merged.Count);
            Assert.Equal("a14", merged[0].Id);
            Assert.True(merged.Zip(merged.Skip(1)).All(p => p.First.Timestamp >= p.Second.Timestamp));
        }

        /// <summary>A conversation with no id cannot be matched; it is kept, not dropped.</summary>
        [Fact]
        public void AConversationWithNoIdIsKept()
        {
            var merged = ChatHistoryDocument.Merge([], [Session("", 5)]);

            Assert.Single(merged);
        }

        [Fact]
        public void MergingTheResultAgainChangesNothing()
        {
            var once = ChatHistoryDocument.Merge(
                [Session("a", 10, 3), Session("b", 30)],
                [Session("a", 20, 2), Session("c", 5)]);

            var twice = ChatHistoryDocument.Merge(once, once);

            Assert.False(ChatHistoryDocument.DiffersFrom(twice, once));
        }

        /// <summary>Opening the history costs no write when nothing new was said on this device.</summary>
        [Fact]
        public void NothingNewMeansNothingToWrite()
        {
            var cloud = Read(AndroidDocument);

            Assert.False(ChatHistoryDocument.DiffersFrom(ChatHistoryDocument.Merge([], cloud), cloud));
            Assert.True(ChatHistoryDocument.DiffersFrom(
                ChatHistoryDocument.Merge([Session("new", 9_999_999_999_999)], cloud), cloud));
        }
    }
}
