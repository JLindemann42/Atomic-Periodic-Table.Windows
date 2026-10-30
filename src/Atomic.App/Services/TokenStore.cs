using System;
using System.Linq;
using Windows.Security.Credentials;

namespace Atomic.App.Services
{
    /// <summary>
    /// Where the Firebase refresh token lives between sessions.
    ///
    /// <para><b>Only the refresh token is persisted.</b> The ID token is a bearer credential valid
    /// for an hour and is kept in memory for exactly that long; writing it anywhere would create a
    /// second copy to leak with no benefit, since it can always be minted again from the refresh
    /// token.</para>
    ///
    /// <para><see cref="PasswordVault"/> encrypts per Windows user account, so another user on the
    /// same machine cannot read it. The uid is the vault's UserName, which also makes "who is
    /// signed in" answerable without a network call.</para>
    /// </summary>
    public static class TokenStore
    {
        private const string Resource = "Atomic.Firebase";

        /// <summary>The signed-in Firebase uid, or null.</summary>
        public static string? SignedInUid => Find()?.UserName;

        /// <summary>The stored refresh token, or null when signed out.</summary>
        public static string? RefreshToken
        {
            get
            {
                var credential = Find();
                if (credential is null) return null;

                // The password is not populated until asked for - retrieving the list gives only
                // the metadata, which is easy to miss and reads as an empty token.
                credential.RetrievePassword();
                return credential.Password;
            }
        }

        public static void Save(string uid, string refreshToken)
        {
            Clear();

            var vault = new PasswordVault();
            vault.Add(new PasswordCredential(Resource, uid, refreshToken));
        }

        /// <summary>Forget the session. Safe to call when already signed out.</summary>
        public static void Clear()
        {
            try
            {
                var vault = new PasswordVault();
                foreach (var credential in vault.FindAllByResource(Resource).ToList())
                    vault.Remove(credential);
            }
            catch (Exception)
            {
                // FindAllByResource throws rather than returning empty when the resource has never
                // been written, which is the normal state before a first sign-in.
            }
        }

        private static PasswordCredential? Find()
        {
            try
            {
                var vault = new PasswordVault();
                return vault.FindAllByResource(Resource).FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
