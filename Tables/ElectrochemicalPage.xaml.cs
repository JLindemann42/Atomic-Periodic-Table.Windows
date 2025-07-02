using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;


namespace Atomic_WinUI
{
    public sealed partial class ElectrochemicalPage : Page
    {
        public ObservableCollection<ElectrochemicalEntry> ElectrochemicalEntries { get; } = new();
        public ObservableCollection<ElectrochemicalEntry> FilteredEntries { get; } = new();

        private void OnSearchBoxTextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
        {
            string searchTerm = SearchBox.Text.Trim();

            FilteredEntries.Clear();

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                foreach (var entry in ElectrochemicalEntries)
                    FilteredEntries.Add(entry);
            }
            else
            {
                foreach (var entry in ElectrochemicalEntries)
                {
                    if (!string.IsNullOrEmpty(entry.Name) &&
                        entry.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        FilteredEntries.Add(entry);
                    }
                }
            }
        }

        public ElectrochemicalPage()
        {
            this.InitializeComponent();

            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Thorium", "Th", "-3.6 (Volt)", "4+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Praseodymium", "Pr", "-3.1 (Volt)", "3+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Erbium", "Er", "-3.0 (Volt)", "3+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Lithium", "Li", "-3.04 (Volt)", "+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Cesium", "Cs", "-3.026 (Volt)", "+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Rubidium", "Rb", "-2.98 (Volt)", "+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Francium", "Fr", "-2.9 (Volt)", "+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Strontium", "Sr", "-2.89 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Calcium", "Ca", "-2.868 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Potassium", "K", "-2.931 (Volt)", "+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Barium", "Ba", "-2.912 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Lanthanum", "La", "-2.9 (Volt)", "3+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Sodium", "Na", "-2.71 (Volt)", "+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Magnesium", "Mg", "-2.372 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Aluminium", "Al", "-1.662 (Volt)", "3+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Manganese", "Mn", "-1.18 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Zinc", "Zn", "-0.7618 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Chromium", "Cr", "-0.74 (Volt)", "3+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Cadmium", "Cd", "-0.403 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Iron", "Fe", "-0.44 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Cobalt", "Co", "-0.28 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Nickel", "Ni", "-0.25 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Tin", "Sn", "-0.13 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Lead", "Pb", "-0.126 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Copper", "Cu", "-0.159 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Antimony", "Sb", "+0.204 (Volt)", "3+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Bismuth", "Bi", "+0.317 (Volt)", "3+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Silver", "Ag", "+0.7996 (Volt)", "+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Mercury", "Hg", "+0.85 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Platinum", "Pt", "+1.188 (Volt)", "2+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Gold", "Au", "+1.52 (Volt)", "3+"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Chlorine", "Cl", "+1.36 (Volt)", "–"));
            ElectrochemicalEntries.Add(new ElectrochemicalEntry("Fluorine", "F", "+2.87 (Volt)", "–"));




            foreach (var entry in ElectrochemicalEntries)
                FilteredEntries.Add(entry);

            this.DataContext = this;
        }

        private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid rootGrid &&
                rootGrid.FindName("ContentGrid") is Grid contentGrid)
            {
                contentGrid.Background = GetThemeBrush("SubtleFillColorSecondaryBrush");
            }
        }

        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid rootGrid &&
                rootGrid.FindName("ContentGrid") is Grid contentGrid)
            {
                contentGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid rootGrid &&
                rootGrid.FindName("ContentGrid") is Grid contentGrid)
            {
                contentGrid.Background = GetThemeBrush("SubtleFillColorSecondaryBrush");
            }
        }

        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid rootGrid &&
                rootGrid.FindName("ContentGrid") is Grid contentGrid)
            {
                contentGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        Brush GetThemeBrush(string key)
        {
            return (Brush)Application.Current.Resources[key];
        }


    }

}