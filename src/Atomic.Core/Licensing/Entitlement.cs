using System;

namespace Atomic.Core.Licensing
{
    /// <summary>What a platform's store has granted.</summary>
    public enum EntitlementTier
    {
        Free,
        Pro,
        ProPlus,
    }

    /// <summary>What the user has paid for.</summary>
    public readonly record struct Entitlement(bool IsPro, bool IsProPlus)
    {
        public static readonly Entitlement Free = new(false, false);

        public static readonly Entitlement Pro = new(true, false);

        public static readonly Entitlement ProPlus = new(true, true);

        /// <summary>True for either paid tier.</summary>
        public bool HasAnyPro => IsPro || IsProPlus;

        public EntitlementTier Tier =>
            IsProPlus ? EntitlementTier.ProPlus
            : IsPro ? EntitlementTier.Pro
            : EntitlementTier.Free;
    }

    /// <summary>
    /// Turns store licences and cloud grants into a stored entitlement.
    ///
    /// <para>Pure and in <c>Atomic.Core</c> because the rule that matters here cannot be tested
    /// through <c>StoreContext</c>: reproducing "the Store answered, but knows nothing about this
    /// installation" requires an unsigned build, no Store sign-in, or no network.</para>
    /// </summary>
    public static class EntitlementRules
    {
        /// <summary>
        /// The platform this build's store purchases belong to.
        /// </summary>
        /// <remarks>
        /// A tier is meaningless without it. PRO bought on Android and PRO bought on Windows are
        /// separate purchases from separate stores, and the shared account document has to be able
        /// to say which one it is describing - otherwise a user who owns PRO+ on their phone and
        /// nothing here cannot be told apart from one who owns it here.
        /// </remarks>
        public const string WindowsPlatform = "windows";

        public const string AndroidPlatform = "android";

        /// <summary>
        /// The wire form of a tier: <c>free</c>, <c>pro</c> or <c>pro+</c>.
        /// </summary>
        /// <remarks>
        /// Lowercase and stable. These strings go into the document Android reads, so they are wire
        /// identifiers rather than anything a user sees - the display name is a resource lookup.
        /// </remarks>
        public static string ToWire(EntitlementTier tier) => tier switch
        {
            EntitlementTier.ProPlus => "pro+",
            EntitlementTier.Pro => "pro",
            EntitlementTier.Free => "free",
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null),
        };

        /// <summary>Parse a wire tier, treating anything unrecognised as free.</summary>
        public static EntitlementTier FromWire(string? wire) => wire switch
        {
            "pro+" => EntitlementTier.ProPlus,
            "pro" => EntitlementTier.Pro,
            _ => EntitlementTier.Free,
        };

        /// <summary>
        /// What a set of add-on licences means.
        /// </summary>
        /// <remarks>
        /// <b>PRO+ implies PRO.</b> Buying PRO+ outright means owning neither the plain PRO add-on
        /// nor the PRO-to-PRO+ upgrade, so reading PRO from its own licence alone leaves those
        /// users outside every PRO gate.
        /// </remarks>
        public static Entitlement FromLicences(
            bool hasPro, bool hasProPlus, bool hasProUpgrade, bool hasUltimate = false)
        {
            var isProPlus = hasProPlus || hasProUpgrade || hasUltimate;
            return new Entitlement(isProPlus || hasPro, isProPlus);
        }

        /// <summary>
        /// Combine what is stored with what the store said.
        /// </summary>
        /// <param name="stored">What the app currently believes.</param>
        /// <param name="answer">
        /// The store's answer, or <c>null</c> when it had none to give - the query failed, the
        /// catalogue could not be read, or the app licence is not active.
        /// </param>
        /// <remarks>
        /// <para><b>A licence check may grant, and may only revoke when it actually knows.</b>
        /// A query that succeeds and returns no add-on licences is indistinguishable from one
        /// against an installation the store has never heard of - a sideloaded build, a machine
        /// with nobody signed in, a first run behind a captive portal. Treating that silence as
        /// "not owned" is what made a paying user's PRO disappear the moment they opened the PRO
        /// page.</para>
        ///
        /// <para>The asymmetry is deliberate and it is not symmetric in cost: leaving a refunded
        /// user with access until the next definite answer is a rounding error, while locking a
        /// paying customer out of what they bought is the worst thing this code can do.</para>
        /// </remarks>
        public static Entitlement Reconcile(Entitlement stored, Entitlement? answer) =>
            answer ?? stored;

        /// <summary>
        /// The badge for a platform's tier - "Windows PRO+", "Android PRO" - or null when free.
        /// </summary>
        /// <remarks>
        /// <para>Named by platform because the tier alone is ambiguous across two stores. A user who
        /// owns PRO+ on their phone and nothing on this machine, and one who owns PRO+ here, are
        /// the same word without it - and they are entitled to different things.</para>
        ///
        /// <para>Composed rather than translated. "Windows", "Android", "PRO" and "PRO+" are all
        /// product names that stay in Latin script in every language the app ships, so building the
        /// string here avoids twelve resources that would each hold the same two words.</para>
        /// </remarks>
        public static string? BadgeFor(string platform, EntitlementTier tier)
        {
            if (tier == EntitlementTier.Free) return null;

            var name = platform switch
            {
                WindowsPlatform => "Windows",
                AndroidPlatform => "Android",
                _ => platform,
            };

            return name + " " + (tier == EntitlementTier.ProPlus ? "PRO+" : "PRO");
        }

        /// <summary>
        /// The badge for Ultimate, which is not a platform tier - it is every platform at once.
        /// </summary>
        public const string UltimateBadge = "Ultimate";

        /// <summary>
        /// Raise an entitlement to PRO+ when Ultimate has been bought on any platform.
        /// </summary>
        /// <remarks>
        /// <para>Ultimate is the one product that crosses stores. It is bought once, on whichever
        /// platform the user happens to be holding, and the account document carries it to the
        /// others - so this only ever grants. A missing or false flag means "no Ultimate seen",
        /// which is not the same as "Ultimate revoked", and the local store licence remains the
        /// authority for this platform's own tier.</para>
        /// </remarks>
        public static Entitlement WithUltimate(Entitlement platformTier, bool ultimate) =>
            ultimate ? Entitlement.ProPlus : platformTier;
    }
}
