using System;
using System.Globalization;

namespace Atomic.Core.Sync
{
    /// <summary>
    /// The Firebase project this app syncs with.
    ///
    /// <para>All three values ship inside the app, and that is correct rather than a compromise.
    /// Google requires a <c>client_secret</c> at the token endpoint for <b>every</b> client type -
    /// verified by probing the endpoint, which answers "client_secret is missing" for a desktop
    /// client just as it does for a web one - so PKCE does not replace it. What differs is that for
    /// an installed app Google documents the secret as <b>not confidential</b>: it is extractable
    /// from any shipped desktop binary, and the security of the flow rests on PKCE plus the
    /// loopback redirect, not on it.</para>
    ///
    /// <para>They are checked in rather than fetched so a build is reproducible. Rotating the
    /// secret means shipping a new build.</para>
    /// </summary>
    /// <param name="ProjectId">The Firebase project, e.g. <c>atomic---periodic-table-41532</c>.</param>
    /// <param name="ProjectNumber">
    /// The numeric project id. Every OAuth client in the project carries it as the prefix of its
    /// client id, which is what <see cref="Validate"/> checks.
    /// </param>
    public sealed record FirebaseConfig(
        string ProjectId,
        string ProjectNumber,
        string WebApiKey,
        string OAuthClientId,
        string OAuthClientSecret)
    {
        /// <summary>
        /// The shipped configuration.
        /// </summary>
        /// <remarks>
        /// <para>Getting here took five attempts, and <see cref="Validate"/> exists because four of
        /// them were wrong in ways that would not have failed obviously: two clients from other GCP
        /// projects (945214413416 and 482472870127), a Firebase <i>App ID</i> rather than a client
        /// id, and the project's own <b>web</b> client - right project, but a confidential client
        /// whose secret an installed app must not ship.</para>
        ///
        /// <para>The values below come from an <c>"installed"</c> (Desktop app) client created in
        /// <see cref="ProjectId"/>, which is the type that accepts a loopback redirect.</para>
        /// </remarks>
        public static readonly FirebaseConfig Default = new(
            ProjectId: "atomic---periodic-table-41532",
            ProjectNumber: "315118082769",
            // Verified against identitytoolkit .../v1/projects?key=, which reported project number
            // 315118082769 and atomic---periodic-table-41532.firebaseapp.com as an authorized
            // domain - the same Firebase project Android uses, which is what makes the uid match.
            WebApiKey: "AIzaSyDNPscfJNeg6wm1cFW6-V5sQm0sm-kfFa0",
            OAuthClientId: "315118082769-usej47uob14p1r9k4rd5euasqeajftoo.apps.googleusercontent.com",
            // Supplied at startup, never committed. See the note on the class.
            OAuthClientSecret: "");

        /// <summary>The same configuration with a secret supplied by the host.</summary>
        public FirebaseConfig WithSecret(string? secret) =>
            this with { OAuthClientSecret = secret ?? string.Empty };

        /// <summary>Whether sync can be attempted at all.</summary>
        public bool IsConfigured => Validate() is null;

        /// <summary>
        /// Why sync cannot run, or null when it can.
        /// </summary>
        /// <remarks>
        /// <para>The project-number check is the whole point. A client id from another project
        /// either fails at <c>signInWithIdp</c> with an opaque error, or - if that project's Firebase
        /// were targeted instead - succeeds while writing to a document Android will never read.
        /// The second outcome looks like working software, which is why this is enforced rather
        /// than documented.</para>
        /// </remarks>
        public string? Validate()
        {
            if (string.IsNullOrWhiteSpace(OAuthClientSecret))
            {
                return "No OAuth client secret is configured. Google requires one at the token " +
                       "endpoint even for a desktop client; it is shipped, not withheld.";
            }

            if (string.IsNullOrWhiteSpace(OAuthClientSecret))
            {
                return "No OAuth client secret has been supplied. Google requires one at the token "
                     + "endpoint even for a desktop client. It is not committed - put it in "
                     + "firebase.client_secret next to the app, which .gitignore excludes.";
            }

            if (string.IsNullOrWhiteSpace(OAuthClientId))
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"No OAuth client id is configured. Create a Desktop app client in the " +
                    $"'{ProjectId}' project; its id will start '{ProjectNumber}-'.");
            }

            if (!OAuthClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal))
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{OAuthClientId}' is not an OAuth client id. A Firebase App ID " +
                    $"(1:{ProjectNumber}:web:...) identifies an app registration, not a client.");
            }

            var prefix = ProjectNumber + "-";
            if (!OAuthClientId.StartsWith(prefix, StringComparison.Ordinal))
            {
                var supplied = OAuthClientId.Split('-', 2)[0];
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"The OAuth client belongs to project {supplied}, but Firebase project " +
                    $"'{ProjectId}' is {ProjectNumber}. Sign-in would not produce the same user id " +
                    $"as Android, so every device would sync to its own document.");
            }

            return null;
        }
    }
}
