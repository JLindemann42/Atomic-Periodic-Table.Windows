using System;
using System.Linq;
using System.Threading.Tasks;
using Atomic.App.Services;
using Atomic.Core.Localization;
using Atomic.Core.Progression;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Atomic.App.Navigation
{
    /// <summary>
    /// Drives the <see cref="NavigationView"/> from <see cref="AppRoutes"/>: builds the pane,
    /// navigates, and keeps the header and back button in sync.
    ///
    /// <para>The header is set in one place - <see cref="OnNavigated"/> - so it is correct however
    /// the page was reached (pane, search box, in-page link, back button). Previously the pane
    /// handler and the Navigated handler each set it, which meant programmatic navigation left a
    /// stale breadcrumb behind.</para>
    /// </summary>
    public sealed class NavigationService
    {
        private const string TablesGroupTag = "__tables";

        private readonly NavigationView _nav;
        private readonly Frame _frame;
        private NavigationViewItem? _tablesGroup;

        /// <summary>
        /// Resolves route keys. Re-read on every pane build rather than cached, so a language
        /// change is picked up by <see cref="RefreshPane"/> without reconstructing the service.
        /// </summary>
        private static IStringProvider Strings => ResourceStringProvider.For(AppLanguage.Current);

        public NavigationService(NavigationView nav, Frame frame)
        {
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));

            BuildPane();

            _nav.SelectionChanged += OnSelectionChanged;
            _nav.BackRequested += (_, _) => GoBack();
            _frame.Navigated += OnNavigated;

            // Signing in or out changes the account entry's icon while the pane is already built.
            //
            // Marshalled: the automatic sync raises this from a timer thread, and assigning
            // NavigationViewItem.Icon off the UI thread throws into a task nobody awaits - so the
            // pane silently kept the placeholder glyph until the next launch.
            UserProfile.Changed += (_, _) =>
                _nav.DispatcherQueue.TryEnqueue(() => _ = RefreshAccountAvatarAsync());
        }

        /// <summary>The route currently displayed, if the page is registered.</summary>
        public RouteDescriptor? CurrentRoute => AppRoutes.ByType(_frame.CurrentSourcePageType);

        /// <summary>The page currently displayed, so the chat can read its element (roadmap 2.9).</summary>
        public object? CurrentContent => _frame.Content;

        /// <summary>Raised after every navigation, so a docked chat can follow the page (roadmap 2.9).</summary>
        public event EventHandler? PageChanged;

        public bool Navigate(string tag, object? parameter = null)
        {
            var route = AppRoutes.ByTag(tag);
            if (route is null)
                return false;
            return Navigate(route, parameter);
        }

        public bool Navigate(RouteDescriptor route, object? parameter = null) =>
            _frame.Navigate(route.PageType, parameter);

        public void GoBack()
        {
            if (_frame.CanGoBack)
                _frame.GoBack();
        }

        /// <summary>Rebuilds the pane in place, re-resolving every label.</summary>
        public void RefreshPane()
        {
            BuildPane();
            SyncSelection(_frame.CurrentSourcePageType);
        }

        /// <summary>
        /// Repaints everything after a language change.
        ///
        /// <para>MRT resolves a resource once per element and caches it, so setting
        /// <c>PrimaryLanguageOverride</c> alone changes nothing already on screen. The pane is
        /// rebuilt from scratch and the current page re-entered, which re-runs its constructor and
        /// so re-reads its strings. The back stack is deliberately left alone - the user changed a
        /// setting, they did not ask to lose their place.</para>
        /// </summary>
        public void ReloadForLanguageChange()
        {
            var current = _frame.CurrentSourcePageType;

            RefreshPane();

            if (current is null)
                return;

            // Re-navigating to the same type is a no-op for Frame, so clear its cache first.
            var cacheSize = _frame.CacheSize;
            _frame.CacheSize = 0;
            _frame.Navigate(current);
            _frame.CacheSize = cacheSize;

            // Navigating pushed a duplicate entry; drop it so Back still goes where it did.
            if (_frame.BackStackDepth > 0 &&
                _frame.BackStack[^1].SourcePageType == current)
            {
                _frame.BackStack.RemoveAt(_frame.BackStack.Count - 1);
            }

            _nav.IsBackEnabled = _frame.CanGoBack;
        }

        /// <summary>Relabels the PRO entry once ownership is known.</summary>
        public void SetProLabel(string text)
        {
            if (FindItem(AppRoutes.Pro) is { } item)
                item.Content = text;
        }

        /// <summary>
        /// Put the signed-in user's picture on the account entry, in place of the person glyph.
        /// </summary>
        /// <remarks>
        /// <para>The bitmap arrives already masked to a circle - see <see cref="UserProfile"/>.
        /// <c>NavigationViewItem.Icon</c> takes an <c>IconElement</c>, which cannot be clipped or
        /// given a shape, so a square photo here would read as a square photo.</para>
        ///
        /// <para>Sized to 24 to match the <c>SymbolIcon</c>s beside it; letting it take the icon
        /// slot's natural size makes the account row taller than every other row in the pane.</para>
        /// </remarks>
        public async Task RefreshAccountAvatarAsync()
        {
            if (FindItem(AppRoutes.Account) is not { } item) return;

            var avatar = await UserProfile.AvatarAsync(AvatarSize);

            item.Icon = avatar is null
                ? new SymbolIcon(Symbol.Contact)
                : new ImageIcon { Source = avatar, Width = AvatarSize, Height = AvatarSize };
        }

        /// <summary>Matches the pane's SymbolIcon metrics, so the account row keeps its height.</summary>
        private const int AvatarSize = 24;

        // -- pane construction ----------------------------------------------------------------

        private void BuildPane()
        {
            _nav.MenuItems.Clear();
            _nav.FooterMenuItems.Clear();

            foreach (var route in AppRoutes.InGroup(NavGroup.Main))
                _nav.MenuItems.Add(CreateItem(route));

            var tables = AppRoutes.InGroup(NavGroup.Tables).ToList();
            if (tables.Count > 0)
            {
                var tablesLabel = Strings.Get("activity_table_title");
                _tablesGroup = new NavigationViewItem
                {
                    Content = tablesLabel,
                    Tag = TablesGroupTag,
                    Icon = new FontIcon { Glyph = "" },
                    SelectsOnInvoked = false,
                };
                ToolTipService.SetToolTip(_tablesGroup, tablesLabel);
                foreach (var route in tables)
                    _tablesGroup.MenuItems.Add(CreateItem(route));
                _nav.MenuItems.Add(_tablesGroup);
            }

            var tools = AppRoutes.InGroup(NavGroup.Tools).ToList();
            if (tools.Count > 0)
            {
                _nav.MenuItems.Add(new NavigationViewItemHeader { Content = Strings.Get("tools") });
                foreach (var route in tools)
                    _nav.MenuItems.Add(CreateItem(route));
            }

            foreach (var route in AppRoutes.InGroup(NavGroup.Footer))
                _nav.FooterMenuItems.Add(CreateItem(route));

            // The settings entry belongs to NavigationView, not to MenuItems, so rebuilding the
            // pane leaves its label at whatever MRT resolved when the template first loaded.
            // Without this it keeps the previous language after a switch.
            if (_nav.SettingsItem is NavigationViewItem settings)
            {
                var label = Strings.Get("settings_title");
                settings.Content = label;
                ToolTipService.SetToolTip(settings, label);
            }

            // CreateItem gave the account entry the person glyph. A rebuild - construction, or a
            // language switch - discards the item the avatar was on, so it goes back afterwards.
            _ = RefreshAccountAvatarAsync();
        }

        private static NavigationViewItem CreateItem(RouteDescriptor route)
        {
            var label = Strings.Get(route.PaneTextKey);
            var item = new NavigationViewItem
            {
                Content = label,
                Tag = route.Tag,
            };

            if (route.Symbol is { } symbol)
                item.Icon = new SymbolIcon(symbol);
            else if (!string.IsNullOrEmpty(route.Glyph))
                item.Icon = new FontIcon { Glyph = route.Glyph };

            ToolTipService.SetToolTip(item, label);
            return item;
        }

        private NavigationViewItem? FindItem(string tag) =>
            _nav.MenuItems.Concat(_nav.FooterMenuItems)
                .OfType<NavigationViewItem>()
                .SelectMany(i => new[] { i }.Concat(i.MenuItems.OfType<NavigationViewItem>()))
                .FirstOrDefault(i => (i.Tag as string) == tag);

        // -- event handling -------------------------------------------------------------------

        private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                Navigate(AppRoutes.Settings);
                return;
            }

            // The "Tables" parent is a container, not a destination.
            if (args.SelectedItemContainer?.Tag is not string tag || tag == TablesGroupTag)
                return;

            if (AppRoutes.ByTag(tag) is { } route && _frame.CurrentSourcePageType != route.PageType)
                Navigate(route);
        }

        private void OnNavigated(object sender, NavigationEventArgs e)
        {
            var route = AppRoutes.ByType(e.SourcePageType);
            var keys = AppRoutes.BreadcrumbFor(e.SourcePageType, _frame);
            if (keys.Length > 0)
            {
                // An explicit lambda, not a method group: Get takes a params array, so a method
                // group cannot bind to Converter<string, string>.
                var strings = Strings;
                var crumbs = System.Array.ConvertAll(keys, k => strings.Get(k));
                _nav.Header = BuildBreadcrumb(crumbs, route?.WideContent ?? false);
            }

            _nav.IsBackEnabled = _frame.CanGoBack;
            SyncSelection(e.SourcePageType);
            RecordVisit(route);
            PageChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Advance the achievements that count visits.
        /// </summary>
        /// <remarks>
        /// <para>Here rather than in each page's OnNavigatedTo because every navigation passes
        /// through this one handler however it was started - pane, search box, in-page link, back
        /// button or an <c>atomic:</c> link - and because the registry already knows which routes
        /// are tables, so the group does not have to be restated as a list of page types.</para>
        ///
        /// <para>Android instead calls into each activity, which is how its "open different tables"
        /// achievement ended up declared and never wired to anything.</para>
        /// </remarks>
        private static void RecordVisit(RouteDescriptor? route)
        {
            if (route is null) return;

            if (route.PageType == typeof(ElementDetailsPage))
                AchievementStore.Record(AchievementTrigger.ElementOpened);
            else if (route.Tag == "DictionaryPage")
                AchievementStore.Record(AchievementTrigger.DictionaryOpened);
            else if (route.Group == NavGroup.Tables)
                AchievementStore.Record(AchievementTrigger.TableOpened);
        }

        /// <summary>
        /// Highlights the pane entry for the page just navigated to, so arriving via the search box
        /// or an in-page link selects the same item the pane would have.
        /// </summary>
        private void SyncSelection(Type? pageType)
        {
            var route = AppRoutes.ByType(pageType);
            if (route is null)
                return;

            if (route.PageType == typeof(SettingsPage))
            {
                _nav.SelectedItem = _nav.SettingsItem;
                return;
            }

            // Walk to the nearest ancestor that actually has a pane entry, so a detail page keeps
            // its parent highlighted rather than clearing the selection.
            var target = route;
            var guard = 0;
            while (target is not null && target.Group == NavGroup.None && guard++ < 16)
                target = AppRoutes.ByTag(target.ParentTag);

            if (target is not null && FindItem(target.Tag) is { } item)
                _nav.SelectedItem = item;
        }

        // -- breadcrumb -----------------------------------------------------------------------

        private StackPanel BuildBreadcrumb(string[] items, bool wideContent)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
            };

            if (!wideContent)
                panel.MaxWidth = 1800;

            for (var i = 0; i < items.Length; i++)
            {
                var isLast = i == items.Length - 1;

                var text = new TextBlock
                {
                    Text = items[i],
                    Margin = new Thickness(0, 0, 8, 0),
                    MaxWidth = 1800,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    FontWeight = isLast ? FontWeights.Bold : FontWeights.Normal,
                };

                if (!isLast)
                    text.PointerPressed += (_, _) => GoBack();

                panel.Children.Add(text);

                if (!isLast)
                    panel.Children.Add(new TextBlock { Text = ">", Margin = new Thickness(0, 0, 8, 0) });
            }

            return panel;
        }
    }
}
