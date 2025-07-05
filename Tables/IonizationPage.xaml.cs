using Atomic_PeriodicTable.Tables;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;

namespace Atomic_WinUI
{
    public sealed partial class IonizationPage : Page
    {
        private ObservableCollection<Ionization> IonizationList { get; set; }
        public ObservableCollection<Ionization> FilteredIonizationList { get; set; }
        private void ListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Ionization selectedIonization)
            {
                // Navigate to IonizationDetailsPage, passing the selected Ionization
                Frame.Navigate(typeof(IonizationDetailsPage), selectedIonization.Name);
            }
        }

        public IonizationPage()
        {
            this.InitializeComponent();
            IonizationList = new ObservableCollection<Ionization>
            {
                  new Ionization { Symbol = "H", Name = "Hydrogen", Number = 1 },
            new Ionization { Symbol = "He", Name = "Helium", Number = 2 },
            new Ionization { Symbol = "Li", Name = "Lithium", Number = 3 },
            new Ionization { Symbol = "Be", Name = "Beryllium", Number = 4 },
            new Ionization { Symbol = "B", Name = "Boron", Number = 5 },
            new Ionization { Symbol = "C", Name = "Carbon", Number = 6 },
            new Ionization { Symbol = "N", Name = "Nitrogen", Number = 7 },
            new Ionization { Symbol = "O", Name = "Oxygen", Number = 8 },
            new Ionization { Symbol = "F", Name = "Fluorine", Number = 9 },
            new Ionization { Symbol = "Ne", Name = "Neon", Number = 10 },
            new Ionization { Symbol = "Na", Name = "Sodium", Number = 11 },
            new Ionization { Symbol = "Mg", Name = "Magnesium", Number = 12 },
            new Ionization { Symbol = "Al", Name = "Aluminum", Number = 13 },
            new Ionization { Symbol = "Si", Name = "Silicon", Number = 14 },
            new Ionization { Symbol = "P", Name = "Phosphorus", Number = 15 },
            new Ionization { Symbol = "S", Name = "Sulfur", Number = 16 },
            new Ionization { Symbol = "Cl", Name = "Chlorine", Number = 17 },
            new Ionization { Symbol = "Ar", Name = "Argon", Number = 18 },
            new Ionization { Symbol = "K", Name = "Potassium", Number = 19 },
            new Ionization { Symbol = "Ca", Name = "Calcium", Number = 20 },
            new Ionization { Symbol = "Sc", Name = "Scandium", Number = 21 },
            new Ionization { Symbol = "Ti", Name = "Titanium", Number = 22 },
            new Ionization { Symbol = "V", Name = "Vanadium", Number = 23 },
            new Ionization { Symbol = "Cr", Name = "Chromium", Number = 24 },
            new Ionization { Symbol = "Mn", Name = "Manganese", Number = 25 },
            new Ionization { Symbol = "Fe", Name = "Iron", Number = 26 },
            new Ionization { Symbol = "Co", Name = "Cobalt", Number = 27 },
            new Ionization { Symbol = "Ni", Name = "Nickel", Number = 28 },
            new Ionization { Symbol = "Cu", Name = "Copper", Number = 29 },
            new Ionization { Symbol = "Zn", Name = "Zinc", Number = 30 },
            new Ionization { Symbol = "Ga", Name = "Gallium", Number = 31 },
            new Ionization { Symbol = "Ge", Name = "Germanium", Number = 32 },
            new Ionization { Symbol = "As", Name = "Arsenic", Number = 33 },
            new Ionization { Symbol = "Se", Name = "Selenium", Number = 34 },
            new Ionization { Symbol = "Br", Name = "Bromine", Number = 35 },
            new Ionization { Symbol = "Kr", Name = "Krypton", Number = 36 },
            new Ionization { Symbol = "Rb", Name = "Rubidium", Number = 37 },
            new Ionization { Symbol = "Sr", Name = "Strontium", Number = 38 },
            new Ionization { Symbol = "Y", Name = "Yttrium", Number = 39 },
            new Ionization { Symbol = "Zr", Name = "Zirconium", Number = 40 },
            new Ionization { Symbol = "Nb", Name = "Niobium", Number = 41 },
            new Ionization { Symbol = "Mo", Name = "Molybdenum", Number = 42 },
            new Ionization { Symbol = "Tc", Name = "Technetium", Number = 43 },
            new Ionization { Symbol = "Ru", Name = "Ruthenium", Number = 44 },
            new Ionization { Symbol = "Rh", Name = "Rhodium", Number = 45 },
            new Ionization { Symbol = "Pd", Name = "Palladium", Number = 46 },
            new Ionization { Symbol = "Ag", Name = "Silver", Number = 47 },
            new Ionization { Symbol = "Cd", Name = "Cadmium", Number = 48 },
            new Ionization { Symbol = "In", Name = "Indium", Number = 49 },
            new Ionization { Symbol = "Sn", Name = "Tin", Number = 50 },
            new Ionization { Symbol = "Sb", Name = "Antimony", Number = 51 },
            new Ionization { Symbol = "Te", Name = "Tellurium", Number = 52 },
            new Ionization { Symbol = "I", Name = "Iodine", Number = 53 },
            new Ionization { Symbol = "Xe", Name = "Xenon", Number = 54 },
            new Ionization { Symbol = "Cs", Name = "Cesium", Number = 55 },
            new Ionization { Symbol = "Ba", Name = "Barium", Number = 56 },
            new Ionization { Symbol = "La", Name = "Lanthanum", Number = 57 },
            new Ionization { Symbol = "Ce", Name = "Cerium", Number = 58 },
            new Ionization { Symbol = "Pr", Name = "Praseodymium", Number = 59 },
            new Ionization { Symbol = "Nd", Name = "Neodymium", Number = 60 },
            new Ionization { Symbol = "Pm", Name = "Promethium", Number = 61 },
            new Ionization { Symbol = "Sm", Name = "Samarium", Number = 62 },
            new Ionization { Symbol = "Eu", Name = "Europium", Number = 63 },
            new Ionization { Symbol = "Gd", Name = "Gadolinium", Number = 64 },
            new Ionization { Symbol = "Tb", Name = "Terbium", Number = 65 },
            new Ionization { Symbol = "Dy", Name = "Dysprosium", Number = 66 },
            new Ionization { Symbol = "Ho", Name = "Holmium", Number = 67 },
            new Ionization { Symbol = "Er", Name = "Erbium", Number = 68 },
            new Ionization { Symbol = "Tm", Name = "Thulium", Number = 69 },
            new Ionization { Symbol = "Yb", Name = "Ytterbium", Number = 70 },
            new Ionization { Symbol = "Lu", Name = "Lutetium", Number = 71 },
            new Ionization { Symbol = "Hf", Name = "Hafnium", Number = 72 },
            new Ionization { Symbol = "Ta", Name = "Tantalum", Number = 73 },
            new Ionization { Symbol = "W", Name = "Tungsten", Number = 74 },
            new Ionization { Symbol = "Re", Name = "Rhenium", Number = 75 },
            new Ionization { Symbol = "Os", Name = "Osmium", Number = 76 },
            new Ionization { Symbol = "Ir", Name = "Iridium", Number = 77 },
            new Ionization { Symbol = "Pt", Name = "Platinum", Number = 78 },
            new Ionization { Symbol = "Au", Name = "Gold", Number = 79 },
            new Ionization { Symbol = "Hg", Name = "Mercury", Number = 80 },
            new Ionization { Symbol = "Tl", Name = "Thallium", Number = 81 },
            new Ionization { Symbol = "Pb", Name = "Lead", Number = 82 },
            new Ionization { Symbol = "Bi", Name = "Bismuth", Number = 83 },
            new Ionization { Symbol = "Po", Name = "Polonium", Number = 84 },
            new Ionization { Symbol = "At", Name = "Astatine", Number = 85 },
            new Ionization { Symbol = "Rn", Name = "Radon", Number = 86 },
            new Ionization { Symbol = "Fr", Name = "Francium", Number = 87 },
            new Ionization { Symbol = "Ra", Name = "Radium", Number = 88 },
            new Ionization { Symbol = "Ac", Name = "Actinium", Number = 89 },
            new Ionization { Symbol = "Th", Name = "Thorium", Number = 90 },
            new Ionization { Symbol = "Pa", Name = "Protactinium", Number = 91 },
            new Ionization { Symbol = "U", Name = "Uranium", Number = 92 },
            new Ionization { Symbol = "Np", Name = "Neptunium", Number = 93 },
            new Ionization { Symbol = "Pu", Name = "Plutonium", Number = 94 },
            new Ionization { Symbol = "Am", Name = "Americium", Number = 95 },
            new Ionization { Symbol = "Cm", Name = "Curium", Number = 96 },
            new Ionization { Symbol = "Bk", Name = "Berkelium", Number = 97 },
            new Ionization { Symbol = "Cf", Name = "Californium", Number = 98 },
            new Ionization { Symbol = "Es", Name = "Einsteinium", Number = 99 },
            new Ionization { Symbol = "Fm", Name = "Fermium", Number = 100 },
            new Ionization { Symbol = "Md", Name = "Mendelevium", Number = 101 },
            new Ionization { Symbol = "No", Name = "Nobelium", Number = 102 },
            new Ionization { Symbol = "Lr", Name = "Lawrencium", Number = 103 },
            new Ionization { Symbol = "Rf", Name = "Rutherfordium", Number = 104 },
            new Ionization { Symbol = "Db", Name = "Dubnium", Number = 105 },
            new Ionization { Symbol = "Sg", Name = "Seaborgium", Number = 106 },
            new Ionization { Symbol = "Bh", Name = "Bohrium", Number = 107 },
            new Ionization { Symbol = "Hs", Name = "Hassium", Number = 108 },
            new Ionization { Symbol = "Mt", Name = "Meitnerium", Number = 109 },
            new Ionization { Symbol = "Ds", Name = "Darmstadtium", Number = 110 },
            new Ionization { Symbol = "Rg", Name = "Roentgenium", Number = 111 },
            new Ionization { Symbol = "Cn", Name = "Copernicium", Number = 112 },
            new Ionization { Symbol = "Fl", Name = "Flerovium", Number = 113 },
            new Ionization { Symbol = "Lv", Name = "Livermorium", Number = 114 },
            new Ionization { Symbol = "Mc", Name = "Moscovium", Number = 115 },
            new Ionization { Symbol = "Lv", Name = "Livermorium", Number = 116 },
            new Ionization { Symbol = "Ts", Name = "Tennessine", Number = 117 },
            new Ionization { Symbol = "Og", Name = "Oganesson", Number = 118 }
            };

            FilteredIonizationList = new ObservableCollection<Ionization>(IonizationList);
        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text?.Trim().ToLower() ?? "";
            FilteredIonizationList.Clear();

            foreach (var item in IonizationList)
            {
                if (item.Name.ToLower().Contains(query) ||
                    item.Symbol.ToLower().Contains(query) ||
                    item.Number.ToString().Contains(query))
                {
                    FilteredIonizationList.Add(item);
                }
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



    public class Ionization
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public int Number { get; set; }
    }
}