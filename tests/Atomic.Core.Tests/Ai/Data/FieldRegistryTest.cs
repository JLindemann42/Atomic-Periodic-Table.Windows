using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>Ported from <c>FieldRegistryTest.kt</c>, plus checks against the shipped data.</summary>
    public class FieldRegistryTest
    {
        [Fact]
        public void FieldIdsAreUnique()
        {
            var duplicates = FieldRegistry.All
                .GroupBy(s => s.Id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            Assert.True(duplicates.Count == 0, "duplicate field ids: " + string.Join(", ", duplicates));
        }

        [Fact]
        public void EveryFieldHasALabelAndAtLeastOneJsonKey()
        {
            foreach (var spec in FieldRegistry.All)
            {
                Assert.False(string.IsNullOrEmpty(spec.LabelKey), $"{spec.Id} has no label key");
                Assert.NotEmpty(spec.JsonKeys);
            }
        }

        [Fact]
        public void NumericFieldsDeclareTheirDimension()
        {
            var missing = FieldRegistry.NumericFields
                .Where(s => s.CanonicalUnit is not null && s.Dimension is null)
                .Select(s => s.Id)
                .ToList();
            Assert.True(missing.Count == 0,
                "numeric fields with a unit but no dimension: " + string.Join(", ", missing));
        }

        [Fact]
        public void RangesAreOnlyAllowedWhereAHyphenMeansTo()
        {
            // Elsewhere a hyphen is a minus sign; a wrong flag makes values silently wrong, not
            // missing - a melting point of -7.2 would parse as the range 7 to 2.
            var expected = new HashSet<string>
            {
                "young_modulus", "bulk_modulus", "shear_modulus", "poisson_ratio",
                "mohs_hardness", "vickers_hardness", "brinell_hardness",
                "speed_of_sound_solid", "speed_of_sound_liquid", "speed_of_sound_gas",
                "refractive_index", "superconducting_point",
            };
            Assert.Equal(expected, FieldRegistry.All.Where(s => s.AllowsRange).Select(s => s.Id).ToHashSet());
        }

        [Fact]
        public void TemperatureFieldsCarryAllThreeScales() =>
            Assert.Equal(
                new[] { "element_melting_kelvin", "element_melting_celsius", "element_melting_fahrenheit" },
                FieldRegistry.ById["melting_point"].JsonKeys);

        [Fact]
        public void IonizationEnergyIsOneBankedFieldNotThirty()
        {
            var spec = FieldRegistry.ById["ionization_energy"];
            Assert.Equal((1, 30), spec.OrdinalRange);
            Assert.Equal("element_ionization_energy1", spec.JsonKeyForOrdinal(null));
            Assert.Equal("element_ionization_energy2", spec.JsonKeyForOrdinal(2));
            Assert.Equal("element_ionization_energy30", spec.JsonKeyForOrdinal(30));
            // Out-of-range slots clamp rather than producing a key that does not exist.
            Assert.Equal("element_ionization_energy30", spec.JsonKeyForOrdinal(99));
            Assert.Equal("element_ionization_energy1", spec.JsonKeyForOrdinal(0));
        }

        [Fact]
        public void ReverseLookupFromJsonKeyWorks()
        {
            Assert.Equal("density", FieldRegistry.ByJsonKey["element_density"].Id);
            Assert.Equal("melting_point", FieldRegistry.ByJsonKey["element_melting_celsius"].Id);
            // The misspelled key is what the data actually uses, in both apps.
            Assert.Equal("electronegativity", FieldRegistry.ByJsonKey["element_electronegativty"].Id);
        }

        // ---- derived period / group / valence ------------------------------------------------

        [Theory]
        [InlineData(1, 1)]    // H
        [InlineData(2, 1)]    // He
        [InlineData(3, 2)]    // Li
        [InlineData(10, 2)]   // Ne
        [InlineData(18, 3)]   // Ar
        [InlineData(36, 4)]   // Kr
        [InlineData(54, 5)]   // Xe
        [InlineData(86, 6)]   // Rn
        [InlineData(118, 7)]  // Og
        public void PeriodsFollowTheRowBoundaries(int z, int period) =>
            Assert.Equal(period, FieldRegistry.PeriodOf(z));

        [Theory]
        [InlineData(1, 1)]    // H
        [InlineData(2, 18)]   // He
        [InlineData(3, 1)]    // Li
        [InlineData(4, 2)]    // Be
        [InlineData(6, 14)]   // C
        [InlineData(8, 16)]   // O
        [InlineData(17, 17)]  // Cl
        [InlineData(18, 18)]  // Ar
        [InlineData(21, 3)]   // Sc
        [InlineData(24, 6)]   // Cr
        [InlineData(29, 11)]  // Cu
        [InlineData(30, 12)]  // Zn
        [InlineData(47, 11)]  // Ag
        [InlineData(79, 11)]  // Au
        [InlineData(80, 12)]  // Hg
        [InlineData(82, 14)]  // Pb
        [InlineData(86, 18)]  // Rn
        [InlineData(104, 4)]  // Rf
        [InlineData(118, 18)] // Og
        public void GroupsMatchThePeriodicTableColumns(int z, int group) =>
            Assert.Equal(group, FieldRegistry.GroupOf(z));

        [Fact]
        public void FBlockElementsHaveNoNumberedGroup()
        {
            Assert.Null(FieldRegistry.GroupOf(58));   // Ce, lanthanoid
            Assert.Null(FieldRegistry.GroupOf(64));   // Gd, lanthanoid
            Assert.Null(FieldRegistry.GroupOf(92));   // U, actinide
            // Lu and Lr are conventionally placed in group 3.
            Assert.Equal(3, FieldRegistry.GroupOf(71));
            Assert.Equal(3, FieldRegistry.GroupOf(103));
        }

        [Fact]
        public void ValenceElectronsOnlyWhereTheConceptApplies()
        {
            Assert.Equal(1, FieldRegistry.ValenceElectrons(1));    // H, group 1
            Assert.Equal(2, FieldRegistry.ValenceElectrons(2));    // He: group 18 but only 2 electrons
            Assert.Equal(4, FieldRegistry.ValenceElectrons(6));    // C, group 14
            Assert.Equal(8, FieldRegistry.ValenceElectrons(18));   // Ar, group 18
            Assert.Null(FieldRegistry.ValenceElectrons(26));       // Fe, d-block
            Assert.Null(FieldRegistry.ValenceElectrons(64));       // Gd, f-block
        }

        [Fact]
        public void SyntheticCoversTheTwoGapsAndEverythingAboveUranium()
        {
            // Without this, "the heaviest naturally occurring element" answers tennessine, which
            // has only ever existed a few atoms at a time in an accelerator.
            Assert.True(FieldRegistry.IsSynthetic(43));    // Tc
            Assert.True(FieldRegistry.IsSynthetic(61));    // Pm
            Assert.True(FieldRegistry.IsSynthetic(93));    // Np
            Assert.True(FieldRegistry.IsSynthetic(117));   // Ts
            Assert.False(FieldRegistry.IsSynthetic(92));   // U
            Assert.False(FieldRegistry.IsSynthetic(26));   // Fe
        }

        // ---- agreement with the shipped data --------------------------------------------------

        [Fact]
        public void EveryNonDerivedJsonKeyExistsOnAtLeastOneElement()
        {
            // A key that matches nothing is a field the agent can be asked about and can never
            // answer. Banked ionization slots are excluded: the data stops well short of 30.
            var present = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var row in TestAssets.English.Rows.Values)
                foreach (var key in row.Keys)
                    present.Add(key);

            var orphans = FieldRegistry.All
                .Where(s => s.OrdinalRange is null)
                .SelectMany(s => s.JsonKeys)
                .Where(k => !k.StartsWith("__derived", System.StringComparison.Ordinal))
                .Distinct(System.StringComparer.Ordinal)
                .Where(k => !present.Contains(k))
                .ToList();

            Assert.True(orphans.Count == 0,
                "field keys absent from every element: " + string.Join(", ", orphans));
        }

        [Fact]
        public void GroupAndPeriodAreNotInTheDataAtAll()
        {
            // The premise for deriving them. If a future data sync adds these keys, the derivation
            // should be revisited rather than silently shadowed.
            foreach (var key in new[] { "element_period", "element_group_number" })
            {
                var count = TestAssets.English.Rows.Values.Count(r => r.ContainsKey(key));
                Assert.Equal(0, count);
            }
        }

        [Fact]
        public void ProGatedFieldsMatchTheElementScreen()
        {
            // The agent has to gate the same set the element screen does, or it becomes a way
            // around the paywall.
            var pro = FieldRegistry.All.Where(s => s.Tier == Tier.Pro).Select(s => s.Id).ToHashSet();
            foreach (var id in new[]
            {
                "young_modulus", "bulk_modulus", "shear_modulus", "poisson_ratio",
                "mohs_hardness", "vickers_hardness", "brinell_hardness",
                "speed_of_sound_solid", "speed_of_sound_liquid", "speed_of_sound_gas",
                "electron_affinity", "curie_point", "neel_point",
                "space_group_name", "space_group_number", "refractive_index",
            })
            {
                Assert.Contains(id, pro);
            }
        }

        [Fact]
        public void AbundanceReservoirsDoNotAllShareAUnit()
        {
            // Stamping mg/kg on all nine labelled sea water and the solar reservoirs with a unit
            // they are not measured in.
            Assert.Equal(FieldRegistry.UnitMicrogramPerLitre,
                FieldRegistry.ById["abundance_sea_water"].CanonicalUnit);
            Assert.Equal(FieldRegistry.UnitAtomsPerSilicon,
                FieldRegistry.ById["abundance_sun"].CanonicalUnit);
            Assert.Equal("mg/kg", FieldRegistry.ById["abundance_earth_crust"].CanonicalUnit);

            // All nine surface their unit, because every one is a bare number somewhere.
            foreach (var spec in FieldRegistry.ByCategory(FieldCategory.Abundance))
            {
                Assert.True(spec.SurfaceUnit, $"{spec.Id} must surface its unit");
                Assert.NotNull(spec.SentenceLabelKey);
            }
        }

        [Fact]
        public void DisplayWithUnitNeverDoublesAnAuthoredUnit()
        {
            var crust = FieldRegistry.ById["abundance_earth_crust"];
            string Label(string key) => "mg/kg";

            // Authored: the unit is already inside Display.
            var authored = new Quantity(8400, Display: "8400 mg/kg", UnitAuthored: true);
            Assert.Equal("8400 mg/kg", crust.DisplayWithUnit(authored, Label));

            // Bare: the caller has to add it, or the figure means nothing.
            var bare = new Quantity(80500, Display: "80500", UnitAuthored: false);
            Assert.Equal("80500 mg/kg", crust.DisplayWithUnit(bare, Label));
        }

        [Fact]
        public void AnUnresolvedUnitLabelNeverPrints()
        {
            // The test provider resolves an unknown key to "str:<key>"; that must not reach a user.
            var crust = FieldRegistry.ById["abundance_earth_crust"];
            var bare = new Quantity(80500, Display: "80500");
            Assert.Equal("80500", crust.DisplayWithUnit(bare, key => "str:" + key));
            Assert.Equal("80500", crust.DisplayWithUnit(bare, _ => ""));
        }

        [Fact]
        public void LabelKeysAllResolveInEnglish()
        {
            // A label key with no resource would print as the raw key in the UI.
            var strings = new ReswStringProvider("en");
            var unresolved = new List<string>();
            foreach (var spec in FieldRegistry.All)
            {
                if (!strings.HasOwn(spec.LabelKey)) unresolved.Add($"{spec.Id}.label={spec.LabelKey}");
                if (spec.SentenceLabelKey is { } s && !strings.HasOwn(s)) unresolved.Add($"{spec.Id}.sentence={s}");
                if (spec.UnitLabelKey is { } u && !strings.HasOwn(u)) unresolved.Add($"{spec.Id}.unit={u}");
            }
            Assert.True(unresolved.Count == 0, string.Join("\n  ", unresolved));
        }
    }
}
