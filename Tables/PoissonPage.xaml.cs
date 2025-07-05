using Atomic_PeriodicTable;
using Atomic_PeriodicTable.Tables;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Storage;

namespace Atomic_WinUI
{
    public sealed partial class PoissonPage : Page
    {
        private ObservableCollection<Poisson> PoissonList { get; set; }
        public ObservableCollection<Poisson> FilteredPoissonList { get; set; }
        private void ListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Poisson selectedPoisson)
            {
                // Navigate to PoissonDetailsPage, passing the selected Ionization
                Frame.Navigate(typeof(PoissonDetailsPage), selectedPoisson);
            }
        }

        public PoissonPage()
        {
            this.InitializeComponent();
            //Check PRO or Not:
            if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
            {
                PoissonToolBar.Visibility = Visibility.Visible;
                PoissonListView.Visibility = Visibility.Visible;
                NoProContent.Visibility = Visibility.Collapsed;
            }
            else
            {
                PoissonToolBar.Visibility = Visibility.Collapsed;
                PoissonListView.Visibility = Visibility.Collapsed;
                NoProContent.Visibility = Visibility.Visible;
            }
            PoissonList = new ObservableCollection<Poisson>
            {
                // Rocks
                new Poisson { Symbol = "An", Name = "Andesite", StartNumber = 0.20, EndNumber = 0.35, Category = "Rock" },
                new Poisson { Symbol = "Ba", Name = "Basalt", StartNumber = 0.10, EndNumber = 0.35, Category = "Rock" },
                new Poisson { Symbol = "Cl", Name = "Claystone", StartNumber = 0.25, EndNumber = 0.40, Category = "Rock" },
                new Poisson { Symbol = "Co", Name = "Conglomerate", StartNumber = 0.10, EndNumber = 0.4, Category = "Rock" },
                new Poisson { Symbol = "Di", Name = "Diabase", StartNumber = 0.10, EndNumber = 0.28, Category = "Rock" },
                new Poisson { Symbol = "Di", Name = "Diorite", StartNumber = 0.20, EndNumber = 0.30, Category = "Rock" },
                new Poisson { Symbol = "Do", Name = "Dolerite", StartNumber = 0.15, EndNumber = 0.35, Category = "Rock" },
                new Poisson { Symbol = "Do", Name = "Dolomite", StartNumber = 0.10, EndNumber = 0.35, Category = "Rock" },
                new Poisson { Symbol = "Gn", Name = "Gneiss", StartNumber = 0.10, EndNumber = 0.30, Category = "Rock" },
                new Poisson { Symbol = "Ba", Name = "Basalt", StartNumber = 0.10, EndNumber = 0.33, Category = "Rock" },
                new Poisson { Symbol = "Gr", Name = "Granodiorite", StartNumber = 0.15, EndNumber = 0.25, Category = "Rock" },
                new Poisson { Symbol = "Gr", Name = "Greywacke", StartNumber = 0.08, EndNumber = 0.23, Category = "Rock" },
                new Poisson { Symbol = "Li", Name = "Linestone", StartNumber = 0.10, EndNumber = 0.33, Category = "Rock" },
                new Poisson { Symbol = "Ma", Name = "Marble", StartNumber = 0.15, EndNumber = 0.30, Category = "Rock" },
                new Poisson { Symbol = "Ma", Name = "Marl", StartNumber = 0.13, EndNumber = 0.33, Category = "Rock" },
                new Poisson { Symbol = "No", Name = "Norite", StartNumber = 0.20, EndNumber = 0.25, Category = "Rock" },
                new Poisson { Symbol = "Qu", Name = "Quartzite", StartNumber = 0.10, EndNumber = 0.33, Category = "Rock" },
                new Poisson { Symbol = "Ro", Name = "Rock Salt", StartNumber = 0.05, EndNumber = 0.30, Category = "Rock" },
                new Poisson { Symbol = "Sa", Name = "Sandstone", StartNumber = 0.05, EndNumber = 0.40, Category = "Rock" },
                new Poisson { Symbol = "Sh", Name = "Shale", StartNumber = 0.05, EndNumber = 0.32, Category = "Rock" },
                new Poisson { Symbol = "Si", Name = "Silstone", StartNumber = 0.13, EndNumber = 0.35, Category = "Rock" },
                new Poisson { Symbol = "Tu", Name = "Tuff", StartNumber = 0.10, EndNumber = 0.28, Category = "Rock" },

                // Soils
                new Poisson { Symbol = "Lo", Name = "Loose Sand", StartNumber = 0.20, EndNumber = 0.40, Category = "Soil" },
                new Poisson { Symbol = "Me", Name = "Medium dense Sand", StartNumber = 0.25, EndNumber = 0.40, Category = "Soil" },
                new Poisson { Symbol = "De", Name = "Dense Sand", StartNumber = 0.30, EndNumber = 0.45, Category = "Soil" },
                new Poisson { Symbol = "Si", Name = "Silty Sand", StartNumber = 0.20, EndNumber = 0.40, Category = "Soil" },
                new Poisson { Symbol = "Sa", Name = "Sand and Gravel", StartNumber = 0.15, EndNumber = 0.35, Category = "Soil" },
                new Poisson { Symbol = "Sa", Name = "Saturated cohesive Soil", StartNumber = 0.50, EndNumber = 0.50, Category = "Soil" },

                // Minerals
                new Poisson { Symbol = "α-", Name = "α-Cristobalite (SiO2)", StartNumber = -0.164, EndNumber = -0.164, Category = "Mineral" },
                new Poisson { Symbol = "Di", Name = "Diamond (C)", StartNumber = 0.069, EndNumber = 0.069, Category = "Mineral" },
                new Poisson { Symbol = "α-", Name = "α-Quartz (SiO2)", StartNumber = 0.079, EndNumber = 0.079, Category = "Mineral" },
                new Poisson { Symbol = "Pe", Name = "Periclase (MgO)", StartNumber = 0.182, EndNumber = 0.182, Category = "Mineral" },
                new Poisson { Symbol = "To", Name = "Topaz (Al2(F,OH)2SiO4)", StartNumber = 0.221, EndNumber = 0.221, Category = "Mineral" },
                new Poisson { Symbol = "Gr", Name = "Graphite (C)", StartNumber = 0.223, EndNumber = 0.223, Category = "Mineral" },
                new Poisson { Symbol = "Sa", Name = "Sapphire (Al2O3)", StartNumber = 0.234, EndNumber = 0.234, Category = "Mineral" },
                new Poisson { Symbol = "Ma", Name = "Magnesite (MgCO3)", StartNumber = 0.251, EndNumber = 0.251, Category = "Mineral" },
                new Poisson { Symbol = "Ha", Name = "Halite (NaCl)", StartNumber = 0.253, EndNumber = 0.253, Category = "Mineral" },
                new Poisson { Symbol = "Ma", Name = "Magnetite (Fe3O4)", StartNumber = 0.262, EndNumber = 0.262, Category = "Mineral" },
                new Poisson { Symbol = "Ga", Name = "Galena (PbS)", StartNumber = 0.270, EndNumber = 0.270, Category = "Mineral" },
                new Poisson { Symbol = "An", Name = "Anhydrite (CaSO4)", StartNumber = 0.273, EndNumber = 0.273, Category = "Mineral" },
                new Poisson { Symbol = "Ru", Name = "Rutile (TiO2)", StartNumber = 0.278, EndNumber = 0.278, Category = "Mineral" },
                new Poisson { Symbol = "Ch", Name = "Chromite (FeOCr2O3)", StartNumber = 0.280, EndNumber = 0.280, Category = "Mineral" },
                new Poisson { Symbol = "Al", Name = "Albite (NaAlSi3O8)", StartNumber = 0.285, EndNumber = 0.285, Category = "Mineral" },
                new Poisson { Symbol = "Fl", Name = "Fluorite (CaF2)", StartNumber = 0.289, EndNumber = 0.289, Category = "Mineral" },
                new Poisson { Symbol = "Do", Name = "Dolomite (CaMg(CO3)2)", StartNumber = 0.292, EndNumber = 0.292, Category = "Mineral" },
                new Poisson { Symbol = "Ca", Name = "Calcite (CaCO3)", StartNumber = 0.309, EndNumber = 0.309, Category = "Mineral" },
                new Poisson { Symbol = "Sp", Name = "Sphalerite (ZnS)", StartNumber = 0.320, EndNumber = 0.320, Category = "Mineral" },
                new Poisson { Symbol = "Ur", Name = "Uraninite (UO2)", StartNumber = 0.325, EndNumber = 0.325, Category = "Mineral" },
                new Poisson { Symbol = "Gy", Name = "Gypsum (CaSO4 2H2O)", StartNumber = 0.336, EndNumber = 0.336, Category = "Mineral" },
                new Poisson { Symbol = "Zi", Name = "Zincite (ZnO)", StartNumber = 0.353, EndNumber = 0, Category="Mineral" }
            };

            FilteredPoissonList = new ObservableCollection<Poisson>(PoissonList);
        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text?.Trim().ToLower() ?? "";
            FilteredPoissonList.Clear();

            foreach (var item in PoissonList)
            {
                //Adding possibility to search poisson ratio values, category etc
                if (item.Name.ToLower().Contains(query) ||
                    item.StartNumber.ToString().Contains(query) ||
                    item.EndNumber.ToString().Contains(query) ||
                    item.Category.ToLower().Contains(query) ||
                    item.Symbol.ToLower().Contains(query))
                {
                    FilteredPoissonList.Add(item);
                }
            }
        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProPage));
        }

        private void OnClearFilterSelected(object sender, RoutedEventArgs e)
        {
            FilteredPoissonList.Clear();
            foreach (var poisson in PoissonList)
                FilteredPoissonList.Add(poisson);
        }

        private void OnDisplayModeSelected(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Text is string category)
            {
                var filtered = PoissonList.Where(eq => eq.Category == category);
                FilteredPoissonList.Clear();
                foreach (var poisson in filtered)
                    FilteredPoissonList.Add(poisson);
            }
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



    public class Poisson
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public double StartNumber { get; set; }
        public double EndNumber { get; set; }
        public string Category { get; set; }

    }
}