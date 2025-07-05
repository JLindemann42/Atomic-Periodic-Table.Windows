using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace Atomic_WinUI
{
    public sealed partial class PeriodicTablePage : Page
    {


        public Element Element1 { get; set; }
        public Element Element2 { get; set; }
        public Element Element3 { get; set; }
        public Element Element4 { get; set; }
        public Element Element5 { get; set; }
        public Element Element6 { get; set; }
        public Element Element7 { get; set; }
        public Element Element8 { get; set; }
        public Element Element9 { get; set; }
        public Element Element10 { get; set; }
        public Element Element11 { get; set; }
        public Element Element12 { get; set; }
        public Element Element13 { get; set; }
        public Element Element14 { get; set; }
        public Element Element15 { get; set; }
        public Element Element16 { get; set; }
        public Element Element17 { get; set; }
        public Element Element18 { get; set; }
        public Element Element19 { get; set; }
        public Element Element20 { get; set; }
        public Element Element21 { get; set; }
        public Element Element22 { get; set; }
        public Element Element23 { get; set; }
        public Element Element24 { get; set; }
        public Element Element25 { get; set; }
        public Element Element26 { get; set; }
        public Element Element27 { get; set; }
        public Element Element28 { get; set; }
        public Element Element29 { get; set; }
        public Element Element30 { get; set; }
        public Element Element31 { get; set; }
        public Element Element32 { get; set; }
        public Element Element33 { get; set; }
        public Element Element34 { get; set; }
        public Element Element35 { get; set; }
        public Element Element36 { get; set; }
        public Element Element37 { get; set; }
        public Element Element38 { get; set; }
        public Element Element39 { get; set; }
        public Element Element40 { get; set; }
        public Element Element41 { get; set; }
        public Element Element42 { get; set; }
        public Element Element43 { get; set; }
        public Element Element44 { get; set; }
        public Element Element45 { get; set; }
        public Element Element46 { get; set; }
        public Element Element47 { get; set; }
        public Element Element48 { get; set; }
        public Element Element49 { get; set; }
        public Element Element50 { get; set; }
        public Element Element51 { get; set; }
        public Element Element52 { get; set; }
        public Element Element53 { get; set; }
        public Element Element54 { get; set; }
        public Element Element55 { get; set; }
        public Element Element56 { get; set; }
        public Element Element57 { get; set; }
        public Element Element58 { get; set; }
        public Element Element59 { get; set; }
        public Element Element60 { get; set; }
        public Element Element61 { get; set; }
        public Element Element62 { get; set; }
        public Element Element63 { get; set; }
        public Element Element64 { get; set; }
        public Element Element65 { get; set; }
        public Element Element66 { get; set; }
        public Element Element67 { get; set; }
        public Element Element68 { get; set; }
        public Element Element69 { get; set; }
        public Element Element70 { get; set; }
        public Element Element71 { get; set; }
        public Element Element72 { get; set; }
        public Element Element73 { get; set; }
        public Element Element74 { get; set; }
        public Element Element75 { get; set; }
        public Element Element76 { get; set; }
        public Element Element77 { get; set; }
        public Element Element78 { get; set; }
        public Element Element79 { get; set; }
        public Element Element80 { get; set; }
        public Element Element81 { get; set; }
        public Element Element82 { get; set; }
        public Element Element83 { get; set; }
        public Element Element84 { get; set; }
        public Element Element85 { get; set; }
        public Element Element86 { get; set; }
        public Element Element87 { get; set; }
        public Element Element88 { get; set; }
        public Element Element89 { get; set; }
        public Element Element90 { get; set; }
        public Element Element91 { get; set; }
        public Element Element92 { get; set; }
        public Element Element93 { get; set; }
        public Element Element94 { get; set; }
        public Element Element95 { get; set; }
        public Element Element96 { get; set; }
        public Element Element97 { get; set; }
        public Element Element98 { get; set; }
        public Element Element99 { get; set; }
        public Element Element100 { get; set; }
        public Element Element101 { get; set; }
        public Element Element102 { get; set; }
        public Element Element103 { get; set; }
        public Element Element104 { get; set; }
        public Element Element105 { get; set; }
        public Element Element106 { get; set; }
        public Element Element107 { get; set; }
        public Element Element108 { get; set; }
        public Element Element109 { get; set; }
        public Element Element110 { get; set; }
        public Element Element111 { get; set; }
        public Element Element112 { get; set; }
        public Element Element113 { get; set; }
        public Element Element114 { get; set; }
        public Element Element115 { get; set; }
        public Element Element116 { get; set; }
        public Element Element117 { get; set; }
        public Element Element118 { get; set; }
        public Element Lanthanoids { get; set; }
        public Element Actinoids { get; set; }



        public PeriodicTablePage()
        {
            this.InitializeComponent();

            // Populate the elements
            Lanthanoids = new Element { AtomicNumber = 1, Symbol = "57-71", Name = "Lanthanoids", OriginalName = "Lanthanoids" };
            Actinoids = new Element { AtomicNumber = 1, Symbol = "89-103", Name = "Actinoids", OriginalName = "Actinoids" };

            Element1 = new Element { AtomicNumber = 1, Symbol = "H", Name = "Hydrogen", OriginalName = "Hydrogen" };
            Element2 = new Element { AtomicNumber = 2, Symbol = "He", Name = "Helium", OriginalName = "Helium" };
            Element3 = new Element { AtomicNumber = 3, Symbol = "Li", Name = "Lithium", OriginalName = "Lithium" };
            Element4 = new Element { AtomicNumber = 4, Symbol = "Be", Name = "Beryllium", OriginalName = "Beryllium" };
            Element5 = new Element { AtomicNumber = 5, Symbol = "B", Name = "Boron", OriginalName = "Boron" };
            Element6 = new Element { AtomicNumber = 6, Symbol = "C", Name = "Carbon", OriginalName = "Carbon" };
            Element7 = new Element { AtomicNumber = 7, Symbol = "N", Name = "Nitrogen", OriginalName = "Nitrogen" };
            Element8 = new Element { AtomicNumber = 8, Symbol = "O", Name = "Oxygen", OriginalName = "Oxygen" };
            Element9 = new Element { AtomicNumber = 9, Symbol = "F", Name = "Fluorine", OriginalName = "Fluorine" };
            Element10 = new Element { AtomicNumber = 10, Symbol = "Ne", Name = "Neon", OriginalName = "Neon" };
            Element11 = new Element { AtomicNumber = 11, Symbol = "Na", Name = "Sodium", OriginalName = "Sodium" };
            Element12 = new Element { AtomicNumber = 12, Symbol = "Mg", Name = "Magnesium", OriginalName = "Magnesium" };
            Element13 = new Element { AtomicNumber = 13, Symbol = "Al", Name = "Aluminium", OriginalName = "Aluminium" };
            Element14 = new Element { AtomicNumber = 14, Symbol = "Si", Name = "Silicon", OriginalName = "Silicon" };
            Element15 = new Element { AtomicNumber = 15, Symbol = "P", Name = "Phosphorus", OriginalName = "Phosphorus" };
            Element16 = new Element { AtomicNumber = 16, Symbol = "S", Name = "Sulfur", OriginalName = "Sulfur" };
            Element17 = new Element { AtomicNumber = 17, Symbol = "Cl", Name = "Chlorine", OriginalName = "Chlorine" };
            Element18 = new Element { AtomicNumber = 18, Symbol = "Ar", Name = "Argon", OriginalName = "Argon" };
            Element19 = new Element { AtomicNumber = 19, Symbol = "K", Name = "Potassium", OriginalName = "Potassium" };
            Element20 = new Element { AtomicNumber = 20, Symbol = "Ca", Name = "Calcium", OriginalName = "Calcium" };
            Element21 = new Element { AtomicNumber = 21, Symbol = "Sc", Name = "Scandium", OriginalName = "Scandium" };
            Element22 = new Element { AtomicNumber = 22, Symbol = "Ti", Name = "Titanium", OriginalName = "Titanium" };
            Element23 = new Element { AtomicNumber = 23, Symbol = "V", Name = "Vanadium", OriginalName = "Vanadium" };
            Element24 = new Element { AtomicNumber = 24, Symbol = "Cr", Name = "Chromium", OriginalName = "Chromium" };
            Element25 = new Element { AtomicNumber = 25, Symbol = "Mn", Name = "Manganese", OriginalName = "Manganese" };
            Element26 = new Element { AtomicNumber = 26, Symbol = "Fe", Name = "Iron", OriginalName = "Iron" };
            Element27 = new Element { AtomicNumber = 27, Symbol = "Co", Name = "Cobalt", OriginalName = "Cobalt" };
            Element28 = new Element { AtomicNumber = 28, Symbol = "Ni", Name = "Nickel", OriginalName = "Nickel" };
            Element29 = new Element { AtomicNumber = 29, Symbol = "Cu", Name = "Copper", OriginalName = "Copper" };
            Element30 = new Element { AtomicNumber = 30, Symbol = "Zn", Name = "Zinc", OriginalName = "Zinc" };
            Element31 = new Element { AtomicNumber = 31, Symbol = "Ga", Name = "Gallium", OriginalName = "Gallium" };
            Element32 = new Element { AtomicNumber = 32, Symbol = "Ge", Name = "Germanium", OriginalName = "Germanium" };
            Element33 = new Element { AtomicNumber = 33, Symbol = "As", Name = "Arsenic", OriginalName = "Arsenic" };
            Element34 = new Element { AtomicNumber = 34, Symbol = "Se", Name = "Selenium", OriginalName = "Selenium" };
            Element35 = new Element { AtomicNumber = 35, Symbol = "Br", Name = "Bromine", OriginalName = "Bromine" };
            Element36 = new Element { AtomicNumber = 36, Symbol = "Kr", Name = "Krypton", OriginalName = "Krypton" };
            Element37 = new Element { AtomicNumber = 37, Symbol = "Rb", Name = "Rubidium", OriginalName = "Rubidium" };
            Element38 = new Element { AtomicNumber = 38, Symbol = "Sr", Name = "Strontium", OriginalName = "Strontium" };
            Element39 = new Element { AtomicNumber = 39, Symbol = "Y", Name = "Yttrium", OriginalName = "Yttrium" };
            Element40 = new Element { AtomicNumber = 40, Symbol = "Zr", Name = "Zirconium", OriginalName = "Zirconium" };
            Element41 = new Element { AtomicNumber = 41, Symbol = "Nb", Name = "Niobium", OriginalName = "Niobium" };
            Element42 = new Element { AtomicNumber = 42, Symbol = "Mo", Name = "Molybdenum", OriginalName = "Molybdenum" };
            Element43 = new Element { AtomicNumber = 43, Symbol = "Tc", Name = "Technetium", OriginalName = "Technetium" };
            Element44 = new Element { AtomicNumber = 44, Symbol = "Ru", Name = "Ruthenium", OriginalName = "Ruthenium" };
            Element45 = new Element { AtomicNumber = 45, Symbol = "Rh", Name = "Rhodium", OriginalName = "Rhodium" };
            Element46 = new Element { AtomicNumber = 46, Symbol = "Pd", Name = "Palladium", OriginalName = "Palladium" };
            Element47 = new Element { AtomicNumber = 47, Symbol = "Ag", Name = "Silver", OriginalName = "Silver" };
            Element48 = new Element { AtomicNumber = 48, Symbol = "Cd", Name = "Cadmium", OriginalName = "Cadmium" };
            Element49 = new Element { AtomicNumber = 49, Symbol = "In", Name = "Indium", OriginalName = "Indium" };
            Element50 = new Element { AtomicNumber = 50, Symbol = "Sn", Name = "Tin", OriginalName = "Tin" };
            Element51 = new Element { AtomicNumber = 51, Symbol = "Sb", Name = "Antimony", OriginalName = "Antimony" };
            Element52 = new Element { AtomicNumber = 52, Symbol = "Te", Name = "Tellurium", OriginalName = "Tellurium" };
            Element53 = new Element { AtomicNumber = 53, Symbol = "I", Name = "Iodine", OriginalName = "Iodine" };
            Element54 = new Element { AtomicNumber = 54, Symbol = "Xe", Name = "Xenon", OriginalName = "Xenon" };
            Element55 = new Element { AtomicNumber = 55, Symbol = "Cs", Name = "Caesium", OriginalName = "Caesium" };
            Element56 = new Element { AtomicNumber = 56, Symbol = "Ba", Name = "Barium", OriginalName = "Barium" };
            Element57 = new Element { AtomicNumber = 57, Symbol = "La", Name = "Lanthanum", OriginalName = "Lanthanum" };
            Element58 = new Element { AtomicNumber = 58, Symbol = "Ce", Name = "Cerium", OriginalName = "Cerium" };
            Element59 = new Element { AtomicNumber = 59, Symbol = "Pr", Name = "Praseodymium", OriginalName = "Praseodymium" };
            Element60 = new Element { AtomicNumber = 60, Symbol = "Nd", Name = "Neodymium", OriginalName = "Neodymium" };
            Element61 = new Element { AtomicNumber = 61, Symbol = "Pm", Name = "Promethium", OriginalName = "Promethium" };
            Element62 = new Element { AtomicNumber = 62, Symbol = "Sm", Name = "Samarium", OriginalName = "Samarium" };
            Element63 = new Element { AtomicNumber = 63, Symbol = "Eu", Name = "Europium", OriginalName = "Europium" };
            Element64 = new Element { AtomicNumber = 64, Symbol = "Gd", Name = "Gadolinium", OriginalName = "Gadolinium" };
            Element65 = new Element { AtomicNumber = 65, Symbol = "Tb", Name = "Terbium", OriginalName = "Terbium" };
            Element66 = new Element { AtomicNumber = 66, Symbol = "Dy", Name = "Dysprosium", OriginalName = "Dysprosium" };
            Element67 = new Element { AtomicNumber = 67, Symbol = "Ho", Name = "Holmium", OriginalName = "Holmium" };
            Element68 = new Element { AtomicNumber = 68, Symbol = "Er", Name = "Erbium", OriginalName = "Erbium" };
            Element69 = new Element { AtomicNumber = 69, Symbol = "Tm", Name = "Thulium", OriginalName = "Thulium" };
            Element70 = new Element { AtomicNumber = 70, Symbol = "Yb", Name = "Ytterbium", OriginalName = "Ytterbium" };
            Element71 = new Element { AtomicNumber = 71, Symbol = "Lu", Name = "Lutetium", OriginalName = "Lutetium" };
            Element72 = new Element { AtomicNumber = 72, Symbol = "Hf", Name = "Hafnium", OriginalName = "Hafnium" };
            Element73 = new Element { AtomicNumber = 73, Symbol = "Ta", Name = "Tantalum", OriginalName = "Tantalum" };
            Element74 = new Element { AtomicNumber = 74, Symbol = "W", Name = "Tungsten", OriginalName = "Tungsten" };
            Element75 = new Element { AtomicNumber = 75, Symbol = "Re", Name = "Rhenium", OriginalName = "Rhenium" };
            Element76 = new Element { AtomicNumber = 76, Symbol = "Os", Name = "Osmium", OriginalName = "Osmium" };
            Element77 = new Element { AtomicNumber = 77, Symbol = "Ir", Name = "Iridium", OriginalName = "Iridium" };
            Element78 = new Element { AtomicNumber = 78, Symbol = "Pt", Name = "Platinum", OriginalName = "Platinum" };
            Element79 = new Element { AtomicNumber = 79, Symbol = "Au", Name = "Gold", OriginalName = "Gold" };
            Element80 = new Element { AtomicNumber = 80, Symbol = "Hg", Name = "Mercury", OriginalName = "Mercury" };
            Element81 = new Element { AtomicNumber = 81, Symbol = "Tl", Name = "Thallium", OriginalName = "Thallium" };
            Element82 = new Element { AtomicNumber = 82, Symbol = "Pb", Name = "Lead", OriginalName = "Lead" };
            Element83 = new Element { AtomicNumber = 83, Symbol = "Bi", Name = "Bismuth", OriginalName = "Bismuth" };
            Element84 = new Element { AtomicNumber = 84, Symbol = "Po", Name = "Polonium", OriginalName = "Polonium" };
            Element85 = new Element { AtomicNumber = 85, Symbol = "At", Name = "Astatine", OriginalName = "Astatine" };
            Element86 = new Element { AtomicNumber = 86, Symbol = "Rn", Name = "Radon", OriginalName = "Radon" };
            Element87 = new Element { AtomicNumber = 87, Symbol = "Fr", Name = "Francium", OriginalName = "Francium" };
            Element88 = new Element { AtomicNumber = 88, Symbol = "Ra", Name = "Radium", OriginalName = "Radium" };
            Element89 = new Element { AtomicNumber = 89, Symbol = "Ac", Name = "Actinium", OriginalName = "Actinium" };
            Element90 = new Element { AtomicNumber = 90, Symbol = "Th", Name = "Thorium", OriginalName = "Thorium" };
            Element91 = new Element { AtomicNumber = 91, Symbol = "Pa", Name = "Protactinium", OriginalName = "Protactinium" };
            Element92 = new Element { AtomicNumber = 92, Symbol = "U", Name = "Uranium", OriginalName = "Uranium" };
            Element93 = new Element { AtomicNumber = 93, Symbol = "Np", Name = "Neptunium", OriginalName = "Neptunium" };
            Element94 = new Element { AtomicNumber = 94, Symbol = "Pu", Name = "Plutonium", OriginalName = "Plutonium" };
            Element95 = new Element { AtomicNumber = 95, Symbol = "Am", Name = "Americium", OriginalName = "Americium" };
            Element96 = new Element { AtomicNumber = 96, Symbol = "Cm", Name = "Curium", OriginalName = "Curium" };
            Element97 = new Element { AtomicNumber = 97, Symbol = "Bk", Name = "Berkelium", OriginalName = "Berkelium" };
            Element98 = new Element { AtomicNumber = 98, Symbol = "Cf", Name = "Californium", OriginalName = "Californium" };
            Element99 = new Element { AtomicNumber = 99, Symbol = "Es", Name = "Einsteinium", OriginalName = "Einsteinium" };
            Element100 = new Element { AtomicNumber = 100, Symbol = "Fm", Name = "Fermium", OriginalName = "Fermium" };
            Element101 = new Element { AtomicNumber = 101, Symbol = "Md", Name = "Mendelevium", OriginalName = "Mendelevium" };
            Element102 = new Element { AtomicNumber = 102, Symbol = "No", Name = "Nobelium", OriginalName = "Nobelium" };
            Element103 = new Element { AtomicNumber = 103, Symbol = "Lr", Name = "Lawrencium", OriginalName = "Lawrencium" };
            Element104 = new Element { AtomicNumber = 104, Symbol = "Rf", Name = "Rutherfordium", OriginalName = "Rutherfordium" };
            Element105 = new Element { AtomicNumber = 105, Symbol = "Db", Name = "Dubnium", OriginalName = "Dubnium" };
            Element106 = new Element { AtomicNumber = 106, Symbol = "Sg", Name = "Seaborgium", OriginalName = "Seaborgium" };
            Element107 = new Element { AtomicNumber = 107, Symbol = "Bh", Name = "Bohrium", OriginalName = "Bohrium" };
            Element108 = new Element { AtomicNumber = 108, Symbol = "Hs", Name = "Hassium", OriginalName = "Hassium" };
            Element109 = new Element { AtomicNumber = 109, Symbol = "Mt", Name = "Meitnerium", OriginalName = "Meitnerium" };
            Element110 = new Element { AtomicNumber = 110, Symbol = "Ds", Name = "Darmstadtium", OriginalName = "Darmstadtium" };
            Element111 = new Element { AtomicNumber = 111, Symbol = "Rg", Name = "Roentgenium", OriginalName = "Roentgenium" };
            Element112 = new Element { AtomicNumber = 112, Symbol = "Cn", Name = "Copernicium", OriginalName = "Copernicium" };
            Element113 = new Element { AtomicNumber = 113, Symbol = "Nh", Name = "Nihonium", OriginalName = "Nihonium" };
            Element114 = new Element { AtomicNumber = 114, Symbol = "Fl", Name = "Flerovium", OriginalName = "Flerovium" };
            Element115 = new Element { AtomicNumber = 115, Symbol = "Mc", Name = "Moscovium", OriginalName = "Moscovium" };
            Element116 = new Element { AtomicNumber = 116, Symbol = "Lv", Name = "Livermorium", OriginalName = "Livermorium" };
            Element117 = new Element { AtomicNumber = 117, Symbol = "Ts", Name = "Tennessine", OriginalName = "Tennessine" };
            Element118 = new Element { AtomicNumber = 118, Symbol = "Og", Name = "Oganesson", OriginalName = "Oganesson" };



            // Set the DataContext for the page
            this.DataContext = this;
        }
        private void OnElementButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Element element)
            {
                // Navigate to a new page or update a content view
                Frame.Navigate(typeof(ElementDetailsPage), element);
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);


            // Start the fade-in animation
            var fadeInStoryboard = (Storyboard)this.Resources["FadeInStoryboard"];
            fadeInStoryboard.Begin();

        }


        private void OnDisplayModeSelected(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem)
            {
                string mode = menuItem.Text;
                System.Diagnostics.Debug.WriteLine($"Selected mode: {mode}");
                string unit = ApplicationData.Current.LocalSettings.Values["DefaultUnit"] as string ?? "Celsius";

                foreach (var child in PeriodicTableGrid.Children)
                {
                    if (child is Button button && button.DataContext is Element element)
                    {
                        // Skip "Lanthanoids" and "Actinoids"
                        if (element.OriginalName == "Lanthanoids" || element.OriginalName == "Actinoids")
                        {
                            continue;
                        }

                        string elementName = element.OriginalName.ToLower(); // Use OriginalName here
                        string filePath = Path.Combine(AppContext.BaseDirectory, "Elements", $"{elementName}.json");

                        if (File.Exists(filePath))
                        {
                            string jsonData = File.ReadAllText(filePath);
                            var elementData = JsonSerializer.Deserialize<JsonElement>(jsonData);

                            if (elementData.ValueKind == JsonValueKind.Object)
                            {
                                // Trigger the fade-out and fade-in animation
                                VisualStateManager.GoToState(button, "NormalState", false); // Reset to NormalState
                                VisualStateManager.GoToState(button, "TextChanging", true);
                                // Update the Name property dynamically
                                switch (mode)
                                {
                                    case "Element Name (Default)":
                                        if (elementData.TryGetProperty("element", out var elementNameD))
                                        {
                                            element.Name = elementNameD.GetString();
                                        }
                                        break;
                                    case "Electronegativity":
                                        if (elementData.TryGetProperty("element_electronegativty", out var electronegativity))
                                        {
                                            element.Name = electronegativity.GetString();
                                        }
                                        break;

                                    case "Atomic Weight":
                                        if (elementData.TryGetProperty("element_atomicmass", out var atomicMass))
                                        {
                                            element.Name = atomicMass.GetString();
                                        }
                                        break;
                                    case "Boiling Point":
                                        if (elementData.TryGetProperty($"element_boiling_{unit.ToLower()}", out var boilingPoint)) //{unit} from settings
                                        {
                                            element.Name = boilingPoint.GetString();
                                        }
                                        break;
                                    case "Melting Point":
                                        if (elementData.TryGetProperty($"element_boiling_{unit.ToLower()}", out var meltingPoint)) //{unit} from settings
                                        {
                                            element.Name = meltingPoint.GetString();
                                        }
                                        break;

                                    case "Element Groups":
                                        if (elementData.TryGetProperty("element_group", out var elementGroup))
                                        {
                                            element.Name = elementGroup.GetString();
                                        }
                                        break;

                                    case "Phase (STP)":
                                        if (elementData.TryGetProperty("element_phase", out var elementPhase))
                                        {
                                            element.Name = elementPhase.GetString();
                                        }
                                        break;

                                    case "Year Discovered":
                                        if (elementData.TryGetProperty("element_year", out var elementYear))
                                        {
                                            element.Name = elementYear.GetString();
                                        }
                                        break;

                                    default:
                                        System.Diagnostics.Debug.WriteLine($"Unsupported mode: {mode}");
                                        break;
                                }
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"File not found: {filePath}");
                        }
                    }
                }
            }
        }
        private void OnRandomElementButtonClick(object sender, RoutedEventArgs e)
        {
            // Use reflection to get all Element properties named "Element1" to "Element118"
            var elementList = new List<Element>();

            for (int i = 1; i <= 118; i++)
            {
                var prop = typeof(PeriodicTablePage).GetProperty($"Element{i}");
                if (prop != null)
                {
                    var element = prop.GetValue(this) as Element;
                    if (element != null)
                    {
                        elementList.Add(element);
                    }
                }
            }

            // Pick a random element
            var random = new Random();
            var selected = elementList[random.Next(elementList.Count)];

            // Navigate to the details page
            Frame.Navigate(typeof(ElementDetailsPage), selected);
        }
    }

}

