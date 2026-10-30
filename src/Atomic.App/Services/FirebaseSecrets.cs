using System;
using System.IO;
using Atomic.Core.Sync;

namespace Atomic.App.Services
{
    /// <summary>
    /// Supplies the OAuth client secret, which is the one Firebase value not in source control.
    ///
    /// <para>Read from <c>firebase.client_secret</c> beside the executable - a single line holding
    /// the <c>GOCSPX-…</c> value. The csproj copies it to the output when it exists and
    /// <c>.gitignore</c> keeps it out of the repository.</para>
    ///
    /// <para><b>Why not just commit it.</b> The secret genuinely ships inside the binary, and
    /// Google documents installed-app secrets as not confidential - so the objection is not that it
    /// is exposed to users. It is that GitHub secret scanning recognises the <c>GOCSPX-</c> prefix
    /// and reports it to Google, which revokes the credential. Committing it would break the
    /// feature it exists to enable, and the fix would be to rotate and re-ship.</para>
    ///
    /// <para>Absent, sync reports itself unconfigured and every other feature is unaffected. Nobody
    /// building this repo without the file gets a broken app, only one without cloud sync.</para>
    /// </summary>
    public static class FirebaseSecrets
    {
        public const string FileName = "firebase.client_secret";

        private static FirebaseConfig? _cached;

        /// <summary>The shipped configuration, with the secret filled in when one is available.</summary>
        public static FirebaseConfig Config => _cached ??= FirebaseConfig.Default.WithSecret(Read());

        /// <summary>Whether cloud sync can run at all. Drives the account UI.</summary>
        public static bool IsConfigured => Config.IsConfigured;

        /// <summary>Why it cannot, for a diagnostics line. Null when it can.</summary>
        public static string? Unavailable => Config.Validate();

        private static string? Read()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, FileName);
                if (!File.Exists(path)) return null;

                // Trimmed, because a file written by an editor or a CI `echo` almost always ends
                // with a newline, and a trailing \n in the POST body is rejected as a bad secret
                // with an error that names nothing useful.
                return File.ReadAllText(path).Trim();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Unreadable is the same as absent: sync stays off rather than the app failing.
                return null;
            }
        }
    }
}
