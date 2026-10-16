using Atomic.Core.Sync;
using Xunit;

namespace Atomic.Core.Tests.Sync
{
    /// <summary>
    /// The guard that turns a recurring credential mistake into a readable failure.
    ///
    /// <para>Three candidate client ids were supplied during P9 and none was usable. Each would
    /// have failed differently and none obviously: a foreign client is rejected by
    /// <c>signInWithIdp</c> with an opaque error, and a Firebase App ID is not a client id at all.
    /// The worst case is the one that looks like success - signing in against another project
    /// yields a different uid, so sync runs, reports no error, and writes to a document Android
    /// will never read.</para>
    /// </summary>
    public sealed class FirebaseConfigTest
    {
        /// <summary>
        /// A config with a secret already supplied, so validation reaches the client-id checks
        /// rather than stopping at the missing secret.
        /// </summary>
        private static FirebaseConfig With(string clientId) =>
            FirebaseConfig.Default.WithSecret("GOCSPX-example") with { OAuthClientId = clientId };

        [Fact]
        public void TheShippedApiKeyIsTheVerifiedOne()
        {
            // Checked against identitytoolkit .../v1/projects?key=, which reported this project.
            Assert.Equal("315118082769", FirebaseConfig.Default.ProjectNumber);
            Assert.Equal("atomic---periodic-table-41532", FirebaseConfig.Default.ProjectId);
            Assert.NotEmpty(FirebaseConfig.Default.WebApiKey);
        }

        /// <summary>
        /// No client id has been supplied yet, so sync must report itself unconfigured rather than
        /// attempt a sign-in that cannot work.
        /// </summary>
        /// <summary>
        /// The committed client belongs to the Firebase project, which is the precondition that
        /// makes a Windows sign-in produce the same uid as Android.
        /// </summary>
        [Fact]
        public void TheCommittedClientBelongsToTheFirebaseProject() =>
            Assert.StartsWith(
                FirebaseConfig.Default.ProjectNumber + "-",
                FirebaseConfig.Default.OAuthClientId,
                System.StringComparison.Ordinal);

        /// <summary>
        /// Supplying the secret is the last step, and it completes the configuration - so the only
        /// thing missing from source is the one value that must not be in it.
        /// </summary>
        [Fact]
        public void SupplyingTheSecretCompletesTheConfiguration()
        {
            var configured = FirebaseConfig.Default.WithSecret("GOCSPX-example");

            Assert.Null(configured.Validate());
            Assert.True(configured.IsConfigured);
        }

        /// <summary>
        /// The secret is not committed - GitHub secret scanning reports the GOCSPX- prefix to
        /// Google, which revokes the credential, so committing it would break the feature it
        /// enables. This asserts the repository stays clean, which no reviewer would reliably
        /// notice by eye.
        /// </summary>
        [Fact]
        public void TheSecretIsNotInSourceControl()
        {
            Assert.Empty(FirebaseConfig.Default.OAuthClientSecret);
            Assert.False(FirebaseConfig.Default.IsConfigured);

            var reason = FirebaseConfig.Default.Validate();
            Assert.NotNull(reason);
            Assert.Contains("firebase.client_secret", reason!, System.StringComparison.Ordinal);
        }

        [Fact]
        public void AClientFromTheRightProjectIsAccepted()
        {
            var config = With("315118082769-abcdefghijklmnop.apps.googleusercontent.com");

            Assert.Null(config.Validate());
            Assert.True(config.IsConfigured);
        }

        /// <summary>The three that were actually supplied, each rejected for its own reason.</summary>
        [Theory]
        // Project 945214413416.
        [InlineData("945214413416-6i7sk6h30dtmeslo4krgf79lhnbogkk8.apps.googleusercontent.com", "945214413416")]
        // Project 482472870127.
        [InlineData("482472870127-m03vj0f793lppk142f7l5a8ivkoho70c.apps.googleusercontent.com", "482472870127")]
        public void AClientFromAnotherProjectIsRejectedAndNamesTheProject(string clientId, string expected)
        {
            var reason = With(clientId).Validate();

            Assert.NotNull(reason);
            Assert.Contains(expected, reason!, System.StringComparison.Ordinal);
            Assert.Contains("same user id as Android", reason!, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// A Firebase App ID carries the right project number, so a prefix check alone would let it
        /// through. It is not a client id.
        /// </summary>
        [Fact]
        public void AFirebaseAppIdIsRejectedEvenThoughItsProjectIsCorrect()
        {
            var reason = With("1:315118082769:web:2b7f7cfb29e6fb350743f0").Validate();

            Assert.NotNull(reason);
            Assert.Contains("not an OAuth client id", reason!, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// The project's web client is shape-valid and in the right project, so no check here can
        /// reject it - the difference is the client TYPE, which only the console and the token
        /// endpoint know. Recorded so the next reader does not reach for it again.
        /// </summary>
        [Fact]
        public void TheWebClientIsShapeValidAndStillTheWrongChoice()
        {
            var webClient = "315118082769-ivqq7fhga4pjja0t95iuansjs2vjdu9j.apps.googleusercontent.com";

            Assert.Null(With(webClient).Validate());
            Assert.NotEqual(webClient, FirebaseConfig.Default.OAuthClientId);
        }
    }
}
