using Atomic_PeriodicTable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Storage;


namespace Atomic_WinUI
{
    public sealed partial class ConstantsPage : Page
    {

        public ObservableCollection<ConstantsEntry> ConstantsEntries { get; } = new();
        public ObservableCollection<ConstantsEntry> FilteredEntries { get; } = new();


        public void FilterByCategory(string selectedCategory)
        {
            FilteredEntries.Clear();
            foreach (var entry in ConstantsEntries)
            {
                if (CategoryContains(entry, selectedCategory))
                    FilteredEntries.Add(entry);
            }
        }


        public bool CategoryContains(ConstantsEntry entry, string selectedCategory)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Category2) || string.IsNullOrWhiteSpace(selectedCategory))
                return false;

            var categories = entry.Category2
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.Trim().ToLowerInvariant());

            return categories.Contains(selectedCategory.Trim().ToLowerInvariant());
        }

        private void OnClearFilterSelected(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            FilteredEntries.Clear();
            foreach (var entry in ConstantsEntries)
                FilteredEntries.Add(entry);
        }

        private void OnDisplayModeSelected(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Text is string category)
            {
                // Fix typo: "Mathemathics" should be "math" to match your data
                string normalized = category.ToLowerInvariant();
                FilterByCategory(normalized);
            }
        }

        private void OnSearchBoxTextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
        {
            string searchTerm = SearchBox.Text.Trim();

            FilteredEntries.Clear();

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                foreach (var entry in ConstantsEntries)
                    FilteredEntries.Add(entry);
            }
            else
            {
                foreach (var entry in ConstantsEntries)
                {
                    if (!string.IsNullOrEmpty(entry.Name) &&
                        entry.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        FilteredEntries.Add(entry);
                    }
                }
            }
        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProPage));
        }

        public ConstantsPage()
        {
            this.InitializeComponent();

            //Check PRO or Not:
            if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
            {
                MainToolBar.Visibility = Visibility.Visible;
                ConstantsListView.Visibility = Visibility.Visible;
                NoProContent.Visibility = Visibility.Collapsed;
            }
            else
            {
                MainToolBar.Visibility = Visibility.Collapsed;
                ConstantsListView.Visibility = Visibility.Collapsed;
                NoProContent.Visibility = Visibility.Visible;
            }


            ConstantsEntries.Add(new ConstantsEntry("Pi (π)", "3.14159265358979323846", "-", "π", "mathematics"));
            ConstantsEntries.Add(new ConstantsEntry("Tau (τ)", "6.28318530717958647692", "-", "τ", "mathematics"));
            ConstantsEntries.Add(new ConstantsEntry("Euler's number", "2.71828182845904523536", "-", "e", "mathematics"));
            ConstantsEntries.Add(new ConstantsEntry("Euler's constant", "0.57721566490153286060", "-", "γ", "mathematics"));
            ConstantsEntries.Add(new ConstantsEntry("Golden ratio", "1.61803398874989484820", "-", "φ", "mathematics"));
            ConstantsEntries.Add(new ConstantsEntry("Catalan constant", "0.57721566490153286060", "-", "C", "mathematics"));
            ConstantsEntries.Add(new ConstantsEntry("Square root of 2", "1.41421356237309504880", "-", "√2", "mathematics"));
            ConstantsEntries.Add(new ConstantsEntry("Square root of 10", "3.16227766016837950288", "-", "√10", "mathematics"));

            ConstantsEntries.Add(new ConstantsEntry("Speed of light", "299792458.0", "ms^-1", "c", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Planck's constant", "6.626070040e-34", "m^2*kg*s^-1", "h", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Gravitational constant", "6.67408e-11", "m^3*kg^-1*s^-2", "G", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Avogadro's number", "6.02214076e23", "mol^-1", "NA", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Elementary charge", "1.602176565e-19", "C", "e", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Electron mass", "9.10938188e-31", "kg", "me", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Proton mass", "1.67262158e-27", "kg", "mp", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Neutron mass", "1.674927281e-27", "kg", "mn", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Atomic mass unit", "1.660538782e-27", "kg", "u", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Boltzmann constant", "1.38064852e-23", "J*K^-1", "k", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Faraday constant", "96485.3399", "C*mol^-1", "F", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Bohr magneton", "9.274009994e-24", "J*T^-1", "μB", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Bohr radius", "5.2917721067e-11", "m", "a₀", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Acceleration due to gravity", "9.80665", "m*s^-2", "g", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Vacuum permittivity", "8.854187817e-12", "F*m^-1", "ε₀", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Vacuum permeability", "1.2566370614e-06", "H*m^-1", "μ₀", "physics"));
            ConstantsEntries.Add(new ConstantsEntry("Gas constant", "8.31446261815324", "J*K^-1*mol^-1", "R", "physics"));

            ConstantsEntries.Add(new ConstantsEntry("Airport, Passenger", "10-20", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Apartment, Bedroom", "380-570", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Automobile service station, Vehicle", "30-60", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Automobile service station, Employee", "35-60", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Bar/ lounge, Seat", "45-95", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Bar/ lounge, Employee", "40-60", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Boarding house, Person", "95-250", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Conference center, Person", "40-60", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Department store, Restroom", "1300-2300", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Department store, Employee", "30-60", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Hotel, Guest", "150-230", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Hotel, Employee", "30-60", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Industrial building, Employee", "60-130", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Laundry (self-service), Machine", "1500-2100", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Laundry (self-service), Customer", "170-210", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Mobile home park, Mobile home", "470-570", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Motel with kitchen, Guest", "210-340", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Motel without kitchen, Guest", "190-290", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Office, Employee", "25-60", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Public restroom, User", "10-20", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Restaurant without bar, Customer", "25-40", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Restaurant with bar, Customer", "35-45", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Shopping center, Employee", "25-55", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Shopping center, Parking space", "5-10", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Theater, Seat", "10-15", "L/(unit*d)", "Wastewater flow rate (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Ks", "25-100", "mg/(L*BOD5)", "Growth constant for domestic wastewater (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("kd", "0-0.30", "d^-1", "Growth constant for domestic wastewater (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("um", "1-8", "d^-1", "Growth constant for domestic wastewater (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Y", "0.4-0.8", "mg VSS/mg BOD5", "Growth constant for domestic wastewater (USA)", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Trash racks", "40-150", "mm", "Nomenclature of racks and screens", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Bar racks, Coarse screens, Fine screens", "6-75", "mm", "Nomenclature of racks and screens", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Very fine screens", "0.25-1.5", "mm", "Nomenclature of racks and screens", "water"));
            ConstantsEntries.Add(new ConstantsEntry("Micro-screens", "0.001-0.3", "mm", "Nomenclature of racks and screens", "water"));




            foreach (var entry in ConstantsEntries)
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