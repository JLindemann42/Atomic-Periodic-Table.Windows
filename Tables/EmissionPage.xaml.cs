using Atomic_PeriodicTable;
using Atomic_PeriodicTable.Tables;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Storage;

namespace Atomic_WinUI
{
    public sealed partial class EmissionPage : Page
    {
        private ObservableCollection<Emission> EmissionList { get; set; }
        public ObservableCollection<Emission> FilteredEmissionList { get; set; }

        public EmissionPage()
        {
            this.InitializeComponent();

            //Check PRO or Not:
            if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
            {
                EmissionToolBar.Visibility = Visibility.Visible;
                EmissionListView.Visibility = Visibility.Visible;
                NoProContent.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmissionToolBar.Visibility = Visibility.Collapsed;
                EmissionListView.Visibility = Visibility.Collapsed;
                NoProContent.Visibility = Visibility.Visible;
            }

            string[] elementSymbols = new string[]
            {
                "H", "He", "Li", "Be", "B",  "C",  "N",  "O",  "F",  "Ne",
                "Na", "Mg", "Al", "Si", "P",  "S",  "Cl", "Ar", "K",  "Ca",
                "Sc", "Ti", "V",  "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn",
                "Ga", "Ge", "As", "Se", "Br", "Kr", "Rb", "Sr", "Y",  "Zr",
                "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd", "In", "Sn",
                "Sb", "Te", "I",  "Xe", "Cs", "Ba", "La", "Ce", "Pr", "Nd",
                "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb",
                "Lu", "Hf", "Ta", "W",  "Re", "Os", "Ir", "Pt", "Au", "Hg",
                "Tl", "Pb", "Bi", "Po", "At", "Rn", "Fr", "Ra", "Ac", "Th",
                "Pa", "U",  "Np", "Pu", "Am", "Cm", "Bk", "Cf",
            };

            string[] elementNames = new string[]
            {
                "Hydrogen", "Helium", "Lithium", "Beryllium", "Boron", "Carbon", "Nitrogen", "Oxygen", "Fluorine", "Neon",
                "Sodium", "Magnesium", "Aluminum", "Silicon", "Phosphorus", "Sulfur", "Chlorine", "Argon", "Potassium", "Calcium",
                "Scandium", "Titanium", "Vanadium", "Chromium", "Manganese", "Iron", "Cobalt", "Nickel", "Copper", "Zinc",
                "Gallium", "Germanium", "Arsenic", "Selenium", "Bromine", "Krypton", "Rubidium", "Strontium", "Yttrium", "Zirconium",
                "Niobium", "Molybdenum", "Technetium", "Ruthenium", "Rhodium", "Palladium", "Silver", "Cadmium", "Indium", "Tin",
                "Antimony", "Tellurium", "Iodine", "Xenon", "Cesium", "Barium", "Lanthanum", "Cerium", "Praseodymium", "Neodymium",
                "Promethium", "Samarium", "Europium", "Gadolinium", "Terbium", "Dysprosium", "Holmium", "Erbium", "Thulium", "Ytterbium",
                "Lutetium", "Hafnium", "Tantalum", "Tungsten", "Rhenium", "Osmium", "Iridium", "Platinum", "Gold", "Mercury",
                "Thallium", "Lead", "Bismuth", "Polonium", "Astatine", "Radon", "Francium", "Radium", "Actinium", "Thorium",
                "Protactinium", "Uranium", "Neptunium", "Plutonium", "Americium", "Curium", "Berkelium", "Californium"
            };

            EmissionList = new ObservableCollection<Emission>(
                elementSymbols.Select((symbol, index) => new Emission
                {
                    Symbol = symbol,
                    Name = elementNames[index],
                    EmissionImageUrl = $"https://www.jlindemann.se/atomic/emission_lines/{symbol}.gif"
                })
            );

            FilteredEmissionList = new ObservableCollection<Emission>(EmissionList);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text?.Trim().ToLower() ?? "";
            FilteredEmissionList.Clear();

            foreach (var item in EmissionList)
            {
                if (item.Name.ToLower().Contains(query) ||
                    item.Symbol.ToLower().Contains(query))
                {
                    FilteredEmissionList.Add(item);
                }
            }
        }

        private void Image_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Image image && image.DataContext is Emission emission)
            {
                image.Source = new BitmapImage(new Uri(emission.EmissionImageUrl));
            }
        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProPage));
        }


        private void ListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Emission selectedEmission)
            {
                // Navigation stub
                Frame.Navigate(typeof(EmissionDetailsPage), selectedEmission);
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

        private Brush GetThemeBrush(string key) =>
            (Brush)Application.Current.Resources[key];
    }

    public class Emission
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public string EmissionImageUrl { get; set; }
    }
}