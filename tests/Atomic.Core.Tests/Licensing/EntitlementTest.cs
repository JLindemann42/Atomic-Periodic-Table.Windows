using Atomic.Core.Licensing;
using Xunit;

namespace Atomic.Core.Tests.Licensing
{
    public class EntitlementRulesTest
    {
        // ---- What a set of licences means -------------------------------------------------------

        [Fact]
        public void NoLicencesMeansFree() =>
            Assert.Equal(Entitlement.Free, EntitlementRules.FromLicences(false, false, false));

        [Fact]
        public void ThePlainProAddOnGrantsProOnly()
        {
            var entitlement = EntitlementRules.FromLicences(hasPro: true, hasProPlus: false, hasProUpgrade: false);

            Assert.True(entitlement.IsPro);
            Assert.False(entitlement.IsProPlus);
        }

        /// <summary>
        /// Buying PRO+ outright means owning neither the plain PRO add-on nor the upgrade, so
        /// reading PRO from its own licence alone puts those users outside every PRO gate.
        /// </summary>
        [Fact]
        public void BuyingProPlusOutrightAlsoGrantsPro()
        {
            var entitlement = EntitlementRules.FromLicences(hasPro: false, hasProPlus: true, hasProUpgrade: false);

            Assert.True(entitlement.IsPro);
            Assert.True(entitlement.IsProPlus);
        }

        [Fact]
        public void TheUpgradeAddOnAlsoGrantsBoth()
        {
            var entitlement = EntitlementRules.FromLicences(hasPro: false, hasProPlus: false, hasProUpgrade: true);

            Assert.True(entitlement.IsPro);
            Assert.True(entitlement.IsProPlus);
        }

        [Fact]
        public void HasAnyProCoversEitherTier()
        {
            Assert.False(Entitlement.Free.HasAnyPro);
            Assert.True(new Entitlement(true, false).HasAnyPro);
            Assert.True(new Entitlement(false, true).HasAnyPro);
        }

        // ---- Reconciling with what is stored -----------------------------------------------------

        /// <summary>
        /// The defect this rule exists for: a successful Store query that returns no add-on
        /// licences is indistinguishable from one against an installation the Store has never heard
        /// of. Writing it over a paid entitlement meant opening the PRO page revoked PRO.
        /// </summary>
        [Fact]
        public void SilenceFromTheStoreNeverRevokes()
        {
            var stored = new Entitlement(IsPro: true, IsProPlus: true);

            Assert.Equal(stored, EntitlementRules.Reconcile(stored, answer: null));
        }

        [Fact]
        public void SilenceFromTheStoreNeverGrantsEither() =>
            Assert.Equal(Entitlement.Free, EntitlementRules.Reconcile(Entitlement.Free, answer: null));

        [Fact]
        public void ADefiniteAnswerGrants()
        {
            var answer = new Entitlement(true, true);

            Assert.Equal(answer, EntitlementRules.Reconcile(Entitlement.Free, answer));
        }

        /// <summary>
        /// A refund still takes effect - the asymmetry is about silence, not about downgrades.
        /// </summary>
        [Fact]
        public void ADefiniteAnswerCanRevoke()
        {
            var stored = new Entitlement(true, true);

            Assert.Equal(Entitlement.Free, EntitlementRules.Reconcile(stored, Entitlement.Free));
        }

        [Fact]
        public void ADefiniteAnswerCanDowngradeProPlusToPro()
        {
            var stored = new Entitlement(IsPro: true, IsProPlus: true);
            var answer = EntitlementRules.FromLicences(hasPro: true, hasProPlus: false, hasProUpgrade: false);

            Assert.Equal(new Entitlement(true, false), EntitlementRules.Reconcile(stored, answer));
        }

        // ---- Platform naming ---------------------------------------------------------------------

        [Fact]
        public void BadgesNameThePlatform()
        {
            Assert.Equal("Windows PRO+", EntitlementRules.BadgeFor(
                EntitlementRules.WindowsPlatform, EntitlementTier.ProPlus));
            Assert.Equal("Windows PRO", EntitlementRules.BadgeFor(
                EntitlementRules.WindowsPlatform, EntitlementTier.Pro));
            Assert.Equal("Android PRO+", EntitlementRules.BadgeFor(
                EntitlementRules.AndroidPlatform, EntitlementTier.ProPlus));
        }

        /// <summary>Free earns no badge - an empty pill is worse than none.</summary>
        [Fact]
        public void FreeHasNoBadge() =>
            Assert.Null(EntitlementRules.BadgeFor(EntitlementRules.WindowsPlatform, EntitlementTier.Free));

        [Theory]
        [InlineData(EntitlementTier.Free, "free")]
        [InlineData(EntitlementTier.Pro, "pro")]
        [InlineData(EntitlementTier.ProPlus, "pro+")]
        public void TiersRoundTripThroughTheWire(EntitlementTier tier, string wire)
        {
            Assert.Equal(wire, EntitlementRules.ToWire(tier));
            Assert.Equal(tier, EntitlementRules.FromWire(wire));
        }

        /// <summary>
        /// A tier this build does not know - one a future Android release invents - must read as
        /// free rather than throw, because it arrives from a document another platform writes.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("PRO+")]
        [InlineData("platinum")]
        public void AnUnknownWireTierReadsAsFree(string? wire) =>
            Assert.Equal(EntitlementTier.Free, EntitlementRules.FromWire(wire));

        [Fact]
        public void TierIsDerivedFromTheFlags()
        {
            Assert.Equal(EntitlementTier.Free, Entitlement.Free.Tier);
            Assert.Equal(EntitlementTier.Pro, Entitlement.Pro.Tier);
            Assert.Equal(EntitlementTier.ProPlus, Entitlement.ProPlus.Tier);
        }

        // ---- Ultimate ----------------------------------------------------------------------------

        /// <summary>
        /// Ultimate is bought once on whichever platform the user happens to be holding, so on this
        /// one it arrives through the account document with no local licence behind it.
        /// </summary>
        [Fact]
        public void UltimateGrantsProPlusWithoutALocalLicence() =>
            Assert.Equal(Entitlement.ProPlus, EntitlementRules.WithUltimate(Entitlement.Free, ultimate: true));

        /// <summary>
        /// No Ultimate seen is not Ultimate revoked - the local store licence still decides.
        /// </summary>
        [Fact]
        public void WithoutUltimateThePlatformTierStands()
        {
            Assert.Equal(Entitlement.Pro, EntitlementRules.WithUltimate(Entitlement.Pro, ultimate: false));
            Assert.Equal(Entitlement.Free, EntitlementRules.WithUltimate(Entitlement.Free, ultimate: false));
            Assert.Equal(Entitlement.ProPlus, EntitlementRules.WithUltimate(Entitlement.ProPlus, ultimate: false));
        }

        [Fact]
        public void TheUltimateAddOnGrantsProPlusFromLicencesToo()
        {
            var entitlement = EntitlementRules.FromLicences(false, false, false, hasUltimate: true);

            Assert.Equal(Entitlement.ProPlus, entitlement);
        }

        /// <summary>
        /// Running the check twice must not change the outcome - it runs on every visit to the page.
        /// </summary>
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ReconcilingIsIdempotent(bool isPro, bool isProPlus)
        {
            var stored = new Entitlement(isPro, isProPlus);

            var once = EntitlementRules.Reconcile(stored, null);
            var twice = EntitlementRules.Reconcile(once, null);

            Assert.Equal(stored, twice);
        }
    }
}
