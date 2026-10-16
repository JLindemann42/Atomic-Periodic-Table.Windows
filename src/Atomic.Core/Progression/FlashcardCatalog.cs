using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Localization;

namespace Atomic.Core.Progression
{
    /// <summary>A level range, inclusive at both ends.</summary>
    public sealed record LevelRange(int First, int Last)
    {
        public bool Contains(int level) => level >= First && level <= Last;
    }

    public sealed record CategorySpec(string Key, string LabelKey, bool IsPro = false);

    public sealed record LevelBoxSpec(LevelRange Range, IReadOnlyList<CategorySpec> Categories);

    /// <summary>
    /// An exam sits directly after the level box that starts at <see cref="UnlockLevel"/> and mixes
    /// questions from every category up to and including that box.
    /// </summary>
    public sealed record ExamSpec(string Key, int UnlockLevel, bool IsPro);

    /// <summary>
    /// The flashcard curriculum: the level boxes, the categories inside them, and the exams that sit
    /// between some of the boxes.
    ///
    /// <para>Both the flashcard screen (which draws the boxes) and the quiz page (which has to know
    /// which categories an exam draws from) read this, so the two cannot drift apart.</para>
    /// </summary>
    public static class FlashcardCatalog
    {
        /// <summary>Bonus XP for finishing an exam, on top of the per-question and finish rewards.</summary>
        public const int ExamCompletionXp = 75;

        /// <summary>Share of correct answers needed for an exam to count as passed.</summary>
        public const int ExamPassPercent = 60;

        public static readonly IReadOnlyList<LevelBoxSpec> LevelBoxes =
        [
            new(new(0, 4), [
                new("element_symbols", S.element_symbols),
                new("element_names", S.element_names),
                new("element_classifications", S.element_groups),
                new("odd_one_out_series", S.game_odd_one_out),
                new("discovered_by", S.discovered_by, IsPro: true),
                new("discovery_year", S.discovery_year, IsPro: true),
            ]),
            new(new(5, 9), [
                new("appearance", S.appearance),
                new("atomic_number", S.atomic_number),
                new("periodic_period", S.game_period),
                new("electrical_type", S.electrical_type, IsPro: true),
                new("radioactive", S.radioactive, IsPro: true),
                new("periodic_group", S.game_group, IsPro: true),
            ]),
            new(new(10, 14), [
                new("atomic_mass", S.atomic_mass),
                new("density", S.density),
                new("compare_pair", S.game_compare_pair),
                new("electronegativity", S.electronegativity, IsPro: true),
                new("block", S.block, IsPro: true),
                new("valence_electrons", S.game_valence_electrons, IsPro: true),
            ]),
            new(new(15, 19), [
                new("magnetic_type", S.magnetic_type),
                new("phase_stp", S.phase_stp),
                new("crystal_structure", S.crystal_structure, IsPro: true),
                new("superconducting_point", S.superconducting_point, IsPro: true),
            ]),
            new(new(20, 24), [
                new("neutron_cross_sectional", S.neutron_cross_sectional),
                new("specific_heat_capacity", S.specific_heat_capacity),
                new("molar_mass", S.game_molar_mass),
                new("mohs_hardness", S.mohs_hardness, IsPro: true),
                new("vickers_hardness", S.vickers_hardness, IsPro: true),
                new("brinell_hardness", S.brinell_hardness, IsPro: true),
            ]),
            new(new(25, 29), [
                new("element_boiling_celsius", S.boiling_point_celsius),
                new("element_boiling_fahrenheit", S.boiling_point_fahrenheit),
                new("element_boiling_kelvin", S.boiling_point_kelvin),
                new("isotope_neutrons", S.game_isotope_neutrons),
                new("element_melting_celsius", S.melting_point_celsius, IsPro: true),
                new("element_melting_fahrenheit", S.melting_point_fahrenheit, IsPro: true),
                new("element_melting_kelvin", S.melting_point_kelvin, IsPro: true),
            ]),
            new(new(30, 34), [
                new("earth_crust", S.abundance_earth_crust),
                new("earth_soils", S.abundance_earth_soils, IsPro: true),
            ]),
            new(new(35, 39), [
                new("nfpa_health", S.hazard_health),
                new("nfpa_flammability", S.hazard_flammability),
                new("nfpa_instability", S.hazard_instability, IsPro: true),
                new("nfpa_diamond", S.hazard_diamond, IsPro: true),
            ]),
            // Comparison games. These rank elements against each other rather than asking for one
            // element's value, so they sit above the single-property boxes.
            new(new(40, 44), [
                new("superlative_highest", S.game_highest),
                new("superlative_lowest", S.game_lowest, IsPro: true),
                // Ranks Tier.Pro fields, so this must stay PRO+ or the quiz leaks gated data.
                new("superlative_advanced", S.game_extremes_advanced, IsPro: true),
            ]),
            new(new(45, 49), [
                new("isotope_stability", S.game_isotope_stability),
                new("isotope_half_life", S.game_isotope_half_life, IsPro: true),
                new("isotope_decay", S.game_isotope_decay, IsPro: true),
            ]),
        ];

        /// <summary>
        /// All exams are PRO+: every one mixes in categories that are PRO+ on their own, so a free
        /// exam would hand out paid content.
        /// </summary>
        public static readonly IReadOnlyList<ExamSpec> Exams =
        [
            new("exam_5", 5, IsPro: true),
            new("exam_15", 15, IsPro: true),
            new("exam_25", 25, IsPro: true),
            new("exam_35", 35, IsPro: true),
            new("exam_45", 45, IsPro: true),
        ];

        /// <summary>Every category in the curriculum, in curriculum order.</summary>
        public static IReadOnlyList<CategorySpec> AllCategories =>
            LevelBoxes.SelectMany(b => b.Categories).ToList();

        /// <summary>The exam drawn below the box covering <paramref name="range"/>, if there is one.</summary>
        public static ExamSpec? ExamAfterBox(LevelRange range) =>
            Exams.FirstOrDefault(e => e.UnlockLevel == range.First);

        public static ExamSpec? ExamFor(string key) =>
            Exams.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));

        public static bool IsExam(string key) => ExamFor(key) is not null;

        private static IReadOnlyList<LevelBoxSpec> BoxesCoveredBy(ExamSpec exam) =>
            LevelBoxes.Where(b => b.Range.First <= exam.UnlockLevel).ToList();

        /// <summary>Every category the exam can ask about: all boxes up to and including the one above it.</summary>
        public static IReadOnlyList<CategorySpec> CategoriesForExam(ExamSpec exam) =>
            BoxesCoveredBy(exam).SelectMany(b => b.Categories).ToList();

        /// <summary>Highest level the exam covers, for the "Levels 0-9" subtitle.</summary>
        public static int TopLevelForExam(ExamSpec exam) =>
            BoxesCoveredBy(exam).Max(b => b.Range.Last);

        /// <summary>
        /// How many questions an exam asks. Longer than a single-category game, since it covers more.
        ///
        /// <para>Scaled by breadth: the last exam sweeps roughly four times as many categories as
        /// the first, and at a flat length it would sample barely half of what it claims to
        /// cover.</para>
        /// </summary>
        public static int ExamQuestionCount(ExamSpec exam, string difficulty)
        {
            var baseCount = difficulty switch
            {
                "medium" => 20,
                "hard" => 30,
                _ => 12,
            };

            var breadth = CategoriesForExam(exam).Count;
            var smallest = CategoriesForExam(Exams[0]).Count;
            return baseCount + baseCount * Math.Max(breadth - smallest, 0) / 60;
        }
    }
}
