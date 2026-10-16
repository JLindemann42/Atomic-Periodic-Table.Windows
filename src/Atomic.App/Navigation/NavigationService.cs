using System;
using System.Linq;
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

        public NavigationService(NavigationView nav, Frame frame)
        {
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));

            BuildPane();

            _nav.SelectionChanged += OnSelectionChanged;
            _nav.BackRequested += (_, _) => GoBack();
            _frame.Navigated += OnNavigated;
        }

        /// <summary>The route currently displayed, if the page is registered.</summary>
        public RouteDescriptor? CurrentRoute => AppRoutes.ByType(_frame.CurrentSourcePageType);

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

        /// <summary>
        /// Rebuilds the pane in place. Phase 2 calls this after a language change, once the labels
        /// come from resources rather than literals.
        /// </summary>
        public void RefreshPane()
        {
            BuildPane();
            SyncSelection(_frame.CurrentSourcePageType);
        }

        /// <summary>Relabels the PRO entry once ownership is known.</summary>
        public void SetProLabel(string text)
        {
            if (FindItem(AppRoutes.Pro) is { } item)
                item.Content = text;
        }

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
                _tablesGroup = new NavigationViewItem
                {
                    Content = "Tables",
                    Tag = TablesGroupTag,
                    Icon = new FontIcon { Glyph = "" },
                    SelectsOnInvoked = false,
                };
                ToolTipService.SetToolTip(_tablesGroup, "Tables");
                foreach (var route in tables)
                    _tablesGroup.MenuItems.Add(CreateItem(route));
                _nav.MenuItems.Add(_tablesGroup);
            }

            var tools = AppRoutes.InGroup(NavGroup.Tools).ToList();
            if (tools.Count > 0)
            {
                _nav.MenuItems.Add(new NavigationViewItemHeader { Content = "Tools" });
                foreach (var route in tools)
                    _nav.MenuItems.Add(CreateItem(route));
            }

            foreach (var route in AppRoutes.InGroup(NavGroup.Footer))
                _nav.FooterMenuItems.Add(CreateItem(route));
        }

        private static NavigationViewItem CreateItem(RouteDescriptor route)
        {
            var item = new NavigationViewItem
            {
                Content = route.PaneText,
                Tag = route.Tag,
            };

            if (route.Symbol is { } symbol)
                item.Icon = new SymbolIcon(symbol);
            else if (!string.IsNullOrEmpty(route.Glyph))
                item.Icon = new FontIcon { Glyph = route.Glyph };

            ToolTipService.SetToolTip(item, route.PaneText);
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
            var crumbs = AppRoutes.BreadcrumbFor(e.SourcePageType, _frame);
            if (crumbs.Length > 0)
                _nav.Header = BuildBreadcrumb(crumbs, route?.WideContent ?? false);

            _nav.IsBackEnabled = _frame.CanGoBack;
            SyncSelection(e.SourcePageType);
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
