using Atomic.App.Navigation;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;
using Atomic.App.Services;
using Atomic.Core.Localization;

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

            // The page makes room for the panel when there is room to make, and not otherwise.
            ChatPanel.LayoutChanged += (_, _) =>
                ChatColumn.Width = new GridLength(ChatPanel.DockedWidth);
            var askAtomic = ResourceStringProvider
                .For(AppLanguage.BaseCode(AppLanguage.Current))
                .Get(Atomic.Core.Localization.S.ask_atomic);
            AskAtomicLabel.Text = askAtomic;
            // The button's content is a panel, so it has no accessible name of its own - a screen
            // reader announces "button" and nothing else.
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AskAtomicButton, askAtomic);

            // Named by platform: "PRO Member" could not say whether the tier came from this
            // machine's Store or the phone's, and with Ultimate in the mix that distinction is
            // the difference between one purchase and two.
            var badge = SettingsStore.HasUltimate
                ? Atomic.Core.Licensing.EntitlementRules.UltimateBadge
                : Atomic.Core.Licensing.EntitlementRules.BadgeFor(
                    Atomic.Core.Licensing.EntitlementRules.WindowsPlatform,
                    SettingsStore.Entitlement.Tier);

            if (badge is not null)
            {
                Navigation.SetProLabel(badge);
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

        /// <summary>
        /// Open the assistant and put a question to it.
        /// </summary>
        /// <remarks>
        /// The entry point for <c>atomic://ask?q=…</c>, which the Quick Ask widget and any
        /// "ask about this" link use. Opening without asking would leave the user staring at an
        /// empty panel wondering what the link did.
        /// </remarks>
        public void AskAtomic(string question)
        {
            ChatPanel.Open();
            _ = ChatPanel.Session.AskAsync(question);
        }

        private void AskAtomic_Invoked(
            Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
            Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            ChatPanel.Toggle();
        }

        /// <summary>How long a closing window will wait for a final sync.</summary>
        /// <remarks>
        /// Short on purpose. Everything here is monotonic and re-runs at next launch, so a slow
        /// network costs the user nothing but a delay - and a delay on quit is the one thing that
        /// makes an app feel broken.
        /// </remarks>
        private static readonly TimeSpan CloseSyncBudget = TimeSpan.FromSeconds(5);

        private bool _closingForReal;

        /// <summary>
        /// Save the window size, then push progress before the process goes away.
        /// </summary>
        /// <remarks>
        /// <para>The close is cancelled and re-issued rather than being awaited in place: a
        /// <c>Closed</c> handler cannot hold the window open by awaiting, so a fire-and-forget sync
        /// here would race the process exit and usually lose. The window is hidden first, so from
        /// the user's side it closed immediately.</para>
        ///
        /// <para>Without this, XP earned in the last few minutes of a session waited for the next
        /// launch to reach the phone - which, if the next thing the user picks up is the phone, is
        /// exactly when they would notice it missing.</para>
        /// </remarks>
        private async void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var size = appWindow.Size;
            SettingsStore.WindowWidth = size.Width;
            SettingsStore.WindowHeight = size.Height;

            if (_closingForReal || !CloudSync.CanSync) return;

            _closingForReal = true;
            args.Handled = true;
            appWindow.Hide();

            var sync = CloudSync.AutoSyncAsync(force: true);
            await Task.WhenAny(sync, Task.Delay(CloseSyncBudget));

            Close();
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
            AchievementStore.Record(Atomic.Core.Progression.AchievementTrigger.Search);

            string elementName = args.QueryText;
            var element = ElementRepository.Get(elementName);

            if (element != null)
            {
                contentFrame.Navigate(typeof(ElementDetailsPage), element);
            }
            else
            {
                sender.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.no_results);
            }
        }

        private void Window_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            nvSample.PaneDisplayMode = e.Size.Width < 860
                ? NavigationViewPaneDisplayMode.LeftCompact
                : NavigationViewPaneDisplayMode.Left;

            ChatPanel.NotifyWindowSize(e.Size.Width);
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
