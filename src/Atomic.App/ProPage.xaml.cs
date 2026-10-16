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

        // Store ids, in one place rather than repeated as literals through the file.
        private const string ProStoreId = "9NMMDNZHJKP4";
        private const string ProPlusStoreId = "9N506D1VGBPL";
        private const string ProUpgradeStoreId = "9MTF8DGJ688J";

        /// <summary>
        /// The Ultimate add-on: PRO+ on every platform, bought once.
        /// </summary>
        /// <remarks>
        /// <b>Replace this with the real product id once it exists in Partner Center.</b> Until the
        /// catalogue returns a product under this id the card stays collapsed, so an unfinished
        /// listing shows nothing rather than a button that cannot complete a purchase.
        /// </remarks>
        private const string UltimateStoreId = "ULTIMATE-STORE-ID-PENDING";

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
            new() { Title = "Element Overview", FreeCheckmark = "✅", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Favorite Bar", FreeCheckmark = "✅", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Element Notes", FreeCheckmark = "✅", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Isotopes", FreeCheckmark = "✅", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Dictionary", FreeCheckmark = "✅", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Save Calculations", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Save in Unit Converter", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Hardness Properties", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Young-, Bulk-, Shear Modulus", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Poisson's Constant, Sound of Speed", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Hazard of Materials", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Table of Nuclides", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Poisson's Ratio Table", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Constants Table", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Geology Table", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Emission Spectrum Table", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "60 lives in flashcards", FreeCheckmark = "❌", ProCheckmark = "✅", ProPlusCheckmark = "✅" },
            new() { Title = "Unlimited lives in flashcards", FreeCheckmark = "❌", ProCheckmark = "❌", ProPlusCheckmark = "✅" },
            new() { Title = "Additional flashcard games", FreeCheckmark = "❌", ProCheckmark = "❌", ProPlusCheckmark = "✅" },
            new() { Title = "Early Access to DEV Features", FreeCheckmark = "❌", ProCheckmark = "❌", ProPlusCheckmark = "✅" },
        };

        public ProPage()
        {
            this.InitializeComponent();

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(storeContext, hwnd);

            ProGradientStoryboard.Begin();

            _ = InitializeStoreAndLicensesAsync();
        }

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
                            || addOns.Products.ContainsKey(ProUpgradeStoreId);

            // Load Pro
            if (addOns.Products.TryGetValue(ProStoreId, out var pro))
            {
                proProduct = pro;
                Pro.Text = pro.Title;
                ProPrice.Text = pro.Price.FormattedPrice;
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
                ProPlusPrice.Text = proPlus.Price.FormattedPrice;
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

            // Ultimate. Absence is not an error the way the other three are - the product may
            // simply not be published yet - so it hides the card instead of reporting a fault.
            if (addOns.Products.TryGetValue(UltimateStoreId, out var ultimate))
            {
                var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

                ultimateProduct = ultimate;
                UltimatePrice.Text = ultimate.Price.FormattedPrice;
                BuyUltimateButton.Content = strings.Get(Atomic.Core.Localization.S.ultimate_buy);
                BuyUltimateButton.IsEnabled = true;
            }
        }

        private async Task CheckUserLicensesAsync()
        {
            try
            {
                var appLicense = await storeContext.GetAppLicenseAsync();

                bool hasProUpgrade = appLicense.AddOnLicenses.TryGetValue(ProUpgradeStoreId + "/0010", out var upgradeLicense) && upgradeLicense.IsActive;
                bool hasPro = appLicense.AddOnLicenses.TryGetValue(ProStoreId + "/0010", out var proLicense) && proLicense.IsActive;
                bool hasProPlus = appLicense.AddOnLicenses.TryGetValue(ProPlusStoreId + "/0010", out var proPlusLicense) && proPlusLicense.IsActive;
                bool hasUltimate = appLicense.AddOnLicenses.TryGetValue(UltimateStoreId + "/0010", out var ultimateLicense) && ultimateLicense.IsActive;

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
                    if (SettingsStore.HasUltimate)
                    {
                        BuyUltimateButton.IsEnabled = false;
                        BuyUltimateButton.Content = EntitlementRules.UltimateBadge;
                    }

                    if (isProPlusUser)
                    {
                        BuyProPlusButton.IsEnabled = false;
                        BuyProPlusButton.Content = "Upgraded to PRO+";
                        ProPrice.Text = "---";

                        BuyProButton.IsEnabled = false;
                        BuyProButton.Content = "Has PRO+ Version";
                        ProPlusPrice.Text = "---";

                        FreeButton.Content = "Has PRO+ Version";
                        FreePrice.Text = "---";

                        // Show full ProPlus price
                        if (proPlusProduct != null)
                        {
                            ProPlusPrice.Text = proPlusProduct.Price.FormattedPrice;
                        }
                    }
                    else if (isProUser)
                    {
                        BuyProButton.IsEnabled = false;
                        BuyProButton.Content = "Current Version";
                        ProPrice.Text = "---";

                        FreeButton.Content = "Has PRO Version";
                        FreePrice.Text = "---";

                        BuyProPlusButton.IsEnabled = true;

                        // Show upgrade price if available
                        if (proUpgradeProduct != null)
                        {
                            ProPlusPrice.Text = proUpgradeProduct.Price.FormattedPrice;
                            ProPlus.Text = "PRO+ Upgrade";
                        }
                        else if (proPlusProduct != null)
                        {
                            // fallback to full price if upgrade product missing
                            ProPlusPrice.Text = proPlusProduct.Price.FormattedPrice;
                        }
                    }
                    else
                    {
                        BuyProButton.IsEnabled = true;
                        BuyProButton.Content = "Buy Pro";

                        BuyProPlusButton.IsEnabled = true;

                        FreeButton.Content = "Free Version";

                        if (proPlusProduct != null)
                        {
                            ProPlusPrice.Text = proPlusProduct.Price.FormattedPrice;
                        }
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
            await PurchaseStoreItem(ultimateProduct, "Ultimate");
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
                    if (item.StoreId == UltimateStoreId)
                    {
                        SettingsStore.HasUltimate = true;
                        SettingsStore.IsProPlusUser = true;
                        _ = CloudSync.AutoSyncAsync(force: true);
                    }

                    if (item.StoreId == ProPlusStoreId || item.StoreId == ProUpgradeStoreId
                        || item.StoreId == UltimateStoreId)
                    {
                        SettingsStore.IsProPlusUser = true;

                        BuyProPlusButton.IsEnabled = false;
                        BuyProPlusButton.Content = "Upgraded to PRO+";

                        BuyProButton.IsEnabled = false;
                        BuyProButton.Content = "Has PRO+ Version";
                        FreeButton.Content = "Has PRO+ Version";
                    }
                    else
                    {
                        BuyProButton.IsEnabled = false;
                        BuyProButton.Content = "Current Version";
                        FreeButton.Content = "Has PRO Version";
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
