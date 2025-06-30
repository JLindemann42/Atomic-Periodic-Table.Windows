using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Storage;

namespace Atomic_WinUI
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitializing = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            updateThemeComboBox();
            updateUnitComboBox();
            _isInitializing = false;
        }

        private void updateThemeComboBox()
        {
            //Theme Combo Box Initialization
            ThemeComboBox.SelectionChanged -= ThemeComboBox_SelectionChanged;

            var savedTheme = ApplicationData.Current.LocalSettings.Values["AppTheme"] as string ?? "Default";
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

            var savedUnit = ApplicationData.Current.LocalSettings.Values["DefaultUnit"] as string ?? "Celsius";
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

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
                return;

            if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string? selectedTheme = selectedItem.Tag as string ?? "Default";
                ApplicationData.Current.LocalSettings.Values["AppTheme"] = selectedTheme;
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
                ApplicationData.Current.LocalSettings.Values["DefaultUnit"] = selectedUnit;
            }
        }
    }
}