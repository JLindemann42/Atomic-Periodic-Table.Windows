using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Atomic.Core.Ai.Cards
{
    /// <summary>How a chart axis maps values to pixels.</summary>
    public enum ChartScale { Linear, Log10 }

    /// <summary>
    /// Human labels for decade gridlines.
    ///
    /// <para>The chart controls hold no branch that depends on what a number <i>means</i> - that
    /// rule is what makes it acceptable that their drawing is untested. Choosing whether 10⁵
    /// seconds should read as "1 day" is exactly such a decision, so it lives here, where a unit
    /// test can pin it.</para>
    /// </summary>
    public static class DecadeLabels
    {
        private static readonly (double Factor, string Unit)[] TimeUnits =
        [
            (1e-15, "fs"), (1e-12, "ps"), (1e-9, "ns"), (1e-6, "µs"), (1e-3, "ms"),
            (1.0, "s"), (60.0, "min"), (3600.0, "h"), (86_400.0, "d"),
            (31_557_600.0, "y"), (31_557_600e3, "ky"), (31_557_600e6, "My"), (31_557_600e9, "Gy"),
        ];

        /// <summary>
        /// A half-life gridline label for a power of ten in seconds.
        ///
        /// <para>Half-lives span roughly 10⁻²² s to 10²⁷ s - fifty decades - so a raw "1.0E+17 s"
        /// tick is useless. This picks the largest unit that leaves a value of at least one.</para>
        /// </summary>
        public static string TimeLabel(int log10Seconds)
        {
            var seconds = Math.Pow(10.0, log10Seconds);

            var chosen = TimeUnits.LastOrDefault(u => seconds >= u.Factor);
            if (chosen.Unit is null) chosen = TimeUnits[0];

            var scaled = seconds / chosen.Factor;
            var rendered = scaled >= 1
                ? scaled.ToString("F0", CultureInfo.InvariantCulture)
                // Kotlin's %.2g. .NET has no G-with-precision that behaves identically, so the two
                // significant figures are asked for directly.
                : scaled.ToString("G2", CultureInfo.InvariantCulture);

            return $"{rendered} {chosen.Unit}";
        }

        /// <summary>
        /// A scientific label for a power of ten, e.g. <c>10⁻⁶</c>.
        ///
        /// <para>Used where the quantity has no natural unit ladder - abundance spans ten orders of
        /// magnitude in mg/kg and there is no "kilo-mg/kg" worth naming.</para>
        /// </summary>
        public static string SciLabel(int log10) =>
            log10 == 0 ? "1" : "10" + Superscript(log10);

        private static string Superscript(int value)
        {
            const string digits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
            var sign = value < 0 ? "⁻" : string.Empty;
            var magnitude = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
            return sign + new string(magnitude.Select(d => digits[d - '0']).ToArray());
        }

        /// <summary>
        /// Whether a set of values needs a log axis.
        ///
        /// <para>A decade of spread is the threshold: below it a linear axis reads more directly,
        /// above it the small values collapse onto the baseline. Returning the decision rather than
        /// making it inside a control is what keeps the choice testable.</para>
        /// </summary>
        public static ChartScale SuggestedScale(IReadOnlyList<double> values)
        {
            var positive = values.Where(v => v > 0).ToList();
            if (positive.Count < 2) return ChartScale.Linear;

            var spread = Math.Log10(positive.Max()) - Math.Log10(positive.Min());
            return spread >= 1.0 ? ChartScale.Log10 : ChartScale.Linear;
        }
    }
}
