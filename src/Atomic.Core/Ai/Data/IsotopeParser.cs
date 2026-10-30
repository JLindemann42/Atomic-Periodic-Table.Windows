using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// One isotope, parsed out of the <c>iso_N</c> / <c>iso_mass_N</c> / <c>decay_type_N</c> /
    /// <c>iso_half_N</c> / <c>iso_Z_N</c> / <c>iso_N_N</c> / <c>iso_A_N</c> block.
    /// </summary>
    /// <param name="HalfLifeSeconds">Normalised half-life; null when stable or unparseable.</param>
    /// <param name="HalfLifeDisplay">The authored half-life string, shown verbatim.</param>
    /// <param name="Stable">True when the source says stable or observationally stable.</param>
    public sealed record Isotope(
        string Name,
        int? MassNumber,
        string? DecayType,
        double? HalfLifeSeconds,
        string HalfLifeDisplay,
        bool Stable,
        int? Protons,
        int? Neutrons,
        int? Nucleons);

    /// <summary>
    /// Reads the flat 42-slot isotope block out of an element row.
    ///
    /// <para>The half-life strings are inconsistent across 47 distinct unit spellings, including a
    /// corrupted "minu-Tes", a bare "m" that means minutes rather than metres, "million years", and
    /// several scientific-notation variants using U+00D7 and U+2212. All of them are handled here
    /// so downstream code sees one normalised number of seconds.</para>
    /// </summary>
    public static class IsotopeParser
    {
        /// <summary>The data tops out at 42 isotope slots for a single element.</summary>
        private const int MaxSlots = 42;

        private static readonly string[] StableMarkers = ["stable", "observationally stable"];

        /// <summary>
        /// Unit spellings to seconds, longest key first so "ms" never shadows "m".
        /// </summary>
        private static readonly (string Token, double Seconds)[] UnitSeconds =
            new (string, double)[]
            {
                ("nanoseconds", 1e-9), ("nanosecond", 1e-9),
                ("microseconds", 1e-6), ("microsecond", 1e-6),
                ("milliseconds", 1e-3), ("millisecond", 1e-3),
                ("million years", 3.15576e7 * 1e6), ("million y", 3.15576e7 * 1e6),
                ("billion years", 3.15576e7 * 1e9),
                ("seconds", 1.0), ("second", 1.0),
                ("minutes", 60.0), ("minute", 60.0),
                ("hours", 3600.0), ("hour", 3600.0),
                ("days", 86400.0), ("day", 86400.0),
                ("years", 3.15576e7), ("year", 3.15576e7),
                ("ps", 1e-12), ("ns", 1e-9), ("μs", 1e-6), ("µs", 1e-6), ("us", 1e-6),
                ("ms", 1e-3), ("min", 60.0), ("hr", 3600.0),
                ("s", 1.0), ("h", 3600.0), ("d", 86400.0), ("y", 3.15576e7), ("a", 3.15576e7),
                // A bare "m" is minutes in this data ("8.718 (m)", "4.12 m"), never metres.
                ("m", 60.0),
            }
            .OrderByDescending(p => p.Item1.Length)
            .ToArray();

        /// <summary>"5.0×10^−21 (s)", "570*10^-24 (s)", "7.17(24)×10^5 (Years)".</summary>
        private static readonly Regex Scientific =
            JavaRegex.Compile(@"([\d.]+)\s*[×xX*]\s*10\s*\^?\s*([+\-−–]?\d+)");

        private static readonly Regex LeadingNumber = JavaRegex.Compile(@"^\s*([\d.]+)");
        private static readonly Regex GluedUncertainty = JavaRegex.Compile(@"(\d)\((\d+)\)");
        private static readonly Regex DigitsAndSeparators = JavaRegex.Compile(@"[\d.,]");

        /// <summary>
        /// Parse every isotope present on an element row.
        ///
        /// <para>All slots are scanned rather than stopping at the first gap: the blocks are
        /// <i>mostly</i> contiguous, but bromine's runs 1-5, skips 6, then continues to 34.
        /// Breaking early there dropped 29 of its isotopes, including the stable ones.</para>
        /// </summary>
        public static IReadOnlyList<Isotope> Parse(ElementRow row)
        {
            var isotopes = new List<Isotope>();

            for (var slot = 1; slot <= MaxSlots; slot++)
            {
                var name = Str(row, $"iso_{slot}");
                if (string.IsNullOrEmpty(name) || ValueParser.IsNullToken(name)) continue;

                var halfRaw = Str(row, $"iso_half_{slot}") ?? string.Empty;
                var decay = Str(row, $"decay_type_{slot}");

                isotopes.Add(new Isotope(
                    Name: name,
                    // iso_A is the mass number. iso_mass is the atomic *mass*, 196.966568 for
                    // gold, and IntOf strips everything but digits - so reading that field made
                    // gold's mass number 196966568, and the isotope chart's axis ran to nine
                    // digits. Rounding the mass is the fallback for a slot with no iso_A.
                    MassNumber: IntOf(row, $"iso_A_{slot}") ?? RoundedMass(row, $"iso_mass_{slot}"),
                    DecayType: decay is not null && !ValueParser.IsNullToken(decay) ? decay : null,
                    HalfLifeSeconds: HalfLifeSeconds(halfRaw),
                    HalfLifeDisplay: halfRaw,
                    Stable: IsStable(halfRaw),
                    Protons: IntOf(row, $"iso_Z_{slot}"),
                    Neutrons: IntOf(row, $"iso_N_{slot}"),
                    Nucleons: IntOf(row, $"iso_A_{slot}")));
            }

            return isotopes;
        }

        /// <summary>True when the source marks the isotope stable, including "Observationally Stable".</summary>
        public static bool IsStable(string halfLife)
        {
            var lower = halfLife.Trim().ToLowerInvariant();
            foreach (var marker in StableMarkers)
                if (lower.StartsWith(marker, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// Normalise a half-life string to seconds.
        /// </summary>
        /// <returns>Seconds, or null when stable, absent, or genuinely unparseable.</returns>
        public static double? HalfLifeSeconds(string raw)
        {
            var s = raw.Trim();
            if (s.Length == 0 || ValueParser.IsNullToken(s) || IsStable(s)) return null;

            // "18.3 minu-Tes" - a corrupted "minutes" that appears 19 times. Only this exact
            // corruption is repaired: stripping hyphens generally would destroy negative exponents
            // such as the "-24" in "570*10^-24 (s)".
            s = Regex.Replace(s, "minu-Tes", "minutes", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            s = GluedUncertainty.Replace(s, m => m.Groups[1].Value);
            s = s.Replace('−', '-').Replace('–', '-');
            s = s.TrimStart('~', '≈', '<', '>', ' ');

            double magnitude;
            string remainder;

            var sci = Scientific.Match(s);
            if (sci.Success)
            {
                if (ParseDouble(sci.Groups[1].Value) is not { } mantissa) return null;
                var exponentText = sci.Groups[2].Value.Replace("+", string.Empty, StringComparison.Ordinal);
                if (!int.TryParse(exponentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exponent))
                    return null;

                magnitude = mantissa * Math.Pow(10.0, exponent);
                remainder = s[(sci.Index + sci.Length)..];
            }
            else
            {
                var m = LeadingNumber.Match(s);
                if (!m.Success) return null;
                if (ParseDouble(m.Groups[1].Value) is not { } value) return null;

                magnitude = value;
                remainder = s[(m.Index + m.Length)..];
            }

            return UnitFactor(remainder) is { } factor ? magnitude * factor : null;
        }

        /// <summary>Match the unit token in the tail of a half-life string; longest spelling wins.</summary>
        private static double? UnitFactor(string remainder)
        {
            var cleaned = remainder.ToLowerInvariant()
                .Replace("(", " ", StringComparison.Ordinal)
                .Replace(")", " ", StringComparison.Ordinal)
                .Replace("^", " ", StringComparison.Ordinal);
            cleaned = DigitsAndSeparators.Replace(cleaned, " ").Trim();
            if (cleaned.Length == 0) return null;

            foreach (var (token, seconds) in UnitSeconds)
            {
                if (string.Equals(cleaned, token, StringComparison.Ordinal) ||
                    cleaned.StartsWith(token + " ", StringComparison.Ordinal))
                {
                    return seconds;
                }
            }

            // Fall back to the first whitespace-delimited word, for tails like "years old".
            var space = cleaned.IndexOf(' ', StringComparison.Ordinal);
            var first = space < 0 ? cleaned : cleaned[..space];
            foreach (var (token, seconds) in UnitSeconds)
                if (string.Equals(token, first, StringComparison.Ordinal))
                    return seconds;

            return null;
        }

        private static string? Str(ElementRow row, string key) =>
            row.TryGetValue(key, out var v) ? v?.ToString()?.Trim() : null;

        private static int? RoundedMass(ElementRow row, string key)
        {
            var s = Str(row, key);
            if (s is null || ValueParser.IsNullToken(s)) return null;

            return double.TryParse(
                       s, NumberStyles.Float, CultureInfo.InvariantCulture, out var mass) && mass > 0
                ? (int)Math.Round(mass, MidpointRounding.AwayFromZero)
                : null;
        }

        private static int? IntOf(ElementRow row, string key)
        {
            var s = Str(row, key);
            if (s is null || ValueParser.IsNullToken(s)) return null;

            var digits = new string(s.Where(char.IsAsciiDigit).ToArray());
            return digits.Length > 0 &&
                   int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i
                : null;
        }

        private static double? ParseDouble(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
