using System.Collections.Generic;
using System.Text;

namespace Atomic.Core.Models
{
    /// <summary>
    /// One cation's row of the solubility table: a value per anion, positionally aligned with
    /// <see cref="SolubilityData.Anions"/>.
    /// </summary>
    public sealed record SolubilityColumn(string Cation, IReadOnlyList<string> Values);

    /// <summary>
    /// The solubility table: which salts dissolve.
    ///
    /// <para>Codes are <c>S</c> soluble, <c>Ss</c> slightly soluble, <c>I</c> insoluble, and
    /// <c>---</c> for a pair that does not form a stable compound in water.</para>
    ///
    /// <para><b>One table, shared with Android.</b> The apps used to disagree (D10): Windows had an
    /// H⁺ row and SO₃²⁻ / SiO₃²⁻ columns, Android a Zn²⁺ row and CrO₄²⁻ / CH₃CO₂⁻ columns, and the
    /// Windows page's hand-written grid was wrong in places - sodium hydroxide insoluble, every
    /// sulfate soluble. Roadmap 0.8 merged them in Android's <c>SolubilityModel.kt</c>, and the page
    /// now renders this table (D58). <c>SolubilityGoldenTest</c> checks it against
    /// <c>shared/golden/solubility.tsv</c>, which Android generates, so the copies cannot drift.</para>
    /// </summary>
    public static class SolubilityData
    {
        public static readonly IReadOnlyList<string> Anions =
        [
            "F^-", "Cl^-", "Br^-", "I^-", "OH^-", "S^2-",
            "SO4^2-", "CO3^2-", "NO3^-", "PO4^3-", "CrO4^2-", "CH3CO2^-",
            "SO3^2-", "SiO3^2-",
        ];

        public static readonly IReadOnlyList<SolubilityColumn> Columns =
        [
            new("H^+",   ["S", "S", "S", "S", "---", "Ss", "S", "Ss", "S", "S", "S", "S", "S", "I"]),
            new("NH4^+", ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "---"]),
            new("Li^+",  ["S", "S", "S", "S", "S", "S", "S", "S", "S", "I", "S", "S", "S", "Ss"]),
            new("Na^+",  ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S"]),
            new("K^+",   ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S"]),
            new("Mg^2+", ["I", "S", "S", "S", "I", "---", "S", "I", "S", "I", "S", "S", "Ss", "I"]),
            new("Ca^2+", ["I", "S", "S", "S", "Ss", "---", "Ss", "I", "S", "I", "S", "S", "I", "I"]),
            new("Ba^2+", ["Ss", "S", "S", "S", "S", "---", "I", "I", "S", "I", "I", "S", "I", "I"]),
            new("Al^3+", ["S", "S", "S", "S", "I", "---", "S", "---", "S", "I", "---", "Ss", "---", "I"]),
            new("Fe^3+", ["Ss", "S", "S", "---", "I", "I", "S", "---", "S", "I", "I", "S", "---", "I"]),
            new("Cu^2+", ["S", "S", "S", "---", "I", "I", "S", "---", "S", "I", "I", "S", "---", "I"]),
            new("Ag^+",  ["S", "I", "I", "I", "---", "I", "Ss", "I", "S", "I", "I", "S", "I", "I"]),
            new("Zn^2+", ["S", "S", "S", "S", "I", "I", "S", "I", "S", "I", "I", "S", "I", "I"]),
            new("Pb^2+", ["I", "S", "Ss", "I", "I", "I", "I", "I", "S", "I", "I", "S", "I", "I"]),
        ];

        /// <summary>The English word for a solubility code, or the code itself if unrecognised.</summary>
        public static string Word(string code) => code switch
        {
            "S" => "soluble",
            "Ss" => "slightly soluble",
            "I" => "insoluble",
            _ => code,
        };

        /// <summary>
        /// An ion as it is printed: <c>SO4^2-</c> → <c>SO₄²⁻</c>, <c>NH4^+</c> → <c>NH₄⁺</c>. Digits
        /// in the formula are subscripts; everything after <c>^</c> is the charge, in superscript.
        /// </summary>
        public static string Label(string ion)
        {
            var caret = ion.IndexOf('^', System.StringComparison.Ordinal);
            var formula = caret < 0 ? ion : ion[..caret];
            var charge = caret < 0 ? "" : ion[(caret + 1)..];

            var label = new StringBuilder(ion.Length);
            foreach (var c in formula) label.Append(c is >= '0' and <= '9' ? (char)('₀' + (c - '0')) : c);
            foreach (var c in charge)
            {
                label.Append(c switch
                {
                    '+' => '⁺',
                    '-' => '⁻',
                    '1' => '¹',
                    '2' => '²',
                    '3' => '³',
                    >= '0' and <= '9' => (char)('⁰' + (c - '0')),
                    _ => c,
                });
            }
            return label.ToString();
        }
    }
}
