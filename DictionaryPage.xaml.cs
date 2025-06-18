using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Atomic_WinUI
{
    public sealed partial class DictionaryPage : Page
    {
        public ObservableCollection<DictionaryEntry> DictionaryEntries { get; } = new();
        public ObservableCollection<DictionaryEntry> FilteredEntries { get; } = new();


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
                // Fix typo: "Mathemathics" should be "math" to match your data
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

        public DictionaryPage()
        {
            this.InitializeComponent();

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Density", //Title
                "The density (more precisely, the volumetric mass density; also known as specific mass), of a substance is its mass per unit volume. The symbol most often used for density is ρ (the lower case Greek letter rho), although the Latin letter D can also be used. Mathematically, density is defined as mass divided by volume.",
                "https://en.wikipedia.org/wiki/Density" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Standard Atomic Weight", //Title
                "The standard atomic weight (Ar, standard) of a chemical element is the weighted arithmetic mean of the relative atomic masses (Ar) of all isotopes of that element weighted by each isotope's abundance on Earth.",
                "https://en.wikipedia.org/wiki/Standard_atomic_weight" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Atomic Radius", //Title
                "The atomic radius of a chemical element is a measure of the size of its atoms, usually the mean or typical distance from the center of the nucleus to the boundary of the surrounding shells of electrons.",
                "https://en.wikipedia.org/wiki/Atomic_radius" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Electronegativity", //Title
                "Electronegativity, symbol χ, is a concept that describes the tendency of an atom to attract a shared pair of electrons (or electron density) towards itself.",
                "https://en.wikipedia.org/wiki/Electronegativity" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics, reactions", //Category
                "Ionization Energy", //Title
                "In physics and chemistry, ionization energy (Ei) is the minimum amount of energy required to remove the most loosely bound electron, the valence electron, of an isolated neutral gaseous atom or molecule.",
                "https://en.wikipedia.org/wiki/Ionization_energy" //Wiki link
            ));
            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
            "Electron Configuration", //Title
            "In atomic physics and quantum chemistry, the electron configuration is the distribution of electrons of an atom or molecule in atomic or molecular orbitals.",
            "https://en.wikipedia.org/wiki/Electron_configuration" //Wiki link
             ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Enthalpy of Fusion", //Title
                "The enthalpy of fusion, also known as latent heat of fusion, is the energy required to change a substance from a solid to a liquid at constant pressure.",
                "https://en.wikipedia.org/wiki/Enthalpy_of_fusion" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Specific Heat Capacity", //Title
                "Specific heat capacity is the amount of heat energy needed to raise the temperature of one unit of mass of a substance by one degree.",
                "https://en.wikipedia.org/wiki/Specific_heat_capacity" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Enthalpy of Vaporization", //Title
                "The enthalpy of vaporization, also known as latent heat of vaporization, is the amount of energy required to transform a substance from liquid to gas at a given pressure.",
                "https://en.wikipedia.org/wiki/Enthalpy_of_vaporization" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", //Category
                "Solubility Chart", //Title
                "A solubility chart lists ions and shows how they interact when mixed, revealing whether they form precipitates or remain dissolved.",
                "https://en.wikipedia.org/wiki/Solubility_chart" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Superconductivity", //Title
                "Superconductivity is a phenomenon where certain materials exhibit zero electrical resistance and expel magnetic fields when cooled below a critical temperature.",
                "https://en.wikipedia.org/wiki/Superconductivity" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Electrical Resistivity", //Title
                "Electrical resistivity is a measure of how strongly a material opposes the flow of electric current. Its inverse property is electrical conductivity.",
                "https://en.wikipedia.org/wiki/Electrical_resistivity_and_conductivity" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Thermodynamic Temperature", //Title
                "Thermodynamic temperature is the absolute measure of temperature based on the principles of thermodynamics.",
                "https://en.wikipedia.org/wiki/Thermodynamic_temperature" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Absolute Zero", //Title
                "Absolute zero is the lowest possible temperature, where molecular motion nearly ceases and entropy reaches its minimum.",
                "https://en.wikipedia.org/wiki/Absolute_zero" //Wiki link
            ));
            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
            "Acid", //Title
            "An acid is a molecule or ion capable of donating a proton (hydrogen ion H+) or forming a covalent bond with an electron pair.",
            "https://en.wikipedia.org/wiki/Acid" //Wiki link
             ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Activation Energy", //Title
                "Activation energy is the minimum amount of energy required to initiate a chemical reaction or other physical process.",
                "https://en.wikipedia.org/wiki/Activation_energy" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Chemical Element", //Title
                "A chemical element is a pure substance consisting of atoms with the same number of protons in their nucleus.",
                "https://en.wikipedia.org/wiki/Chemical_element" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Electron", //Title
                "The electron is a subatomic particle with a negative charge, fundamental to electrical conductivity and chemical bonding.",
                "https://en.wikipedia.org/wiki/Electron" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Proton", //Title
                "A proton is a positively charged subatomic particle found in the nucleus of an atom.",
                "https://en.wikipedia.org/wiki/Proton" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Neutron", //Title
                "The neutron is a neutral subatomic particle found in atomic nuclei, contributing to atomic mass.",
                "https://en.wikipedia.org/wiki/Neutron" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Block (Periodic Table)", //Title
                "A block in the periodic table consists of elements grouped by their valence electron configurations.",
                "https://en.wikipedia.org/wiki/Block_(periodic_table)" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Period (Periodic Table)", //Title
                "A period in the periodic table is a horizontal row of elements arranged by increasing atomic number.",
                "https://en.wikipedia.org/wiki/Period_(periodic_table)" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Covalent Radius", //Title
                "The covalent radius is the measure of an atom’s size based on the distance between bonded atoms.",
                "https://en.wikipedia.org/wiki/Covalent_radius" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
            "Van der Waals Radius", //Title
            "The Van der Waals radius of an atom represents the distance of closest approach for another atom.",
            "https://en.wikipedia.org/wiki/Van_der_Waals_radius" //Wiki link
               ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Dark Matter", //Title
                "Dark matter is an invisible form of matter that accounts for most of the universe’s mass.",
                "https://en.wikipedia.org/wiki/Dark_matter" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Elementary Particle", //Title
                "An elementary particle is a fundamental particle that is not composed of smaller particles.",
                "https://simple.wikipedia.org/wiki/Elementary_particle" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Fats", //Title
                "Fats are esters of fatty acids, commonly occurring in living organisms and food.",
                "https://en.wikipedia.org/wiki/Fat" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Functional Group", //Title
                "A functional group is a specific arrangement of atoms within molecules responsible for chemical behavior.",
                "https://en.wikipedia.org/wiki/Functional_group" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Hydrophile", //Title
                "A hydrophile is a molecule that has an affinity for water and tends to dissolve in it.",
                "https://en.wikipedia.org/wiki/Hydrophile" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Hydrophobe", //Title
                "A hydrophobe is a molecule that repels water and does not readily dissolve in it.",
                "https://en.wikipedia.org/wiki/Hydrophobe" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Isotope", //Title
                "Isotopes are variations of a chemical element that have different numbers of neutrons.",
                "https://en.wikipedia.org/wiki/Isotope" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Liquid", //Title
                "A liquid is a fluid that conforms to the shape of its container but retains a constant volume.",
                "https://en.wikipedia.org/wiki/Liquid" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
    "Macromolecule", //Title
    "A macromolecule is a large molecule composed of thousands of atoms, often formed by polymerization.",
    "https://en.wikipedia.org/wiki/Macromolecule" //Wiki link
));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Magnetic Field", //Title
                "A magnetic field describes the magnetic influence of moving electric charges and magnetized materials.",
                "https://en.wikipedia.org/wiki/Magnetic_field" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Gas", //Title
                "Gas is one of the four fundamental states of matter, characterized by its ability to expand to fill its container.",
                "https://en.wikipedia.org/wiki/Gas" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Molar Mass", //Title
                "Molar mass is the mass of a sample divided by the amount of substance in moles.",
                "https://en.wikipedia.org/wiki/Molar_mass" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Nuclear Matter", //Title
                "Nuclear matter is an interacting system of protons and neutrons found in atomic nuclei.",
                "https://en.wikipedia.org/wiki/Nuclear_matter" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Pressure", //Title
                "Pressure is the force applied perpendicular to a surface per unit area over which the force is distributed.",
                "https://en.wikipedia.org/wiki/Pressure" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Rust", //Title
                "Rust is an iron oxide formed when iron reacts with oxygen in the presence of water or air moisture.",
                "https://en.wikipedia.org/wiki/Rust" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Semiconductors", //Title
                "A semiconductor material has an electrical conductivity value between that of a conductor and an insulator.",
                "https://en.wikipedia.org/wiki/Semiconductor" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Solubility", //Title
                "Solubility refers to the ability of a chemical substance to dissolve in a solvent to form a solution.",
                "https://en.wikipedia.org/wiki/Solubility" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Spectrometer", //Title
                "A spectrometer is an instrument used to separate and measure spectral components of a physical phenomenon.",
                "https://en.wikipedia.org/wiki/Spectrometer" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
    "Vacuum", //Title
    "A vacuum is a space devoid of matter, often characterized by low pressure compared to atmospheric pressure.",
    "https://en.wikipedia.org/wiki/Vacuum" //Wiki link
));

            DictionaryEntries.Add(new DictionaryEntry("physics, math", //Category
                "Wavelength", //Title
                "Wavelength is the distance over which the shape of a wave repeats, commonly measured between adjacent crests.",
                "https://en.wikipedia.org/wiki/Wavelength" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Weight", //Title
                "Weight is the force exerted on an object due to gravity, proportional to its mass.",
                "https://en.wikipedia.org/wiki/Weight" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "White Noise", //Title
                "White noise is a random signal with equal intensity across different frequencies, creating a constant spectral density.",
                "https://en.wikipedia.org/wiki/White_noise" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics, math", //Category
                "Frequency", //Title
                "Frequency refers to the number of times an event repeats per unit time, commonly used in wave physics.",
                "https://en.wikipedia.org/wiki/Frequency" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Friction", //Title
                "Friction is the resistive force that opposes relative motion between two surfaces in contact.",
                "https://en.wikipedia.org/wiki/Friction" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("math", //Category
                "Efficiency", //Title
                "Efficiency measures how effectively input is converted into useful output, commonly expressed as a percentage.",
                "https://en.wikipedia.org/wiki/Efficiency" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Impulse", //Title
                "Impulse is the change in momentum of an object due to the application of a force over time.",
                "https://en.wikipedia.org/wiki/Impulse_(physics)" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Intensity", //Title
                "Intensity represents the power transferred per unit area in a given direction, commonly used in wave physics.",
                "https://en.wikipedia.org/wiki/Intensity_(physics)" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Centripetal Force", //Title
                "Centripetal force acts perpendicular to the motion of an object, keeping it moving in a curved path.",
                "https://en.wikipedia.org/wiki/Centripetal_force" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
    "Momentum", //Title
    "Momentum is the product of an object's mass and velocity, describing the quantity of motion it possesses.",
    "https://en.wikipedia.org/wiki/Momentum" //Wiki link
));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Spectrum", //Title
                "A spectrum represents a continuum of values without discrete steps, commonly used in optics and electromagnetic analysis.",
                "https://en.wikipedia.org/wiki/Spectrum" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Electromagnetic Spectrum", //Title
                "The electromagnetic spectrum is the range of frequencies of electromagnetic radiation, including visible light, radio waves, and X-rays.",
                "https://en.wikipedia.org/wiki/Electromagnetic_spectrum" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Electric Charge", //Title
                "Electric charge is a property of matter that causes it to experience force when placed in an electromagnetic field.",
                "https://en.wikipedia.org/wiki/Electric_charge" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, physics", //Category
                "Matter", //Title
                "Matter is any substance with mass that occupies space, forming the physical components of the universe.",
                "https://en.wikipedia.org/wiki/Matter" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Photon", //Title
                "A photon is a fundamental particle of electromagnetic radiation, always moving at the speed of light.",
                "https://en.wikipedia.org/wiki/Photon" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Boson", //Title
                "A boson is a type of elementary particle that follows Bose–Einstein statistics, including force carriers like photons and gluons.",
                "https://en.wikipedia.org/wiki/Boson" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Fermion", //Title
                "Fermions are elementary particles that obey the Pauli exclusion principle, including quarks and leptons.",
                "https://en.wikipedia.org/wiki/Fermion" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Quark", //Title
                "A quark is a fundamental particle and a building block of protons and neutrons within atomic nuclei.",
                "https://en.wikipedia.org/wiki/Quark" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Lepton", //Title
                "Leptons are elementary particles that do not undergo strong interactions, including electrons and neutrinos.",
                "https://en.wikipedia.org/wiki/Lepton" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
    "Particle Accelerator", //Title
    "A particle accelerator is a machine that uses electromagnetic fields to propel charged particles to high speeds and energies.",
    "https://en.wikipedia.org/wiki/Particle_accelerator" //Wiki link
));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Electric Field", //Title
                "An electric field is a physical field surrounding electrically charged particles, exerting force on other charged particles.",
                "https://en.wikipedia.org/wiki/Electric_field" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Molecule", //Title
                "A molecule is a neutral group of two or more atoms held together by chemical bonds.",
                "https://en.wikipedia.org/wiki/Molecule" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", //Category
                "Hydrogen Bond", //Title
                "A hydrogen bond is an electrostatic attraction between a hydrogen atom covalently bound to an electronegative atom and another electronegative atom.",
                "https://en.wikipedia.org/wiki/Hydrogen_bond" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", //Category
                "Van der Waals Force", //Title
                "Van der Waals force is a weak, distance-dependent interaction between atoms or molecules, distinct from ionic or covalent bonds.",
                "https://en.wikipedia.org/wiki/Van_der_Waals_force" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", //Category
                "Ionic Bonding", //Title
                "Ionic bonding is a type of chemical bonding involving electrostatic attraction between oppositely charged ions.",
                "https://en.wikipedia.org/wiki/Ionic_bonding" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", //Category
                "Ion", //Title
                "An ion is an atom or molecule with a net electrical charge due to the loss or gain of electrons.",
                "https://en.wikipedia.org/wiki/Ion" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", //Category
                "Cation", //Title
                "A cation is a positively charged ion, formed when an atom loses electrons.",
                "https://en.wikipedia.org/wiki/Ion#Anions_and_cations" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry, reactions", //Category
                "Anion", //Title
                "An anion is a negatively charged ion, formed when an atom gains electrons.",
                "https://en.wikipedia.org/wiki/Ion#Anions_and_cations" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Energy", //Title
                "Energy is the quantitative property that must be transferred to a system to perform work or heat it.",
                "https://en.wikipedia.org/wiki/Energy" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
    "Elastic Energy", //Title
    "Elastic energy is the mechanical potential energy stored in a material or system when subjected to elastic deformation.",
    "https://en.wikipedia.org/wiki/Elastic_energy" //Wiki link
));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Kinetic Energy", //Title
                "Kinetic energy is the energy an object possesses due to its motion, defined as the work needed to accelerate it to a given velocity.",
                "https://en.wikipedia.org/wiki/Kinetic_energy" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics", //Category
                "Potential Energy", //Title
                "Potential energy is the stored energy an object has due to its position relative to other objects or forces acting upon it.",
                "https://en.wikipedia.org/wiki/Potential_energy" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("physics, chemistry", //Category
                "Mass", //Title
                "Mass is a fundamental property of physical objects, representing the amount of matter contained within them.",
                "https://en.wikipedia.org/wiki/Mass" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("math", //Category
                "Measure", //Title
                "In mathematics, a measure systematically assigns a numerical value to subsets of a given set, generalizing concepts like length and volume.",
                "https://en.wikipedia.org/wiki/Measure_(mathematics)" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("math", //Category
                "Function", //Title
                "A function is a relation between two sets where each element of the first set corresponds to exactly one element of the second set.",
                "https://en.wikipedia.org/wiki/Function_(mathematics)" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("math", //Category
                "Differentiable Function", //Title
                "A differentiable function is one whose derivative exists at each point in its domain, ensuring smoothness and continuity.",
                "https://en.wikipedia.org/wiki/Differentiable_function" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("math", //Category
                "Calculus", //Title
                "Calculus is the mathematical study of continuous change, encompassing differentiation and integration.",
                "https://en.wikipedia.org/wiki/Calculus" //Wiki link
            ));

            DictionaryEntries.Add(new DictionaryEntry("chemistry", //Category
                "Solid-Water Distribution Coefficient", //Title
                "The solid-water distribution coefficient describes the ratio of a compound’s equilibrium concentrations in solid and aqueous phases.",
                "https://chempedia.info/info/solid_water_distribution_coefficient/" //Wiki link
            ));



            foreach (var entry in DictionaryEntries)
                FilteredEntries.Add(entry);

            this.DataContext = this;
        }


    }

}