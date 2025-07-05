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
    public sealed partial class GeologyPage : Page
    {
        private ObservableCollection<Geology> GeologyList { get; set; }
        public ObservableCollection<Geology> FilteredGeologyList { get; set; }
        private void ListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Geology selectedGeology)
            {
                // Navigate to PoissonDetailsPage, passing the selected Ionization
                Frame.Navigate(typeof(GeologyDetailsPage), selectedGeology);
            }
        }

        public GeologyPage()
        {
            this.InitializeComponent();
            //Check PRO or Not:
            if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
            {
                GeologyToolBar.Visibility = Visibility.Visible;
                GeologyListView.Visibility = Visibility.Visible;
                NoProContent.Visibility = Visibility.Collapsed;
            }
            else
            {
                GeologyToolBar.Visibility = Visibility.Collapsed;
                GeologyListView.Visibility = Visibility.Collapsed;
                NoProContent.Visibility = Visibility.Visible;
            }
            GeologyList = new ObservableCollection<Geology>
            {

                new Geology { Name = "Plagioclase", Type = "Mineral", Group = "Feldspar/ Tectosilicate", Color = "White, Gray, or Bluish White", Streak = "White", Cristal = "Triclinic", Hardness = "6-6.5", Luster = "Vitreous", Density = "2.63-2.75 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Graphite", Type = "Mineral", Group = "Native Element", Color = "Gray to Black", Streak = "Black", Cristal = "Hexagonal", Hardness = "1-2", Luster = "Metallic to Earthy", Density = "2.09-2.23 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Quartz", Type = "Mineral", Group = "Silicate", Color = "Colorless through various colors", Streak = "White", Cristal = "Hexagonal", Hardness = "7", Luster = "Vitreous", Density = "2.65 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Olivine", Type = "Mineral", Group = "Nesosilicate", Color = "Olive Green", Streak = "White", Cristal = "Orthorhombic", Hardness = "6.5-7", Luster = "Vitreous", Density = "3.27-4.37 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Pyrite", Type = "Mineral", Group = "Sulfide", Color = "Brass Yellow", Streak = "Greenish Black to Brownish Black", Cristal = "Isometric", Hardness = "6-6.5", Luster = "Metallic", Density = "4.9-5.2 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Mica", Type = "Mineral", Group = "Phyllosilicate", Color = "Colorless through various colors", Streak = "White", Cristal = "Monoclinic", Hardness = "2.5-4", Luster = "Vitreous to Pearly", Density = "2.7-3.1 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Biotite", Type = "Mineral", Group = "Phyllosilicate", Color = "Dark Green, Brown, Black", Streak = "White to Gray", Cristal = "Monoclinic", Hardness = "2.5-3", Luster = "Vitreous to Pearly", Density = "2.7-3.3 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Chlorite", Type = "Mineral", Group = "Phyllosilicate", Color = "Green", Streak = "White", Cristal = "Monoclinic or Triclinic", Hardness = "2-2.5", Luster = "Vitreous to Dull", Density = "2.6-3.3 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Talc", Type = "Mineral", Group = "Phyllosilicate", Color = "White, Gray, Green", Streak = "White", Cristal = "Monoclinic", Hardness = "1", Luster = "Pearly", Density = "2.7-2.8 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Magnetite", Type = "Mineral", Group = "Oxide", Color = "Black", Streak = "Black", Cristal = "Isometric", Hardness = "5.5-6.5", Luster = "Metallic", Density = "5.17-5.18 g/cm3", Magnetism = "Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Fluorite", Type = "Mineral", Group = "Halide", Color = "Colorless through various colors", Streak = "White", Cristal = "Isometric", Hardness = "4", Luster = "Vitreous", Density = "3.0-3.2 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Diopside", Type = "Mineral", Group = "Inosilicate", Color = "Light to Dark Green", Streak = "White to Gray", Cristal = "Monoclinic", Hardness = "5.5-6.5", Luster = "Vitreous to Dull", Density = "3.22-3.38 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Chalcopyrite", Type = "Mineral", Group = "Sulfide", Color = "Brass Yellow", Streak = "Greenish Black", Cristal = "Tetragonal", Hardness = "3.5-4", Luster = "Metallic", Density = "4.1-4.3 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Beryl", Type = "Mineral", Group = "Cyclosilicate", Color = "Green, Blue, Yellow, Colorless, Pink", Streak = "White", Cristal = "Hexagonal", Hardness = "7.5-8", Luster = "Vitreous", Density = "2.63-2.80 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Garnet", Type = "Mineral", Group = "Nesosilicate", Color = "Red, Yellow, Green, Brown, Black", Streak = "White", Cristal = "Isometric", Hardness = "6.5-7.5", Luster = "Vitreous to Resinous", Density = "3.1-4.3 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Potassium Feldspar", Type = "Mineral", Group = "Tectosilicate", Color = "White, Pink, Gray, Red, or Green", Streak = "White", Cristal = "Monoclinic or Triclinic", Hardness = "6-6.5", Luster = "Vitreous", Density = "2.55-2.63 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Hornblende", Type = "Mineral", Group = "Inosilicate", Color = "Green to Black", Streak = "White to Gray", Cristal = "Monoclinic", Hardness = "5-6", Luster = "Vitreous to Dull", Density = "3.0-3.5 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Epidote", Type = "Mineral", Group = "Sorosilicate", Color = "Green to Yellowish Green", Streak = "White to Gray", Cristal = "Monoclinic", Hardness = "6-7", Luster = "Vitreous", Density = "3.35-3.45 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Hematite", Type = "Mineral", Group = "Oxide", Color = "Black to Steel or Silver-Gray, Brown to Reddish-Brown, or Red", Streak = "Red to Reddish Brown", Cristal = "Hexagonal", Hardness = "5-6", Luster = "Metallic to Earthy", Density = "5.26 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Galena", Type = "Mineral", Group = "Sulfide", Color = "Lead Gray", Streak = "Lead Gray to Black", Cristal = "Isometric", Hardness = "2.5-2.75", Luster = "Metallic", Density = "7.4-7.6 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Gypsum", Type = "Mineral", Group = "Sulfate", Color = "Colorless to White, also various colors due to impurities", Streak = "White", Cristal = "Monoclinic", Hardness = "2", Luster = "Vitreous to Pearly", Density = "2.3-2.4 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Goethite", Type = "Mineral", Group = "Hydroxide", Color = "Yellowish to Reddish Brown, Black", Streak = "Brownish Yellow", Cristal = "Orthorhombic", Hardness = "5-5.5", Luster = "Adamantine to Silky", Density = "3.3-4.3 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Apatite", Type = "Mineral", Group = "Phosphate", Color = "Green, Blue, Yellow, Brown, Purple", Streak = "White", Cristal = "Hexagonal", Hardness = "5", Luster = "Vitreous", Density = "3.1-3.2 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Barite", Type = "Mineral", Group = "Sulfate", Color = "Colorless, White, Yellow, Brown, Blue, Gray", Streak = "White", Cristal = "Orthorhombic", Hardness = "3-3.5", Luster = "Vitreous to Pearly", Density = "4.48 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" },
                new Geology { Name = "Dolomite", Type = "Mineral", Group = "Carbonate", Color = "Colorless, White, Pink, Green, Brown, Gray", Streak = "White", Cristal = "Trigonal", Hardness = "3.5-4", Luster = "Vitreous to Pearly", Density = "2.85 g/cm3", Magnetism = "Non-Magnetic", Hydrochloride = "Hydrochloride doesn't effect" }
             };

            FilteredGeologyList = new ObservableCollection<Geology>(GeologyList);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text?.Trim().ToLower() ?? "";
            FilteredGeologyList.Clear();

            foreach (var item in GeologyList)
            {
                //Adding possibility to search geology names, category etc
                if (item.Name.ToLower().Contains(query) ||
                    item.Group.ToString().Contains(query) ||
                    item.Color.ToString().Contains(query) ||
                    item.Cristal.ToLower().Contains(query))
                {
                    FilteredGeologyList.Add(item);
                }
            }
        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProPage));
        }

        private void OnClearFilterSelected(object sender, RoutedEventArgs e)
        {
            FilteredGeologyList.Clear();
            foreach (var geology in GeologyList)
                FilteredGeologyList.Add(geology);
        }

        private void OnDisplayModeSelected(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Text is string category)
            {
                var filtered = GeologyList.Where(eq => eq.Type == category);
                FilteredGeologyList.Clear();
                foreach (var geology in filtered)
                    FilteredGeologyList.Add(geology);
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



    public class Geology
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Group { get; set; }
        public string Color { get; set; }
        public string Streak { get; set; }
        public string Cristal { get; set; }
        public string Hardness { get; set; }
        public string Luster { get; set; }
        public string Density { get; set; }
        public string Magnetism { get; set; }
        public string Hydrochloride { get; set; }
    }
}