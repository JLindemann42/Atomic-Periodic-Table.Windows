using Atomic_PeriodicTable;
using Atomic_PeriodicTable.Tables;
using Atomic_WinUI.Helpers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Windows.Foundation;
using Windows.Storage;


namespace Atomic_WinUI
{
    public sealed partial class ElementDetailsPage : Page
    {
        private bool IsDarkMode()
        {
            if (Window.Current?.Content is FrameworkElement root)
            {
                var actualTheme = root.ActualTheme;
                return actualTheme == ElementTheme.Dark;
            }
            return false;
        }

        private Element loadedElementData;
        public Element Element { get; set; }
        public bool IsProUser =>
    (ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true;

        private WriteableBitmap _cubeBitmap;
        private double _cubeYaw = 0, _cubePitch = 0, _cubeRoll = 0;
        private Point _lastPointer;
        private bool _isDragging = false;
        private DispatcherTimer _cubeTimer;

        private string _currentCrystalSystem = "Cubic"; // Default = Cubic, change later in Show3DView()

        public ObservableCollection<FavoriteProperty> FavoriteProperties { get; set; } = new();
        private List<FavoriteProperty> AllProperties { get; set; } = new();
        public ElementDetailsPage()
        {
            this.InitializeComponent();
            this.Loaded += ElementDetailsPage_Loaded;
            DataContext = this;
            InitializeAllProperties();
            LoadFavoriteProperties();
            UpdateIsLastOnFavoriteProperties();
            Setup3DView();
        }

        private void ElementDetailsPage_Loaded(object sender, RoutedEventArgs e)
        {
            var slideStoryboard = (Storyboard)this.Resources["SlideInStoryboard"];
            slideStoryboard.Completed += (s, args) =>
            {
                var fadeStoryboard = (Storyboard)this.Resources["FadeInBordersStoryboard"];
                fadeStoryboard.Begin();
            };
            slideStoryboard.Begin();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Element element)
            {
                Element = element;
                // Do NOT set DataContext = Element; This breaks FavoriteProperties binding!

                PopulateDescriptionFromJson(Element);

                // Construct the emission spectrum URL
                if (!string.IsNullOrEmpty(Element.Symbol))
                {
                    string emissionSpectrumUrl = $"https://www.jlindemann.se/atomic/emission_lines/{Element.Symbol}.gif";
                    EmissionSpectrumImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(emissionSpectrumUrl));
                }

                // Update the breadcrumb header
                if (Window.Current?.Content is Frame frame &&
                    frame.Content is MainWindow mainWindow)
                {
                    string breadcrumbName = !string.IsNullOrWhiteSpace(Element.Name) ? Element.Name : Element.OriginalName;
                }

                // Update favorite bar values after loading the element
                UpdateFavoritePropertyValues();
                LoadDefaultFavoritesIfNeeded();
            }
        }

        private void InitializeAllProperties()
        {
            AllProperties = new List<FavoriteProperty>
    {
        new FavoriteProperty { Key = "AtomicNumber", DisplayName = "Atomic Number" },
        new FavoriteProperty { Key = "ElementAtomicWeight", DisplayName = "Atomic Weight" },
        new FavoriteProperty { Key = "ElementDensity", DisplayName = "Density" },
        new FavoriteProperty { Key = "ElementElectronegativity", DisplayName = "Electronegativity" },
        new FavoriteProperty { Key = "ElementBlock", DisplayName = "Block" },
        new FavoriteProperty { Key = "BoilingPointCelsius", DisplayName = "Boiling Point (°C)" },
        new FavoriteProperty { Key = "BoilingPointKelvin", DisplayName = "Boiling Point (K)" },
        new FavoriteProperty { Key = "BoilingPointFahrenheit", DisplayName = "Boiling Point (°F)" },
        new FavoriteProperty { Key = "MeltingPointCelsius", DisplayName = "Melting Point (°C)" },
        new FavoriteProperty { Key = "MeltingPointKelvin", DisplayName = "Melting Point (K)" },
        new FavoriteProperty { Key = "MeltingPointFahrenheit", DisplayName = "Melting Point (°F)" },
        new FavoriteProperty { Key = "Phase", DisplayName = "Phase (STP)" },
        new FavoriteProperty { Key = "Radioactive", DisplayName = "Radioactive" },
        new FavoriteProperty { Key = "ElementGroup", DisplayName = "Group" },
        new FavoriteProperty { Key = "ElementAppearance", DisplayName = "Appearance" },
        new FavoriteProperty { Key = "ElectronConfiguration", DisplayName = "Electron Configuration" },
        new FavoriteProperty { Key = "IonCharge", DisplayName = "Ion Charge" },
        new FavoriteProperty { Key = "IonizationEnergies", DisplayName = "Ionization Energies" },
        new FavoriteProperty { Key = "AtomicRadiusEmpirical", DisplayName = "Atomic Radius (Empirical)" },
        new FavoriteProperty { Key = "AtomicRadiusCalculated", DisplayName = "Atomic Radius (Calculated)" },
        new FavoriteProperty { Key = "CovalentRadius", DisplayName = "Covalent Radius" },
        new FavoriteProperty { Key = "VanDerWallsRadius", DisplayName = "Van Der Waals Radius" },
        new FavoriteProperty { Key = "ElectricalType", DisplayName = "Electrical Type" },
        new FavoriteProperty { Key = "ElectricalResistivity", DisplayName = "Electrical Resistivity" },
        new FavoriteProperty { Key = "MagneticType", DisplayName = "Magnetic Type" },
        new FavoriteProperty { Key = "SuperconductingPoint", DisplayName = "Superconducting Point" },
        new FavoriteProperty { Key = "FusionHeat", DisplayName = "Fusion Heat" },
        new FavoriteProperty { Key = "SpecificHeatCapacity", DisplayName = "Specific Heat Capacity" },
        new FavoriteProperty { Key = "VaporizationHeat", DisplayName = "Vaporization Heat" },
        new FavoriteProperty { Key = "NeutronCrossSectional", DisplayName = "Neutron Cross Sectional" },
        // PRO-only properties:
        new FavoriteProperty { Key = "MohsHardness", DisplayName = "Mohs Hardness", IsProOnly = true },
        new FavoriteProperty { Key = "VickersHardness", DisplayName = "Vickers Hardness", IsProOnly = true },
        new FavoriteProperty { Key = "BrinellHardness", DisplayName = "Brinell Hardness", IsProOnly = true },
        new FavoriteProperty { Key = "CASNumber", DisplayName = "CAS Number" },
        new FavoriteProperty { Key = "EGNumber", DisplayName = "EG Number" }
    };
        }

        private void UpdateIsLastOnFavoriteProperties()
        {
            for (int i = 0; i < FavoriteProperties.Count; i++)
            {
                FavoriteProperties[i].IsLast = (i == FavoriteProperties.Count - 1);
            }
        }

        private void LoadFavoriteProperties()
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("FavoritePropertyKeys", out var obj) && obj is string json)
            {
                try
                {
                    var keys = JsonSerializer.Deserialize<List<string>>(json);
                    if (keys != null && keys.Count > 0)
                    {
                        FavoriteProperties.Clear();
                        foreach (var key in keys)
                        {
                            var prop = AllProperties.FirstOrDefault(p => p.Key == key);
                            if (prop != null)
                            {
                                prop.Value = GetElementPropertyValue(prop.Key);
                                FavoriteProperties.Add(new FavoriteProperty
                                {
                                    Key = prop.Key,
                                    DisplayName = prop.DisplayName,
                                    Value = prop.Value
                                });
                            }
                        }
                    }
                }
                catch { /* Ignore errors and fall back to default */ }
            }
        }

        private void LoadDefaultFavoritesIfNeeded()
        {
            if (FavoriteProperties.Count == 0)
            {
                // List of keys for the default favorite properties
                var defaultKeys = new[] { "ElementAtomicWeight", "ElementElectronegativity", "Phase", "ElementDensity" };

                foreach (var key in defaultKeys)
                {
                    var prop = AllProperties.FirstOrDefault(p => p.Key == key);
                    if (prop != null)
                    {
                        prop.IsSelected = true;
                        prop.Value = GetElementPropertyValue(prop.Key);
                        FavoriteProperties.Add(new FavoriteProperty
                        {
                            Key = prop.Key,
                            DisplayName = prop.DisplayName,
                            Value = prop.Value
                        });
                    }
                }
            }
        }

        private void UpdateFavoritePropertyValues()
        {
            foreach (var prop in AllProperties)
            {
                prop.Value = GetElementPropertyValue(prop.Key);
            }

            foreach (var fav in FavoriteProperties)
            {
                var match = AllProperties.FirstOrDefault(p => p.Key == fav.Key);
                if (match != null)
                    fav.Value = match.Value;
            }
        }

        private string GetElementPropertyValue(string key)
        {
            var source = loadedElementData ?? Element;
            if (source == null) return "";

            return key switch
            {
                "AtomicNumber" => source.Number?.ToString(),
                "ElementAtomicWeight" => source.ElementAtomicWeight,
                "ElementDensity" => source.ElementDensity,
                "ElementElectronegativity" => source.ElementElectronegativity,
                "ElementBlock" => source.ElementBlock,
                "BoilingPointCelsius" => source.BoilingPointCelsius,
                "BoilingPointKelvin" => source.BoilingPointKelvin,
                "BoilingPointFahrenheit" => source.BoilingPointFahrenheit,
                "MeltingPointCelsius" => source.MeltingPointCelsius,
                "MeltingPointKelvin" => source.MeltingPointKelvin,
                "MeltingPointFahrenheit" => source.MeltingPointFahrenheit,
                "Phase" => source.Phase,
                "Radioactive" => source.Radioactive,
                "ElementGroup" => source.ElementGroup,
                "ElementAppearance" => source.ElementAppearance,
                "ElectronConfiguration" => source.ElectronConfiguration,
                "IonCharge" => source.IonCharge,
                "IonizationEnergies" => source.IonizationEnergies,
                "AtomicRadiusEmpirical" => source.AtomicRadiusEmpirical,
                "AtomicRadiusCalculated" => source.AtomicRadiusCalculated,
                "CovalentRadius" => source.CovalentRadius,
                "VanDerWallsRadius" => source.VanDerWallsRadius,
                "ElectricalType" => source.ElectricalType,
                "ElectricalResistivity" => source.ElectricalResistivity,
                "MagneticType" => source.MagneticType,
                "SuperconductingPoint" => source.SuperconductingPoint,
                "FusionHeat" => source.FusionHeat,
                "SpecificHeatCapacity" => source.SpecificHeatCapacity,
                "VaporizationHeat" => source.VaporizationHeat,
                "NeutronCrossSectional" => source.NeutronCrossSectional,
                "MohsHardness" => source.MohsHardness,
                "VickersHardness" => source.VickersHardness,
                "BrinellHardness" => source.BrinellHardness,
                "CASNumber" => source.CASNumber,
                "EGNumber" => source.EGNumber,
                _ => ""
            };
        }

        private async void EditFavorites_Click(object sender, RoutedEventArgs e)
        {
            foreach (var prop in AllProperties)
                prop.IsSelected = FavoriteProperties.Any(f => f.Key == prop.Key);

            bool isPro = IsProUser;

            foreach (var prop in AllProperties)
            {
                prop.IsSelectable = isPro || !prop.IsProOnly;
                prop.ForegroundBrush = prop.IsSelectable
                    ? new SolidColorBrush(Microsoft.UI.Colors.Black)
                    : new SolidColorBrush(Microsoft.UI.Colors.Gray);
            }

            // Always show all properties in the dialog
            FavoritePropertiesList.ItemsSource = AllProperties;

            var result = await FavoritePropertiesDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                FavoriteProperties.Clear();
                foreach (var prop in AllProperties.Where(p => p.IsSelected && p.IsSelectable))
                {
                    prop.Value = GetElementPropertyValue(prop.Key);
                    FavoriteProperties.Add(new FavoriteProperty
                    {
                        Key = prop.Key,
                        DisplayName = prop.DisplayName,
                        Value = prop.Value
                    });
                }
                UpdateIsLastOnFavoriteProperties(); // Add this line
                SaveFavoriteProperties(); // Save after editing
            }
        }


        private async void NotesClick(object sender, RoutedEventArgs e)
        {
            if (Element == null)
                return;

            NotesDialog.Title = $"{Element.OriginalName} Notes";
            string notesKey = $"ElementNotes_{Element.AtomicNumber}";
            var localSettings = ApplicationData.Current.LocalSettings;
            NotesTextBox.Text = localSettings.Values[notesKey] as string ?? "";

            var result = await NotesDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                localSettings.Values[notesKey] = NotesTextBox.Text;
            }
        }

        private void PopulateDescriptionFromJson(Element element)
        {
            try
            {
                string jsonFilePath = Path.Combine(AppContext.BaseDirectory, "Elements", $"{element.OriginalName.ToLower()}.json");

                if (File.Exists(jsonFilePath))
                {
                    string jsonContent = File.ReadAllText(jsonFilePath);
                    var elementData = JsonSerializer.Deserialize<Element>(jsonContent);

                    if (elementData != null)
                    {
                        loadedElementData = elementData; // Store for FavoriteBar use

                        Title.Text = elementData.Title;
                        AtomicNumber.Text = elementData.Number;

                        Element.WikipediaLink = elementData.WikipediaLink;
                        string imageLink = elementData.ElementImage;
                        if (!string.IsNullOrWhiteSpace(imageLink))
                        {
                            try
                            {
                                ElementImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(imageLink));
                            }
                            catch (UriFormatException)
                            {
                                if (ActualTheme == ElementTheme.Dark)
                                {
                                    ElementImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/no_image_dark.png"));

                                }
                                else
                                {
                                    ElementImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/no_image_light.png"));

                                }
                            }
                        }

                        Description.Text = elementData.Description;
                        YearDiscovered.Text = elementData.YearDiscovered;
                        DiscoveredBy.Text = elementData.DiscoveredBy;
                        ElementGroup.Text = elementData.ElementGroup;
                        ElementAppearance.Text = elementData.ElementAppearance;
                        ElementElectrons.Text = elementData.ElementElectrons;

                        Protons.Text = elementData.ElementProtons;
                        Electrons.Text = elementData.ElementElectrons;
                        Neutrons.Text = elementData.ElementNeutrons;

                        ElementAtomicWeight.Text = elementData.ElementAtomicWeight;
                        ElementDensity.Text = elementData.ElementDensity;
                        ElementElectronegativity.Text = elementData.ElementElectronegativity;
                        ElementBlock.Text = elementData.ElementBlock;

                        BoilingPointCelsius.Text = elementData.BoilingPointCelsius;
                        BoilingPointKelvin.Text = elementData.BoilingPointKelvin;
                        BoilingPointFahrenheit.Text = elementData.BoilingPointFahrenheit;
                        MeltingPointCelsius.Text = elementData.MeltingPointCelsius;
                        MeltingPointKelvin.Text = elementData.MeltingPointKelvin;
                        MeltingPointFahrenheit.Text = elementData.MeltingPointFahrenheit;

                        setUpOxidationStates(elementData);

                        ElectronConfiguration.Text = elementData.ElectronConfiguration;
                        IonCharge.Text = elementData.IonCharge;
                        IonizationEnergies.Text = elementData.IonizationEnergies;
                        AtomicRadiusEmpirical.Text = elementData.AtomicRadiusEmpirical;
                        AtomicRadiusCalculated.Text = elementData.AtomicRadiusCalculated;
                        CovalentRadius.Text = elementData.CovalentRadius;
                        VanDerWallsRadius.Text = elementData.VanDerWallsRadius;

                        ElectricalType.Text = elementData.ElectricalType;
                        ElectricalResistivity.Text = elementData.ElectricalResistivity;
                        MagneticType.Text = elementData.MagneticType;
                        SuperconductingPoint.Text = elementData.SuperconductingPoint;

                        Phase.Text = elementData.Phase;
                        VaporizationHeat.Text = elementData.VaporizationHeat;
                        SpecificHeatCapacity.Text = elementData.SpecificHeatCapacity;
                        FusionHeat.Text = elementData.FusionHeat;

                        Radioactive.Text = elementData.Radioactive;
                        NeutronCrossSectional.Text = elementData.NeutronCrossSectional;

                        if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
                        {
                            MohsHardness.Text = elementData.MohsHardness;
                            VickersHardness.Text = elementData.VickersHardness;
                            BrinellHardness.Text = elementData.BrinellHardness;
                        }
                        else
                        {
                            MohsHardness.Text = "Requires PRO-Version";
                            VickersHardness.Text = "Requires PRO-Version";
                            BrinellHardness.Text = "Requires PRO-Version";
                        }

                        if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
                        {
                            if (elementData.SoundOfSpeedSolid is not "---")
                            {
                                SoundOfSpeed.Text = elementData.SoundOfSpeedSolid;
                            }
                            if (elementData.SoundOfSpeedLiquid is not "---")
                            {
                                SoundOfSpeed.Text = elementData.SoundOfSpeedLiquid;
                            }
                            if (elementData.SoundOfSpeedGas is not "---")
                            {
                                SoundOfSpeed.Text = elementData.SoundOfSpeedGas;
                            }
                            PoissonConstant.Text = elementData.PoissonRatio;
                            BulkModulus.Text = elementData.BulkModulus;
                            YoungModulus.Text = elementData.BulkModulus;
                            ShearModulus.Text = elementData.ShearModulus;
                        }
                        else
                        {
                            SoundOfSpeed.Text = "Requires PRO-Version";
                            PoissonConstant.Text = "Requires PRO-Version";
                            BulkModulus.Text = "Requires PRO-Version";
                            YoungModulus.Text = "Requires PRO-Version";
                            ShearModulus.Text = "Requires PRO-Version";
                        }

                        EarthCrustAbundance.Text = elementData.EarthCrust + " mg/kg (ppm)";
                        EarthSoilAbundance.Text = elementData.EarthSoils + " mg/kg (ppm)";
                        UrbanSoilAbundance.Text = elementData.UrbanSoils + " mg/kg (ppm)";
                        SeaWaterAbundance.Text = elementData.SeaWater + " mg/kg (ppm)";
                        CrustalRocksAbundance.Text = elementData.CrustalRocks + " μg/l";
                        SunAtomsAbundance.Text = elementData.Sun + " (atoms per 10^6 atoms of silicon)";
                        SolarSystemAtomsAbundance.Text = elementData.SolarSystem + " (atoms per 10^6 atoms of silicon)";

                        if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
                        {
                            HealthHazard.Text = string.IsNullOrWhiteSpace(elementData.Health?.ToString()) ? "-" : elementData.Health.ToString();
                            hHealth.Content = string.IsNullOrWhiteSpace(elementData.Health?.ToString()) ? "-" : elementData.Health.ToString();

                            FireHazard.Text = string.IsNullOrWhiteSpace(elementData.Flammability?.ToString()) ? "-" : elementData.Flammability.ToString();
                            hPhase.Content = string.IsNullOrWhiteSpace(elementData.Flammability?.ToString()) ? "-" : elementData.Flammability.ToString();

                            Reactivity.Text = string.IsNullOrWhiteSpace(elementData.Instability?.ToString()) ? "-" : elementData.Instability.ToString();
                            hReactivity.Content = string.IsNullOrWhiteSpace(elementData.Instability?.ToString()) ? "-" : elementData.Instability.ToString();

                            SpecificHazard.Text = string.IsNullOrWhiteSpace(elementData.Special?.ToString()) ? "-" : elementData.Special.ToString();
                            hSpecific.Content = string.IsNullOrWhiteSpace(elementData.Special?.ToString()) ? "-" : elementData.Special.ToString();
                        }
                        else
                        {
                            HealthHazard.Text = "Requires PRO-Version";
                            FireHazard.Text = "Requires PRO-Version";
                            Reactivity.Text = "Requires PRO-Version";
                            SpecificHazard.Text = "Requires PRO-Version";
                        }

                        CASNumber.Text = elementData.CASNumber;
                        EGNumber.Text = elementData.EGNumber;

                        //Grid Parameters:
                        CrystalStructure.Text = elementData.CrystalStructure;
                        Show3DView(elementData.CrystalStructure); //Display the 3D View
                        GridParameters.Text = LatticeParameterFormatter.GetLatticeSystemParameters(
                            elementData.CrystalStructure,
                            elementData.LatticeConstants
                        );
                        DebyeTemperatureLowTemp.Text = TemperatureFormatter.FormatTemperatureAllUnits(elementData.DebyeTemperature?.LowTemperatureLimit);

                        if (!string.IsNullOrWhiteSpace(elementData.DebyeTemperature?.RoomTemperature))
                        {
                            DebyeTemperatureRoomTemp.Text = TemperatureFormatter.FormatTemperatureAllUnits(elementData.DebyeTemperature.RoomTemperature);
                            DebyeTemperatureRoomTemp.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            DebyeTemperatureRoomTemp.Text = string.Empty;
                            DebyeTemperatureRoomTemp.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"JSON file not found: {jsonFilePath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading JSON file: {ex.Message}");
            }

            // Always update favorite bar after loading JSON
            UpdateFavoritePropertyValues();
        }


        private void Show3DView(string crystalStructure)
        {
            var cubeImage = this.FindName("Cube3DImage") as Image;
            var cubeText = this.FindName("CubeOverlayText") as TextBlock;

            // Hide the 3D view if crystalStructure is null or empty
            if (string.IsNullOrEmpty(crystalStructure))
            {
                _currentCrystalSystem = "Cubic";
                if (cubeImage != null) cubeImage.Visibility = Visibility.Collapsed;
                if (cubeText != null) cubeText.Visibility = Visibility.Collapsed;
                return;
            }

            if (crystalStructure.Contains("Hexagonal"))
            {
                _currentCrystalSystem = "Hexagonal";
            }
            else
            {
                _currentCrystalSystem = crystalStructure;
            }

            // Hide the 3D view if _currentCrystalSystem is null or empty
            if (string.IsNullOrEmpty(_currentCrystalSystem))
            {
                if (cubeImage != null) cubeImage.Visibility = Visibility.Collapsed;
                if (cubeText != null) cubeText.Visibility = Visibility.Collapsed;
                return;
            }

            // Show the 3D view if valid
            if (cubeImage != null) cubeImage.Visibility = Visibility.Visible;
            if (cubeText != null) cubeText.Visibility = Visibility.Visible;
            DrawCrystalStructure(cubeImage, cubeText);
        }


        private void Setup3DView()
        {
            var cubeImage = this.FindName("Cube3DImage") as Image;
            var cubeText = this.FindName("CubeOverlayText") as TextBlock;
            if (cubeImage == null || cubeText == null)
                return;

            // Set initial rotation: 20 degrees
            _cubeYaw = 20 * Math.PI / 180;
            _cubePitch = 20 * Math.PI / 180;
            _cubeRoll = 20 * Math.PI / 180;

            // Determine which structure to show
            string structure = loadedElementData?.CrystalStructure ?? Element?.CrystalStructure ?? "Cubic";
            if (!CrystalStructures.Data.ContainsKey(structure))
                structure = "Cubic";
            _currentCrystalSystem = structure;

            int size = 400;
            _cubeBitmap = new WriteableBitmap(size, size);
            cubeImage.Source = _cubeBitmap;

            cubeImage.PointerPressed += (s, e) =>
            {
                _isDragging = true;
                _lastPointer = e.GetCurrentPoint(cubeImage).Position;
                cubeImage.CapturePointer(e.Pointer);
            };
            cubeImage.PointerReleased += (s, e) =>
            {
                _isDragging = false;
                cubeImage.ReleasePointerCapture(e.Pointer);
            };
            cubeImage.PointerMoved += (s, e) =>
            {
                if (_isDragging)
                {
                    var pt = e.GetCurrentPoint(cubeImage).Position;
                    _cubeYaw += (pt.X - _lastPointer.X) * 0.01;
                    _cubePitch += (pt.Y - _lastPointer.Y) * 0.01;
                    _lastPointer = pt;
                    DrawCrystalStructure(cubeImage, cubeText);
                }
            };

            _cubeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _cubeTimer.Tick += (s, e) =>
            {
                _cubeYaw += 0.01;
                DrawCrystalStructure(cubeImage, cubeText);
            };
            _cubeTimer.Start();
            DrawCrystalStructure(cubeImage, cubeText);
        }


        // New method to draw the selected structure
        private void DrawCrystalStructure(Image cubeImage, TextBlock cubeText)
        {
            var (vertices, edges, displayName) = Atomic_WinUI.Helpers.CrystalStructures.Data[_currentCrystalSystem]; int vertexCount = vertices.GetLength(0);
            int edgeCount = edges.GetLength(0);

            int size = _cubeBitmap.PixelWidth;
            int center = size / 2;
            int scale = (int)(size * 0.3);

            Point[] pts = new Point[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var v = CrystalStructureRenderer.Rotate(vertices[i, 0], vertices[i, 1], vertices[i, 2], _cubeYaw, _cubePitch, _cubeRoll);
                pts[i] = new Point(center + v.x * scale, center - v.y * scale);
            }

            using (var buf = _cubeBitmap.PixelBuffer.AsStream())
            {
                byte[] clear = new byte[size * size * 4];
                buf.Write(clear, 0, clear.Length);
            }

            if (ActualTheme == ElementTheme.Dark)
            {
                for (int i = 0; i < edgeCount; i++)
                {
                    CrystalStructureRenderer.DrawLine(_cubeBitmap, pts[edges[i, 0]], pts[edges[i, 1]], Colors.White);
                }
            }
            else
            {
                for (int i = 0; i < edgeCount; i++)
                {
                    CrystalStructureRenderer.DrawLine(_cubeBitmap, pts[edges[i, 0]], pts[edges[i, 1]], Colors.Black);
                }
            }


            // Show the crystal system name and lattice parameters
            cubeText.Margin = new Thickness(10, 10, 0, 0);

            string crystalSystem = _currentCrystalSystem;
            LatticeConstants lattice = loadedElementData?.LatticeConstants ?? Element?.LatticeConstants;
            string latticeParams = LatticeParameterFormatter.GetLatticeSystemParameters(crystalSystem, lattice);

            cubeText.Text = $"";

            _cubeBitmap.Invalidate();
        }


        private void setUpOxidationStates(Element elementData)
        {
            var oxUp = elementData.OxidationStageNeg;
            var oxDown = elementData.OxidationStagePos;
            var lightGreenBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGreen);
            var lightBlueBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightBlue);
            var lightRedBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightCoral);

            if (oxUp.Contains("0"))
            {
                up1.Text = "0";
                up1b.Background = lightGreenBrush;
            }

            if (oxUp.Contains("1"))
            {
                up2.Text = "-1";
                up2b.Background = lightBlueBrush;
            }

            if (oxUp.Contains("2"))
            {
                up3.Text = "-2";
                up3b.Background = lightBlueBrush;
            }

            if (oxUp.Contains("3"))
            {
                up3.Text = "-3";
                up3b.Background = lightBlueBrush;
            }

            if (oxUp.Contains("4"))
            {
                up4.Text = "-4";
                up4b.Background = lightBlueBrush;
            }

            if (oxUp.Contains("5"))
            {
                up5.Text = "-5";
                up5b.Background = lightBlueBrush;
            }

            if (oxDown.Contains("0"))
            {
                d1.Text = "0";
                d1b.Background = lightRedBrush;
            }
            if (oxDown.Contains("1"))
            {
                d2.Text = "+1";
                d2b.Background = lightRedBrush;
            }
            if (oxDown.Contains("2"))
            {
                d3.Text = "+2";
                d3b.Background = lightRedBrush;
            }
            if (oxDown.Contains("3"))
            {
                d4.Text = "+3";
                d4b.Background = lightRedBrush;
            }
            if (oxDown.Contains("4"))
            {
                d5.Text = "+4";
                d5b.Background = lightRedBrush;
            }
            if (oxDown.Contains("5"))
            {
                d6.Text = "+5";
                d6b.Background = lightRedBrush;
            }
            if (oxDown.Contains("6"))
            {
                d7.Text = "+6";
                d7b.Background = lightRedBrush;
            }
            if (oxDown.Contains("7"))
            {
                d8.Text = "+7";
                d8b.Background = lightRedBrush;
            }
            if (oxDown.Contains("8"))
            {
                d9.Text = "+8";
                d9b.Background = lightRedBrush;
            }
        }

        private void SaveFavoriteProperties()
        {
            var selectedKeys = FavoriteProperties.Select(f => f.Key).ToList();
            var json = JsonSerializer.Serialize(selectedKeys);
            ApplicationData.Current.LocalSettings.Values["FavoritePropertyKeys"] = json;
        }

        private void IsotopeClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(IsotopeDetailsPage), Element);
        }
        private void IonizationClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(IonizationDetailsPage), Element);
        }

        private void WikipediaClick(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Element?.WikipediaLink))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = Element.WikipediaLink,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open Wikipedia link: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Wikipedia link is not available.");
            }
        }

        private void FavoriteClick(object sender, RoutedEventArgs e)
        {
            // Add your button click logic here if needed
        }
    }

}
