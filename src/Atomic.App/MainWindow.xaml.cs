using Atomic.App.Navigation;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;
using Atomic.App.Services;

namespace Atomic.App
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

        public static MainWindow Instance { get; private set; }

        /// <summary>Owns the pane, navigation and breadcrumb. See <see cref="AppRoutes"/>.</summary>
        public NavigationService Navigation { get; private set; }

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

            Navigation = new NavigationService(nvSample, contentFrame);
            Navigation.Navigate(AppRoutes.PeriodicTable);

            ChatPanel.Navigation = Navigation;
            // The panel closes itself from four places, so the launcher follows its state rather
            // than being toggled alongside it.
            ChatPanel.OpenChanged += (_, open) =>
                AskAtomicButton.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            var askAtomic = ResourceStringProvider
                .For(AppLanguage.BaseCode(AppLanguage.Current))
                .Get(Atomic.Core.Localization.S.ask_atomic);
            AskAtomicLabel.Text = askAtomic;
            // The button's content is a panel, so it has no accessible name of its own - a screen
            // reader announces "button" and nothing else.
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AskAtomicButton, askAtomic);

            if (SettingsStore.HasAnyPro)
            {
                Navigation.SetProLabel("PRO Member");
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

            appWindow.Resize(new SizeInt32(SettingsStore.WindowWidth, SettingsStore.WindowHeight));
        }

        private void AskAtomic_Click(object sender, RoutedEventArgs e) => ChatPanel.Toggle();

        private void AskAtomic_Invoked(
            Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
            Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            ChatPanel.Toggle();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var size = appWindow.Size;
            SettingsStore.WindowWidth = size.Width;
            SettingsStore.WindowHeight = size.Height;
        }

        private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            string userInput = sender.Text?.Trim();
            if (!string.IsNullOrEmpty(userInput))
            {
                var filtered = ElementRepository.All()
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
            var element = ElementRepository.Get(elementName);

            if (element != null)
            {
                contentFrame.Navigate(typeof(ElementDetailsPage), element);
            }
            else
            {
                sender.Text = "Element not found";
            }
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
