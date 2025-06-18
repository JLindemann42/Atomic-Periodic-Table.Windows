using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;
using System;
using System.Xml.Linq;
using Microsoft.UI.Text;
using System.Runtime.InteropServices;
using System.IO;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using WinRT;
using Atomic_PeriodicTable;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.Storage;

namespace Atomic_WinUI
{
    public sealed partial class MainWindow : Window
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETICON = 0x80;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        public string BreadcrumbHeader { get; set; } = "Periodic Table";
        public static MainWindow Instance { get; private set; }

        public MainWindow()
        {
            Instance = this;


            this.InitializeComponent();
            this.SizeChanged += MainWindow_SizeChanged;
            this.Closed += MainWindow_Closed; // Add this line
            ExtendsContentIntoTitleBar = true;

            // Set the window icon
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "atomic_icon_transparent.ico");
            IntPtr hIcon = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x00000010 | 0x00000002);
            SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, hIcon);
            SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG, hIcon);

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // Restore window size if saved
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.TryGetValue("WindowWidth", out object widthObj) &&
                settings.Values.TryGetValue("WindowHeight", out object heightObj) &&
                widthObj is int width && heightObj is int height)
            {
                appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
            }
            else
            {
                // Optionally set a default size
                appWindow.Resize(new Windows.Graphics.SizeInt32(1000, 700));
            }

            contentFrame.Navigated += ContentFrame_Navigated;

            contentFrame.Navigate(typeof(PeriodicTablePage));
            nvSample.Header = CreateBreadcrumb("Periodic Table"); // Set the header for the home page

            // Set the PeriodicTablePage as the active item in the NavigationView
            nvSample.SelectedItem = PeriodicTablePageItem;

        }

        //Handle minimum size of the app window
        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            const int minWidth = 600;
            const int minHeight = 500;

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            int width = (int)e.Size.Width;
            int height = (int)e.Size.Height;

            bool needsResize = false;
            if (width < minWidth || height < minHeight)
            {
                width = Math.Max(width, minWidth);
                height = Math.Max(height, minHeight);
                needsResize = true;
            }

            if (needsResize)
            {
                appWindow.Resize(new SizeInt32(width, height));
            }
        }

        //Store the window size when the window is closed
        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            var size = appWindow.Size;
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["WindowWidth"] = size.Width;
            settings.Values["WindowHeight"] = size.Height;
        }

        private MicaController micaController;
        private SystemBackdropConfiguration backdropConfiguration;


        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hInstance, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        public void UpdateHeader(string headerText)
        {
            nvSample.Header = headerText; // Update the header
        }

        private void NvSample_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (contentFrame.CanGoBack)
            {
                contentFrame.GoBack();
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            if (e.SourcePageType == typeof(PeriodicTablePage))
            {
                BreadcrumbHeader = "Periodic Table";
                nvSample.Header = CreateBreadcrumb("Periodic Table");
            }
            else if (e.SourcePageType == typeof(ElementDetailsPage))
            {
                BreadcrumbHeader = "Periodic Table > Element Details";
                nvSample.Header = CreateBreadcrumb("Periodic Table", "Element Details");
            }
            else if (e.SourcePageType == typeof(IsotopePage))
            {
                BreadcrumbHeader = "Isotopes";
                nvSample.Header = CreateBreadcrumb("Isotopes");
            }
            else if (e.SourcePageType == typeof(IsotopeDetailsPage))
            {
                // Check previous page in the back stack
                var backStack = contentFrame.BackStack;
                string breadcrumbHeader;
                StackPanel breadcrumb;

                if (backStack.Count > 0 && backStack.Last().SourcePageType == typeof(ElementDetailsPage))
                {
                    // Navigated from ElementDetailsPage
                    breadcrumbHeader = "... > Element Details > Isotopes";
                    breadcrumb = CreateBreadcrumb("...", "Element Details", "Isotopes");
                }
                else
                {
                    // Navigated from IsotopePage or elsewhere
                    breadcrumbHeader = "Isotopes > Isotopes Details";
                    breadcrumb = CreateBreadcrumb("Isotopes", "Isotopes Details");
                }

                BreadcrumbHeader = breadcrumbHeader;
                nvSample.Header = breadcrumb;
            }
            // Enable or disable the back button based on the navigation stack
            nvSample.IsBackEnabled = contentFrame.CanGoBack;
        }

        private void NvSample_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                // Navigate to SettingsPage when the settings button is clicked
                contentFrame.Navigate(typeof(SettingsPage));
                nvSample.Header = CreateBreadcrumb("Settings");
            }
            else if (args.SelectedItemContainer?.Tag is string pageTag)
            {
                switch (pageTag)
                {
                    case "PeriodicTablePage":
                        contentFrame.Navigate(typeof(PeriodicTablePage));
                        nvSample.Header = CreateBreadcrumb("Periodic Table");
                        break;

                    case "IsotopePage":
                        contentFrame.Navigate(typeof(IsotopePage));
                        nvSample.Header = CreateBreadcrumb("Isotopes");
                        break;

                    case "SolubilityPage":
                        contentFrame.Navigate(typeof(SolubilityPage));
                        nvSample.Header = CreateBreadcrumb("Solubility");
                        break;

                    case "DictionaryPage":
                        contentFrame.Navigate(typeof(DictionaryPage));
                        nvSample.Header = CreateBreadcrumb("Dictionary");
                        break;

                    case "CalculatorPage":
                        contentFrame.Navigate(typeof(CalculatorPage));
                        nvSample.Header = CreateBreadcrumb("Calculator");
                        break;

                    case "BlogPage":
                        contentFrame.Navigate(typeof(BlogPage));
                        nvSample.Header = CreateBreadcrumb("Blog");
                        break;

                    case "phPage":
                        contentFrame.Navigate(typeof(phPage));
                        nvSample.Header = CreateBreadcrumb("pH-Indicators");
                        break;
                }
            }
        }

        private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                // Ensure the input text is not null or empty
                string userInput = sender.Text?.Trim();
                if (!string.IsNullOrEmpty(userInput))
                {
                    // Filter the element names based on user input
                    var filtered = ElementData.Elements
                        .Where(element => element.OriginalName.StartsWith(userInput, StringComparison.OrdinalIgnoreCase))
                        .Select(element => element.OriginalName) // Extract only the names for suggestions
                        .ToList();

                    // Update the suggestions
                    sender.ItemsSource = filtered;
                }
                else
                {
                    // Clear suggestions if input is empty
                    sender.ItemsSource = null;
                }
            }
        }

        private void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string elementName = args.QueryText;

            // Retrieve the Element object based on the name
            var element = ElementData.GetElementByName(elementName);

            if (element != null)
            {
                // Pass the Element object to ElementDetailsPage
                contentFrame.Navigate(typeof(ElementDetailsPage), element);
            }
            else
            {
                // Optionally, show a message if no match is found
                sender.Text = "Element not found";
            }
        }
        

        private StackPanel CreateBreadcrumb(params string[] items)
        {
            var breadcrumb = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < items.Length; i++)
            {
                var textBlock = new TextBlock
                {
                    Text = items[i],
                    Margin = new Thickness(0, 0, 8, 0),
                    FontWeight = i == items.Length - 1 ? FontWeights.Bold : FontWeights.Normal
                };

                if (i < items.Length - 1)
                {
                    textBlock.PointerPressed += (s, e) =>
                    {
                        if (contentFrame.CanGoBack)
                        {
                            contentFrame.GoBack();
                        }
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
            // Set the threshold width for collapsing the NavigationView
            double collapseThreshold = 800;

            if (e.Size.Width < collapseThreshold)
            {
                nvSample.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact; // Collapse the pane
            }
            else
            {
                nvSample.PaneDisplayMode = NavigationViewPaneDisplayMode.Left; // Expand the pane
            }
        }



    }

}