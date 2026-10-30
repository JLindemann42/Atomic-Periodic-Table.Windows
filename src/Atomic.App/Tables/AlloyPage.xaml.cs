using System;
using System.Collections.ObjectModel;
using System.Linq;
using Atomic.App.Navigation;
using Atomic.App.Services;
using Atomic.Core.Localization;
using Atomic.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atomic.App.Tables
{
    /// <summary>
    /// The alloy reference table - fifteen common alloys, filterable by base metal.
    ///
    /// <para>Alloys are not elements, so nothing in the periodic table data covers them. Each entry
    /// names its base metal, which is what the "go to base element" link uses.</para>
    /// </summary>
    public sealed partial class AlloyPage : Page
    {
        private readonly IStringProvider _strings = ResourceStringProvider.For(AppLanguage.Current);
        private readonly ObservableCollection<Alloy> _visible = [];

        /// <summary>Selected base metal, or null for all. Persisted like Android's AlloyPreference.</summary>
        private string? _baseFilter;

        public AlloyPage()
        {
            InitializeComponent();
            AlloyList.ItemsSource = _visible;

            SearchBox.PlaceholderText = _strings.Get(S.alloy_search);
            NoProText.Text = _strings.Get(S.requires_pro);
            NoProButton.Content = _strings.Get(S.get_pro_title);

            var hasPro = SettingsStore.HasAnyPro;
            MainContent.Visibility = hasPro ? Visibility.Visible : Visibility.Collapsed;
            NoProContent.Visibility = hasPro ? Visibility.Collapsed : Visibility.Visible;

            _baseFilter = SettingsStore.Get(SettingsStore.Keys.AlloyBaseFilter, string.Empty);
            if (_baseFilter.Length == 0) _baseFilter = null;

            BuildFilterChips();
            Refresh();
        }

        /// <summary>
        /// A "clear" chip followed by one per base metal that actually occurs.
        ///
        /// <para>The chip labels come from the localized alloy_base_* strings rather than the raw
        /// element key, because showing the key made the detail panel read "Basmetall: Iron" in a
        /// translated app.</para>
        /// </summary>
        private void BuildFilterChips()
        {
            FilterPanel.Children.Clear();
            FilterPanel.Children.Add(Chip(_strings.Get(S.alloy_clear_filter), null));

            foreach (var metal in AlloyModel.BaseMetals)
                FilterPanel.Children.Add(Chip(BaseName(metal), metal));
        }

        private Button Chip(string label, string? metal)
        {
            var selected = string.Equals(_baseFilter, metal, StringComparison.Ordinal);
            var chip = new Button
            {
                Content = label,
                Tag = metal,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 6, 14, 6),
            };
            if (selected)
                chip.Style = (Style)Application.Current.Resources["AccentButtonStyle"];

            chip.Click += (s, _) =>
            {
                _baseFilter = (string?)((Button)s).Tag;
                SettingsStore.Set(SettingsStore.Keys.AlloyBaseFilter, _baseFilter ?? string.Empty);
                BuildFilterChips();
                Refresh();
            };
            return chip;
        }

        /// <summary>The localized name of a base metal, falling back to the key.</summary>
        private string BaseName(string metal)
        {
            var key = "alloy_base_" + metal;
            var label = _strings.Get(key);
            return string.Equals(label, key, StringComparison.Ordinal) ? Capitalise(metal) : label;
        }

        private static string Capitalise(string s) =>
            s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                Refresh();
        }

        private void Refresh()
        {
            _visible.Clear();
            // Search covers the composition as well as the name, so "zinc" finds brass.
            foreach (var alloy in AlloyModel.Filter(_baseFilter, SearchBox.Text))
                _visible.Add(alloy);
        }

        private async void Alloy_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string name) return;
            var alloy = AlloyModel.All.FirstOrDefault(a => a.Name == name);
            if (alloy is null) return;

            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(Label(_strings.Get(S.alloy_base_metal), BaseName(alloy.Base)));
            body.Children.Add(Label(_strings.Get(S.alloy_composition), alloy.Composition));
            body.Children.Add(new TextBlock
            {
                Text = alloy.Description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });

            var dialog = new ContentDialog
            {
                Title = alloy.Name,
                Content = new ScrollViewer { Content = body, MaxHeight = 420 },
                // Both button labels are Android keys, so they are translated in all twelve
                // languages. "Cancel" would be the obvious dismiss label but it is wrong here -
                // there is nothing to undo, the dialog only reads.
                PrimaryButtonText = _strings.Get(S.open_base_element),
                CloseButtonText = _strings.Get(S.ok),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            // The base is a lowercase English element key, which is exactly what the repository
            // wants - the same mechanism the table, search and chat deep links all use.
            if (ElementRepository.Get(alloy.Base) is { } element)
                MainWindow.Instance?.Navigation?.Navigate("ElementDetailsPage", element);
        }

        private static StackPanel Label(string caption, string value)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 12,
                Opacity = 0.75,
            });
            panel.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e) =>
            MainWindow.Instance?.Navigation?.Navigate(AppRoutes.Pro);
    }
}
