using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Atomic.Core.Sync
{
    /// <summary>
    /// The Firestore REST wire format.
    ///
    /// <para>Firestore's REST API does not exchange plain JSON. Every field is wrapped in a
    /// one-key object naming its type - <c>{"integerValue": "42"}</c> - and the two traps below are
    /// the reason this is a class with tests rather than a few inline calls.</para>
    ///
    /// <list type="number">
    /// <item><b><c>integerValue</c> is a JSON string, not a number.</b> Writing it as a number is
    /// accepted by some client libraries and rejected by the REST API, and reading it as a number
    /// throws on every document another client wrote.</item>
    /// <item><b><c>timestampValue</c> must be RFC3339 UTC.</b> A local-offset stamp is rejected
    /// outright.</item>
    /// </list>
    ///
    /// <para>Pure: it turns objects into <see cref="JsonNode"/> and back, and knows nothing about
    /// HTTP. That is what lets the shape be tested without a network or a project.</para>
    /// </summary>
    public static class FirestoreValue
    {
        /// <summary>
        /// RFC3339 UTC with microsecond precision.
        ///
        /// <para>Six fractional digits, not .NET's default seven: Firestore's parser rejects the
        /// extra digit, and the tick it represents is below the resolution the service stores
        /// anyway.</para>
        /// </summary>
        private const string Rfc3339 = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

        public static JsonNode Integer(long value) =>
            new JsonObject
            {
                // A string. See the class remarks - this is the trap.
                ["integerValue"] = value.ToString(CultureInfo.InvariantCulture),
            };

        public static JsonNode Double(double value) =>
            new JsonObject { ["doubleValue"] = value };

        public static JsonNode String(string? value) =>
            value is null
                ? new JsonObject { ["nullValue"] = null }
                : new JsonObject { ["stringValue"] = value };

        public static JsonNode Boolean(bool value) =>
            new JsonObject { ["booleanValue"] = value };

        public static JsonNode Timestamp(DateTimeOffset value) =>
            new JsonObject
            {
                ["timestampValue"] = value.ToUniversalTime().ToString(Rfc3339, CultureInfo.InvariantCulture),
            };

        public static JsonNode Null() => new JsonObject { ["nullValue"] = null };

        public static JsonNode Array(IEnumerable<JsonNode> values) =>
            new JsonObject
            {
                ["arrayValue"] = new JsonObject
                {
                    ["values"] = new JsonArray(values.ToArray()),
                },
            };

        public static JsonNode Map(IReadOnlyDictionary<string, JsonNode> fields)
        {
            var map = new JsonObject();
            foreach (var (key, value) in fields) map[key] = value;
            return new JsonObject { ["mapValue"] = new JsonObject { ["fields"] = map } };
        }

        // ---- Reading ---------------------------------------------------------------------------

        /// <summary>
        /// Read an integer, whether it arrived as a string or a number.
        /// </summary>
        /// <remarks>
        /// Tolerant on the way in and strict on the way out. The REST API always sends a string, but
        /// documents written by the Admin SDK or the console can carry a number, and a reader that
        /// only handled one form would throw on the other user's data rather than on its own.
        /// </remarks>
        public static long? ReadInteger(JsonNode? value)
        {
            if (value?["integerValue"] is not { } raw) return null;

            return raw.GetValueKind() switch
            {
                JsonValueKind.String => long.TryParse(
                    raw.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null,
                JsonValueKind.Number => raw.GetValue<long>(),
                _ => null,
            };
        }

        public static double? ReadDouble(JsonNode? value)
        {
            // Firestore narrows a whole number to an integer on write, so a field that was a double
            // can come back as one. Reading only doubleValue silently loses every round figure.
            if (value?["doubleValue"] is { } raw && raw.GetValueKind() == JsonValueKind.Number)
                return raw.GetValue<double>();

            return ReadInteger(value);
        }

        public static string? ReadString(JsonNode? value) =>
            value?["stringValue"] is { } raw && raw.GetValueKind() == JsonValueKind.String
                ? raw.GetValue<string>()
                : null;

        public static bool? ReadBoolean(JsonNode? value) =>
            value?["booleanValue"] is { } raw &&
            raw.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                ? raw.GetValue<bool>()
                : null;

        public static DateTimeOffset? ReadTimestamp(JsonNode? value)
        {
            if (value?["timestampValue"] is not { } raw) return null;
            if (raw.GetValueKind() != JsonValueKind.String) return null;

            // Invariant and forced to UTC. Parsing with the ambient culture would read a Hijri
            // calendar under ur-PK, the same hazard StreakMath documents.
            return DateTimeOffset.TryParse(
                raw.GetValue<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;
        }

        public static IReadOnlyList<JsonNode?> ReadArray(JsonNode? value) =>
            value?["arrayValue"]?["values"] is JsonArray array
                ? array.ToList()
                : [];

        public static IReadOnlyDictionary<string, JsonNode?> ReadMap(JsonNode? value)
        {
            var fields = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            if (value?["mapValue"]?["fields"] is not JsonObject map) return fields;

            foreach (var (key, child) in map) fields[key] = child;
            return fields;
        }

        /// <summary>The <c>fields</c> of a whole document.</summary>
        public static IReadOnlyDictionary<string, JsonNode?> ReadDocument(JsonNode? document)
        {
            var fields = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            if (document?["fields"] is not JsonObject map) return fields;

            foreach (var (key, child) in map) fields[key] = child;
            return fields;
        }

        /// <summary>Wrap fields as a document body for a PATCH.</summary>
        public static JsonObject Document(IReadOnlyDictionary<string, JsonNode> fields)
        {
            var map = new JsonObject();
            foreach (var (key, value) in fields) map[key] = value;
            return new JsonObject { ["fields"] = map };
        }
    }
}
