using Atomic.App;
using Atomic.App.Tables;
using Atomic.App.Helpers;
using Atomic.Core.Ai.Data;
using Atomic.Core.Localization;
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
using Atomic.App.Models;
using Atomic.App.Converters;
using Atomic.App.Services;


namespace Atomic.App
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
    SettingsStore.HasAnyPro;

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

        /// <summary>Below this the card is too narrow for the list and the diagram side by side.</summary>
        private const double SideBySideWidth = 760;

        private void AtomicPropertiesBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var wide = e.NewSize.Width >= SideBySideWidth;

            if (wide)
            {
                Grid.SetColumn(ShellChart, 1);
                Grid.SetRow(ShellChart, 0);
                Grid.SetColumnSpan(ShellChart, 1);
                ShellChart.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumnSpan(AtomicList, 1);
                Grid.SetRow(AtomicList, 0);
                Grid.SetRowSpan(AtomicList, 2);
            }
            else
            {
                // Stacked: the diagram first, then the list under it, each the card's full width.
                Grid.SetColumn(ShellChart, 0);
                Grid.SetRow(ShellChart, 0);
                Grid.SetColumnSpan(ShellChart, 2);
                ShellChart.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetColumnSpan(AtomicList, 2);
                Grid.SetRow(AtomicList, 1);
                Grid.SetRowSpan(AtomicList, 1);
            }
        }

        /// <summary>
        /// Fill the electron shell diagram this page shares with the chat cards.
        ///
        /// <para>It draws from the same parser, so the shells a user sees here are the shells the
        /// assistant shows. With no data it is collapsed rather than left as an empty box.</para>
        /// </summary>
        private void PopulateCharts(Element element)
        {
            var store = Atomic.Core.Ai.Retrieval.RetrievalService
                .Get(Services.ElementRepository.Source)?.Store;
            var record = store?.Element(Services.ElementRepository.Normalise(element.OriginalName ?? string.Empty));

            if (record is null)
            {
                ShellInfo.Visibility = Visibility.Collapsed;
                ShellChart.Visibility = Visibility.Collapsed;
                return;
            }

            var shells = (record.Value("electron_shells") as Atomic.Core.Ai.Data.FieldValue.Text)?.Raw;
            var counts = Atomic.Core.Ai.Cards.ElectronShells.Parse(shells);
            if (counts.Count > 0)
            {
                ShellInfo.Visibility = Visibility.Visible;
                ShellChart.Visibility = Visibility.Visible;
                ShellChart.SetShells(shells, record.Symbol);

                // K, L, M ... are the shells' names; the number after each is how many electrons it holds.
                // Every shell is listed, the empty ones as 0, so the eight always read as a set.
                ShellLabels.Children.Clear();
                for (var i = 0; i < "KLMNOPQR".Length; i++)
                {
                    ShellLabels.Children.Add(new TextBlock
                    {
                        Text = "KLMNOPQR"[i] + (i < counts.Count ? counts[i] : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        FontSize = 14,
                        IsTextSelectionEnabled = true,
                    });
                }
            }
            else
            {
                ShellInfo.Visibility = Visibility.Collapsed;
                ShellChart.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Element element)
            {
                Element = element;
                // Do NOT set DataContext = Element; This breaks FavoriteProperties binding!

                PopulateDescriptionFromJson(Element);
                PopulateCharts(Element);

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

        /// <summary>
        /// The properties a user can pin to the summary bar.
        /// </summary>
        /// <remarks>
        /// <para>Labels come from resources rather than English literals. This list is the reason
        /// the element page read half-translated after P2e: the page's own headings went through
        /// <c>{loc:Str}</c>, but these are built in code, so "Atomic Weight" and "Density" stayed
        /// English beside a Swedish "Översikt".</para>
        ///
        /// <para>Every key already existed - these are the same labels the property tables below
        /// use - so this needed no new vocabulary. The trailing colon is trimmed because those keys
        /// are authored for a "Label: value" row and these are chips.</para>
        /// </remarks>
        private void InitializeAllProperties()
        {
            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

            FavoriteProperty Property(string key, string labelKey, bool isProOnly = false) =>
                new()
                {
                    Key = key,
                    DisplayName = strings.Get(labelKey).TrimEnd(':', '：', ' '),
                    IsProOnly = isProOnly,
                };

            AllProperties = new List<FavoriteProperty>
            {
                Property("AtomicNumber", S.atomic_number),
                Property("ElementAtomicWeight", S.atomic_weight),
                Property("ElementDensity", S.density),
                Property("ElementElectronegativity", S.electronegativity),
                Property("ElementBlock", S.block),
                Property("BoilingPointCelsius", S.boiling_point_celsius),
                Property("BoilingPointKelvin", S.boiling_point_kelvin),
                Property("BoilingPointFahrenheit", S.boiling_point_fahrenheit),
                Property("MeltingPointCelsius", S.melting_point_celsius),
                Property("MeltingPointKelvin", S.melting_point_kelvin),
                Property("MeltingPointFahrenheit", S.melting_point_fahrenheit),
                Property("Phase", S.phase_stp),
                Property("Radioactive", S.radioactive),
                Property("ElementGroup", S.ai_group),
                Property("ElementAppearance", S.appearance),
                Property("ElectronConfiguration", S.electron_configuration_colon),
                Property("IonCharge", S.ion_charge_colon),
                Property("IonizationEnergies", S.ion),
                Property("AtomicRadiusEmpirical", S.atomic_radius_empirical_colon),
                Property("AtomicRadiusCalculated", S.atomic_radius_calculated_colon),
                Property("CovalentRadius", S.covalent_radius_colon),
                Property("VanDerWallsRadius", S.van_der_waals_radius_colon),
                Property("ElectricalType", S.electrical_type),
                Property("ElectricalResistivity", S.electrical_resistivity),
                Property("MagneticType", S.magnetic_type),
                Property("SuperconductingPoint", S.superconducting_point),
                Property("FusionHeat", S.fusion_heat),
                Property("SpecificHeatCapacity", S.specific_heat_capacity),
                Property("VaporizationHeat", S.vaporization_heat),
                Property("NeutronCrossSectional", S.neutron_cross_sectional),
                // PRO-only properties:
                Property("MohsHardness", S.mohs_hardness, isProOnly: true),
                Property("VickersHardness", S.vickers_hardness, isProOnly: true),
                Property("BrinellHardness", S.brinell_hardness, isProOnly: true),
                Property("CASNumber", S.cas_number),
                Property("EGNumber", S.eg_number),
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
            var json = SettingsStore.FavoritePropertyKeys;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var keys = JsonSerializer.Deserialize(json, ElementJsonContext.Default.ListString);
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

            NotesDialog.Title = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.element_notes_title, Element.OriginalName);
            NotesTextBox.Text = SettingsStore.GetElementNotes(Element.AtomicNumber);

            var result = await NotesDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                SettingsStore.SetElementNotes(Element.AtomicNumber, NotesTextBox.Text);
            }
        }

        /// <summary>
        /// Show the element's photograph, or the "no image" placeholder.
        /// </summary>
        /// <remarks>
        /// <para>The URL is composed from the English name by <see cref="ElementImages"/>, the same
        /// rule Android uses, so both apps ask for the same file. The element table's own
        /// <c>element_image</c> field is not used: it is empty for all 118 rows, which is why no
        /// picture ever appeared here.</para>
        ///
        /// <para><b>The failure has to be handled on the event, not in a catch.</b> A
        /// <c>BitmapImage</c> downloads after the constructor returns, so a 404 arrives as
        /// <c>ImageFailed</c> long after any try block has exited - the previous code caught
        /// <c>UriFormatException</c>, which a well-formed URL never raises however dead the link.</para>
        /// </remarks>
        private void LoadElementImage(string englishName)
        {
            if (ElementImages.UrlFor(englishName) is not { } url)
            {
                ShowNoElementImage();
                return;
            }

            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(url));

            // Not every element has a photograph on the server, and that is ordinary rather than an
            // error - the placeholder is the answer, not a message.
            bitmap.ImageFailed += (_, _) => ShowNoElementImage();

            ElementImage.Source = bitmap;
        }

        /// <summary>The placeholder, in whichever theme is showing.</summary>
        private void ShowNoElementImage()
        {
            var asset = ActualTheme == ElementTheme.Dark
                ? "ms-appx:///Assets/no_image_dark.png"
                : "ms-appx:///Assets/no_image_light.png";

            ElementImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(asset));
        }

        private void PopulateDescriptionFromJson(Element element)
        {
            try
            {
                var elementData = ElementRepository.Get(element.OriginalName);
                    if (elementData != null)
                    {
                        loadedElementData = elementData; // Store for FavoriteBar use

                        Title.Text = elementData.Title;
                        AtomicNumber.Text = elementData.Number;

                        Element.WikipediaLink = elementData.WikipediaLink;
                        LoadElementImage(element.OriginalName);

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

                        if (SettingsStore.HasAnyPro)
                        {
                            MohsHardness.Text = elementData.MohsHardness;
                            VickersHardness.Text = elementData.VickersHardness;
                            BrinellHardness.Text = elementData.BrinellHardness;
                        }
                        else
                        {
                            MohsHardness.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                            VickersHardness.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                            BrinellHardness.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                        }

                        if (SettingsStore.HasAnyPro)
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
                            SoundOfSpeed.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                            PoissonConstant.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                            BulkModulus.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                            YoungModulus.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                            ShearModulus.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                        }

                        if (SettingsStore.HasAnyPro)
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
                            HealthHazard.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                            FireHazard.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                            Reactivity.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
                            SpecificHazard.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.requires_pro);
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
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Element not found: {element.OriginalName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading element data: {ex.Message}");
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
            var (vertices, edges, displayName) = Atomic.App.Helpers.CrystalStructures.Data[_currentCrystalSystem]; int vertexCount = vertices.GetLength(0);
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
            var json = JsonSerializer.Serialize(selectedKeys, ElementJsonContext.Default.ListString);
            SettingsStore.FavoritePropertyKeys = json;
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
