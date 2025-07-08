using Atomic_PeriodicTable;
using Atomic_PeriodicTable.Tables;
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
        private double _cubeYaw = 0, _cubePitch = 0;
        private Point _lastPointer;
        private bool _isDragging = false;
        private DispatcherTimer _cubeTimer;

        private readonly Dictionary<string, (double[,], int[,], string)> _crystalStructures = new()
        {

            // Vertices, Edges, Display Name
            // Cubic: a = b = c; α = β = γ = 90°
            ["Cubic"] = (
        new double[8, 3] {
            { -1, -1, -1 }, { 1, -1, -1 }, { 1, 1, -1 }, { -1, 1, -1 },
            { -1, -1,  1 }, { 1, -1,  1 }, { 1, 1,  1 }, { -1, 1,  1 }
        },
        new int[12, 2] {
            {0,1},{1,2},{2,3},{3,0},
            {4,5},{5,6},{6,7},{7,4},
            {0,4},{1,5},{2,6},{3,7}
        },
        "Cubic"
    ),
            // Tetragonal: a = b ≠ c; α = β = γ = 90°
            ["Tetragonal"] = (
        new double[8, 3] {
            { -1, -1, -1.5 }, { 1, -1, -1.5 }, { 1, 1, -1.5 }, { -1, 1, -1.5 },
            { -1, -1,  1.5 }, { 1, -1,  1.5 }, { 1, 1,  1.5 }, { -1, 1,  1.5 }
        },
        new int[12, 2] {
            {0,1},{1,2},{2,3},{3,0},
            {4,5},{5,6},{6,7},{7,4},
            {0,4},{1,5},{2,6},{3,7}
        },
        "Tetragonal"
    ),
            // Orthorhombic: a ≠ b ≠ c; α = β = γ = 90°
            ["Orthorhombic"] = (
        new double[8, 3] {
            { -1.5, -1, -0.5 }, { 1.5, -1, -0.5 }, { 1.5, 1, -0.5 }, { -1.5, 1, -0.5 },
            { -1.5, -1,  0.5 }, { 1.5, -1,  0.5 }, { 1.5, 1,  0.5 }, { -1.5, 1,  0.5 }
        },
        new int[12, 2] {
            {0,1},{1,2},{2,3},{3,0},
            {4,5},{5,6},{6,7},{7,4},
            {0,4},{1,5},{2,6},{3,7}
        },
        "Orthorhombic"
    ),
            // Hexagonal: a = b ≠ c; α = β = 90°, γ = 120°
            ["Hexagonal"] = (
        new double[12, 3] {
            { 1, 0, -1 }, { 0.5, 0.866, -1 }, { -0.5, 0.866, -1 }, { -1, 0, -1 }, { -0.5, -0.866, -1 }, { 0.5, -0.866, -1 },
            { 1, 0, 1 }, { 0.5, 0.866, 1 }, { -0.5, 0.866, 1 }, { -1, 0, 1 }, { -0.5, -0.866, 1 }, { 0.5, -0.866, 1 }
        },
        new int[18, 2] {
            {0,1},{1,2},{2,3},{3,4},{4,5},{5,0},
            {6,7},{7,8},{8,9},{9,10},{10,11},{11,6},
            {0,6},{1,7},{2,8},{3,9},{4,10},{5,11}
        },
        "Hexagonal"
    ),
            // Trigonal (Rhombohedral): a = b = c; α = β = γ ≠ 90°
            ["Trigonal"] = (
        new double[8, 3] {
            { -1, -1, -1 }, { 1, -1, -1 }, { 1, 1, -1 }, { -1, 1, -1 },
            { -0.5, -0.5, 1 }, { 1.5, -0.5, 1 }, { 1.5, 1.5, 1 }, { -0.5, 1.5, 1 }
        },
        new int[12, 2] {
            {0,1},{1,2},{2,3},{3,0},
            {4,5},{5,6},{6,7},{7,4},
            {0,4},{1,5},{2,6},{3,7}
        },
        "Trigonal"
    ),
            // Monoclinic: a ≠ b ≠ c; α = γ = 90°, β ≠ 90°
            ["Monoclinic"] = (
        new double[8, 3] {
            { -1, -1, -1 }, { 1, -1, -1 }, { 1.2, 1, -1 }, { -0.8, 1, -1 },
            { -1, -1, 1 }, { 1, -1, 1 }, { 1.2, 1, 1 }, { -0.8, 1, 1 }
        },
        new int[12, 2] {
            {0,1},{1,2},{2,3},{3,0},
            {4,5},{5,6},{6,7},{7,4},
            {0,4},{1,5},{2,6},{3,7}
        },
        "Monoclinic"
    ),
            // Triclinic: a ≠ b ≠ c; α ≠ β ≠ γ ≠ 90°
            ["Triclinic"] = (
        new double[8, 3] {
            { -1, -1, -1 }, { 1.2, -0.8, -1 }, { 1, 1, -0.7 }, { -1.1, 1.1, -0.5 },
            { -0.8, -1.2, 1 }, { 1, -1, 1.2 }, { 1.3, 1, 1 }, { -1, 1.2, 1.1 }
        },
        new int[12, 2] {
            {0,1},{1,2},{2,3},{3,0},
            {4,5},{5,6},{6,7},{7,4},
            {0,4},{1,5},{2,6},{3,7}
        },
        "Triclinic"
    ),

        };

        private string _currentCrystalSystem = "Cubic"; // Default


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

                // Update the navigation header
                if (Window.Current?.Content is Frame frame &&
                    frame.Content is MainWindow mainWindow)
                {
                    mainWindow.UpdateHeader(Element.OriginalName);
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
                        ElementImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(imageLink));

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
                        GridParameters.Text = GetLatticeSystemParameters(
                            elementData.CrystalStructure,
                            elementData.LatticeConstants
                        );
                        DebyeTemperatureLowTemp.Text = FormatTemperatureAllUnits(elementData.DebyeTemperature?.LowTemperatureLimit);
                        DebyeTemperatureRoomTemp.Text = FormatTemperatureAllUnits(elementData.DebyeTemperature?.RoomTemperature);

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

        private static string GetLatticeSystemParameters(string crystalSystem, LatticeConstants constants)
        {
            // Fallbacks if values are missing
            string a = constants?.A ?? "-";
            string b = constants?.B ?? "-";
            string c = constants?.C ?? "-";
            string alpha = constants?.A ?? "-";
            string beta = constants?.B ?? "-";
            string gamma = constants?.C ?? "-";

            // Wikipedia conventions for each system
            return crystalSystem switch
            {
                "Cubic" => $"a = b = c = {a}; α = β = γ = 90°",
                "Tetragonal" => $"a = b = {a}; c = {c}; α = β = γ = 90°",
                "Orthorhombic" => $"a = {a}; b = {b}; c = {c}; α = β = γ = 90°",
                "Hexagonal" => $"a = b = {a}; c = {c}; α = β = 90°; γ = 120°",
                "Trigonal" or "Rhombohedral" => $"a = b = c = {a}; α = β = γ = {alpha}° (≠ 90°)",
                "Monoclinic" => $"a = {a}; b = {b}; c = {c}; α = γ = 90°; β = {beta}° (≠ 90°)",
                "Triclinic" => $"a = {a}; b = {b}; c = {c}; α = {alpha}°; β = {beta}°; γ = {gamma}° (all ≠ 90°)",
                _ => $"a = {a}; b = {b}; c = {c}; α = {alpha}°; β = {beta}°; γ = {gamma}°"
            };
        }


        private static string FormatTemperatureAllUnits(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "-";

            // Try to extract the numeric value and the unit
            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return input;

            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                return input;

            string unit = parts[1].ToUpperInvariant();

            double kelvin, celsius, fahrenheit;

            switch (unit)
            {
                case "K":
                    kelvin = value;
                    celsius = kelvin - 273.15;
                    fahrenheit = celsius * 9 / 5 + 32;
                    break;
                case "°C":
                case "C":
                    celsius = value;
                    kelvin = celsius + 273.15;
                    fahrenheit = celsius * 9 / 5 + 32;
                    break;
                case "°F":
                case "F":
                    fahrenheit = value;
                    celsius = (fahrenheit - 32) * 5 / 9;
                    kelvin = celsius + 273.15;
                    break;
                default:
                    return input;
            }

            // Format with two decimals for C and F, no decimals for K
            return $"{kelvin:0} K = {celsius:0.##} °C = {fahrenheit:0.##} °F";
        }

        private void Show3DView(string crystalStructure)
        {
            if (string.IsNullOrWhiteSpace(crystalStructure) || !_crystalStructures.ContainsKey(crystalStructure))
                _currentCrystalSystem = "Cubic";
            else
                _currentCrystalSystem = crystalStructure;

            var cubeImage = this.FindName("Cube3DImage") as Image;
            var cubeText = this.FindName("CubeOverlayText") as TextBlock;
            if (cubeImage != null && cubeText != null)
            {
                DrawCrystalStructure(cubeImage, cubeText);
            }
        }

        private void Setup3DView()
        {
            var cubeImage = this.FindName("Cube3DImage") as Image;
            var cubeText = this.FindName("CubeOverlayText") as TextBlock;
            if (cubeImage == null || cubeText == null)
                return;

            // Determine which structure to show
            string structure = loadedElementData?.CrystalStructure ?? Element?.CrystalStructure ?? "Cubic";
            if (!_crystalStructures.ContainsKey(structure))
                structure = "Cubic"; // fallback

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
            var (vertices, edges, displayName) = _crystalStructures[_currentCrystalSystem];
            int vertexCount = vertices.GetLength(0);
            int edgeCount = edges.GetLength(0);

            int size = _cubeBitmap.PixelWidth;
            int center = size / 2;
            int scale = (int)(size * 0.3);

            Point[] pts = new Point[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var v = Rotate(vertices[i, 0], vertices[i, 1], vertices[i, 2], _cubeYaw, _cubePitch);
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
                    DrawLine(_cubeBitmap, pts[edges[i, 0]], pts[edges[i, 1]], Colors.White);
                }
            }
            else
            {
                for (int i = 0; i < edgeCount; i++)
                {
                    DrawLine(_cubeBitmap, pts[edges[i, 0]], pts[edges[i, 1]], Colors.Black);
                }
            }


            // Show the crystal system name and lattice parameters
            cubeText.Margin = new Thickness(10, 10, 0, 0);

            string crystalSystem = _currentCrystalSystem;
            LatticeConstants lattice = loadedElementData?.LatticeConstants ?? Element?.LatticeConstants;
            string latticeParams = GetLatticeSystemParameters(crystalSystem, lattice);

            cubeText.Text = $"";

            _cubeBitmap.Invalidate();
        }

        private (double x, double y, double z) Rotate(double x, double y, double z, double yaw, double pitch)
        {
            // Yaw (Y axis)
            double cosa = Math.Cos(yaw), sina = Math.Sin(yaw);
            double x1 = x * cosa - z * sina;
            double z1 = x * sina + z * cosa;
            // Pitch (X axis)
            double cosb = Math.Cos(pitch), sinb = Math.Sin(pitch);
            double y1 = y * cosb - z1 * sinb;
            double z2 = y * sinb + z1 * cosb;
            return (x1, y1, z2);
        }

        private void DrawLine(WriteableBitmap bmp, Point p1, Point p2, Windows.UI.Color color)
        {
            int x0 = (int)p1.X, y0 = (int)p1.Y, x1 = (int)p2.X, y1 = (int)p2.Y;
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            using (var buf = bmp.PixelBuffer.AsStream())
            {
                while (true)
                {
                    if (x0 >= 0 && x0 < w && y0 >= 0 && y0 < h)
                    {
                        long idx = (y0 * w + x0) * 4;
                        buf.Position = idx;
                        buf.WriteByte(color.B);
                        buf.WriteByte(color.G);
                        buf.WriteByte(color.R);
                        buf.WriteByte(255);
                    }
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 > -dy) { err -= dy; x0 += sx; }
                    if (e2 < dx) { err += dx; y0 += sy; }
                }
            }
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



    public class FavoriteProperty
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string Value { get; set; }
        public bool IsSelected { get; set; }
        public bool IsProOnly { get; set; }

        // These are set by the page before binding to the dialog
        public bool IsSelectable { get; set; } = true;
        public string DisplayNameWithProSuffix =>
            !IsSelectable && IsProOnly ? $"{DisplayName} (Requires PRO)" : DisplayName;

        public Brush ForegroundBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Black);

        // Add this property:
        public bool IsLast { get; set; }
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isLast = value is bool b && b;
            return isLast ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }


}
