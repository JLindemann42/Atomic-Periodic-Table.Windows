using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// The catalogue of every field the agent can answer about.
    ///
    /// <para>Two fields the legacy agent read - <c>element_period</c> and
    /// <c>element_group_number</c> - do not exist in the data at all (0 of 118 elements), so the
    /// period and group-number branches silently answered with empty strings. They are derived
    /// here from atomic number instead.</para>
    /// </summary>
    public static class FieldRegistry
    {
        /// <summary>Sentinel keys for values computed rather than read. Never present in the data.</summary>
        public const string DerivedPeriod = "__derived_period";
        public const string DerivedGroup = "__derived_group";
        public const string DerivedValence = "__derived_valence";
        public const string DerivedSynthetic = "__derived_synthetic";

        /// <summary>
        /// The invariant token for a reservoir measured in µg of element per litre of sea water.
        ///
        /// <para>Deliberately absent from the unit converter's factor table: this is mass per
        /// <i>volume</i>, and turning it into the mg/kg the other reservoirs use needs sea water's
        /// density. Leaving it unconvertible makes an "in ppm" request fall back to the authored
        /// figure rather than inventing a number.</para>
        /// </summary>
        public const string UnitMicrogramPerLitre = "µg/l";

        /// <summary>
        /// The invariant token for the solar reservoirs, counted as atoms against silicon = 10⁶.
        ///
        /// <para>Not a concentration at all, so it carries <see cref="Dimension.Dimensionless"/>
        /// and never converts. Users see the localized label instead of this token, which is why
        /// it can stay ASCII - unit canonicalisation runs NFKC, and that folds a superscript ⁶
        /// down to a plain 6.</para>
        /// </summary>
        public const string UnitAtomsPerSilicon = "Si=10^6";

        /// <summary>
        /// Elements with no stable isotopes that do not occur naturally in appreciable amounts.
        ///
        /// <para>Technetium (43) and promethium (61) are the two gaps below uranium; everything
        /// heavier than uranium (92) is synthetic. Neptunium and plutonium occur in trace amounts
        /// in uranium ore but are conventionally counted as synthetic, which is the convention
        /// used here.</para>
        ///
        /// <para>This matters for correctness, not just completeness: without it "the heaviest
        /// naturally occurring element" answers tennessine, which has only ever existed a few
        /// atoms at a time in an accelerator. The right answer is uranium.</para>
        /// </summary>
        public static bool IsSynthetic(int atomicNumber) =>
            atomicNumber == 43 || atomicNumber == 61 || atomicNumber > 92;

        /// <summary>
        /// Valence electrons for a main-group element, or null where the concept does not apply
        /// cleanly - the d-block and f-block, where inner shells are still filling.
        ///
        /// <para>Groups 1 and 2 give 1 and 2; groups 13 to 18 give 3 to 8. Helium is the
        /// exception: it sits in group 18 but has only 2 electrons in total.</para>
        /// </summary>
        public static int? ValenceElectrons(int atomicNumber)
        {
            if (atomicNumber == 2) return 2;
            if (GroupOf(atomicNumber) is not { } group) return null;
            return group switch
            {
                1 or 2 => group,
                >= 13 and <= 18 => group - 10,
                _ => null,
            };
        }

        /// <summary>
        /// Period for an atomic number. The data has no <c>element_period</c> key, so it is derived
        /// from where each period ends: 2, 10, 18, 36, 54, 86, 118.
        /// </summary>
        public static int PeriodOf(int atomicNumber) => atomicNumber switch
        {
            <= 0 => 0,
            <= 2 => 1,
            <= 10 => 2,
            <= 18 => 3,
            <= 36 => 4,
            <= 54 => 5,
            <= 86 => 6,
            <= 118 => 7,
            _ => 0,
        };

        /// <summary>
        /// Group (column) 1..18 for an atomic number, or null for the f-block, which sits outside
        /// the numbered groups. Also derived, since the data has no <c>element_group_number</c> key.
        /// </summary>
        public static int? GroupOf(int atomicNumber) =>
            atomicNumber >= 0 && atomicNumber < GroupOfZ.Length && GroupOfZ[atomicNumber] > 0
                ? GroupOfZ[atomicNumber]
                : null;

        /// <summary>Index is the atomic number; 0 means "no numbered group" (lanthanoids, actinides).</summary>
        private static readonly int[] GroupOfZ = BuildGroups();

        private static int[] BuildGroups()
        {
            var g = new int[119];

            g[1] = 1; g[2] = 18;
            g[3] = 1; g[4] = 2; g[5] = 13; g[6] = 14;
            g[7] = 15; g[8] = 16; g[9] = 17; g[10] = 18;
            g[11] = 1; g[12] = 2; g[13] = 13; g[14] = 14;
            g[15] = 15; g[16] = 16; g[17] = 17; g[18] = 18;
            g[19] = 1; g[20] = 2;

            for (var z = 21; z <= 30; z++) g[z] = z - 18;   // Sc..Zn -> 3..12
            for (var z = 31; z <= 36; z++) g[z] = z - 18;   // Ga..Kr -> 13..18

            g[37] = 1; g[38] = 2;
            for (var z = 39; z <= 48; z++) g[z] = z - 36;   // Y..Cd  -> 3..12
            for (var z = 49; z <= 54; z++) g[z] = z - 36;   // In..Xe -> 13..18

            g[55] = 1; g[56] = 2;
            // 57..70 lanthanoids: no numbered group, left at 0
            g[71] = 3;                                      // Lu is conventionally group 3
            for (var z = 72; z <= 80; z++) g[z] = z - 68;   // Hf..Hg -> 4..12
            for (var z = 81; z <= 86; z++) g[z] = z - 68;   // Tl..Rn -> 13..18

            g[87] = 1; g[88] = 2;
            // 89..102 actinides: no numbered group
            g[103] = 3;                                     // Lr, group 3
            for (var z = 104; z <= 112; z++) g[z] = z - 100; // Rf..Cn -> 4..12
            for (var z = 113; z <= 118; z++) g[z] = z - 100; // Nh..Og -> 13..18

            return g;
        }

        public static readonly IReadOnlyList<FieldSpec> All =
        [
            // ---- Identity -------------------------------------------------------------------
            Spec("name", "element", FieldKind.Text, FieldCategory.Identity, S.english_name_colon, localized: true),
            Spec("symbol", "short", FieldKind.Text, FieldCategory.Identity, S.symbol_colon),
            Spec("atomic_number", "element_atomic_number", FieldKind.Numeric, FieldCategory.Identity, S.atomic_number_label),
            Spec("description", "description", FieldKind.Text, FieldCategory.Identity, S.description_colon, localized: true),
            Spec("appearance", "element_appearance", FieldKind.Text, FieldCategory.Identity, S.appearance_colon, localized: true),
            Spec("year_discovered", "element_year", FieldKind.Numeric, FieldCategory.Identity, S.year_discovered_colon),
            Spec("discovered_by", "element_discovered_name", FieldKind.Text, FieldCategory.Identity, S.discovered_by_colon),
            Spec("series", "element_group", FieldKind.Enum, FieldCategory.Identity, S.group_label, localized: true),
            Spec("block", "element_block", FieldKind.Enum, FieldCategory.Identity, S.block_colon),

            // Period and group are computed from atomic number, not stored: the data has no
            // element_period or element_group_number key on any of the 118 elements. They are
            // declared here so they can still be asked about, filtered on and cited.
            Spec("period", DerivedPeriod, FieldKind.Numeric, FieldCategory.Identity, S.period_colon),
            Spec("group_number", DerivedGroup, FieldKind.Numeric, FieldCategory.Identity, S.group_number_colon),
            Spec("valence_electrons", DerivedValence, FieldKind.Numeric, FieldCategory.Atomic, S.valence_electrons_colon),
            Spec("synthetic", DerivedSynthetic, FieldKind.Enum, FieldCategory.Identity, S.synthetic_colon),
            Spec("phase", "element_phase", FieldKind.Enum, FieldCategory.Identity, S.phase_stp_colon, localized: true),
            Spec("wikilink", "wikilink", FieldKind.Link, FieldCategory.Identity, S.description_colon),

            // ---- Atomic ---------------------------------------------------------------------
            Spec("atomic_mass", "element_atomicmass", FieldKind.Numeric, FieldCategory.Atomic, S.atomic_mass_colon, Dimension.Mass, "u"),
            Spec("density", "element_density", FieldKind.Numeric, FieldCategory.Atomic, S.density_colon, Dimension.Density, "g/cm³"),
            // The misspelled JSON key is in both apps' data and is not a typo to fix here.
            Spec("electronegativity", "element_electronegativty", FieldKind.Numeric, FieldCategory.Atomic, S.electronegativity_colon, Dimension.Dimensionless),
            Spec("electronegativity_allen", "electronegativity_allen", FieldKind.Numeric, FieldCategory.Atomic, S.electronegativity_allen_colon, Dimension.Dimensionless),
            Spec("electron_configuration", "element_electron_config", FieldKind.Text, FieldCategory.Atomic, S.electron_configuration_colon),
            Spec("electron_shells", "element_shells_electrons", FieldKind.Text, FieldCategory.Atomic, S.electron_shell_colon),
            Spec("electrons", "element_electrons", FieldKind.Numeric, FieldCategory.Atomic, S.element_electrons),
            Spec("protons", "element_protons", FieldKind.Numeric, FieldCategory.Atomic, S.atomic_number_label),
            Spec("atomic_radius", "element_atomic_radius", FieldKind.Numeric, FieldCategory.Atomic, S.atomic_radius_calculated_colon, Dimension.Length, "pm"),
            Spec("atomic_radius_empirical", "element_atomic_radius_e", FieldKind.Numeric, FieldCategory.Atomic, S.atomic_radius_empirical_colon, Dimension.Length, "pm"),
            Spec("covalent_radius", "element_covalent_radius", FieldKind.Numeric, FieldCategory.Atomic, S.covalent_radius_colon, Dimension.Length, "pm"),
            Spec("van_der_waals_radius", "element_van_der_waals", FieldKind.Numeric, FieldCategory.Atomic, S.van_der_waals_radius_colon, Dimension.Length, "pm"),
            Spec("electron_affinity", "electron_affinity", FieldKind.Numeric, FieldCategory.Atomic, S.electron_affinity_colon, Dimension.EnergyPerMol, "kJ/mol", tier: Tier.Pro),
            Spec("work_function", "work_function", FieldKind.Numeric, FieldCategory.Atomic, S.work_function_colon, Dimension.Energy, "eV"),
            Spec("molar_volume", "molar_volume", FieldKind.Numeric, FieldCategory.Atomic, S.molar_volume_colon, Dimension.VolumePerMol, "cm³/mol"),
            Spec("ion_charge", "element_ion_charge", FieldKind.Text, FieldCategory.Atomic, S.ion_charge_colon),
            Spec("oxidation_states", "oxidation_state_pos", FieldKind.Text, FieldCategory.Atomic, S.oxidation_states_colon),
            Spec("oxidation_states_negative", "oxidation_state_neg", FieldKind.Text, FieldCategory.Atomic, S.oxidation_states_colon),

            new FieldSpec(
                "ionization_energy",
                Enumerable.Range(1, 30).Select(i => "element_ionization_energy" + i).ToList(),
                FieldKind.Numeric, FieldCategory.Atomic, S.ionization_energies_colon,
                Dimension.Energy, "eV", DeepLinkTarget.Ion, OrdinalRange: (1, 30)),

            // ---- Thermodynamic --------------------------------------------------------------
            MultiUnit("melting_point", ["element_melting_kelvin", "element_melting_celsius", "element_melting_fahrenheit"], S.melting_point_colon),
            MultiUnit("boiling_point", ["element_boiling_kelvin", "element_boiling_celsius", "element_boiling_fahrenheit"], S.boiling_point_colon),
            MultiUnit("sublimation_point", ["element_sublimation_kelvin", "element_sublimation_celsius", "element_sublimation_fahrenheit"], S.melting_point_colon),
            Spec("fusion_heat", "element_fusion_heat", FieldKind.Numeric, FieldCategory.Thermo, S.fusion_heat_colon, Dimension.EnergyPerMol, "kJ/mol"),
            Spec("vaporization_heat", "element_vaporization_heat", FieldKind.Numeric, FieldCategory.Thermo, S.vaporization_heat_colon, Dimension.EnergyPerMol, "kJ/mol"),
            Spec("specific_heat_capacity", "element_specific_heat_capacity", FieldKind.Numeric, FieldCategory.Thermo, S.specific_heat_capacity_colon, Dimension.HeatCapacity, "J/(g·K)"),
            Spec("molar_heat_capacity", "molar_heat_capacity", FieldKind.Numeric, FieldCategory.Thermo, S.molar_heat_capacity_colon, Dimension.HeatCapacity, "J/(mol·K)"),
            Spec("thermal_conductivity", "thermal_conductivity", FieldKind.Numeric, FieldCategory.Thermo, S.thermal_conductivity_colon, Dimension.Conductivity, "W/(m·K)"),
            // Greek mu, not the micro sign: unit canonicalisation applies NFKC, which folds
            // U+00B5 to U+03BC. Writing the micro sign here would never compare equal.
            Spec("thermal_expansion", "thermal_expansion", FieldKind.Numeric, FieldCategory.Thermo, S.thermal_expansion_colon, Dimension.Dimensionless, "μm/(m·K)"),
            Spec("debye_temperature", "debye_temperature", FieldKind.Struct, FieldCategory.Thermo, S.debye_temperature_room_temperature, Dimension.Temperature, "K"),

            // ---- Electromagnetic ------------------------------------------------------------
            Spec("electrical_type", "electrical_type", FieldKind.Enum, FieldCategory.Electromagnetic, S.electrical_type_colon, localized: true),
            Spec("resistivity", "resistivity", FieldKind.Numeric, FieldCategory.Electromagnetic, S.electrical_resistivity_colon, Dimension.Resistivity, "nΩm"),
            Spec("magnetic_type", "magnetic_type", FieldKind.Enum, FieldCategory.Electromagnetic, S.magnetic_type_colon, localized: true),
            Spec("superconducting_point", "superconducting_point", FieldKind.Numeric, FieldCategory.Electromagnetic, S.superconducting_point_colon, Dimension.Temperature, "K", allowsRange: true),
            Spec("curie_point", "curie_point", FieldKind.Numeric, FieldCategory.Electromagnetic, S.curie_point_colon, Dimension.Temperature, "K", tier: Tier.Pro),
            Spec("neel_point", "neel_point", FieldKind.Numeric, FieldCategory.Electromagnetic, S.neel_point_colon, Dimension.Temperature, "K", tier: Tier.Pro),
            Spec("refractive_index", "refractive_index", FieldKind.Numeric, FieldCategory.Electromagnetic, S.refractive_index_colon, Dimension.Dimensionless, allowsRange: true, tier: Tier.Pro),

            // ---- Crystal --------------------------------------------------------------------
            Spec("crystal_structure", "crystal_structure", FieldKind.Enum, FieldCategory.Crystal, S.crystal_structure),
            Spec("lattice_constants", "lattice_constants", FieldKind.Struct, FieldCategory.Crystal, S.crystal_structure, Dimension.Length, "Å"),
            Spec("space_group_name", "space_group_name", FieldKind.Text, FieldCategory.Crystal, S.space_group_name_colon, tier: Tier.Pro),
            Spec("space_group_number", "space_group_number", FieldKind.Numeric, FieldCategory.Crystal, S.space_group_number_colon, tier: Tier.Pro),

            // ---- Mechanical -----------------------------------------------------------------
            Spec("young_modulus", "young_modulus", FieldKind.Numeric, FieldCategory.Mechanical, S.element_young_modulus, Dimension.Pressure, "GPa", allowsRange: true, tier: Tier.Pro),
            Spec("bulk_modulus", "bulk_modulus", FieldKind.Numeric, FieldCategory.Mechanical, S.element_bulk_modulus, Dimension.Pressure, "GPa", allowsRange: true, tier: Tier.Pro),
            Spec("shear_modulus", "shear_modulus", FieldKind.Numeric, FieldCategory.Mechanical, S.element_shear_modulus, Dimension.Pressure, "GPa", allowsRange: true, tier: Tier.Pro),
            Spec("poisson_ratio", "poisson_ratio", FieldKind.Numeric, FieldCategory.Mechanical, S.element_poisson_ratio, Dimension.Dimensionless, allowsRange: true, deepLink: DeepLinkTarget.Poisson, tier: Tier.Pro),
            Spec("mohs_hardness", "mohs_hardness", FieldKind.Numeric, FieldCategory.Mechanical, S.mohs_hardness_colon, Dimension.Dimensionless, allowsRange: true, tier: Tier.Pro),
            Spec("vickers_hardness", "vickers_hardness", FieldKind.Numeric, FieldCategory.Mechanical, S.vickers_hardness_colon, Dimension.Pressure, "MPa", allowsRange: true, tier: Tier.Pro),
            Spec("brinell_hardness", "brinell_hardness", FieldKind.Numeric, FieldCategory.Mechanical, S.brinell_hardness_colon, Dimension.Pressure, "MPa", allowsRange: true, tier: Tier.Pro),
            Spec("speed_of_sound_solid", "speed_of_sound_solid", FieldKind.Numeric, FieldCategory.Mechanical, S.element_speed_sound, Dimension.Velocity, "m/s", allowsRange: true, tier: Tier.Pro),
            Spec("speed_of_sound_liquid", "speed_of_sound_liquid", FieldKind.Numeric, FieldCategory.Mechanical, S.element_speed_sound, Dimension.Velocity, "m/s", allowsRange: true, tier: Tier.Pro),
            Spec("speed_of_sound_gas", "speed_of_sound_gas", FieldKind.Numeric, FieldCategory.Mechanical, S.element_speed_sound, Dimension.Velocity, "m/s", allowsRange: true, tier: Tier.Pro),

            // ---- Nuclear --------------------------------------------------------------------
            Spec("radioactive", "radioactive", FieldKind.Enum, FieldCategory.Nuclear, S.radioactive_colon),
            Spec("neutron_cross_section", "neutron_cross_sectional", FieldKind.Numeric, FieldCategory.Nuclear, S.neutron_cross_sectional_colon, Dimension.Area, "b", deepLink: DeepLinkTarget.Nuclide),
            Spec("common_neutrons", "element_neutron_common", FieldKind.Numeric, FieldCategory.Nuclear, S.isotopes_colon, deepLink: DeepLinkTarget.Nuclide),

            // ---- Abundance ------------------------------------------------------------------
            // Every abundance label is a table heading phrased as a prepositional phrase - "In the
            // Earth's crust", "I jordskorpan" - so each carries a sentence form as well.
            //
            // The units are taken from the element info screen, which is the app's source of truth
            // for them, and they are not all the same. Stamping mg/kg on all nine - as this table
            // used to - labelled sea water and the two solar reservoirs with a unit they are not
            // measured in.
            Abundance("abundance_earth_crust", "earth_crust", S.abundance_earth_crust, S.ai_field_abundance_earth_crust),
            Abundance("abundance_earth_soils", "earth_soils", S.abundance_earth_soils, S.ai_field_abundance_earth_soils),
            Abundance("abundance_urban_soils", "urban_soils", S.abundance_urban_soil, S.ai_field_abundance_urban_soils),
            Abundance("abundance_sea_water", "sea_water", S.abundance_sea_water, S.ai_field_abundance_sea_water,
                canonicalUnit: UnitMicrogramPerLitre, unitLabelKey: S.ai_unit_ug_per_l),
            Abundance("abundance_crustal_rocks", "crustal_rocks", S.abundance_crustal_rocks, S.ai_field_abundance_crustal_rocks),
            Abundance("abundance_sun", "sun", S.abundance_sun, S.ai_field_abundance_sun,
                dimension: Dimension.Dimensionless, canonicalUnit: UnitAtomsPerSilicon, unitLabelKey: S.ai_unit_atoms_per_si),
            Abundance("abundance_solar_system", "solar_system", S.abundance_solar_system, S.ai_field_abundance_solar_system,
                dimension: Dimension.Dimensionless, canonicalUnit: UnitAtomsPerSilicon, unitLabelKey: S.ai_unit_atoms_per_si),
            // Meteorite and human-body values author their own unit - "8400 mg/kg", "65% (by mass)" -
            // so the label is only a fallback for the rare cell that does not.
            Abundance("abundance_meteorites", "meteorites", S.abundance_meteorites, S.ai_field_abundance_meteorites),
            Abundance("abundance_human_body", "human_body", S.abundance_human_body, S.ai_field_abundance_human_body),

            // ---- Safety (NFPA 704) ----------------------------------------------------------
            // Labels, not sentence templates. These four used to point at ai_safety_health and its
            // siblings - "• **Health:** %1$d/4 (%2$s)" - which a label lookup resolves with no
            // arguments, so the placeholders reached the screen verbatim. nfpa_special pointed at
            // the *health* template on top of that, so it was mislabelled as well as malformed.
            Spec("nfpa_health", "health", FieldKind.Numeric, FieldCategory.Safety, S.ai_field_nfpa_health, tier: Tier.Pro),
            Spec("nfpa_flammability", "flammability", FieldKind.Numeric, FieldCategory.Safety, S.ai_field_nfpa_flammability, tier: Tier.Pro),
            Spec("nfpa_instability", "instability", FieldKind.Numeric, FieldCategory.Safety, S.ai_field_nfpa_instability, tier: Tier.Pro),
            Spec("nfpa_special", "special", FieldKind.Text, FieldCategory.Safety, S.ai_field_nfpa_special, tier: Tier.Pro),

            // ---- Identifiers ----------------------------------------------------------------
            Spec("cas_number", "cas_number", FieldKind.Id, FieldCategory.Ids, S.cas_number_colon),
            Spec("eg_number", "eg_number", FieldKind.Id, FieldCategory.Ids, S.eg_number_colon),
        ];

        public static readonly FrozenDictionary<string, FieldSpec> ById =
            All.ToFrozenDictionary(s => s.Id, StringComparer.Ordinal);

        /// <summary>Reverse lookup from any backing JSON key to its spec.</summary>
        public static readonly FrozenDictionary<string, FieldSpec> ByJsonKey =
            All.SelectMany(spec => spec.JsonKeys.Select(k => (Key: k, Spec: spec)))
               .ToFrozenDictionary(p => p.Key, p => p.Spec, StringComparer.Ordinal);

        /// <summary>Fields that can be ranked, filtered and aggregated.</summary>
        public static readonly IReadOnlyList<FieldSpec> NumericFields =
            All.Where(s => s.Kind == FieldKind.Numeric).ToList();

        public static IReadOnlyList<FieldSpec> ByCategory(FieldCategory category) =>
            All.Where(s => s.Category == category).ToList();

        // -- builders --------------------------------------------------------------------------

        private static FieldSpec Spec(
            string id,
            string jsonKey,
            FieldKind kind,
            FieldCategory category,
            string labelKey,
            Dimension? dimension = null,
            string? canonicalUnit = null,
            DeepLinkTarget deepLink = DeepLinkTarget.ElementInfo,
            bool allowsRange = false,
            bool localized = false,
            bool higherIsMore = true,
            Tier tier = Tier.Free) =>
            new(id, [jsonKey], kind, category, labelKey, dimension, canonicalUnit,
                deepLink, allowsRange, localized, higherIsMore, Tier: tier);

        /// <summary>Temperature fields that ship all three scales, so a unit request needs no conversion.</summary>
        private static FieldSpec MultiUnit(string id, IReadOnlyList<string> jsonKeys, string labelKey) =>
            new(id, jsonKeys, FieldKind.Numeric, FieldCategory.Thermo, labelKey,
                Dimension.Temperature, "K");

        /// <summary>
        /// An abundance reservoir.
        ///
        /// <para>The nine reservoirs do <b>not</b> share a unit, which is why this takes one per
        /// field rather than stamping mg/kg on all of them: sea water is µg/l, the sun and solar
        /// system are atom counts against silicon, and the human-body figures are authored as
        /// either mg/kg or a percentage. Every one of them is stored as a bare number in at least
        /// some elements, so they all set <see cref="FieldSpec.SurfaceUnit"/>.</para>
        /// </summary>
        private static FieldSpec Abundance(
            string id,
            string jsonKey,
            string labelKey,
            string? sentenceLabelKey = null,
            Dimension dimension = Dimension.Concentration,
            string canonicalUnit = "mg/kg",
            string? unitLabelKey = null) =>
            new(id, [jsonKey], FieldKind.Numeric, FieldCategory.Abundance, labelKey,
                dimension, canonicalUnit,
                SentenceLabelKey: sentenceLabelKey,
                UnitLabelKey: unitLabelKey ?? S.ai_unit_mg_per_kg,
                SurfaceUnit: true);
    }
}
