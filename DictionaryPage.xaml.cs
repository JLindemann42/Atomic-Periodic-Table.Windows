using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Atomic_WinUI
{
    public sealed partial class DictionaryPage : Page
    {
        public ObservableCollection<DictionaryEntry> DictionaryEntries { get; } = new();
        public ObservableCollection<DictionaryEntry> FilteredEntries { get; } = new();

        public DictionaryPage()
        {
            this.InitializeComponent();

            // Responsive card width handlers
            this.SizeChanged += DictionaryPage_SizeChanged;
            this.Loaded += DictionaryPage_Loaded;

            // --- DictionaryEntries initialization (unchanged) ---
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Density", "The density (more precisely, the volumetric mass density; also known as specific mass), of a substance is its mass per unit volume. The symbol most often used for density is ρ (the lower case Greek letter rho), although the Latin letter D can also be used. Mathematically, density is defined as mass divided by volume.", "https://en.wikipedia.org/wiki/Density"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Standard Atomic Weight", "The standard atomic weight (Ar, standard) of a chemical element is the weighted arithmetic mean of the relative atomic masses (Ar) of all isotopes of that element weighted by each isotope's abundance on Earth.", "https://en.wikipedia.org/wiki/Standard_atomic_weight"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Atomic Radius", "The atomic radius of a chemical element is a measure of the size of its atoms, usually the mean or typical distance from the center of the nucleus to the boundary of the surrounding shells of electrons.", "https://en.wikipedia.org/wiki/Atomic_radius"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Electronegativity", "Electronegativity, symbol χ, is a concept that describes the tendency of an atom to attract a shared pair of electrons (or electron density) towards itself.", "https://en.wikipedia.org/wiki/Electronegativity"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics, reactions", "Ionization Energy", "In physics and chemistry, ionization energy (Ei) is the minimum amount of energy required to remove the most loosely bound electron, the valence electron, of an isolated neutral gaseous atom or molecule.", "https://en.wikipedia.org/wiki/Ionization_energy"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Electron Configuration", "In atomic physics and quantum chemistry, the electron configuration is the distribution of electrons of an atom or molecule in atomic or molecular orbitals.", "https://en.wikipedia.org/wiki/Electron_configuration"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Enthalpy of Fusion", "The enthalpy of fusion, also known as latent heat of fusion, is the energy required to change a substance from a solid to a liquid at constant pressure.", "https://en.wikipedia.org/wiki/Enthalpy_of_fusion"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Specific Heat Capacity", "Specific heat capacity is the amount of heat energy needed to raise the temperature of one unit of mass of a substance by one degree.", "https://en.wikipedia.org/wiki/Specific_heat_capacity"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Enthalpy of Vaporization", "The enthalpy of vaporization, also known as latent heat of vaporization, is the amount of energy required to transform a substance from liquid to gas at a given pressure.", "https://en.wikipedia.org/wiki/Enthalpy_of_vaporization"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", "Solubility Chart", "A solubility chart lists ions and shows how they interact when mixed, revealing whether they form precipitates or remain dissolved.", "https://en.wikipedia.org/wiki/Solubility_chart"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Superconductivity", "Superconductivity is a phenomenon where certain materials exhibit zero electrical resistance and expel magnetic fields when cooled below a critical temperature.", "https://en.wikipedia.org/wiki/Superconductivity"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Electrical Resistivity", "Electrical resistivity is a measure of how strongly a material opposes the flow of electric current. Its inverse property is electrical conductivity.", "https://en.wikipedia.org/wiki/Electrical_resistivity_and_conductivity"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Thermodynamic Temperature", "Thermodynamic temperature is the absolute measure of temperature based on the principles of thermodynamics.", "https://en.wikipedia.org/wiki/Thermodynamic_temperature"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Absolute Zero", "Absolute zero is the lowest possible temperature, where molecular motion nearly ceases and entropy reaches its minimum.", "https://en.wikipedia.org/wiki/Absolute_zero"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Acid", "An acid is a molecule or ion capable of donating a proton (hydrogen ion H+) or forming a covalent bond with an electron pair.", "https://en.wikipedia.org/wiki/Acid"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Activation Energy", "Activation energy is the minimum amount of energy required to initiate a chemical reaction or other physical process.", "https://en.wikipedia.org/wiki/Activation_energy"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Chemical Element", "A chemical element is a pure substance consisting of atoms with the same number of protons in their nucleus.", "https://en.wikipedia.org/wiki/Chemical_element"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Electron", "The electron is a subatomic particle with a negative charge, fundamental to electrical conductivity and chemical bonding.", "https://en.wikipedia.org/wiki/Electron"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Proton", "A proton is a positively charged subatomic particle found in the nucleus of an atom.", "https://en.wikipedia.org/wiki/Proton"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Neutron", "The neutron is a neutral subatomic particle found in atomic nuclei, contributing to atomic mass.", "https://en.wikipedia.org/wiki/Neutron"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Block (Periodic Table)", "A block in the periodic table consists of elements grouped by their valence electron configurations.", "https://en.wikipedia.org/wiki/Block_(periodic_table)"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Period (Periodic Table)", "A period in the periodic table is a horizontal row of elements arranged by increasing atomic number.", "https://en.wikipedia.org/wiki/Period_(periodic_table)"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Covalent Radius", "The covalent radius is the measure of an atom’s size based on the distance between bonded atoms.", "https://en.wikipedia.org/wiki/Covalent_radius"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Van der Waals Radius", "The Van der Waals radius of an atom represents the distance of closest approach for another atom.", "https://en.wikipedia.org/wiki/Van_der_Waals_radius"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Dark Matter", "Dark matter is an invisible form of matter that accounts for most of the universe’s mass.", "https://en.wikipedia.org/wiki/Dark_matter"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Elementary Particle", "An elementary particle is a fundamental particle that is not composed of smaller particles.", "https://simple.wikipedia.org/wiki/Elementary_particle"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Fats", "Fats are esters of fatty acids, commonly occurring in living organisms and food.", "https://en.wikipedia.org/wiki/Fat"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Functional Group", "A functional group is a specific arrangement of atoms within molecules responsible for chemical behavior.", "https://en.wikipedia.org/wiki/Functional_group"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Hydrophile", "A hydrophile is a molecule that has an affinity for water and tends to dissolve in it.", "https://en.wikipedia.org/wiki/Hydrophile"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Hydrophobe", "A hydrophobe is a molecule that repels water and does not readily dissolve in it.", "https://en.wikipedia.org/wiki/Hydrophobe"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Isotope", "Isotopes are variations of a chemical element that have different numbers of neutrons.", "https://en.wikipedia.org/wiki/Isotope"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Liquid", "A liquid is a fluid that conforms to the shape of its container but retains a constant volume.", "https://en.wikipedia.org/wiki/Liquid"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Macromolecule", "A macromolecule is a large molecule composed of thousands of atoms, often formed by polymerization.", "https://en.wikipedia.org/wiki/Macromolecule"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Magnetic Field", "A magnetic field describes the magnetic influence of moving electric charges and magnetized materials.", "https://en.wikipedia.org/wiki/Magnetic_field"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Gas", "Gas is one of the four fundamental states of matter, characterized by its ability to expand to fill its container.", "https://en.wikipedia.org/wiki/Gas"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Molar Mass", "Molar mass is the mass of a sample divided by the amount of substance in moles.", "https://en.wikipedia.org/wiki/Molar_mass"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Nuclear Matter", "Nuclear matter is an interacting system of protons and neutrons found in atomic nuclei.", "https://en.wikipedia.org/wiki/Nuclear_matter"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Pressure", "Pressure is the force applied perpendicular to a surface per unit area over which the force is distributed.", "https://en.wikipedia.org/wiki/Pressure"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Rust", "Rust is an iron oxide formed when iron reacts with oxygen in the presence of water or air moisture.", "https://en.wikipedia.org/wiki/Rust"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Semiconductors", "A semiconductor material has an electrical conductivity value between that of a conductor and an insulator.", "https://en.wikipedia.org/wiki/Semiconductor"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Solubility", "Solubility refers to the ability of a chemical substance to dissolve in a solvent to form a solution.", "https://en.wikipedia.org/wiki/Solubility"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Spectrometer", "A spectrometer is an instrument used to separate and measure spectral components of a physical phenomenon.", "https://en.wikipedia.org/wiki/Spectrometer"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Vacuum", "A vacuum is a space devoid of matter, often characterized by low pressure compared to atmospheric pressure.", "https://en.wikipedia.org/wiki/Vacuum"));
            DictionaryEntries.Add(new DictionaryEntry("physics, math", "Wavelength", "Wavelength is the distance over which the shape of a wave repeats, commonly measured between adjacent crests.", "https://en.wikipedia.org/wiki/Wavelength"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Weight", "Weight is the force exerted on an object due to gravity, proportional to its mass.", "https://en.wikipedia.org/wiki/Weight"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "White Noise", "White noise is a random signal with equal intensity across different frequencies, creating a constant spectral density.", "https://en.wikipedia.org/wiki/White_noise"));
            DictionaryEntries.Add(new DictionaryEntry("physics, math", "Frequency", "Frequency refers to the number of times an event repeats per unit time, commonly used in wave physics.", "https://en.wikipedia.org/wiki/Frequency"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Friction", "Friction is the resistive force that opposes relative motion between two surfaces in contact.", "https://en.wikipedia.org/wiki/Friction"));
            DictionaryEntries.Add(new DictionaryEntry("math", "Efficiency", "Efficiency measures how effectively input is converted into useful output, commonly expressed as a percentage.", "https://en.wikipedia.org/wiki/Efficiency"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Impulse", "Impulse is the change in momentum of an object due to the application of a force over time.", "https://en.wikipedia.org/wiki/Impulse_(physics)"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Intensity", "Intensity represents the power transferred per unit area in a given direction, commonly used in wave physics.", "https://en.wikipedia.org/wiki/Intensity_(physics)"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Centripetal Force", "Centripetal force acts perpendicular to the motion of an object, keeping it moving in a curved path.", "https://en.wikipedia.org/wiki/Centripetal_force"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Momentum", "Momentum is the product of an object's mass and velocity, describing the quantity of motion it possesses.", "https://en.wikipedia.org/wiki/Momentum"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Spectrum", "A spectrum represents a continuum of values without discrete steps, commonly used in optics and electromagnetic analysis.", "https://en.wikipedia.org/wiki/Spectrum"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Electromagnetic Spectrum", "The electromagnetic spectrum is the range of frequencies of electromagnetic radiation, including visible light, radio waves, and X-rays.", "https://en.wikipedia.org/wiki/Electromagnetic_spectrum"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Electric Charge", "Electric charge is a property of matter that causes it to experience force when placed in an electromagnetic field.", "https://en.wikipedia.org/wiki/Electric_charge"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", "Matter", "Matter is any substance with mass that occupies space, forming the physical components of the universe.", "https://en.wikipedia.org/wiki/Matter"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Photon", "A photon is a fundamental particle of electromagnetic radiation, always moving at the speed of light.", "https://en.wikipedia.org/wiki/Photon"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Boson", "A boson is a type of elementary particle that follows Bose–Einstein statistics, including force carriers like photons and gluons.", "https://en.wikipedia.org/wiki/Boson"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Fermion", "Fermions are elementary particles that obey the Pauli exclusion principle, including quarks and leptons.", "https://en.wikipedia.org/wiki/Fermion"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Quark", "A quark is a fundamental particle and a building block of protons and neutrons within atomic nuclei.", "https://en.wikipedia.org/wiki/Quark"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Lepton", "Leptons are elementary particles that do not undergo strong interactions, including electrons and neutrinos.", "https://en.wikipedia.org/wiki/Lepton"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Particle Accelerator", "A particle accelerator is a machine that uses electromagnetic fields to propel charged particles to high speeds and energies.", "https://en.wikipedia.org/wiki/Particle_accelerator"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Electric Field", "An electric field is a physical field surrounding electrically charged particles, exerting force on other charged particles.", "https://en.wikipedia.org/wiki/Electric_field"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Molecule", "A molecule is a neutral group of two or more atoms held together by chemical bonds.", "https://en.wikipedia.org/wiki/Molecule"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", "Hydrogen Bond", "A hydrogen bond is an electrostatic attraction between a hydrogen atom covalently bound to an electronegative atom and another electronegative atom.", "https://en.wikipedia.org/wiki/Hydrogen_bond"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", "Van der Waals Force", "Van der Waals force is a weak, distance-dependent interaction between atoms or molecules, distinct from ionic or covalent bonds.", "https://en.wikipedia.org/wiki/Van_der_Waals_force"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", "Ionic Bonding", "Ionic bonding is a type of chemical bonding involving electrostatic attraction between oppositely charged ions.", "https://en.wikipedia.org/wiki/Ionic_bonding"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", "Ion", "An ion is an atom or molecule with a net electrical charge due to the loss or gain of electrons.", "https://en.wikipedia.org/wiki/Ion"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", "Cation", "A cation is a positively charged ion, formed when an atom loses electrons.", "https://en.wikipedia.org/wiki/Ion#Anions_and_cations"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", "Anion", "An anion is a negatively charged ion, formed when an atom gains electrons.", "https://en.wikipedia.org/wiki/Ion#Anions_and_cations"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Energy", "Energy is the quantitative property that must be transferred to a system to perform work or heat it.", "https://en.wikipedia.org/wiki/Energy"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Elastic Energy", "Elastic energy is the mechanical potential energy stored in a material or system when subjected to elastic deformation.", "https://en.wikipedia.org/wiki/Elastic_energy"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Kinetic Energy", "Kinetic energy is the energy an object possesses due to its motion, defined as the work needed to accelerate it to a given velocity.", "https://en.wikipedia.org/wiki/Kinetic_energy"));
            DictionaryEntries.Add(new DictionaryEntry("physics", "Potential Energy", "Potential energy is the stored energy an object has due to its position relative to other objects or forces acting upon it.", "https://en.wikipedia.org/wiki/Potential_energy"));
            DictionaryEntries.Add(new DictionaryEntry("physics, chemistry", "Mass", "Mass is a fundamental property of physical objects, representing the amount of matter contained within them.", "https://en.wikipedia.org/wiki/Mass"));
            DictionaryEntries.Add(new DictionaryEntry("math", "Measure", "In mathematics, a measure systematically assigns a numerical value to subsets of a given set, generalizing concepts like length and volume.", "https://en.wikipedia.org/wiki/Measure_(mathematics)"));
            DictionaryEntries.Add(new DictionaryEntry("math", "Function", "A function is a relation between two sets where each element of the first set corresponds to exactly one element of the second set.", "https://en.wikipedia.org/wiki/Function_(mathematics)"));
            DictionaryEntries.Add(new DictionaryEntry("math", "Differentiable Function", "A differentiable function is one whose derivative exists at each point in its domain, ensuring smoothness and continuity.", "https://en.wikipedia.org/wiki/Differentiable_function"));
            DictionaryEntries.Add(new DictionaryEntry("math", "Calculus", "Calculus is the mathematical study of continuous change, encompassing differentiation and integration.", "https://en.wikipedia.org/wiki/Calculus"));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", "Solid-Water Distribution Coefficient", "The solid-water distribution coefficient describes the ratio of a compound’s equilibrium concentrations in solid and aqueous phases.", "https://chempedia.info/info/solid_water_distribution_coefficient/"));

            foreach (var entry in DictionaryEntries)
                FilteredEntries.Add(entry);

            this.DataContext = this;
        }

        // Responsive card width logic
        private void DictionaryPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateGridViewItemWidth();
        }

        private void DictionaryPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateGridViewItemWidth();
        }

        private void UpdateGridViewItemWidth()
        {
            // Adjust as needed
            double minCardWidth = 240;
            double maxCardWidth = 340;
            double leftPadding = 50;
            double rightPadding = 24;

            double availableWidth = this.ActualWidth - leftPadding - rightPadding;
            if (availableWidth < minCardWidth)
                availableWidth = minCardWidth;

            int columns = (int)(availableWidth / minCardWidth);
            if (columns < 1) columns = 1;
            if (columns > 5) columns = 5; // Enforce maximum of 5 columns

            double cardWidth = availableWidth / columns;
            if (cardWidth > maxCardWidth) cardWidth = maxCardWidth;

            if (DictionaryGridView?.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                wrapGrid.ItemWidth = cardWidth;
            }
        }

        public void FilterByCategory(string selectedCategory)
        {
            FilteredEntries.Clear();
            foreach (var entry in DictionaryEntries)
            {
                if (CategoryContains(entry, selectedCategory))
                    FilteredEntries.Add(entry);
            }
        }

        public bool CategoryContains(DictionaryEntry entry, string selectedCategory)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Category) || string.IsNullOrWhiteSpace(selectedCategory))
                return false;

            var categories = entry.Category
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.Trim().ToLowerInvariant());

            return categories.Contains(selectedCategory.Trim().ToLowerInvariant());
        }

        private void OnClearFilterSelected(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            FilteredEntries.Clear();
            foreach (var entry in DictionaryEntries)
                FilteredEntries.Add(entry);
        }

        private void OnDisplayModeSelected(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Text is string category)
            {
                string normalized = category.ToLowerInvariant();
                if (normalized == "mathemathics") normalized = "math";
                FilterByCategory(normalized);
            }
        }

        private void OnSearchBoxTextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
        {
            string searchTerm = SearchBox.Text.Trim();

            FilteredEntries.Clear();

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                foreach (var entry in DictionaryEntries)
                    FilteredEntries.Add(entry);
            }
            else
            {
                foreach (var entry in DictionaryEntries)
                {
                    if (!string.IsNullOrEmpty(entry.Term) &&
                        entry.Term.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        FilteredEntries.Add(entry);
                    }
                }
            }
        }
    }
}
