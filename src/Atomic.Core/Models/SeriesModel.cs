using System.Collections.Generic;

namespace Atomic.Core.Models
{
    /// <summary>
    /// One entry in the electrochemical series.
    /// </summary>
    /// <param name="ElementKey">
    /// Lowercase English element key, matching the element table - so caesium, not cesium. The
    /// display name is resolved through the element table rather than stored, which is what lets
    /// the series read "Kalium" in a German app.
    /// </param>
    /// <param name="Voltage">Standard electrode potential in volts.</param>
    /// <param name="Charge">The ion's charge, e.g. "+", "2+", "-" for the halogens.</param>
    public sealed record Series(string ElementKey, double Voltage, string Charge, string Symbol);

    /// <summary>
    /// Standard electrode potentials.
    ///
    /// <para>Unlike the other reference tables, this one is <b>not</b> generated from Android.
    /// Android's SeriesModel has 18 entries; the Windows page had 33, a strict superset with
    /// identical values for every shared row. Regenerating from Android would have deleted
    /// fifteen elements including both halogens, so the richer table is the source of truth here
    /// and the flow is Windows to Android rather than the other way. See docs/porting-notes.md.</para>
    /// </summary>
    public static class SeriesModel
    {
        /// <summary>All 33 couples, most negative first - the order the series is read in.</summary>
        public static readonly IReadOnlyList<Series> All =
        [
            new("thorium",       -3.6,    "4+", "Th"),
            new("praseodymium",  -3.1,    "3+", "Pr"),
            new("erbium",        -3.0,    "3+", "Er"),
            new("lithium",       -3.04,   "+",  "Li"),
            new("caesium",       -3.026,  "+",  "Cs"),
            new("rubidium",      -2.98,   "+",  "Rb"),
            new("potassium",     -2.931,  "+",  "K"),
            new("barium",        -2.912,  "2+", "Ba"),
            new("francium",      -2.9,    "+",  "Fr"),
            new("lanthanum",     -2.9,    "3+", "La"),
            new("strontium",     -2.89,   "2+", "Sr"),
            new("calcium",       -2.868,  "2+", "Ca"),
            new("sodium",        -2.71,   "+",  "Na"),
            new("magnesium",     -2.372,  "2+", "Mg"),
            new("aluminium",     -1.662,  "3+", "Al"),
            new("manganese",     -1.18,   "2+", "Mn"),
            new("zinc",          -0.7618, "2+", "Zn"),
            new("chromium",      -0.74,   "3+", "Cr"),
            new("iron",          -0.44,   "2+", "Fe"),
            new("cadmium",       -0.403,  "2+", "Cd"),
            new("cobalt",        -0.28,   "2+", "Co"),
            new("nickel",        -0.25,   "2+", "Ni"),
            new("copper",        -0.159,  "2+", "Cu"),
            new("tin",           -0.13,   "2+", "Sn"),
            new("lead",          -0.126,  "2+", "Pb"),
            new("antimony",       0.204,  "3+", "Sb"),
            new("bismuth",        0.317,  "3+", "Bi"),
            new("silver",         0.7996, "+",  "Ag"),
            new("mercury",        0.85,   "2+", "Hg"),
            new("platinum",       1.188,  "2+", "Pt"),
            new("chlorine",       1.36,   "-",  "Cl"),
            new("gold",           1.52,   "3+", "Au"),
            new("fluorine",       2.87,   "-",  "F"),
        ];
    }
}
