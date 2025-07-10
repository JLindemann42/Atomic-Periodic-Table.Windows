using Atomic_PeriodicTable;
using Atomic_PeriodicTable.Tables;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;

namespace Atomic_WinUI
{
    public sealed partial class MainWindow : Window
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hInstance, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        private const int WM_SETICON = 0x80;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        public string BreadcrumbHeader { get; private set; } = "Periodic Table";
        public static MainWindow Instance { get; private set; }

        private MicaController micaController;
        private SystemBackdropConfiguration backdropConfiguration;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();
            Closed += MainWindow_Closed;
            ExtendsContentIntoTitleBar = true;

            RootGrid.Loaded += (s, e) =>
            {
                RootGrid.ActualThemeChanged += RootGrid_ActualThemeChanged;
                UpdateTitleBarButtonColors(RootGrid.ActualTheme);
            };

            SetWindowIcon();
            RestoreWindowSize();

            contentFrame.Navigated += ContentFrame_Navigated;
            contentFrame.Navigate(typeof(PeriodicTablePage));
            nvSample.Header = CreateBreadcrumb("Periodic Table");
            nvSample.SelectedItem = PeriodicTablePageItem;

            if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
            {
                ProPageItem.Content = "PRO Member";
            }
        }

        private void SetWindowIcon()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "atomic_icon_transparent.ico");
            IntPtr hIcon = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x00000010 | 0x00000002);
            SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, hIcon);
            SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG, hIcon);
        }

        private void RestoreWindowSize()
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.TryGetValue("WindowWidth", out object widthObj) &&
                settings.Values.TryGetValue("WindowHeight", out object heightObj) &&
                widthObj is int width && heightObj is int height)
            {
                appWindow.Resize(new SizeInt32(width, height));
            }
            else
            {
                appWindow.Resize(new SizeInt32(1400, 900));
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var size = appWindow.Size;
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["WindowWidth"] = size.Width;
            settings.Values["WindowHeight"] = size.Height;
        }

        public void UpdateHeader(string headerText)
        {
            nvSample.Header = headerText;
        }

        private void NvSample_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (contentFrame.CanGoBack)
                contentFrame.GoBack();
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            BreadcrumbHeader = e.SourcePageType switch
            {
                var t when t == typeof(PeriodicTablePage) => "Periodic Table",
                var t when t == typeof(ProPage) => "PRO Version",
                var t when t == typeof(ElementDetailsPage) => "Periodic Table > Element Details",
                var t when t == typeof(PoissonDetailsPage) => "Poisson's Ratio Table > Poisson's Ratio Details",
                var t when t == typeof(GeologyDetailsPage) => "Geology Table > Geology Table Details",
                var t when t == typeof(FormulasDetailsPage) => "Equations > Equations Details",
                var t when t == typeof(IsotopePage) => "Isotopes",
                var t when t == typeof(PoissonPage) => "Poisson's Ratio Table",
                var t when t == typeof(GeologyPage) => "Geology Table",
                var t when t == typeof(FormulasPage) => "Equations",
                var t when t == typeof(EmissionPage) => "Emission Spectrum Table",
                var t when t == typeof(EmissionDetailsPage) => "Emission Spectrum Table > Emission Spectrum Table Details",
                var t when t == typeof(NuclidePage) => "Nuclide Table",
                _ => BreadcrumbHeader
            };

            nvSample.Header = e.SourcePageType switch
            {
                var t when t == typeof(PeriodicTablePage) => CreateBreadcrumb("Periodic Table"),
                var t when t == typeof(ProPage) => CreateBreadcrumb("PRO Version"),
                var t when t == typeof(ElementDetailsPage) => CreateBreadcrumb("Periodic Table", "Element Details"),
                var t when t == typeof(PoissonDetailsPage) => CreateBreadcrumb("Poisson's Ratio Table", "Poisson's Ratio Details"),
                var t when t == typeof(GeologyDetailsPage) => CreateBreadcrumb("Geology Table", "Geology Table Details"),
                var t when t == typeof(FormulasDetailsPage) => CreateBreadcrumb("Equations", "Equations Details"),
                var t when t == typeof(IsotopePage) => CreateBreadcrumb("Isotopes"),
                var t when t == typeof(PoissonPage) => CreateBreadcrumb("Poisson's Ratio Table"),
                var t when t == typeof(GeologyPage) => CreateBreadcrumb("Geology Table"),
                var t when t == typeof(FormulasPage) => CreateBreadcrumb("Equations"),
                var t when t == typeof(EmissionPage) => CreateBreadcrumb("Emission Spectrum Table"),
                var t when t == typeof(EmissionDetailsPage) => CreateBreadcrumb("Emission Spectrum Table", "Emission Spectrum Table Details"),
                var t when t == typeof(NuclidePage) => CreateBreadcrumb("Nuclide Table"),
                var t when t == typeof(IsotopeDetailsPage) => GetIsotopeBreadcrumb(),
                var t when t == typeof(IonizationDetailsPage) => GetIonizationBreadcrumb(),
                _ => nvSample.Header
            };

            nvSample.IsBackEnabled = contentFrame.CanGoBack;
        }

        private StackPanel GetIsotopeBreadcrumb()
        {
            var backStack = contentFrame.BackStack;
            if (backStack.Count > 0 && backStack.Last().SourcePageType == typeof(ElementDetailsPage))
                return CreateBreadcrumb("...", "Element Details", "Isotope Details");
            if (backStack.Count > 0 && backStack.Last().SourcePageType == typeof(NuclidePage))
                return CreateBreadcrumb("Nuclide Table", "Isotope Details");
            return CreateBreadcrumb("Isotopes", "Isotopes Details");
        }

        private StackPanel GetIonizationBreadcrumb()
        {
            var backStack = contentFrame.BackStack;
            if (backStack.Count > 0 && backStack.Last().SourcePageType == typeof(ElementDetailsPage))
                return CreateBreadcrumb("...", "Element Details", "Ionization Energies Details");
            return CreateBreadcrumb("Ionization Energies", "Ionization Energies Details");
        }

        private void NvSample_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                contentFrame.Navigate(typeof(SettingsPage));
                nvSample.Header = CreateBreadcrumb("Settings");
                return;
            }

            if (args.SelectedItemContainer?.Tag is not string pageTag)
                return;

            var navigationMap = new (string Tag, Type Page, string Header)[]
            {
                ("PeriodicTablePage", typeof(PeriodicTablePage), "Periodic Table"),
                ("ProPage", typeof(ProPage), "PRO Version"),
                ("IsotopePage", typeof(IsotopePage), "Isotopes"),
                ("SolubilityPage", typeof(SolubilityPage), "Solubility"),
                ("DictionaryPage", typeof(DictionaryPage), "Dictionary"),
                ("EquationsPage", typeof(FormulasPage), "Equations"),
                ("IonizationPage", typeof(IonizationPage), "Ionization Table"),
                ("CalculatorPage", typeof(CalculatorPage), "Calculator"),
                ("FlashcardsPage", typeof(FlashCardsPage), "Flashcards"),
                ("BlogPage", typeof(BlogPage), "Blog"),
                ("phPage", typeof(phPage), "pH-Indicators"),
                ("ElectrochemicalPage", typeof(ElectrochemicalPage), "Electrochemical Series"),
                ("ConstantsPage", typeof(ConstantsPage), "Constants Table"),
                ("PoissonPage", typeof(PoissonPage), "Poisson's Ratio Table"),
                ("GeologyPage", typeof(GeologyPage), "Geology Table"),
                ("EmissionPage", typeof(EmissionPage), "Emission Spectrum Table"),
                ("NuclidePage", typeof(NuclidePage), "Nuclide Table")
            };

            var nav = navigationMap.FirstOrDefault(n => n.Tag == pageTag);
            if (nav.Page != null)
            {
                contentFrame.Navigate(nav.Page);
                nvSample.Header = CreateBreadcrumb(nav.Header);
            }
        }

        private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            string userInput = sender.Text?.Trim();
            if (!string.IsNullOrEmpty(userInput))
            {
                var filtered = ElementData.Elements
                    .Where(element => element.OriginalName.StartsWith(userInput, StringComparison.OrdinalIgnoreCase))
                    .Select(element => element.OriginalName)
                    .ToList();

                sender.ItemsSource = filtered;
            }
            else
            {
                sender.ItemsSource = null;
            }
        }

        private void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string elementName = args.QueryText;
            var element = ElementData.GetElementByName(elementName);

            if (element != null)
            {
                contentFrame.Navigate(typeof(ElementDetailsPage), element);
            }
            else
            {
                sender.Text = "Element not found";
            }
        }

        private StackPanel CreateBreadcrumb(params string[] items)
        {
            bool isWideContent = items.Contains("Dictionary") ||
                (items.Contains("Periodic Table") && !items.Contains("Element Details")) ||
                items.Contains("Nuclide Table") ||
                items.Contains("Isotopes Details");

            var breadcrumb = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0)
            };

            if (!isWideContent)
                breadcrumb.MaxWidth = 1800;

            for (int i = 0; i < items.Length; i++)
            {
                var textBlock = new TextBlock
                {
                    Text = items[i],
                    Margin = new Thickness(0, 0, 8, 0),
                    MaxWidth = 1800,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    FontWeight = i == items.Length - 1 ? FontWeights.Bold : FontWeights.Normal
                };

                if (i < items.Length - 1)
                {
                    textBlock.PointerPressed += (s, e) =>
                    {
                        if (contentFrame.CanGoBack)
                            contentFrame.GoBack();
                    };
                }

                breadcrumb.Children.Add(textBlock);

                if (i < items.Length - 1)
                {
                    breadcrumb.Children.Add(new TextBlock { Text = ">", Margin = new Thickness(0, 0, 8, 0) });
                }
            }

            return breadcrumb;
        }

        private void Window_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            nvSample.PaneDisplayMode = e.Size.Width < 860
                ? NavigationViewPaneDisplayMode.LeftCompact
                : NavigationViewPaneDisplayMode.Left;
        }

        private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdateTitleBarButtonColors(sender.ActualTheme);
        }

        private void UpdateTitleBarButtonColors(ElementTheme theme)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var titleBar = appWindow.TitleBar;

            if (theme == ElementTheme.Dark)
            {
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(60, 255, 255, 255);
                titleBar.ButtonPressedForegroundColor = Colors.White;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveForegroundColor = Colors.Gray;
            }
            else
            {
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonForegroundColor = Colors.Black;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 0, 0, 0);
                titleBar.ButtonHoverForegroundColor = Colors.Black;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(60, 0, 0, 0);
                titleBar.ButtonPressedForegroundColor = Colors.Black;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveForegroundColor = Colors.Gray;
            }
        }
    }
}
