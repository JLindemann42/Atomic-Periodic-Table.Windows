using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Storage;
using Atomic.App.Services;

namespace Atomic.App
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitializing = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
            NameOpenAccountButton();
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            updateThemeComboBox();
            updateUnitComboBox();
            UpdateLanguageComboBox();
            _isInitializing = false;
        }

        private void UpdateLanguageComboBox()
        {
            var strings = ResourceStringProvider.For(AppLanguage.Current);
            LanguageHeader.Text = strings.Get("settings_language");
            LanguageDescription.Text = strings.Get("settings_language_desc");

            LanguageComboBox.Items.Clear();
            LanguageComboBox.Items.Add(new ComboBoxItem
            {
                Content = strings.Get("settings_language_system"),
                Tag = AppLanguage.System,
            });
            foreach (var (tag, nativeName) in AppLanguage.Supported)
            {
                // Listed in each language's own name - a user looking for their language cannot
                // be expected to recognise its English exonym.
                LanguageComboBox.Items.Add(new ComboBoxItem { Content = nativeName, Tag = tag });
            }

            var preference = AppLanguage.Preference;
            LanguageComboBox.SelectedIndex = 0;
            for (var i = 0; i < LanguageComboBox.Items.Count; i++)
            {
                if (LanguageComboBox.Items[i] is ComboBoxItem item &&
                    (item.Tag as string) == preference)
                {
                    LanguageComboBox.SelectedIndex = i;
                    break;
                }
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || LanguageComboBox.SelectedItem is not ComboBoxItem selected)
                return;

            var tag = selected.Tag as string ?? AppLanguage.System;
            if (tag == AppLanguage.Preference)
                return;

            AppLanguage.Preference = tag;
            AppLanguage.Apply(tag);

            // MRT resolves each resource once per element and caches it, so text already realised
            // does not change on its own. Rebuild the pane and re-enter the current page.
            MainWindow.Instance?.Navigation?.ReloadForLanguageChange();
        }

        private void updateThemeComboBox()
        {
            //Theme Combo Box Initialization
            ThemeComboBox.SelectionChanged -= ThemeComboBox_SelectionChanged;

            var savedTheme = SettingsStore.AppTheme;
            bool found = false;
            int index = 0;
            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if ((item.Tag as string)?.Equals(savedTheme, StringComparison.OrdinalIgnoreCase) == true)
                {
                    ThemeComboBox.SelectedIndex = index;
                    found = true;
                    break;
                }
                index++;
            }
            if (!found)
            {
                ThemeComboBox.SelectedIndex = 0;
            }

            ThemeComboBox.SelectionChanged += ThemeComboBox_SelectionChanged;
        }

        private void updateUnitComboBox()
        {
            //Init Unit Combo Box
            UnitComboBox.SelectionChanged -= UnitComboBox_SelectionChanged;

            var savedUnit = SettingsStore.DefaultUnit;
            bool unitFound = false;
            int unitIndex = 0;
            foreach (ComboBoxItem item in UnitComboBox.Items)
            {
                if ((item.Tag as string)?.Equals(savedUnit, StringComparison.OrdinalIgnoreCase) == true)
                {
                    UnitComboBox.SelectedIndex = unitIndex;
                    unitFound = true;
                    break;
                }
                unitIndex++;
            }
            if (!unitFound)
            {
                UnitComboBox.SelectedIndex = 0;
            }

            UnitComboBox.SelectionChanged += UnitComboBox_SelectionChanged;
        }

        /// <summary>
        /// Open the account screen, where cloud sync is signed in and run.
        /// </summary>
        private void OpenAccount_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(Atomic.App.Tools.AccountPage));

        /// <summary>
        /// Name the chevron for a screen reader.
        /// </summary>
        /// <remarks>
        /// Its content is an icon, so without this it is announced as "button" and nothing else.
        /// </remarks>
        private void NameOpenAccountButton() =>
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                OpenAccountButton,
                Services.ResourceStringProvider
                    .For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current))
                    .Get(Atomic.Core.Localization.S.user_title));

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
                return;

            if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string? selectedTheme = selectedItem.Tag as string ?? "Default";
                SettingsStore.AppTheme = selectedTheme;
                (Application.Current as App)?.ApplyAppTheme(selectedTheme);
            }
        }
        private void UnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
                return;

            if (UnitComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string? selectedUnit = selectedItem.Tag as string ?? "Celsius";
                SettingsStore.DefaultUnit = selectedUnit;
            }
        }

    }
}
