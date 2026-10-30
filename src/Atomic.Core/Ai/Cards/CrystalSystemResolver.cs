using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Atomic.Core.Ai.Cards
{
    /// <summary>
    /// Maps an authored <c>crystal_structure</c> string onto a wireframe key, or null.
    ///
    /// <para><b>The null is the point.</b> The crystal-structure renderer falls back to "Cubic" for
    /// anything it does not recognise, which on the element screen is a cosmetic guess sitting next
    /// to a text label that states the truth. In chat the card <i>is</i> the answer, so drawing a
    /// cubic cell for a rhombohedral element would be a fabrication rather than a rough edge. The
    /// renderer keeps its fallback so its existing behaviour is unchanged; the card layer uses the
    /// nullable result and simply shows no card.</para>
    /// </summary>
    public static class CrystalSystemResolver
    {
        /// <summary>
        /// The wireframe keys the app can draw.
        ///
        /// <para>Declared here rather than read from <c>Atomic.App.Helpers.CrystalStructures</c>,
        /// which this assembly cannot reference - Atomic.Core targets plain net8.0 by design. These
        /// are the crystal systems themselves rather than app data, so the duplication is a naming
        /// contract rather than a copy of a table:
        /// <see cref="CrystalSystemResolverTest"/> asserts the two agree.</para>
        /// </summary>
        public static readonly FrozenSet<string> KnownSystems = new[]
        {
            "Cubic", "Body-centered cubic (bcc)", "Face-centered cubic (fcc)", "Hexagonal",
            "Tetragonal", "Orthorhombic", "Rhombohedral", "Trigonal", "Monoclinic", "Triclinic",
        }.ToFrozenSet(StringComparer.Ordinal);

        /// <returns>A key in <see cref="KnownSystems"/>, or null when the string is unrecognised.</returns>
        public static string? Resolve(string? raw)
        {
            var value = raw?.Trim() ?? string.Empty;
            if (value.Length == 0) return null;

            // Hexagonal is matched loosely because the data spells it several ways
            // ("Hexagonal close-packed (hcp)", "Simple hexagonal").
            if (value.Contains("Hexagonal", StringComparison.OrdinalIgnoreCase)) return "Hexagonal";

            foreach (var system in KnownSystems)
            {
                if (string.Equals(value, system, StringComparison.OrdinalIgnoreCase)) return system;
            }
            return null;
        }
    }
}
