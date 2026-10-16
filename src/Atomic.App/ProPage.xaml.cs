using Atomic.App.Services;
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

        private async Task InitializeStoreAndLicensesAsync()
        {
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

            // Load Pro
            if (addOns.Products.TryGetValue("9NMMDNZHJKP4", out var pro))
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
            if (addOns.Products.TryGetValue("9N506D1VGBPL", out var proPlus))
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
            if (addOns.Products.TryGetValue("9MTF8DGJ688J", out var upgrade))
            {
                proUpgradeProduct = upgrade;
            }
            else
            {
                ShowError("Missing PRO Upgrade", "Store item 'Pro Upgrade Version' was not found.");
            }
        }

        private async Task CheckUserLicensesAsync()
        {
            try
            {
                var appLicense = await storeContext.GetAppLicenseAsync();

                bool isProUser = false;
                bool isProPlusUser = false;

                bool hasProUpgrade = appLicense.AddOnLicenses.TryGetValue("9MTF8DGJ688J/0010", out var upgradeLicense) && upgradeLicense.IsActive;
                bool hasPro = appLicense.AddOnLicenses.TryGetValue("9NMMDNZHJKP4/0010", out var proLicense) && proLicense.IsActive;
                bool hasProPlus = appLicense.AddOnLicenses.TryGetValue("9N506D1VGBPL/0010", out var proPlusLicense) && proPlusLicense.IsActive;

                isProPlusUser = hasProUpgrade || hasProPlus;

                // PRO+ always implies PRO. Buying PRO+ outright means owning neither the plain PRO
                // add-on nor the PRO->PRO+ upgrade, so deriving IsProUser from hasPro alone left
                // those users with IsProUser == false. The purchase handler set it to true, but
                // this method overwrote it on the next launch and every PRO gate closed again.
                isProUser = isProPlusUser || hasPro;

                SettingsStore.IsProUser = isProUser;
                SettingsStore.IsProPlusUser = isProPlusUser;

                DispatcherQueue.TryEnqueue(() =>
                {
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

                    if (item.StoreId == "9N506D1VGBPL" || item.StoreId == "9MTF8DGJ688J")
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
