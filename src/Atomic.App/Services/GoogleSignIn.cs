using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Atomic.Core.Sync;

namespace Atomic.App.Services
{
    /// <summary>A live Firebase session.</summary>
    /// <param name="IdToken">Bearer token for Firestore. Valid an hour; never persisted.</param>
    public sealed record FirebaseSession(
        string Uid, string IdToken, string RefreshToken, DateTimeOffset ExpiresAt, string? Email)
    {
        /// <summary>
        /// Refreshed at 55 minutes rather than 60.
        ///
        /// <para>A token that expires mid-request fails the request, and the five-minute margin
        /// covers both clock skew between this machine and Google and a slow upload.</para>
        /// </summary>
        public bool NeedsRefresh => DateTimeOffset.UtcNow >= ExpiresAt - TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Google sign-in by RFC 8252: loopback redirect, system browser, PKCE.
    ///
    /// <para>The shape is forced. <c>WebAuthenticationBroker</c> throws on WinUI 3 desktop, and
    /// Google answers <c>disallowed_useragent</c> for WebView2 on the installed-app flow. Loopback
    /// works here because the app declares <c>runFullTrust</c> and so is not in an AppContainer,
    /// where network isolation would block it.</para>
    /// </summary>
    public static class GoogleSignIn
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>How long the user has to finish in the browser before the listener gives up.</summary>
        private static readonly TimeSpan ConsentTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Run the interactive flow. Opens the user's browser and waits for the redirect.
        /// </summary>
        /// <exception cref="InvalidOperationException">Configuration is incomplete or Google refused.</exception>
        public static async Task<FirebaseSession> SignInAsync(CancellationToken cancellation = default)
        {
            var config = FirebaseSecrets.Config;
            if (config.Validate() is { } problem) throw new InvalidOperationException(problem);

            var codes = OAuthFlow.NewCodes();

            // Port 0 lets the OS pick a free port; a fixed one would collide with whatever else is
            // listening and fail for reasons the user cannot act on.
            using var listener = new HttpListener();
            var port = FreePort();
            var redirectUri = OAuthFlow.RedirectUri(port);
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            try
            {
                Browse(OAuthFlow.AuthorizationUrl(config.OAuthClientId, redirectUri, codes));

                var callback = await AwaitCallbackAsync(listener, cancellation).ConfigureAwait(false);

                if (!OAuthFlow.IsValid(callback, codes))
                {
                    throw new InvalidOperationException(
                        callback.Error is { } error
                            ? "Google returned '" + error + "'."
                            : "The sign-in response did not match this request and was ignored.");
                }

                var google = await ExchangeCodeAsync(config, callback.Code!, codes.Verifier, redirectUri)
                    .ConfigureAwait(false);

                return await SignInToFirebaseAsync(config, google).ConfigureAwait(false);
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>Mint a new ID token from a stored refresh token, without user interaction.</summary>
        public static async Task<FirebaseSession?> RestoreAsync()
        {
            var refreshToken = TokenStore.RefreshToken;
            var uid = TokenStore.SignedInUid;
            if (refreshToken is null || uid is null) return null;

            var config = FirebaseSecrets.Config;
            if (!config.IsConfigured) return null;

            try
            {
                var response = await PostFormAsync(
                    OAuthFlow.RefreshEndpoint + "?key=" + Uri.EscapeDataString(config.WebApiKey),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = refreshToken,
                    }).ConfigureAwait(false);

                var idToken = response["id_token"]?.GetValue<string>();
                var newRefresh = response["refresh_token"]?.GetValue<string>() ?? refreshToken;
                if (idToken is null) return null;

                // Google rotates refresh tokens; storing the new one is what stops a session dying
                // after the old one is retired.
                TokenStore.Save(uid, newRefresh);

                var session = new FirebaseSession(
                    uid, idToken, newRefresh, ExpiryFrom(response), Email: null);

                await EnsureProfileAsync(config, session).ConfigureAwait(false);
                return session;
            }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException)
            {
                // A revoked or expired refresh token means signed out, not broken.
                return null;
            }
        }

        /// <summary>
        /// Fill in the picture and name if they are not already known.
        /// </summary>
        /// <remarks>
        /// <para>The interactive flow captures both, so this is a no-op for anyone who signs in on
        /// this build. It exists for the session that was established before the app had a use for
        /// them: <c>securetoken</c> returns only tokens, so without a lookup those users would keep
        /// the placeholder glyph until they happened to sign out and in again.</para>
        ///
        /// <para>Failure is silent. A missing avatar is not worth failing a sync over.</para>
        /// </remarks>
        private static async Task EnsureProfileAsync(FirebaseConfig config, FirebaseSession session)
        {
            if (UserProfile.PhotoUrl.Length > 0) return;

            try
            {
                var response = await PostJsonAsync(
                    OAuthFlow.LookupEndpoint + "?key=" + Uri.EscapeDataString(config.WebApiKey),
                    new JsonObject { ["idToken"] = session.IdToken }).ConfigureAwait(false);

                if (response["users"] is not JsonArray { Count: > 0 } users) return;

                UserProfile.Remember(
                    users[0]?["photoUrl"]?.GetValue<string>(),
                    users[0]?["displayName"]?.GetValue<string>());
            }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException)
            {
            }
        }

        public static void SignOut()
        {
            TokenStore.Clear();
            UserProfile.Clear();
        }

        // ---- Steps -----------------------------------------------------------------------------

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static void Browse(string url) =>
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        private static async Task<OAuthFlow.Callback> AwaitCallbackAsync(
            HttpListener listener, CancellationToken cancellation)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(ConsentTimeout);

            // GetContextAsync has no cancellation overload, so the token is honoured by stopping
            // the listener - which completes the pending call.
            await using var registration = timeout.Token.Register(listener.Stop).ConfigureAwait(false);

            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                throw new InvalidOperationException("Sign-in timed out or was cancelled.");
            }

            var callback = OAuthFlow.ParseCallback(context.Request.Url?.Query);
            await RespondAsync(context, callback.Error is null).ConfigureAwait(false);
            return callback;
        }

        /// <summary>
        /// Answer the browser so the user is not left on a blank tab.
        /// </summary>
        /// <remarks>
        /// Deliberately a self-contained page with no resources: the listener stops immediately
        /// after, so anything it referenced would fail to load.
        /// </remarks>
        private static async Task RespondAsync(HttpListenerContext context, bool success)
        {
            var body = Encoding.UTF8.GetBytes(
                "<!doctype html><meta charset=\"utf-8\"><title>Atomic</title>" +
                "<body style=\"font-family:Segoe UI,system-ui,sans-serif;padding:3rem;text-align:center\">" +
                (success
                    ? "<h2>Signed in</h2><p>You can close this tab and return to Atomic.</p>"
                    : "<h2>Sign-in cancelled</h2><p>You can close this tab.</p>") +
                "</body>");

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            context.Response.Close();
        }

        private static async Task<JsonNode> ExchangeCodeAsync(
            FirebaseConfig config, string code, string verifier, string redirectUri) =>
            await PostFormAsync(
                OAuthFlow.TokenEndpoint,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["code"] = code,
                    ["client_id"] = config.OAuthClientId,
                    // Required even for an installed client - Google answers "client_secret is
                    // missing" without it. Not confidential; see FirebaseSecrets.
                    ["client_secret"] = config.OAuthClientSecret,
                    ["code_verifier"] = verifier,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = redirectUri,
                }).ConfigureAwait(false);

        /// <summary>
        /// Trade the Google ID token for a Firebase session.
        /// </summary>
        /// <remarks>
        /// <c>localId</c> here is the Firebase uid, and it matches Android only because the API key
        /// and the OAuth client belong to the same project. That is the P0 precondition, and it is
        /// the one thing about this flow that fails silently rather than loudly.
        /// </remarks>
        private static async Task<FirebaseSession> SignInToFirebaseAsync(
            FirebaseConfig config, JsonNode googleTokens)
        {
            var googleIdToken = googleTokens["id_token"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Google did not return an ID token.");

            var response = await PostJsonAsync(
                OAuthFlow.SignInWithIdpEndpoint + "?key=" + Uri.EscapeDataString(config.WebApiKey),
                new JsonObject
                {
                    ["postBody"] = "id_token=" + googleIdToken + "&providerId=google.com",
                    ["requestUri"] = "http://localhost",
                    ["returnSecureToken"] = true,
                    ["returnIdpCredential"] = true,
                }).ConfigureAwait(false);

            var uid = response["localId"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Firebase did not return a user id.");
            var idToken = response["idToken"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Firebase did not return an ID token.");
            var refreshToken = response["refreshToken"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Firebase did not return a refresh token.");

            TokenStore.Save(uid, refreshToken);

            // photoUrl and displayName come back because the authorization request asks for the
            // "profile" scope. The refresh endpoint returns neither, so this is the only point in
            // the flow where they can be captured.
            UserProfile.Remember(
                response["photoUrl"]?.GetValue<string>(),
                response["displayName"]?.GetValue<string>());

            var session = new FirebaseSession(
                uid, idToken, refreshToken, ExpiryFrom(response), response["email"]?.GetValue<string>());

            await EnsureProfileAsync(config, session).ConfigureAwait(false);
            return session;
        }

        private static DateTimeOffset ExpiryFrom(JsonNode response)
        {
            // Firestore sends expiresIn as a string of seconds; the refresh endpoint uses
            // expires_in. Either way it is a string, not a number.
            var raw = response["expiresIn"]?.GetValue<string>()
                ?? response["expires_in"]?.GetValue<string>();

            var seconds = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 3600;

            return DateTimeOffset.UtcNow.AddSeconds(seconds);
        }

        // ---- Transport -------------------------------------------------------------------------

        private static Task<JsonNode> PostFormAsync(string url, Dictionary<string, string> form) =>
            SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form),
            });

        private static Task<JsonNode> PostJsonAsync(string url, JsonObject body) =>
            SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            });

        private static async Task<JsonNode> SendAsync(HttpRequestMessage request)
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var parsed = JsonNode.Parse(body);

            if (!response.IsSuccessStatusCode)
            {
                // Google's error body is far more useful than the status code - "client_secret is
                // missing" against a bare 400 tells you what to fix.
                var message = parsed?["error"]?["message"]?.GetValue<string>()
                    ?? parsed?["error_description"]?.GetValue<string>()
                    ?? parsed?["error"]?.GetValue<string>()
                    ?? response.StatusCode.ToString();

                throw new InvalidOperationException(message);
            }

            return parsed ?? throw new InvalidOperationException("The response was not JSON.");
        }
    }
}
