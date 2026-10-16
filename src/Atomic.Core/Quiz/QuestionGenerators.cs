using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Localization;

namespace Atomic.Core.Quiz
{
    /// <summary>
    /// Every category's generator.
    ///
    /// <para><b>All 47 categories are served here.</b> Android registers 14 and leaves the other 33
    /// to a ~300-line <c>when</c> block inside the quiz activity, which reads raw JSON strings and
    /// so cannot rank, compare or de-duplicate. The 14 purpose-built generators are kept as they
    /// are; the rest become <see cref="FieldLookupGenerator"/> entries, which is a table rather
    /// than a branch - a new field is a new line.</para>
    /// </summary>
    public static class QuestionGenerators
    {
        /// <summary>
        /// Free-tier numeric fields with coverage above ~85 of 118 elements, where "which is
        /// highest" is a real question.
        ///
        /// <para>Excluded on purpose: <c>atomic_number</c>, <c>protons</c>, <c>electrons</c>,
        /// <c>period</c>, <c>group_number</c>, <c>year_discovered</c> and <c>valence_electrons</c> -
        /// ordering them is either trivial from the periodic table or meaningless. This mirrors
        /// <c>QueryExecutor</c>'s unrankable fields.</para>
        /// </summary>
        private static readonly IReadOnlyList<RankableField> BasicFields =
        [
            new("atomic_mass", S.question_highest_atomic_mass, S.question_lowest_atomic_mass),
            new("density", S.question_highest_density, S.question_lowest_density),
            new("melting_point", S.question_highest_melting_point, S.question_lowest_melting_point),
            new("boiling_point", S.question_highest_boiling_point, S.question_lowest_boiling_point),
            new("electronegativity", S.question_highest_electronegativity, S.question_lowest_electronegativity),
            new("fusion_heat", S.question_highest_fusion_heat, S.question_lowest_fusion_heat),
            new("vaporization_heat", S.question_highest_vaporization_heat, S.question_lowest_vaporization_heat),
            new("thermal_conductivity", S.question_highest_thermal_conductivity, S.question_lowest_thermal_conductivity),
            new("molar_volume", S.question_largest_molar_volume, S.question_smallest_molar_volume),
            new("atomic_radius", S.question_largest_atomic_radius, S.question_smallest_atomic_radius),
        ];

        /// <summary>
        /// Tier.Pro fields. Categories built on these must stay PRO+ in the catalogue, otherwise the
        /// quiz hands out data the AI engine gates.
        /// </summary>
        private static readonly IReadOnlyList<RankableField> AdvancedFields =
        [
            new("mohs_hardness", S.question_highest_mohs_hardness, S.question_lowest_mohs_hardness),
            new("vickers_hardness", S.question_highest_vickers_hardness, S.question_lowest_vickers_hardness),
            new("brinell_hardness", S.question_highest_brinell_hardness, S.question_lowest_brinell_hardness),
            new("young_modulus", S.question_highest_young_modulus, S.question_lowest_young_modulus),
            new("bulk_modulus", S.question_highest_bulk_modulus, S.question_lowest_bulk_modulus),
            new("shear_modulus", S.question_highest_shear_modulus, S.question_lowest_shear_modulus),
            new("speed_of_sound_solid", S.question_highest_speed_of_sound, S.question_lowest_speed_of_sound),
        ];

        private static readonly IReadOnlyList<IQuestionGenerator> All =
        [
            new SuperlativeGenerator("superlative_highest", 90, BasicFields, descending: true),
            new SuperlativeGenerator("superlative_lowest", 90, BasicFields, descending: false),
            new SuperlativeGenerator("superlative_advanced", 110, AdvancedFields, descending: null),
            // Two options rather than four, and either direction, so it plays as a head-to-head.
            new SuperlativeGenerator("compare_pair", 60, BasicFields, descending: null, optionCount: 2),
            new OddOneOutGenerator("odd_one_out_series", 20),
            // Takes over the existing category: the raw JSON spells several classifications two
            // ways, which the old lookup offered as separate answers.
            new ClassificationGenerator("element_classifications", 13),
            new PositionGenerator("periodic_period", 40, PositionAspect.Period),
            new PositionGenerator("periodic_group", 50, PositionAspect.Group),
            new PositionGenerator("valence_electrons", 60, PositionAspect.Valence),
            // XP for these two sits in the band their box already uses rather than the end-game
            // rate, because they are placed mid-curriculum.
            new MolarMassGenerator("molar_mass", 60),
            new IsotopeNeutronGenerator("isotope_neutrons", 75),
            new IsotopeStabilityGenerator("isotope_stability", 100),
            new IsotopeHalfLifeGenerator("isotope_half_life", 110),
            new IsotopeDecayGenerator("isotope_decay", 110),

            // ---- Field lookups -------------------------------------------------------------
            //
            // The 33 categories Android leaves to its activity's when-block. XP rises roughly with
            // the level box the category sits in, matching the bands already in the curriculum.

            new FieldLookupGenerator("element_symbols", 10, "symbol", S.question_element_symbol),
            new FieldLookupGenerator("element_names", 10, "name", S.question_element_name),
            new FieldLookupGenerator("discovered_by", 15, "discovered_by", S.question_discovered_by),
            new FieldLookupGenerator("discovery_year", 15, "year_discovered", S.question_discovery_year),

            new FieldLookupGenerator("appearance", 20, "appearance", S.question_appearance),
            new FieldLookupGenerator("atomic_number", 20, "atomic_number", S.question_atomic_number),
            new FieldLookupGenerator("electrical_type", 25, "electrical_type", S.question_electrical_type),
            new FieldLookupGenerator("radioactive", 25, "radioactive", S.question_radioactive),

            new FieldLookupGenerator("atomic_mass", 30, "atomic_mass", S.question_atomic_mass),
            new FieldLookupGenerator("density", 30, "density", S.question_density),
            new FieldLookupGenerator("electronegativity", 35, "electronegativity", S.question_electronegativity),
            new FieldLookupGenerator("block", 35, "block", S.question_block),

            new FieldLookupGenerator("magnetic_type", 40, "magnetic_type", S.question_magnetic_type),
            new FieldLookupGenerator("phase_stp", 40, "phase", S.question_phase_stp),
            new FieldLookupGenerator("crystal_structure", 45, "crystal_structure", S.question_crystal_structure),
            new FieldLookupGenerator("superconducting_point", 45, "superconducting_point", S.question_superconducting_point),

            new FieldLookupGenerator("neutron_cross_sectional", 50, "neutron_cross_section", S.question_neutron_cross_sectional),
            new FieldLookupGenerator("specific_heat_capacity", 50, "specific_heat_capacity", S.question_specific_heat_capacity),
            new FieldLookupGenerator("mohs_hardness", 55, "mohs_hardness", S.question_mohs_hardness),
            new FieldLookupGenerator("vickers_hardness", 55, "vickers_hardness", S.question_vickers_hardness),
            new FieldLookupGenerator("brinell_hardness", 55, "brinell_hardness", S.question_brinell_hardness),

            // The temperature categories ask for one unit each, but melting_point and
            // boiling_point are single multi-key fields - their Kelvin/Celsius/Fahrenheit variants
            // live together under one id. The unit is therefore a conversion, not a second field:
            // the generator converts the canonical value and states the unit it asked in.
            new FieldLookupGenerator("element_boiling_kelvin", 60, "boiling_point", S.question_boiling_point_kelvin, "K"),
            new FieldLookupGenerator("element_boiling_celsius", 60, "boiling_point", S.question_boiling_point_celsius, "°C"),
            new FieldLookupGenerator("element_boiling_fahrenheit", 60, "boiling_point", S.question_boiling_point_fahrenheit, "°F"),
            new FieldLookupGenerator("element_melting_kelvin", 60, "melting_point", S.question_melting_point_kelvin, "K"),
            new FieldLookupGenerator("element_melting_celsius", 60, "melting_point", S.question_melting_point_celsius, "°C"),
            new FieldLookupGenerator("element_melting_fahrenheit", 60, "melting_point", S.question_melting_point_fahrenheit, "°F"),

            new FieldLookupGenerator("earth_crust", 70, "abundance_earth_crust", S.question_earth_crust_abundance),
            new FieldLookupGenerator("earth_soils", 70, "abundance_earth_soils", S.question_earth_soils_abundance),

            new FieldLookupGenerator("nfpa_health", 80, "nfpa_health", S.question_nfpa_health),
            new FieldLookupGenerator("nfpa_flammability", 80, "nfpa_flammability", S.question_nfpa_flammability),
            new FieldLookupGenerator("nfpa_instability", 80, "nfpa_instability", S.question_nfpa_instability),
            new FieldLookupGenerator("nfpa_diamond", 85, "nfpa_special", S.question_nfpa_diamond),
        ];

        private static readonly FrozenDictionary<string, IQuestionGenerator> ByKey =
            All.ToFrozenDictionary(g => g.CategoryKey, StringComparer.Ordinal);

        /// <summary>Category keys served by a generator. Used to check the curriculum wiring.</summary>
        public static IReadOnlySet<string> Keys => ByKey.Keys.ToFrozenSet(StringComparer.Ordinal);

        /// <summary>Categories that rank Tier.Pro fields and so must stay PRO+ in the curriculum.</summary>
        public static readonly FrozenSet<string> ProFieldKeys =
            new[] { "superlative_advanced" }.ToFrozenSet(StringComparer.Ordinal);

        public static IQuestionGenerator? ForCategory(string key) =>
            ByKey.TryGetValue(key, out var generator) ? generator : null;
    }
}
