using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Text.Json;
using System;
using System.IO;
using System.Linq;
using Atomic_PeriodicTable;
using Windows.Storage;
using Atomic_PeriodicTable.Tables;

namespace Atomic_WinUI
{
    public sealed partial class ElementDetailsPage : Page
    {
        public Element Element { get; set; }

        public ElementDetailsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Element element)
            {
                Element = element;
                DataContext = Element; // Bind the Element object to the UI

                PopulateDescriptionFromJson(Element);

                // Construct the emission spectrum URL
                if (!string.IsNullOrEmpty(Element.Symbol))
                {
                    string emissionSpectrumUrl = $"https://www.jlindemann.se/atomic/emission_lines/{Element.Symbol}.gif";

                    // Set the emission spectrum image source
                    EmissionSpectrumImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(emissionSpectrumUrl));
                }

                // Update the navigation header
                if (Window.Current?.Content is Frame frame &&
                    frame.Content is MainWindow mainWindow)
                {
                    mainWindow.UpdateHeader(Element.OriginalName); // Use the public method to update the header
                }

            }
        }

      
        private void PopulateDescriptionFromJson(Element element)
        {
            try
            {
                // Construct the JSON file path dynamically based on the element's name
                string jsonFilePath = Path.Combine(AppContext.BaseDirectory, "Elements", $"{element.OriginalName.ToLower()}.json");


                if (File.Exists(jsonFilePath))
                {
                    // Read and deserialize the JSON file
                    string jsonContent = File.ReadAllText(jsonFilePath);
                    var elementData = JsonSerializer.Deserialize<Element>(jsonContent);

                    if (elementData != null)
                    {
                        Element.WikipediaLink = elementData.WikipediaLink;

                        string imageLink = elementData.ElementImage;

                        // Set the emission spectrum image source
                        ElementImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(imageLink));

                        //Overview
                        Description.Text = elementData.Description;
                        YearDiscovered.Text = elementData.YearDiscovered;
                        DiscoveredBy.Text = elementData.DiscoveredBy;
                        ElementGroup.Text = elementData.ElementGroup;
                        ElementAppearance.Text = elementData.ElementAppearance;
                        ElementElectrons.Text = elementData.ElementElectrons;

                        //Most common isotope card
                        Protons.Text = elementData.ElementProtons;
                        Electrons.Text = elementData.ElementElectrons;
                        Neutrons.Text = elementData.ElementNeutrons;

                        //Properties
                        ElementAtomicWeight.Text = elementData.ElementAtomicWeight;
                        ElementDensity.Text = elementData.ElementDensity;
                        ElementElectronegativity.Text = elementData.ElementElectronegativity;
                        ElementBlock.Text = elementData.ElementBlock;

                        //Temperature
                        BoilingPointCelsius.Text = elementData.BoilingPointCelsius;
                        BoilingPointKelvin.Text = elementData.BoilingPointKelvin;
                        BoilingPointFahrenheit.Text = elementData.BoilingPointFahrenheit;
                        MeltingPointCelsius.Text = elementData.MeltingPointCelsius;
                        MeltingPointKelvin.Text = elementData.MeltingPointKelvin;
                        MeltingPointFahrenheit.Text = elementData.MeltingPointFahrenheit;

                        //Atomic Properties
                        setUpOxidationStates(elementData);

                        ElectronConfiguration.Text = elementData.ElectronConfiguration;
                        IonCharge.Text = elementData.IonCharge;
                        IonizationEnergies.Text = elementData.IonizationEnergies;
                        AtomicRadiusEmpirical.Text = elementData.AtomicRadiusEmpirical;
                        AtomicRadiusCalculated.Text = elementData.AtomicRadiusCalculated;
                        CovalentRadius.Text = elementData.CovalentRadius;
                        VanDerWallsRadius.Text = elementData.VanDerWallsRadius;

                        //Electromagnetic Properties
                        ElectricalType.Text = elementData.ElectricalType;
                        ElectricalResistivity.Text = elementData.ElectricalResistivity;
                        MagneticType.Text = elementData.MagneticType;
                        SuperconductingPoint.Text = elementData.SuperconductingPoint;

                        //Thermodynamic Properties
                        Phase.Text = elementData.Phase;
                        VaporizationHeat.Text = elementData.VaporizationHeat;
                        SpecificHeatCapacity.Text = elementData.SpecificHeatCapacity;
                        FusionHeat.Text = elementData.FusionHeat;

                        //Radioactive Properties
                        Radioactive.Text = elementData.Radioactive;
                        NeutronCrossSectional.Text = elementData.NeutronCrossSectional;

                        //Hardness Properties
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


                        //Additional Properties
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


                            //Abundance
                            EarthCrustAbundance.Text = elementData.EarthCrust + " mg/kg (ppm)";
                        EarthSoilAbundance.Text = elementData.EarthSoils + " mg/kg (ppm)";
                        UrbanSoilAbundance.Text = elementData.UrbanSoils + " mg/kg (ppm)";
                        SeaWaterAbundance.Text = elementData.SeaWater + " mg/kg (ppm)";
                        CrustalRocksAbundance.Text = elementData.CrustalRocks + " μg/l";
                        SunAtomsAbundance.Text = elementData.Sun + " (atoms per 10^6 atoms of silicon)";
                        SolarSystemAtomsAbundance.Text = elementData.SolarSystem + " (atoms per 10^6 atoms of silicon)";

                        //Hazard Properties
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
 


                        //Other Properties
                        CASNumber.Text = elementData.CASNumber;
                        EGNumber.Text = elementData.EGNumber;



                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"JSON file not found: {jsonFilePath}");
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., log the error)
                System.Diagnostics.Debug.WriteLine($"Error reading JSON file: {ex.Message}");
            }
        }

        private void IsotopeClick(object sender, global::Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            Frame.Navigate(typeof(IsotopeDetailsPage), Element);
        }
        private void IonizationClick(object sender, global::Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            Frame.Navigate(typeof(IonizationDetailsPage), Element);
        }

        private void WikipediaClick(object sender, global::Microsoft.UI.Xaml.RoutedEventArgs e)
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


            //Down
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
        private void FavoriteClick(object sender, global::Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // Add your button click logic here
        }
    }
}