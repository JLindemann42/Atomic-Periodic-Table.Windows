using System;
using System.Collections.Generic;
using System.Globalization;

namespace Atomic.Core.Ai.Cards
{
    /// <summary>
    /// Reads the authored shell-occupancy string into electron counts per shell.
    ///
    /// <para>The field is authored as <c>K2 L8 M18 N32 O18 P1 Q0 R0</c> - a letter per shell and its
    /// occupancy, always eight slots, zero-padded. The letters carry no information the position
    /// does not, so they are stripped.</para>
    ///
    /// <para>Lives here rather than in the chart for the same reason the markdown grammar does: it
    /// is a decision about data that fails silently. A chart with a missing outer ring looks
    /// plausible.</para>
    /// </summary>
    public static class ElectronShells
    {
        /// <summary>Shells beyond this are not authored; the field is always eight slots wide.</summary>
        private const int MaxShells = 8;

        /// <summary>
        /// Electron counts per shell, innermost first, with the zero padding removed.
        /// </summary>
        /// <remarks>
        /// <para><b>Only trailing zeros are dropped.</b> Android filters every zero, wherever it
        /// falls. The two agree on all 118 shipped elements, because a ground-state configuration
        /// fills shells in order and cannot leave an interior gap - but if a future data edit ever
        /// introduced one, filtering would silently renumber every shell outside it, while this
        /// leaves a visible empty ring. Same output today, louder failure tomorrow.</para>
        /// </remarks>
        public static IReadOnlyList<int> Parse(string? authored)
        {
            if (string.IsNullOrWhiteSpace(authored)) return [];

            var counts = new List<int>(MaxShells);
            var digits = new System.Text.StringBuilder();

            foreach (var ch in authored)
            {
                if (char.IsAsciiDigit(ch))
                {
                    digits.Append(ch);
                    continue;
                }

                Flush();
            }
            Flush();

            // Trailing zeros are padding, not shells: gold is authored to R0 but has six shells.
            var last = counts.FindLastIndex(c => c > 0);
            if (last < 0) return [];

            counts.RemoveRange(last + 1, counts.Count - last - 1);
            return counts;

            void Flush()
            {
                if (digits.Length == 0) return;
                if (int.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                    counts.Add(value);
                digits.Clear();
            }
        }
    }
}
