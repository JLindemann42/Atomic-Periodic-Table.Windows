using Atomic_PeriodicTable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;

namespace Atomic_WinUI
{
    public sealed partial class IsotopePage : Page
    {
        private ObservableCollection<Isotope> IsotopeList { get; set; }
        public ObservableCollection<Isotope> FilteredIsotopeList { get; set; }

        private void ListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Isotope selectedIsotope)
            {
                // Navigate to IsotopeDetailsPage, passing the selected isotope
                Frame.Navigate(typeof(IsotopeDetailsPage), selectedIsotope);
            }
        }


        public IsotopePage()
        {
            this.InitializeComponent();
            IsotopeList = new ObservableCollection<Isotope>
            {
            new Isotope { Symbol = "H", Name = "Hydrogen", Number = 1 },
            new Isotope { Symbol = "He", Name = "Helium", Number = 2 },
            new Isotope { Symbol = "Li", Name = "Lithium", Number = 3 },
            new Isotope { Symbol = "Be", Name = "Beryllium", Number = 4 },
            new Isotope { Symbol = "B", Name = "Boron", Number = 5 },
            new Isotope { Symbol = "C", Name = "Carbon", Number = 6 },
            new Isotope { Symbol = "N", Name = "Nitrogen", Number = 7 },
            new Isotope { Symbol = "O", Name = "Oxygen", Number = 8 },
            new Isotope { Symbol = "F", Name = "Fluorine", Number = 9 },
            new Isotope { Symbol = "Ne", Name = "Neon", Number = 10 },
            new Isotope { Symbol = "Na", Name = "Sodium", Number = 11 },
            new Isotope { Symbol = "Mg", Name = "Magnesium", Number = 12 },
            new Isotope { Symbol = "Al", Name = "Aluminum", Number = 13 },
            new Isotope { Symbol = "Si", Name = "Silicon", Number = 14 },
            new Isotope { Symbol = "P", Name = "Phosphorus", Number = 15 },
            new Isotope { Symbol = "S", Name = "Sulfur", Number = 16 },
            new Isotope { Symbol = "Cl", Name = "Chlorine", Number = 17 },
            new Isotope { Symbol = "Ar", Name = "Argon", Number = 18 },
            new Isotope { Symbol = "K", Name = "Potassium", Number = 19 },
            new Isotope { Symbol = "Ca", Name = "Calcium", Number = 20 },
            new Isotope { Symbol = "Sc", Name = "Scandium", Number = 21 },
            new Isotope { Symbol = "Ti", Name = "Titanium", Number = 22 },
            new Isotope { Symbol = "V", Name = "Vanadium", Number = 23 },
            new Isotope { Symbol = "Cr", Name = "Chromium", Number = 24 },
            new Isotope { Symbol = "Mn", Name = "Manganese", Number = 25 },
            new Isotope { Symbol = "Fe", Name = "Iron", Number = 26 },
            new Isotope { Symbol = "Co", Name = "Cobalt", Number = 27 },
            new Isotope { Symbol = "Ni", Name = "Nickel", Number = 28 },
            new Isotope { Symbol = "Cu", Name = "Copper", Number = 29 },
            new Isotope { Symbol = "Zn", Name = "Zinc", Number = 30 },
            new Isotope { Symbol = "Ga", Name = "Gallium", Number = 31 },
            new Isotope { Symbol = "Ge", Name = "Germanium", Number = 32 },
            new Isotope { Symbol = "As", Name = "Arsenic", Number = 33 },
            new Isotope { Symbol = "Se", Name = "Selenium", Number = 34 },
            new Isotope { Symbol = "Br", Name = "Bromine", Number = 35 },
            new Isotope { Symbol = "Kr", Name = "Krypton", Number = 36 },
            new Isotope { Symbol = "Rb", Name = "Rubidium", Number = 37 },
            new Isotope { Symbol = "Sr", Name = "Strontium", Number = 38 },
            new Isotope { Symbol = "Y", Name = "Yttrium", Number = 39 },
            new Isotope { Symbol = "Zr", Name = "Zirconium", Number = 40 },
            new Isotope { Symbol = "Nb", Name = "Niobium", Number = 41 },
            new Isotope { Symbol = "Mo", Name = "Molybdenum", Number = 42 },
            new Isotope { Symbol = "Tc", Name = "Technetium", Number = 43 },
            new Isotope { Symbol = "Ru", Name = "Ruthenium", Number = 44 },
            new Isotope { Symbol = "Rh", Name = "Rhodium", Number = 45 },
            new Isotope { Symbol = "Pd", Name = "Palladium", Number = 46 },
            new Isotope { Symbol = "Ag", Name = "Silver", Number = 47 },
            new Isotope { Symbol = "Cd", Name = "Cadmium", Number = 48 },
            new Isotope { Symbol = "In", Name = "Indium", Number = 49 },
            new Isotope { Symbol = "Sn", Name = "Tin", Number = 50 },
            new Isotope { Symbol = "Sb", Name = "Antimony", Number = 51 },
            new Isotope { Symbol = "Te", Name = "Tellurium", Number = 52 },
            new Isotope { Symbol = "I", Name = "Iodine", Number = 53 },
            new Isotope { Symbol = "Xe", Name = "Xenon", Number = 54 },
            new Isotope { Symbol = "Cs", Name = "Cesium", Number = 55 },
            new Isotope { Symbol = "Ba", Name = "Barium", Number = 56 },
            new Isotope { Symbol = "La", Name = "Lanthanum", Number = 57 },
            new Isotope { Symbol = "Ce", Name = "Cerium", Number = 58 },
            new Isotope { Symbol = "Pr", Name = "Praseodymium", Number = 59 },
            new Isotope { Symbol = "Nd", Name = "Neodymium", Number = 60 },
            new Isotope { Symbol = "Pm", Name = "Promethium", Number = 61 },
            new Isotope { Symbol = "Sm", Name = "Samarium", Number = 62 },
            new Isotope { Symbol = "Eu", Name = "Europium", Number = 63 },
            new Isotope { Symbol = "Gd", Name = "Gadolinium", Number = 64 },
            new Isotope { Symbol = "Tb", Name = "Terbium", Number = 65 },
            new Isotope { Symbol = "Dy", Name = "Dysprosium", Number = 66 },
            new Isotope { Symbol = "Ho", Name = "Holmium", Number = 67 },
            new Isotope { Symbol = "Er", Name = "Erbium", Number = 68 },
            new Isotope { Symbol = "Tm", Name = "Thulium", Number = 69 },
            new Isotope { Symbol = "Yb", Name = "Ytterbium", Number = 70 },
            new Isotope { Symbol = "Lu", Name = "Lutetium", Number = 71 },
            new Isotope { Symbol = "Hf", Name = "Hafnium", Number = 72 },
            new Isotope { Symbol = "Ta", Name = "Tantalum", Number = 73 },
            new Isotope { Symbol = "W", Name = "Tungsten", Number = 74 },
            new Isotope { Symbol = "Re", Name = "Rhenium", Number = 75 },
            new Isotope { Symbol = "Os", Name = "Osmium", Number = 76 },
            new Isotope { Symbol = "Ir", Name = "Iridium", Number = 77 },
            new Isotope { Symbol = "Pt", Name = "Platinum", Number = 78 },
            new Isotope { Symbol = "Au", Name = "Gold", Number = 79 },
            new Isotope { Symbol = "Hg", Name = "Mercury", Number = 80 },
            new Isotope { Symbol = "Tl", Name = "Thallium", Number = 81 },
            new Isotope { Symbol = "Pb", Name = "Lead", Number = 82 },
            new Isotope { Symbol = "Bi", Name = "Bismuth", Number = 83 },
            new Isotope { Symbol = "Po", Name = "Polonium", Number = 84 },
            new Isotope { Symbol = "At", Name = "Astatine", Number = 85 },
            new Isotope { Symbol = "Rn", Name = "Radon", Number = 86 },
            new Isotope { Symbol = "Fr", Name = "Francium", Number = 87 },
            new Isotope { Symbol = "Ra", Name = "Radium", Number = 88 },
            new Isotope { Symbol = "Ac", Name = "Actinium", Number = 89 },
            new Isotope { Symbol = "Th", Name = "Thorium", Number = 90 },
            new Isotope { Symbol = "Pa", Name = "Protactinium", Number = 91 },
            new Isotope { Symbol = "U", Name = "Uranium", Number = 92 },
            new Isotope { Symbol = "Np", Name = "Neptunium", Number = 93 },
            new Isotope { Symbol = "Pu", Name = "Plutonium", Number = 94 },
            new Isotope { Symbol = "Am", Name = "Americium", Number = 95 },
            new Isotope { Symbol = "Cm", Name = "Curium", Number = 96 },
            new Isotope { Symbol = "Bk", Name = "Berkelium", Number = 97 },
            new Isotope { Symbol = "Cf", Name = "Californium", Number = 98 },
            new Isotope { Symbol = "Es", Name = "Einsteinium", Number = 99 },
            new Isotope { Symbol = "Fm", Name = "Fermium", Number = 100 },
            new Isotope { Symbol = "Md", Name = "Mendelevium", Number = 101 },
            new Isotope { Symbol = "No", Name = "Nobelium", Number = 102 },
            new Isotope { Symbol = "Lr", Name = "Lawrencium", Number = 103 },
            new Isotope { Symbol = "Rf", Name = "Rutherfordium", Number = 104 },
            new Isotope { Symbol = "Db", Name = "Dubnium", Number = 105 },
            new Isotope { Symbol = "Sg", Name = "Seaborgium", Number = 106 },
            new Isotope { Symbol = "Bh", Name = "Bohrium", Number = 107 },
            new Isotope { Symbol = "Hs", Name = "Hassium", Number = 108 },
            new Isotope { Symbol = "Mt", Name = "Meitnerium", Number = 109 },
            new Isotope { Symbol = "Ds", Name = "Darmstadtium", Number = 110 },
            new Isotope { Symbol = "Rg", Name = "Roentgenium", Number = 111 },
            new Isotope { Symbol = "Cn", Name = "Copernicium", Number = 112 },
            new Isotope { Symbol = "Fl", Name = "Flerovium", Number = 113 },
            new Isotope { Symbol = "Lv", Name = "Livermorium", Number = 114 },
            new Isotope { Symbol = "Mc", Name = "Moscovium", Number = 115 },
            new Isotope { Symbol = "Lv", Name = "Livermorium", Number = 116 },
            new Isotope { Symbol = "Ts", Name = "Tennessine", Number = 117 },
            new Isotope { Symbol = "Og", Name = "Oganesson", Number = 118 }
            };
            FilteredIsotopeList = new ObservableCollection<Isotope>(IsotopeList);

        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text?.Trim().ToLower() ?? "";
            FilteredIsotopeList.Clear();

            foreach (var item in IsotopeList)
            {
                if (item.Name.ToLower().Contains(query) ||
                    item.Symbol.ToLower().Contains(query) ||
                    item.Number.ToString().Contains(query))
                {
                    FilteredIsotopeList.Add(item);
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



    public class Isotope
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public int Number { get; set; }
    }
}