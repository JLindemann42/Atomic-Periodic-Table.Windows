using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Atomic.Core.Analytics;

namespace Atomic.App.Services
{
    /// <summary>
    /// Sends events to the app's Firebase/GA4 property over the Measurement Protocol - the Windows
    /// counterpart of Android's <c>AnalyticsHelper</c> (D45, roadmap 0.7).
    ///
    /// <para><b>Configured by a file, not by source.</b> <c>firebase.analytics</c> beside the
    /// executable holds two lines, <c>measurement_id=G-…</c> and <c>api_secret=…</c>. The secret
    /// lets anyone write events into the property, so it stays out of the repository like
    /// <see cref="FirebaseSecrets"/>; without the file nothing is sent and nothing else changes.</para>
    ///
    /// <para><b>Off when the user says so.</b> The Settings switch is Android's "Usage Analytics",
    /// on by default as there. The client id is a random per-install GUID, not tied to the account,
    /// and no event carries anything the user typed.</para>
    /// </summary>
    public static class Analytics
    {
        public const string FileName = "firebase.analytics";

        private const string EnabledKey = "AnalyticsEnabled";
        private const string ClientIdKey = "AnalyticsClientId";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private static readonly Lazy<(string MeasurementId, string ApiSecret)?> Config = new(Read);

        /// <summary>The user's choice. Defaults to on, as on Android.</summary>
        public static bool IsEnabled
        {
            get => SettingsStore.Get(EnabledKey, true);
            set => SettingsStore.Set(EnabledKey, value);
        }

        /// <summary>Fire and forget. Never throws and never blocks the caller.</summary>
        public static void Log(string eventName, IEnumerable<KeyValuePair<string, object?>> parameters)
        {
            if (!IsEnabled || Config.Value is not { } config) return;

            var body = MeasurementProtocol.Body(ClientId(), eventName, parameters.ToList()).ToJsonString();
            var url = MeasurementProtocol.CollectUrl(config.MeasurementId, config.ApiSecret);
            _ = Send(url, body);
        }

        public static void Log(string eventName, params (string Key, object? Value)[] parameters) =>
            Log(eventName, parameters.Select(p => new KeyValuePair<string, object?>(p.Key, p.Value)));

        private static async System.Threading.Tasks.Task Send(string url, string body)
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var _ = await Http.PostAsync(url, content).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or System.Threading.Tasks.TaskCanceledException)
            {
                // Losing an event is better than surfacing an analytics failure to the user.
            }
        }

        private static string ClientId()
        {
            var id = SettingsStore.Get<string?>(ClientIdKey, null);
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                SettingsStore.Set(ClientIdKey, id);
            }
            return id;
        }

        private static (string, string)? Read()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, FileName);
                if (!File.Exists(path)) return null;

                var values = File.ReadAllLines(path)
                    .Select(line => line.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.Ordinal);

                return values.TryGetValue("measurement_id", out var id) && id.Length > 0 &&
                       values.TryGetValue("api_secret", out var secret) && secret.Length > 0
                    ? (id, secret)
                    : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
