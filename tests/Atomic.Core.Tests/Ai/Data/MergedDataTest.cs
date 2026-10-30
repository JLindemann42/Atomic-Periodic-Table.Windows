using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// Guards the outcome of the P1 element-data merge (tools/data/merge_elements.py).
    ///
    /// <para>Each of these was verified by hand against the merge report before the data was
    /// committed. They are pinned here because the merge is re-runnable: a future sync from
    /// Android that changes a rule would otherwise silently undo a correction.</para>
    /// </summary>
    public class MergedDataTest
    {
        private static readonly string[] V5Fields =
        [
            "curie_point", "electron_affinity", "electronegativity_allen", "human_body",
            "meteorites", "molar_heat_capacity", "molar_volume", "neel_point",
            "refractive_index", "space_group_name", "space_group_number",
            "thermal_conductivity", "thermal_expansion", "work_function",
        ];

        [Fact]
        public void AllFourteenV5PropertiesPresentOnEveryElement()
        {
            var missing = new List<string>();
            foreach (var (key, row) in TestAssets.English.Rows)
                missing.AddRange(V5Fields.Where(f => !row.ContainsKey(f)).Select(f => $"{key}.{f}"));

            Assert.Empty(missing);
        }

        [Fact]
        public void WindowsOnlyFieldsSurvivedTheMerge()
        {
            var rows = TestAssets.English.Rows;
            Assert.Equal("7439-89-6", rows["iron"]["cas_number"]);
            Assert.Equal("231-096-4", rows["iron"]["eg_number"]);

            // Android already carried cas_number on 88 elements; Windows supplied the other 30.
            // Asserting the total is what catches a regression - the per-side split is only
            // interesting while reading the merge report.
            Assert.Equal(118, rows.Values.Count(r => r.ContainsKey("cas_number")));
            Assert.Equal(103, rows.Values.Count(r => r.ContainsKey("eg_number")));
        }

        [Fact]
        public void WindowsBlanksWereNotImportedAsData()
        {
            // Windows had oxidation_state_pos on 9 elements Android lacks, but every one of those
            // values is a blank marker, so the merge correctly imported none of them. The count is
            // unchanged from Android's 106. If a future run of the merge starts importing blanks,
            // this is where it shows up.
            var rows = TestAssets.English.Rows;
            Assert.Equal(106, rows.Values.Count(r => r.ContainsKey("oxidation_state_pos")));

            // cas_number and eg_number exist only where a real value exists, so any blank in them
            // would have to have come from the merge. oxidation_state_pos is excluded: Android
            // itself stores "" for the five elements with no positive oxidation state - helium,
            // neon, argon, fluorine and promethium - which is correct and predates the merge.
            var blanks = new[] { "---", "--", "empty", "unknown", "???", "N/A", "" };
            var imported = rows
                .SelectMany(kv => kv.Value.Select(f => (Element: kv.Key, f.Key, f.Value)))
                .Where(x => x.Key is "cas_number" or "eg_number")
                .Where(x => x.Value is string s && blanks.Contains(s.Trim()))
                .Select(x => $"{x.Element}.{x.Key}")
                .ToList();

            Assert.Empty(imported);
        }

        [Fact]
        public void ShellElectronsNoLongerDropTheMShell()
        {
            // 41 Windows elements had lost the M label: "K2 L8 18 N18 O5" for "K2 L8 M18 N18 O5".
            var broken = TestAssets.English.Rows
                .Where(kv => kv.Value.TryGetValue("element_shells_electrons", out var v)
                             && v is string s && s.Contains(" 18 "))
                .Select(kv => kv.Key)
                .ToList();

            Assert.Empty(broken);
            Assert.Equal("K2 L8 M18 N18 O5 P0 Q0 R0",
                         TestAssets.English.Rows["antimony"]["element_shells_electrons"]);
        }

        [Fact]
        public void MeltingAndBoilingPointsAreNotTransposed()
        {
            // Windows had these the wrong way round. Cerium melts at 795 C and boils at 3443 C.
            var cerium = TestAssets.English["cerium"]!;
            Assert.Equal("795 (°C)", cerium["element_melting_celsius"]);
            Assert.Equal("3443 (°C)", cerium["element_boiling_celsius"]);

            // Curium's melting point had lost a digit: 134 for 1340.
            Assert.Equal("1340 (°C)", TestAssets.English["curium"]!["element_melting_celsius"]);
        }

        [Fact]
        public void TransuranicIsotopeSlotsWereRestored()
        {
            // Android trimmed these hard; Windows kept them. berkelium 5 -> 21, einsteinium 3 -> 18.
            Assert.Equal(21, CountIsotopes("berkelium"));
            Assert.Equal(18, CountIsotopes("einsteinium"));
        }

        [Fact]
        public void HassiumDiscoveryFilledFromWindows()
        {
            var hassium = TestAssets.English["hassium"]!;
            Assert.Equal("1984", hassium["element_year"]);
            Assert.Equal("GSI Helmholtz Centre for Heavy Ion Research", hassium["element_discovered_name"]);
        }

        [Fact]
        public void TechnetiumDidNotInheritTheGadoliniteImage()
        {
            // The Windows table pointed technetium at a photograph of gadolinite, a
            // gadolinium/yttrium mineral. link is never back-filled from Windows for this reason.
            var link = TestAssets.English["technetium"]!["link"] as string;
            Assert.DoesNotContain("gadolini", link ?? "", System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void OnlySevenFieldsDifferBetweenLanguages()
        {
            // The engine's LocalizedView overlay depends on this: it swaps seven fields per
            // language rather than reparsing. If a numeric field ever diverged, the overlay would
            // hand back the English number while claiming to be localised.
            var en = TestAssets.English;
            var sv = TestAssets.ElementSource.Load("sv")!;

            var localised = new HashSet<string>
            {
                "element", "element_group", "description", "element_appearance",
                "element_phase", "electrical_type", "magnetic_type",
            };

            var unexpected = new List<string>();
            foreach (var (key, enRow) in en.Rows)
            {
                var svRow = sv[key];
                if (svRow is null) continue;
                foreach (var (field, enValue) in enRow)
                {
                    if (localised.Contains(field)) continue;
                    if (!svRow.TryGetValue(field, out var svValue)) continue;
                    if (!SameValue(enValue, svValue))
                        unexpected.Add($"{key}.{field}");
                }
            }

            Assert.Empty(unexpected);
        }

        /// <summary>
        /// Structural comparison. The two nested fields (lattice_constants, debye_temperature)
        /// arrive as separate dictionary instances per language, and Equals on those is reference
        /// equality - so a plain Equals reports every element as differing.
        /// </summary>
        private static bool SameValue(object? a, object? b)
        {
            if (a is IReadOnlyDictionary<string, string> da &&
                b is IReadOnlyDictionary<string, string> db)
            {
                return da.Count == db.Count
                       && da.All(kv => db.TryGetValue(kv.Key, out var v) && v == kv.Value);
            }
            return Equals(a, b);
        }

        private static int CountIsotopes(string element) =>
            TestAssets.English[element]!.Keys.Count(
                k => k.StartsWith("iso_", System.StringComparison.Ordinal)
                     && k.Count(c => c == '_') == 1);
    }
}
