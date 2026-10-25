using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Atomic.Core.Sync;

namespace Atomic.App.Services
{
    /// <summary>
    /// Sends a question the assistant could not answer to the developer - only when the user asks
    /// to, question by question (roadmap 0.7).
    ///
    /// <para>The FAQ promises that nothing typed in the chat leaves the device, so this is opt-in per
    /// question, behind a dialog that says exactly what is sent. Unauthenticated on purpose:
    /// signed-out users hit declines too, and a uid is exactly what the document must not carry.
    /// <see cref="UnansweredQuestionDocument"/> is what is sent; this is only the transport.</para>
    /// </summary>
    public static class UnansweredQuestions
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        public static string Clean(string question) => UnansweredQuestionDocument.Clean(question);

        /// <returns>Whether the question was stored. Never throws.</returns>
        public static async Task<bool> SendAsync(string question, string language, string? reason)
        {
            if (string.IsNullOrWhiteSpace(Clean(question))) return false;

            try
            {
                var config = FirebaseSecrets.Config;
                var body = UnansweredQuestionDocument.CommitBody(
                    config.ProjectId, Guid.NewGuid().ToString("N"), question, language, reason, AppVersion());
                var url = "https://firestore.googleapis.com/v1/projects/" + config.ProjectId +
                          "/databases/(default)/documents:commit?key=" + Uri.EscapeDataString(config.WebApiKey);

                using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(url, content).ConfigureAwait(true);
                return response.IsSuccessStatusCode;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                return false;
            }
        }

        private static string AppVersion() =>
            typeof(UnansweredQuestions).Assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
