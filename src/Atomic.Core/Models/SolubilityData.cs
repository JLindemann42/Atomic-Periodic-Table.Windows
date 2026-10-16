using System.Collections.Generic;

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
    /// <c>---</c> for a combination that does not form a stable compound worth tabulating.</para>
    ///
    /// <para><b>This is Android's table, and the two apps disagree.</b> The Windows page has a
    /// H⁺ row and SO₃²⁻ / SiO₃²⁻ columns that Android lacks; Android has a Zn²⁺ row and
    /// CrO₄²⁻ / CH₃CO₂⁻ columns that Windows lacks. Reconciling them means authoring roughly
    /// fifty new cells, which is a chemistry question rather than a merge, so it is deliberately
    /// left open - see docs/porting-notes.md. Android's table is used here because the AI corpus
    /// was written against it.</para>
    /// </summary>
    public static class SolubilityData
    {
        public static readonly IReadOnlyList<string> Anions =
        [
            "F^-", "Cl^-", "Br^-", "I^-", "OH^-", "S^2-",
            "SO4^2-", "CO3^2-", "NO3^-", "PO4^3-", "CrO4^2-", "CH3CO2^-",
        ];

        public static readonly IReadOnlyList<SolubilityColumn> Columns =
        [
            new("NH4^+", ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S"]),
            new("Li^+",  ["S", "S", "S", "S", "S", "S", "S", "S", "S", "I", "S", "S"]),
            new("Na^+",  ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S"]),
            new("K^+",   ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S", "S"]),
            new("Mg^2+", ["I", "S", "S", "S", "I", "---", "S", "I", "S", "I", "S", "S"]),
            new("Ca^2+", ["I", "S", "S", "S", "Ss", "---", "Ss", "I", "S", "I", "S", "S"]),
            new("Ba^2+", ["Ss", "S", "S", "S", "S", "---", "I", "I", "S", "I", "I", "S"]),
            new("Al^3+", ["S", "S", "S", "S", "I", "---", "S", "---", "S", "I", "---", "Ss"]),
            new("Fe^3+", ["Ss", "S", "S", "---", "I", "I", "S", "---", "S", "I", "I", "S"]),
            new("Cu^2+", ["S", "S", "S", "---", "I", "I", "S", "---", "S", "I", "I", "S"]),
            new("Ag^+",  ["S", "I", "I", "I", "---", "I", "Ss", "I", "S", "I", "I", "S"]),
            new("Zn^2+", ["S", "S", "S", "S", "I", "I", "S", "I", "S", "I", "I", "S"]),
            new("Pb^2+", ["I", "S", "Ss", "I", "I", "I", "I", "I", "S", "I", "I", "S"]),
        ];

        /// <summary>The English word for a solubility code, or the code itself if unrecognised.</summary>
        public static string Word(string code) => code switch
        {
            "S" => "soluble",
            "Ss" => "slightly soluble",
            "I" => "insoluble",
            _ => code,
        };
    }
}
