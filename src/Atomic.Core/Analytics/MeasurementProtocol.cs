using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Atomic.Core.Analytics
{
    /// <summary>
    /// The request body for Google Analytics 4's Measurement Protocol - how Windows reports to the
    /// same Firebase/GA4 property Android's SDK reports to (roadmap 0.7).
    ///
    /// <para>Pure, so the conversion rules are tested: the ones that matter are Android's, so events
    /// from both platforms aggregate. Nulls are dropped rather than sent as "null", booleans go as 1/0
    /// because GA4 has no boolean type, and strings are cut at GA4's 100-character limit.</para>
    /// </summary>
    public static class MeasurementProtocol
    {
        public const string Endpoint = "https://www.google-analytics.com/mp/collect";

        /// <summary>GA4 rejects longer parameter values.</summary>
        public const int MaxValueLength = 100;

        public static string CollectUrl(string measurementId, string apiSecret) =>
            Endpoint + "?measurement_id=" + Uri.EscapeDataString(measurementId) +
            "&api_secret=" + Uri.EscapeDataString(apiSecret);

        public static JsonObject Body(
            string clientId, string eventName, IEnumerable<KeyValuePair<string, object?>> parameters)
        {
            var values = new JsonObject();
            foreach (var (key, value) in parameters)
            {
                if (Value(value) is { } node) values[key] = node;
            }

            // Without engagement time, GA4 counts the event but not the user as active.
            values["engagement_time_msec"] = 1;

            return new JsonObject
            {
                ["client_id"] = clientId,
                ["non_personalized_ads"] = true,
                ["events"] = new JsonArray(new JsonObject
                {
                    ["name"] = eventName,
                    ["params"] = values,
                }),
            };
        }

        private static JsonNode? Value(object? value) => value switch
        {
            null => null,
            bool b => b ? 1 : 0,
            int i => i,
            long l => l,
            double d => d,
            string s => s.Length <= MaxValueLength ? s : s[..MaxValueLength],
            _ => value.ToString() is { } text ? Value(text) : null,
        };
    }
}
