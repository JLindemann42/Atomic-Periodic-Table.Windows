using Atomic_PeriodicTable;
using Atomic_PeriodicTable.Tables;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using Windows.Storage;

namespace Atomic_WinUI
{
    public sealed partial class FormulasPage : Page
    {
        private ObservableCollection<Equation> EquationList { get; set; }
        public ObservableCollection<Equation> FilteredEquationList { get; set; }
        private void ListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Equation selectedEquation)
            {
                // Navigate to PoissonDetailsPage, passing the selected Ionization
                Frame.Navigate(typeof(FormulasDetailsPage), selectedEquation);
            }
        }

        public FormulasPage()
        {
            this.InitializeComponent();
            //Check PRO or Not:
            if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
            {
                EquationToolBar.Visibility = Visibility.Visible;
                EquationListView.Visibility = Visibility.Visible;
                NoProContent.Visibility = Visibility.Collapsed;
            }
            else
            {
                EquationToolBar.Visibility = Visibility.Collapsed;
                EquationListView.Visibility = Visibility.Collapsed;
                NoProContent.Visibility = Visibility.Visible;
            }
            EquationList = new ObservableCollection<Equation>
            {

                new Equation { Name = "Density", Category = "General", Image = "e_density", Description = "p: Density \nm: Mass \nV: Volume" },
                new Equation { Name = "Power/ Effect", Category = "General", Image = "e_powereffect", Description = "P: Power/ Effect \nE: Energy \nt: Time" },
                new Equation { Name = "Intensity", Category = "General", Image = "e_intensity", Description = "I: Intensity \nP: Power/ Effect \nA: Area" },
                new Equation { Name = "Efficiency", Category = "General", Image = "e_efficiency", Description = "n: Energy conversion efficiency \nE(in): Energy (in) \nE(out): Energy (out) \nP(in): Power (in) \nP(out): Power (out)" },
                new Equation { Name = "Frequency", Category = "General", Image = "e_frequency", Description = "f: Frequency \nT: Period" },
                new Equation { Name = "Angular Velocity", Category = "General", Image = "e_angularvelocity", Description = "w: Angular Velocity \nf: Frequency \nT: Period" },
                new Equation { Name = "Average Velocity", Category = "Mechanics", Image = "e_averagevelocity", Description = "v(average): Average Velocity \ns: End Position \ns(0): Start Position \nt: Time" },
                new Equation { Name = "Linear Motion", Category = "Mechanics", Image = "e_linearmotion", Description = "s: Position Change \nv: Velocity \nt: Time" },
                new Equation { Name = "Average Acceleration", Category = "Mechanics", Image = "e_averageacceleration", Description = "a(average): Average Acceleration \nv: End Velocity \nv(0): Start Velocity \nt: Time" },
                new Equation { Name = "Instantaneous Velocity #1", Category = "Mechanics", Image = "e_instantvelocity_1", Description = "v: End Velocity \nv(0): Start Velocity \na: Acceleration \nt: Time" },
                new Equation { Name = "Instantaneous Velocity #2", Category = "Mechanics", Image = "e_instantvelocity_2", Description = "s: Position Change \nv(0): Start Velocity \na: Acceleration \nt: Time" },
                new Equation { Name = "Instantaneous Velocity #3", Category = "Mechanics", Image = "e_instantvelocity_3", Description = "v: End Velocity \nv(0): Start Velocity \na: Acceleration \ns: Position Change" },
                new Equation { Name = "Instantaneous Velocity #4", Category = "Mechanics", Image = "e_instantvelocity_4", Description = "v(average): Average Velocity \nv(0): Start Velocity \nv: End Acceleration" },
                new Equation { Name = "Newtown’s Second Law (Force Law)", Category = "Mechanics", Image = "e_forcelaw", Description = "F: Force \nm: Mass \na: Acceleration" },
                new Equation { Name = "Friction", Category = "Mechanics", Image = "e_friction", Description = "F(friction): Friction Force \nF(N): Normal Force \nu: Friction Coefficient" },
                new Equation { Name = "Hookes Law", Category = "Mechanics", Image = "e_hookeslaw", Description = "F(spring): Spring Force \nk: Spring Coefficient \ndelta l: Spring Extension" },
                new Equation { Name = "Law of Gravity", Category = "Mechanics", Image = "e_lawofgravity", Description = "F(g): Gravitational Force \nG: Gravitation Coefficient \nm: Mass \nr: Radius" },
                new Equation { Name = "Force Moment", Category = "Mechanics", Image = "e_moment", Description = "M: Force Moment \nF: Force \nt: Time" },
                new Equation { Name = "Impulse", Category = "Mechanics", Image = "e_impulse", Description = "I: Impulse \nF: Force \nt: Time" },
                new Equation { Name = "Law of Impulse", Category = "Mechanics", Image = "e_lawofimpulse", Description = "F: Force \nt: Time \nn: Mass \nv: End Velocity \nv(0) Start Velocity" },
                new Equation { Name = "Momentum", Category = "Mechanics", Image = "e_momentum", Description = "p: Momentum \nm: Mass \nv: Velocity" },
                new Equation { Name = "Conservation of Momentum", Category = "Mechanics", Image = "e_lawofmomentum", Description = "u: Velocity before \nv: Velocity after \nm: Mass" },
                new Equation { Name = "Work", Category = "Mechanics", Image = "e_work", Description = "W: Work \nF: Force \ns: Displacement" },
                new Equation { Name = "Potential Energy", Category = "Mechanics", Image = "e_potentialenergy", Description = "e(p): Potential Energy \nm: Mass \ng: Gravitational Acceleration \nh: Height" },
                new Equation { Name = "Kinetic Energy", Category = "Mechanics", Image = "e_kineticenergy", Description = "e(k): Kinetic Energy \nm: Mass \nv: Velocity" },
                new Equation { Name = "Elastic Energy", Category = "Mechanics", Image = "e_elasticeergy", Description = "e(elastic): Elastic Energy \nk: Spring Constant \nl(delta): Spring Displacement" },
                new Equation { Name = "Centripetal Force", Category = "Mechanics", Image = "e_centripetalforce", Description = "F(c): Centripetal Force \nm: Mass \na(c): Centripetal Acceleration" },
                new Equation { Name = "Centripetal Acceleration", Category = "Mechanics", Image = "e_centripetalacceleration", Description = "a(c): Centripetal Acceleration \nv: Velocity \nr: Radius Acceleration \nT: Period \nw: Angular Velocity" },
                new Equation { Name = "Harmonic Oscillator #1", Category = "Mechanics", Image = "e_harmonicoscillator1", Description = "T: Time Period \nm: Mass \nk: Spring Constant" },
                new Equation { Name = "Harmonic Oscillator #2", Category = "Mechanics", Image = "e_harmonicoscillator2", Description = "y: Elongation \nA: Amplitude \nT: Time Period \nt: Time" },
                new Equation { Name = "Harmonic Oscillator #3", Category = "Mechanics", Image = "e_harmonicoscillator_3", Description = "v: Velocity \nA: Amplitude \nT: Time Period \nt: Time" },
                new Equation { Name = "Harmonic Oscillator #4", Category = "Mechanics", Image = "e_harmonicoscillator_4", Description = "a: Acceleration \nA: Amplitude \nT: Time Period \nt: Time" },
                new Equation { Name = "Simple Pendulum", Category = "Mechanics", Image = "e_simplependulum", Description = "T: Time Period \nl: Pendulum length \ng: Gravitational Acceleration" },
                new Equation { Name = "Mass-energy Equivalence", Category = "Theory of Relativity", Image = "e_massenergy", Description = "E: Energy \nm: Mass \nc: Speed of Light" },
                new Equation { Name = "Thermal Energy", Category = "Thermodynamics", Image = "e_thermalenergy", Description = "E(T): Thermal Energy \nc: Specific Heat Capacity \nT: Change in Temperature" },
                new Equation { Name = "Latent Energy (Fusion)", Category = "Thermodynamics", Image = "e_latentenergyfus", Description = "E(f): Fusion Energy \nm: Mass \nl(f): Fusion Heat" },
                new Equation { Name = "Latent Energy (Vaporization)", Category = "Thermodynamics", Image = "e_latentenergyvap", Description = "E(v): Vaporization Energy \nm: Mass \nl(f): Vaporization Heat" },
                new Equation { Name = "Ideal Gas Law", Category = "Thermodynamics", Image = "e_idealenergy", Description = "p: Pressure \nV: Volume \nT: Temperature" },
                new Equation { Name = "Pressure", Category = "Thermodynamics", Image = "e_pressure", Description = "p: Pressure \nF: Force \nA: Area" },
                new Equation { Name = "Pressure in Liquids", Category = "Thermodynamics", Image = "e_liquidpressure", Description = "p: Pressure \np: Liquid Density \nG: Gravitational Acceleration \nh: Height" },
                new Equation { Name = "Lift Force", Category = "Thermodynamics", Image = "e_liftforce", Description = "F(L): Lift Force \np: Density \ng: Gravitational Acceleration \nV: Volume" },
                new Equation { Name = "Velocity Factor (Propagation Speed)", Category = "Wavelengths", Image = "e_velocityfactor", Description = "v: Wavelengths propagation speed \nLambda: wavelength \nf: Frequency" },
                new Equation { Name = "Light Interference (Double Slit)", Category = "Wavelengths", Image = "e_lightinterference", Description = "a: Angel between light-maxima and central-maximum \nLambda: wavelength \nn: Light-maxima's sorting-number. \nd: Double Slit constant" },
                new Equation { Name = "Law of Refraction", Category = "Wavelengths", Image = "e_lawofrefraction", Description = "i: Angle \nb: Refraction Angle \nv: Wavelength's propagation-speed" },
                new Equation { Name = "Emittance", Category = "Wavelengths", Image = "e_emittance", Description = "M(e): Emittance \nP: Effect \nT: Temperature" },
                new Equation { Name = "Stefan-Boltzmanns Law", Category = "Wavelengths", Image = "e_stefanboltzmann", Description = "M(e): Emittance \no: Stefan-Boltzmanns Constant \nT: Temperature" },
                new Equation { Name = "Wiens Displacement Law", Category = "Wavelengths", Image = "e_wiens", Description = "Lambda(max): Wavelength at radience-maximum \nT: Temperature \nb: Wiens displacement constant" },
                new Equation { Name = "Coulombs Law", Category = "Electricity", Image = "e_coulombs", Description = "F(E): Electrical Force \nk: Coulombs Constant \nQ: Charge \nr: Distance" },
                new Equation { Name = "Current", Category = "Electricity", Image = "e_current", Description = "I: Current \nQ: Charge \nt: Time" },
                new Equation { Name = "Electrical Voltage", Category = "Electricity", Image = "e_electricalvoltage", Description = "U: Voltage \nE: Energy \nQ: Charge" },
                new Equation { Name = "Ohms Law", Category = "Electricity", Image = "e_ohmslaw", Description = "I: Current \nU: Voltage \nR: Resistance" },
                new Equation { Name = "Electrical Effect", Category = "Electricity", Image = "e_electricaleffect", Description = "P: Effect \nU: Voltage \nI: Current" },
                new Equation { Name = "Electrical Field Strength", Category = "Electricity", Image = "e_electricalfieldstrenght", Description = "E: Electrical Field Strength \nU: Voltage \nd: Distance" },
                new Equation { Name = "Force Charge on Particle in Electric Field", Category = "Electricity", Image = "e_chargeparticle", Description = "F(E): Electrical Force \nE: Electrical Field Strength \nQ: Charge Amount" },
                new Equation { Name = "Equivalent Resistance (Series)", Category = "Electricity", Image = "e_seriesresistance", Description = "" },
                new Equation { Name = "Equivalent Resistance (Parallel)", Category = "Electricity", Image = "e_parallelresistance", Description = "" },
                new Equation { Name = "Magnetic Field around electrical conduct", Category = "Magnetism and Induction", Image = "e_magneticfieldelectricalconduct", Description = "B: Magnetic Flow \nu(0): Permeability in vacuum \nI: current \na: distance from conduct" },
                new Equation { Name = "Magnetic Field in flat coil", Category = "Magnetism and Induction", Image = "e_magneticfieldflat", Description = "B: Magnetic Flow \nu(0): Permeability in vacuum \nI: current \nN: Amount of laps \nr: Radius of coil" },
                new Equation { Name = "Magnetic Field in solenoid", Category = "Magnetism and Induction", Image = "e_magneticfieldsolenoid", Description = "B: Magnetic Flow Density\nu(0): Permeability in vacuum \nI: current \na: Length of solenoid" },
                new Equation { Name = "Power Effect on conductor in magnetic field", Category = "Magnetism and Induction", Image = "e_powereffectconductor", Description = "F(B): Magnetic Force Density\nB: Flow \nI: current \nl: Length" },
                new Equation { Name = "Power Effect on charged particle in magnetic field", Category = "Magnetism and Induction", Image = "e_powereffectcharged", Description = "F(B): Magnetic Force \nB: Flow Fensity\nQ: Charge \nv: Velocity" },
                new Equation { Name = "Magnetic Flow", Category = "Magnetism and Induction", Image = "e_powereffectcharged", Description = "o: Magnetic Flow \nB: Flow Density\nA: Area" },
                new Equation { Name = "Induced Voltage (Moving Conductor)", Category = "Magnetism and Induction", Image = "e_inducedvoltagemoving", Description = "U: Induced Voltage \nB: Flow Density\nl: Length \nv: Velocity" },
                new Equation { Name = "Induced Voltage (Variable Magnetic Flow)", Category = "Magnetism and Induction", Image = "e_inducedvoltagevariable", Description = "U: Induced Voltage \no: Change of magnetic flow\nt: Time Period" },
                new Equation { Name = "Transformer", Category = "Magnetism and Induction", Image = "e_transformer", Description = "U: Induced Voltage \nN: Rounds Count\nI: Current \np: Primary Side \ns: Secondary Side" },
                new Equation { Name = "Photon Energy", Category = "Atomic Physics", Image = "e_photonenergy", Description = "E(photon): Photon Energy \nh: Plancks Constant\nf: Frequency \nc: Speed of Light \nLambda: Wavelength" },
                new Equation { Name = "Photoelectric Effect", Category = "Atomic Physics", Image = "e_photoelectriceffect", Description = "E(photon): Photon Energy \nE(0): Exit-work\nE(k): Kinetic Energy" },
                new Equation { Name = "Photon Momentum", Category = "Atomic Physics", Image = "e_photonmomentum", Description = "p: Momentum \nh: Plancks Constant \nLambda: Wavelength" },
                new Equation { Name = "Matter Wavelength (de Broglie)", Category = "Atomic Physics", Image = "e_matterwave", Description = "Lambda: Wavelength \nh: Plancks Constant \nm: Mass \nv: Velocity" },
                new Equation { Name = "Hydrogen's energy levels (Bohrs)", Category = "Atomic Physics", Image = "e_hydrogenenergylevels", Description = "E(n): Energy level at orbital n \nn: Orbital Number" },
                new Equation { Name = "Law of Decay", Category = "Nuclear Physics", Image = "e_lawofdecay", Description = "N: Number of nucleus at time t at orbital n \nN(0): Number of nucleus at time 0 \nLambda: Decay Constant \nt: Time" },
                new Equation { Name = "Half-Life", Category = "Nuclear Physics", Image = "e_halflife", Description = "T(1/2): Half-life \nLambda: Decay Constant" },
                new Equation { Name = "Activity #1", Category = "Nuclear Physics", Image = "e_activity1", Description = "A: Activity \nlambda: Decay Constant \nN: Number of nucleus" },
                new Equation { Name = "Activity #2", Category = "Nuclear Physics", Image = "e_activity2", Description = "A: Activity at time 0 \nA(0): Activity at time 0 \nlambda: Decay Constant \nt: Time" },
                new Equation { Name = "Absorbed Dose", Category = "Nuclear Physics", Image = "e_absorbeddose", Description = "D: Absorbed Dose \nE: Energy \nm: Mass" },
                new Equation { Name = "Equivalent Dose", Category = "Nuclear Physics", Image = "e_equivalentdose", Description = "H: Equivalent Dose \nD: Absorbed Dose \nQ: Quality Factor" },
                new Equation { Name = "Energy in Capacitor", Category = "Electricity", Image = "e_equivalentdose", Description = "" }            };

            FilteredEquationList = new ObservableCollection<Equation>(EquationList);
        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text?.Trim().ToLower() ?? "";
            FilteredEquationList.Clear();

            foreach (var item in EquationList)
            {
                //Adding possibility to search equation names, category etc
                if (item.Name.ToLower().Contains(query) ||
                    item.Category.ToString().Contains(query) ||
                    item.Description.ToString().Contains(query) ||
                    item.Category.ToLower().Contains(query))
                {
                    FilteredEquationList.Add(item);
                }
            }
        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProPage));
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



    public class Equation
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string Image { get; set; }
        public string Description { get; set; }

        public string ImagePath => $"ms-appx:///Equations/{Image}.png";


    }
}