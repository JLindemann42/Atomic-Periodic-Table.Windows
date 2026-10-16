using Atomic.App.Services;
using Atomic.Core.Licensing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Services.Store;
using Windows.Storage;
using WinRT.Interop;

namespace Atomic.App
{
    public sealed partial class ProPage : Page
    {
        private StoreContext storeContext = StoreContext.GetDefault();

        private StoreProduct proProduct;
        private StoreProduct proPlusProduct;
        private StoreProduct proUpgradeProduct;
        private StoreProduct ultimateProduct;
        private StoreProduct ultimateUpgradeProduct;

        // Store ids, in one place rather than repeated as literals through the file.
        private const string ProStoreId = "9NMMDNZHJKP4";
        private const string ProPlusStoreId = "9N506D1VGBPL";
        private const string ProUpgradeStoreId = "9MTF8DGJ688J";

        /// <summary>
        /// The Ultimate add-ons. Partner Center calls them "PRO PLUS ULTIMATE" and
        /// "PRO PLUS ULTIMATE UPGRADE"; these are their Store ids.
        /// </summary>
        /// <remarks>
        /// <para>Store ids, like the three above, because that is what keys both the catalogue
        /// dictionary and <c>AddOnLicenses</c>. The Partner Center product id is a different
        /// string - it surfaces as <see cref="StoreProduct.InAppOfferToken"/> - and passing one to
        /// <c>Products.TryGetValue</c> finds nothing at all, which looks exactly like an
        /// unpublished product.</para>
        ///
        /// <para>Two of them because a PRO+ owner upgrades at a reduced price rather than paying
        /// the full amount again - the same shape as the PRO to PRO+ path handled above.</para>
        /// </remarks>
        private const string UltimateStoreId = "9P8D5MG71B9X";

        private const string UltimateUpgradeStoreId = "9N69V9JQQL9D";

        /// <summary>
        /// Whether the Store answered the catalogue query well enough to be believed about licences.
        /// </summary>
        /// <remarks>
        /// A catalogue that came back with an error, or without a single one of the three known
        /// add-ons in it, is a Store that has nothing to say about this installation. Its silence
        /// about licences then means nothing either - see <see cref="EntitlementRules.Reconcile"/>.
        /// </remarks>
        private bool storeAnswered;

        public ObservableCollection<FeatureComparison> FeatureList { get; } = new()
        {
            new() { Title = Text(Atomic.Core.Localization.S.feature_element_overview), FreeCheckmark = "✅", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.favorite_bar), FreeCheckmark = "✅", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_element_notes), FreeCheckmark = "✅", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.iso), FreeCheckmark = "✅", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.Dictionary), FreeCheckmark = "✅", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_save_calculations), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_save_unit_converter), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.hardness_properties), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_young_bulk_shear), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_poisson_sound), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_hazard_materials), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.nuc), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_poisson_table), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.con), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.geo), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.emi), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_sixty_lives), FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_unlimited_lives), FreeCheckmark = "❌", ProCheckmark = "❌", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_additional_games), FreeCheckmark = "❌", ProCheckmark = "❌", ProPlusCheckmark = "✅" },
            new() { Title = Text(Atomic.Core.Localization.S.feature_early_access), FreeCheckmark = "❌", ProCheckmark = "❌", ProPlusCheckmark = "✅" },
        };

        public ProPage()
        {
            this.InitializeComponent();

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(storeContext, hwnd);

            ProGradientStoryboard.Begin();

            _ = InitializeStoreAndLicensesAsync();
        }

        /// <summary>
        /// One translated string, in the app's current language.
        /// </summary>
        /// <remarks>
        /// Static so the feature-comparison list can use it from its field initialiser. That list
        /// is built once per page instance, which is why a language change rebuilds the frame -
        /// see NavigationService.
        /// </remarks>
        private static string Text(string key) =>
            ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current)).Get(key);

        public static bool IsProUser => SettingsStore.IsProUser;

        public static bool IsProPlus => SettingsStore.IsProPlusUser;

        /// <summary>
        /// The Ultimate card's text.
        /// </summary>
        /// <remarks>
        /// In code rather than markup because the card is built before the catalogue answers, and
        /// its title is the product name - "Ultimate" is not translated in any language, so it is
        /// a literal rather than a resource that would hold the same word twelve times.
        /// </remarks>
        private void ApplyUltimateStrings()
        {
            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

            Ultimate.Text = "Ultimate";
            UltimateDescription.Text = strings.Get(Atomic.Core.Localization.S.ultimate_description);
            UltimateBullets.Text = strings.Get(Atomic.Core.Localization.S.ultimate_bullets);
            UltimateBadge.Text = strings.Get(Atomic.Core.Localization.S.ultimate_badge);

            // Until the catalogue answers, the card is a placeholder: it describes the tier and
            // says when, rather than offering a button that cannot complete a purchase.
            BuyUltimateButton.Content = strings.Get(Atomic.Core.Localization.S.ultimate_coming_soon);
            BuyUltimateButton.IsEnabled = false;
        }

        private async Task InitializeStoreAndLicensesAsync()
        {
            ApplyUltimateStrings();

            await GetProProductsAsync();
            await CheckUserLicensesAsync();
        }

        private async Task GetProProductsAsync()
        {

            string[] filterList = new[] { "Durable" };
            StoreProductQueryResult addOns = await storeContext.GetAssociatedStoreProductsAsync(filterList);

            if (addOns.ExtendedError != null)
            {
                Utils.ReportExtendedError(addOns.ExtendedError);
                return;
            }

            // Recognising any one of the three is enough: it proves the query reached a Store that
            // knows this app, which is the only thing the licence check needs from here.
            storeAnswered = addOns.Products.ContainsKey(ProStoreId)
                            || addOns.Products.ContainsKey(ProPlusStoreId)
                            || addOns.Products.ContainsKey(ProUpgradeStoreId)
                            || addOns.Products.ContainsKey(UltimateStoreId);

            // Load Pro
            if (addOns.Products.TryGetValue(ProStoreId, out var pro))
            {
                proProduct = pro;
                Pro.Text = pro.Title;
            }
            else
            {
                ShowError("Missing PRO", "Store item 'PRO Version' was not found.");
            }

            // Load ProPlus
            if (addOns.Products.TryGetValue(ProPlusStoreId, out var proPlus))
            {
                proPlusProduct = proPlus;
                ProPlus.Text = proPlus.Title;
            }
            else
            {
                ShowError("Missing PRO+", "Store item 'ProPlus Version' was not found.");
            }

            // Load Pro Upgrade
            if (addOns.Products.TryGetValue(ProUpgradeStoreId, out var upgrade))
            {
                proUpgradeProduct = upgrade;
            }
            else
            {
                ShowError("Missing PRO Upgrade", "Store item 'Pro Upgrade Version' was not found.");
            }

            addOns.Products.TryGetValue(UltimateStoreId, out ultimateProduct);
            addOns.Products.TryGetValue(UltimateUpgradeStoreId, out ultimateUpgradeProduct);

            ApplyPrices();
            ApplyUltimateOffer();
        }

        /// <summary>
        /// Which Ultimate product to sell, and what the card says about it.
        /// </summary>
        /// <remarks>
        /// <para>Run twice: once when the catalogue arrives, and again after the licence check,
        /// because the choice depends on whether the user already owns PRO+. The catalogue query
        /// completes first, so on its own it decides using whatever the last session stored - which
        /// offered the full price to a PRO+ owner whose entitlement had only just been confirmed.</para>
        ///
        /// <para>Absence is not an error the way a missing PRO listing is: the card simply keeps
        /// saying "coming soon".</para>
        /// </remarks>
        /// <summary>Shown in place of a price for something the user already owns.</summary>
        private const string NoPrice = "---";

        /// <summary>
        /// A price, or nothing to pay.
        /// </summary>
        /// <remarks>
        /// A price on a card for something already bought reads as an invitation to buy it twice.
        /// The rule was previously spelled out per branch, and the PRO+ branch set "---" and then
        /// overwrote it with the full price four lines later - so a PRO+ owner was quoted for PRO+.
        /// </remarks>
        private static void ShowPrice(TextBlock target, StoreProduct product, bool owned) =>
            target.Text = owned || product is null ? NoPrice : product.Price.FormattedPrice;

        /// <summary>
        /// Every price on the page, from what is owned and what is for sale.
        /// </summary>
        /// <remarks>
        /// Run when the catalogue arrives and again after the licence check, for the reason in
        /// <see cref="ApplyUltimateOffer"/>: the catalogue completes first, so on its own it prices
        /// the page against the previous session's tier.
        /// </remarks>
        private void ApplyPrices()
        {
            // Free is owned by definition.
            FreePrice.Text = NoPrice;

            ShowPrice(ProPrice, proProduct, SettingsStore.IsProUser);

            // A PRO owner buys the upgrade rather than the full product, so that is the price to
            // show them - and neither, once they hold PRO+.
            var proPlusOffer = SettingsStore.IsProUser && proUpgradeProduct is not null
                ? proUpgradeProduct
                : proPlusProduct;

            ShowPrice(ProPlusPrice, proPlusOffer, SettingsStore.IsProPlusUser);
        }

        private void ApplyUltimateOffer()
        {
            var offer = SettingsStore.IsProPlusUser && ultimateUpgradeProduct is not null
                ? ultimateUpgradeProduct
                : ultimateProduct;

            if (offer is null) return;

            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

            // Name the offer. A reduced upgrade price beside a card headed "Ultimate" is a number
            // with nothing to attach it to, and the PRO+ card above relabels itself the same way.
            Ultimate.Text = offer.StoreId == UltimateUpgradeStoreId
                ? strings.Get(Atomic.Core.Localization.S.ultimate_upgrade_title)
                : "Ultimate";

            ShowPrice(UltimatePrice, offer, SettingsStore.HasUltimate);
            BuyUltimateButton.Content = strings.Get(Atomic.Core.Localization.S.ultimate_buy);
            BuyUltimateButton.IsEnabled = !SettingsStore.HasUltimate;
        }

        private async Task CheckUserLicensesAsync()
        {
            try
            {
                var appLicense = await storeContext.GetAppLicenseAsync();

                bool hasProUpgrade = appLicense.AddOnLicenses.TryGetValue(ProUpgradeStoreId + "/0010", out var upgradeLicense) && upgradeLicense.IsActive;
                bool hasPro = appLicense.AddOnLicenses.TryGetValue(ProStoreId + "/0010", out var proLicense) && proLicense.IsActive;
                bool hasProPlus = appLicense.AddOnLicenses.TryGetValue(ProPlusStoreId + "/0010", out var proPlusLicense) && proPlusLicense.IsActive;
                // Either product grants it - the upgrade and the outright purchase are two ways
                // to own the same thing.
                bool hasUltimateOutright = appLicense.AddOnLicenses.TryGetValue(UltimateStoreId + "/0010", out var ultimateLicense) && ultimateLicense.IsActive;
                bool hasUltimateUpgrade = appLicense.AddOnLicenses.TryGetValue(UltimateUpgradeStoreId + "/0010", out var ultimateUpgradeLicence) && ultimateUpgradeLicence.IsActive;
                bool hasUltimate = hasUltimateOutright || hasUltimateUpgrade;

                if (hasUltimate) SettingsStore.HasUltimate = true;

                // Null means "the Store had nothing to say", which is not the same as "nothing is
                // owned". A sideloaded build, a machine with nobody signed into the Store, or a
                // first run behind a captive portal all return a perfectly successful query with
                // no add-on licences in it - and this method used to write that straight over a
                // paid entitlement, so opening this page was enough to lose PRO.
                Entitlement? answer =
                    storeAnswered && appLicense.IsActive
                        ? EntitlementRules.FromLicences(hasPro, hasProPlus, hasProUpgrade, hasUltimate)
                        : null;

                // Ultimate applied last, and only ever upward: bought on the phone it arrives
                // through the account document, where this machine's Store has no licence to show
                // for it and would otherwise answer a confident "free".
                var entitlement = EntitlementRules.WithUltimate(
                    EntitlementRules.Reconcile(SettingsStore.Entitlement, answer),
                    SettingsStore.HasUltimate);

                bool isProUser = entitlement.IsPro;
                bool isProPlusUser = entitlement.IsProPlus;

                SettingsStore.IsProUser = isProUser;
                SettingsStore.IsProPlusUser = isProPlusUser;

                DispatcherQueue.TryEnqueue(() =>
                {
                    // Now that the tier is known, the offer may have changed from the full
                    // product to the reduced upgrade.
                    ApplyUltimateOffer();

                    if (SettingsStore.HasUltimate)
                    {
                        BuyUltimateButton.IsEnabled = false;
                        BuyUltimateButton.Content = EntitlementRules.UltimateBadge;
                    }

                    // Prices in one place, from ownership - see ApplyPrices. The branches below
                    // set only what the buttons say.
                    ApplyPrices();

                    if (isProPlusUser)
                    {
                        BuyProPlusButton.IsEnabled = false;
                        BuyProPlusButton.Content = Text(Atomic.Core.Localization.S.pro_state_upgraded);

                        BuyProButton.IsEnabled = false;
                        BuyProButton.Content = Text(Atomic.Core.Localization.S.pro_plus_version);

                        FreeButton.Content = Text(Atomic.Core.Localization.S.pro_plus_version);
                    }
                    else if (isProUser)
                    {
                        BuyProButton.IsEnabled = false;
                        BuyProButton.Content = Text(Atomic.Core.Localization.S.pro_state_current);

                        FreeButton.Content = Text(Atomic.Core.Localization.S.pro_title);

                        BuyProPlusButton.IsEnabled = true;

                        // A PRO owner is being sold the upgrade, so the card says so.
                        if (proUpgradeProduct != null)
                            ProPlus.Text = Text(Atomic.Core.Localization.S.popup_action_button);
                    }
                    else
                    {
                        BuyProButton.IsEnabled = true;
                        BuyProButton.Content = Text(Atomic.Core.Localization.S.get_pro);

                        BuyProPlusButton.IsEnabled = true;

                        FreeButton.Content = Text(Atomic.Core.Localization.S.pro_card_free_title);
                    }
                });
            }
            catch (Exception ex)
            {
                Utils.ReportExtendedError(ex);
            }
        }

        private async void BuyProVersion_Click(object sender, RoutedEventArgs e)
        {
            await PurchaseStoreItem(proProduct, "Pro Version");
        }

        private async void BuyProPlusVersion_Click(object sender, RoutedEventArgs e)
        {
            // If user owns Pro, buy upgrade product; else buy full ProPlus
            if (SettingsStore.IsProUser && proUpgradeProduct != null)
            {
                await PurchaseStoreItem(proUpgradeProduct, "ProPlus Upgrade");
            }
            else
            {
                await PurchaseStoreItem(proPlusProduct, "ProPlus Version");
            }
        }

        private async void BuyUltimate_Click(object sender, RoutedEventArgs e)
        {
            // PRO+ owners get the reduced upgrade, matching how PRO owners reach PRO+.
            var item = SettingsStore.IsProPlusUser && ultimateUpgradeProduct is not null
                ? ultimateUpgradeProduct
                : ultimateProduct;

            await PurchaseStoreItem(item, "Ultimate");
        }

        private async Task PurchaseStoreItem(StoreProduct item, string label)
        {
            if (item == null)
            {
                ShowError("Purchase Error", "Store item not loaded.");
                return;
            }

            StorePurchaseResult result = await storeContext.RequestPurchaseAsync(item.StoreId);

            if (result.ExtendedError != null)
            {
                Utils.ReportExtendedError(result.ExtendedError);
                return;
            }

            switch (result.Status)
            {
                case StorePurchaseStatus.AlreadyPurchased:
                case StorePurchaseStatus.Succeeded:
                    SettingsStore.IsProUser = true;

                    // Ultimate is recorded separately as well as granting PRO+, because that flag
                    // is what the account document carries to the other platforms - the tier alone
                    // would tell the phone only that this machine has PRO+, not that it may too.
                    var boughtUltimate =
                        item.StoreId == UltimateStoreId || item.StoreId == UltimateUpgradeStoreId;

                    if (boughtUltimate)
                    {
                        SettingsStore.HasUltimate = true;
                        SettingsStore.IsProPlusUser = true;
                        _ = CloudSync.AutoSyncAsync(force: true);
                    }

                    if (item.StoreId == ProPlusStoreId || item.StoreId == ProUpgradeStoreId
                        || boughtUltimate)
                    {
                        SettingsStore.IsProPlusUser = true;

                        BuyProPlusButton.IsEnabled = false;
                        BuyProPlusButton.Content = Text(Atomic.Core.Localization.S.pro_state_upgraded);

                        BuyProButton.IsEnabled = false;
                        BuyProButton.Content = Text(Atomic.Core.Localization.S.pro_plus_version);
                        FreeButton.Content = Text(Atomic.Core.Localization.S.pro_plus_version);
                    }
                    else
                    {
                        BuyProButton.IsEnabled = false;
                        BuyProButton.Content = Text(Atomic.Core.Localization.S.pro_state_current);
                        FreeButton.Content = Text(Atomic.Core.Localization.S.pro_title);
                    }

                    AppNotificationManager.Default.Show(
                        new AppNotificationBuilder()
                            .AddText("Purchase Complete")
                            .AddText($"You now own the {label}.")
                            .BuildNotification());
                    break;

                case StorePurchaseStatus.NotPurchased:
                    ShowError("Not Purchased", "Purchase was canceled or not completed.");
                    break;
                case StorePurchaseStatus.NetworkError:
                    ShowError("Network Error", "A network error occurred.");
                    break;
                case StorePurchaseStatus.ServerError:
                    ShowError("Server Error", "A server error occurred.");
                    break;
                default:
                    ShowError("Unknown Error", "An unknown error occurred.");
                    break;
            }
        }

        private void ShowError(string title, string message)
        {
            AppNotificationManager.Default.Show(
                new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(message)
                    .BuildNotification());
        }

        public static class Utils
        {
            static int IAP_E_UNEXPECTED = unchecked((int)0x803f6107);

            public static void ReportExtendedError(Exception extendedError)
            {
                string message = extendedError.HResult == IAP_E_UNEXPECTED
                    ? "Not properly configured"
                    : $"ExtendedError: {extendedError.Message}";

                AppNotificationManager.Default.Show(
                    new AppNotificationBuilder()
                        .AddText("Error")
                        .AddText(message)
                        .BuildNotification());
            }
        }
    }

    public class FeatureComparison
    {
        public string Title { get; set; }
        public string FreeCheckmark { get; set; }
        public string ProCheckmark { get; set; }
        public string ProPlusCheckmark { get; set; }
    }
}
