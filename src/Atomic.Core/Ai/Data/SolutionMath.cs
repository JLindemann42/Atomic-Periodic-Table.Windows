using System;
using System.Linq;

namespace Atomic.Core.Ai.Data
{
    /// <summary>The four quantities in C₁V₁ = C₂V₂, all known once the missing one is solved for.</summary>
    public sealed record Dilution(double C1, double V1, double C2, double V2);

    /// <summary>
    /// Solution chemistry: n = c·V, mass through a molar mass, and dilution.
    ///
    /// <para>Everything here is in mol, litres, mol/L and grams. Converting mL, mmol and mM is the
    /// scanner's job, done once at the edge - carrying mixed units this far in is how a factor of a
    /// thousand ends up in an answer that otherwise looks right.</para>
    /// </summary>
    public static class SolutionMath
    {
        /// <summary>
        /// Complete n = c·V from any two of the three.
        /// </summary>
        /// <returns>
        /// Moles, molarity in mol/L and volume in litres; null when fewer than two are known or a
        /// known value is not positive - a zero volume or a negative concentration is a misread
        /// query, not a solution to report.
        /// </returns>
        public static (double Moles, double Molarity, double Litres)? Complete(
            double? moles, double? molarity, double? litres)
        {
            var known = new[] { moles, molarity, litres }.Where(v => v is not null).Select(v => v!.Value).ToList();
            if (known.Count < 2) return null;
            if (known.Any(v => v <= 0.0 || !double.IsFinite(v))) return null;

            if (moles is { } n && molarity is { } c) return (n, c, n / c);
            if (moles is { } n2 && litres is { } v2) return (n2, n2 / v2, v2);
            if (molarity is { } c3 && litres is { } v3) return (c3 * v3, c3, v3);
            return null;
        }

        /// <summary>Grams of a substance of known molar mass. Null when the molar mass is not positive.</summary>
        public static double? MassOf(double moles, double molarMassGramsPerMol) =>
            molarMassGramsPerMol > 0.0 && double.IsFinite(moles)
                ? moles * molarMassGramsPerMol
                : null;

        /// <summary>Moles in a mass of a substance of known molar mass.</summary>
        public static double? MolesOf(double grams, double molarMassGramsPerMol) =>
            ChemistryMath.MassToMoles(grams, molarMassGramsPerMol);

        /// <summary>
        /// Solve C₁V₁ = C₂V₂ for whichever one of the four is missing.
        /// </summary>
        /// <returns>
        /// Every value filled in, or null unless exactly three are known and positive. Three is the
        /// whole condition: with four the user has asked nothing, and with two the equation has a
        /// family of answers rather than one.
        /// </returns>
        public static Dilution? DilutionOf(double? c1, double? v1, double? c2, double? v2)
        {
            var known = new[] { c1, v1, c2, v2 };
            if (known.Count(v => v is not null) != 3) return null;
            if (known.Where(v => v is not null).Any(v => v!.Value <= 0.0 || !double.IsFinite(v.Value)))
                return null;

            if (c1 is null) return new Dilution(c2!.Value * v2!.Value / v1!.Value, v1.Value, c2.Value, v2.Value);
            if (v1 is null) return new Dilution(c1.Value, c2!.Value * v2!.Value / c1.Value, c2.Value, v2.Value);
            if (c2 is null) return new Dilution(c1.Value, v1.Value, c1.Value * v1.Value / v2!.Value, v2.Value);
            return new Dilution(c1.Value, v1.Value, c2.Value, c1.Value * v1.Value / c2.Value);
        }

        /// <summary>
        /// Solvent to add to get from one volume to another.
        ///
        /// <para>Negative when the target is the more concentrated of the two, which is a real
        /// answer: it says the sample has to be evaporated or made up differently, not
        /// diluted.</para>
        /// </summary>
        public static double SolventToAdd(double v1, double v2) => v2 - v1;
    }
}
