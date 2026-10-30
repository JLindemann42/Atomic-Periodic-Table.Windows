using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Atomic.Core.Sync
{
    /// <summary>
    /// The PKCE pair for one sign-in attempt.
    /// </summary>
    /// <param name="Verifier">Kept in memory; sent only in the token exchange.</param>
    /// <param name="Challenge">Sent in the authorization URL, where it is visible.</param>
    /// <param name="State">Echoed back by the server; a mismatch means the callback is not ours.</param>
    public sealed record PkceCodes(string Verifier, string Challenge, string State);

    /// <summary>
    /// The parts of the OAuth loopback flow that are pure, and therefore testable without a browser,
    /// a network or a Google project.
    /// </summary>
    /// <remarks>
    /// <para>The flow is RFC 8252 - loopback redirect, system browser, PKCE. It works here
    /// specifically because the app declares <c>runFullTrust</c> and so is not in an AppContainer:
    /// the loopback network-isolation restriction would otherwise block the redirect.
    /// <c>WebAuthenticationBroker</c> throws on WinUI 3 desktop, and WebView2 is out because Google
    /// returns <c>disallowed_useragent</c> for embedded webviews on the installed-app flow.</para>
    ///
    /// <para>No client secret is involved. For an installed app the client id is public by design,
    /// and PKCE is what stands in for a secret - which is why the verifier must never leave this
    /// process except in the token exchange.</para>
    /// </remarks>
    public static class OAuthFlow
    {
        public const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

        public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

        /// <summary>Identity Toolkit, which turns a Google ID token into a Firebase session.</summary>
        public const string SignInWithIdpEndpoint =
            "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp";

        public const string RefreshEndpoint = "https://securetoken.googleapis.com/v1/token";

        /// <summary>
        /// Reads the account's profile - name and picture - from a live ID token.
        /// </summary>
        /// <remarks>
        /// Needed because the refresh endpoint returns neither, so a restored session has no way to
        /// learn them, and a user who signed in before the app displayed a picture would never get
        /// one without signing out and back in.
        /// </remarks>
        public const string LookupEndpoint =
            "https://identitytoolkit.googleapis.com/v1/accounts:lookup";

        /// <summary>Only identity is needed; the app reads no Google user data beyond who they are.</summary>
        public const string Scopes = "openid email profile";

        /// <summary>Generate a fresh verifier, challenge and state.</summary>
        public static PkceCodes NewCodes()
        {
            // 64 bytes, per the plan. RFC 7636 allows 43-128 characters after encoding; 64 bytes
            // base64url-encodes to 86, comfortably inside that.
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));

            return new PkceCodes(verifier, challenge, state);
        }

        /// <summary>
        /// Base64url without padding, as RFC 7636 requires.
        /// </summary>
        /// <remarks>
        /// Plain base64 would be wrong twice over: <c>+</c> and <c>/</c> are not URL-safe, and the
        /// <c>=</c> padding is explicitly disallowed. Google rejects the request rather than
        /// mis-verifying it, but the failure arrives as a generic invalid_grant much later.
        /// </remarks>
        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');

        /// <summary>The loopback redirect for a port the listener has just been assigned.</summary>
        public static string RedirectUri(int port) =>
            "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/";

        /// <summary>Build the URL the system browser is sent to.</summary>
        public static string AuthorizationUrl(string clientId, string redirectUri, PkceCodes codes)
        {
            var query = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = Scopes,
                ["code_challenge"] = codes.Challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = codes.State,
            };

            var builder = new StringBuilder(AuthorizeEndpoint).Append('?');
            var first = true;
            foreach (var (key, value) in query)
            {
                if (!first) builder.Append('&');
                first = false;
                builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            }

            return builder.ToString();
        }

        /// <summary>What the browser sent back to the loopback listener.</summary>
        /// <param name="Code">The authorization code, when the user approved.</param>
        /// <param name="Error">Google's error code, when they did not.</param>
        public sealed record Callback(string? Code, string? State, string? Error);

        /// <summary>
        /// Read the query of the loopback request.
        /// </summary>
        /// <remarks>
        /// Nothing is trusted here beyond parsing; <see cref="IsValid"/> is what decides whether the
        /// callback is ours. Separated so the state check cannot be skipped by a caller that only
        /// looked at <c>Code</c>.
        /// </remarks>
        public static Callback ParseCallback(string? query)
        {
            if (string.IsNullOrEmpty(query)) return new Callback(null, null, null);

            var trimmed = query.StartsWith('?') ? query[1..] : query;
            string? code = null, state = null, error = null;

            foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var split = pair.Split('=', 2);
                if (split.Length != 2) continue;

                var value = Uri.UnescapeDataString(split[1].Replace('+', ' '));
                switch (split[0])
                {
                    case "code": code = value; break;
                    case "state": state = value; break;
                    case "error": error = value; break;
                }
            }

            return new Callback(code, state, error);
        }

        /// <summary>
        /// Whether a callback may be acted on.
        /// </summary>
        /// <remarks>
        /// The state must match what this attempt generated. Without that check any page the user
        /// happens to have open could drive a code into the listener - the loopback port is
        /// reachable by every process on the machine, so the state is the only thing tying the
        /// response to the request. Compared in fixed time, since it is a secret for the length of
        /// one sign-in.
        /// </remarks>
        public static bool IsValid(Callback callback, PkceCodes codes) =>
            callback.Error is null &&
            !string.IsNullOrEmpty(callback.Code) &&
            callback.State is { } state &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(codes.State));
    }
}
