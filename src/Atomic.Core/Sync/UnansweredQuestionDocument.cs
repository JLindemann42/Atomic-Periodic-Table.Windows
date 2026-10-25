using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Atomic.Core.Sync
{
    /// <summary>
    /// The body that creates one <c>unanswered_questions/{id}</c> document (roadmap 0.7).
    ///
    /// <para>Kept apart from the HTTP call so the one thing that matters - exactly which fields
    /// leave the device - is tested. The collection is create-only, shared with Android, and its
    /// security rule accepts these six fields and nothing else: the question as typed, its
    /// language, the decline reason, the app version, the platform and a server timestamp. No uid,
    /// no device id.</para>
    ///
    /// <para>A <c>:commit</c> rather than a plain create, because a create cannot ask for the server
    /// time, and the rule insists <c>createdAt == request.time</c> so a client cannot backdate one.
    /// The <c>exists: false</c> precondition makes the write a create even though it names its
    /// document.</para>
    /// </summary>
    public static class UnansweredQuestionDocument
    {
        public const string Collection = "unanswered_questions";

        /// <summary>Longest question kept. The security rule rejects anything longer.</summary>
        public const int MaxChars = 500;

        /// <summary>
        /// Trimmed, capped, and without the three control characters the chat-action codec uses as
        /// delimiters - the question travels inside a chip's args before it is sent.
        /// </summary>
        public static string Clean(string question)
        {
            var kept = new string(question.Where(c => c is < '\u001D' or > '\u001F').ToArray()).Trim();
            return kept.Length <= MaxChars ? kept : kept[..MaxChars].TrimEnd();
        }

        /// <param name="documentId">A fresh random id; the precondition rejects a reused one.</param>
        public static JsonObject CommitBody(
            string projectId, string documentId, string question, string language, string? reason, string appVersion)
        {
            var name = $"projects/{projectId}/databases/(default)/documents/{Collection}/{documentId}";
            var fields = new Dictionary<string, JsonNode>
            {
                ["text"] = FirestoreValue.String(Clean(question)),
                ["language"] = FirestoreValue.String(language),
                ["reason"] = FirestoreValue.String(reason ?? "unknown"),
                ["appVersion"] = FirestoreValue.String(appVersion),
                ["platform"] = FirestoreValue.String("windows"),
            };

            var update = FirestoreValue.Document(fields);
            update["name"] = name;

            return new JsonObject
            {
                ["writes"] = new JsonArray(new JsonObject
                {
                    ["update"] = update,
                    ["updateTransforms"] = new JsonArray(new JsonObject
                    {
                        ["fieldPath"] = "createdAt",
                        ["setToServerValue"] = "REQUEST_TIME",
                    }),
                    ["currentDocument"] = new JsonObject { ["exists"] = false },
                }),
            };
        }
    }
}
