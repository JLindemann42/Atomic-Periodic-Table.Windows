using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Models
{
    /// <summary>
    /// A metal alloy.
    /// </summary>
    /// <param name="Base">The element it is mostly made of, as a lowercase English element key.</param>
    /// <param name="Composition">The main constituents, in plain language.</param>
    /// <param name="Description">What it is, and why the alloy is used instead of the base metal.</param>
    public sealed record Alloy(
        string Name,
        string Base,
        string Composition,
        string Description,
        string Wiki);

    /// <summary>
    /// Common alloys, so questions like "what is the difference between iron and steel" have an
    /// answer.
    ///
    /// <para>Alloys are not elements, so nothing in the periodic table data covers them. Each entry
    /// names its base element, which is what lets an element-versus-alloy question be recognised
    /// and answered - and what the "go to base element" link uses.</para>
    ///
    /// <para>Lives in Atomic.Core rather than the app because the engine's dataset index reads the
    /// same list.</para>
    ///
    /// <para>Like every other reference dataset in both apps, the text is English only.</para>
    /// </summary>
    public static class AlloyModel
    {
        public static readonly IReadOnlyList<Alloy> All =
        [
            new("Steel", "iron", "Iron with up to about 2% carbon",
                "Steel is iron alloyed with a small amount of carbon. The carbon atoms sit between the iron atoms and obstruct the planes along which they would otherwise slide, which makes steel far harder and stronger than pure iron. Pure iron is soft and corrodes readily; steel can be tuned across a wide range of hardness and toughness by varying the carbon content and heat treatment.",
                "https://en.wikipedia.org/wiki/Steel"),

            new("Stainless steel", "iron", "Iron, at least 10.5% chromium, often nickel",
                "Stainless steel is steel with enough chromium that a thin, self-repairing chromium oxide layer forms on the surface and blocks further corrosion. Nickel is often added to improve toughness and formability.",
                "https://en.wikipedia.org/wiki/Stainless_steel"),

            new("Cast iron", "iron", "Iron with roughly 2 to 4% carbon",
                "Cast iron holds more carbon than steel, which makes it hard, brittle and easy to cast, but not practical to forge. Its high carbon content also gives it good vibration damping and wear resistance.",
                "https://en.wikipedia.org/wiki/Cast_iron"),

            new("Brass", "copper", "Copper and zinc",
                "Brass is copper alloyed with zinc. It is harder than copper, easy to machine and cast, resists corrosion, and is naturally antimicrobial. The proportion of zinc sets its colour and workability.",
                "https://en.wikipedia.org/wiki/Brass"),

            new("Bronze", "copper", "Copper and tin",
                "Bronze is copper alloyed with tin. It is harder than either constituent and resists corrosion in seawater, which is why it was the defining metal of an age and is still used for bearings, propellers and bells.",
                "https://en.wikipedia.org/wiki/Bronze"),

            new("Solder", "tin", "Tin with lead, silver or copper",
                "Solder is a low-melting alloy used to join metals electrically and mechanically. Traditional solder was tin and lead; modern electronics solder replaces the lead with silver and copper.",
                "https://en.wikipedia.org/wiki/Solder"),

            new("Pewter", "tin", "Mostly tin, with copper and antimony",
                "Pewter is a soft, low-melting tin alloy used for tableware and decorative casting.",
                "https://en.wikipedia.org/wiki/Pewter"),

            new("Duralumin", "aluminium", "Aluminium with copper, magnesium and manganese",
                "Duralumin is an age-hardening aluminium alloy that approaches the strength of soft steel at roughly a third of the density, which made early all-metal aircraft possible.",
                "https://en.wikipedia.org/wiki/Duralumin"),

            new("Ti-6Al-4V", "titanium", "Titanium with 6% aluminium and 4% vanadium",
                "The workhorse titanium alloy, and the reason titanium dominates aerospace: it combines a very high strength-to-weight ratio with excellent corrosion resistance and retains its strength at high temperature.",
                "https://en.wikipedia.org/wiki/Ti-6Al-4V"),

            new("Nichrome", "nickel", "Nickel and chromium",
                "Nichrome has a high electrical resistance and does not oxidise appreciably when hot, which makes it the standard material for heating elements.",
                "https://en.wikipedia.org/wiki/Nichrome"),

            new("Sterling silver", "silver", "92.5% silver, usually with copper",
                "Pure silver is too soft for most objects; adding copper gives sterling silver enough hardness for tableware and jewellery while keeping the colour and lustre.",
                "https://en.wikipedia.org/wiki/Sterling_silver"),

            new("White gold", "gold", "Gold with palladium, nickel or silver",
                "White gold is gold alloyed with a white metal to change its colour, and usually plated with rhodium for a brighter finish.",
                "https://en.wikipedia.org/wiki/Colored_gold"),

            new("Amalgam", "mercury", "Mercury with silver, tin and copper",
                "An amalgam is an alloy of mercury with another metal. Dental amalgam was widely used for fillings because it is durable and sets at body temperature.",
                "https://en.wikipedia.org/wiki/Amalgam_(chemistry)"),

            new("Magnalium", "aluminium", "Aluminium with magnesium",
                "Magnalium is lighter and stiffer than pure aluminium and is used where weight matters more than cost.",
                "https://en.wikipedia.org/wiki/Magnalium"),

            new("Inconel", "nickel", "Nickel with chromium and iron",
                "Inconel is a family of nickel superalloys that keep their strength and resist oxidation at temperatures where steel would fail, so they are used in turbine blades and rocket engines.",
                "https://en.wikipedia.org/wiki/Inconel"),
        ];

        /// <summary>
        /// The base metals that actually appear, in the order the filter chips should show them.
        /// Derived rather than listed, so a new alloy cannot arrive without its chip.
        /// </summary>
        public static readonly IReadOnlyList<string> BaseMetals =
            All.Select(a => a.Base).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>
        /// Alloys matching a base metal and a search term.
        /// </summary>
        /// <param name="baseMetal">Lowercase element key, or null for all.</param>
        /// <param name="search">
        /// Matched against the name <b>and</b> the composition, so searching "zinc" finds brass -
        /// which is the point of the search, since zinc is not in the name.
        /// </param>
        public static IReadOnlyList<Alloy> Filter(string? baseMetal, string? search)
        {
            IEnumerable<Alloy> results = All;

            if (!string.IsNullOrEmpty(baseMetal))
                results = results.Where(a => string.Equals(a.Base, baseMetal, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                results = results.Where(a =>
                    a.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    a.Composition.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return results.ToList();
        }
    }
}
