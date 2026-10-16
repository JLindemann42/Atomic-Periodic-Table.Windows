using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// Turns the element data's unit-suffixed strings into typed <see cref="FieldValue"/>s.
    ///
    /// <para>Every rule here was derived from a survey of the shipped element table, so the
    /// ordering is load-bearing: scientific notation must be tried before the plain-number rule,
    /// the range rule must run after the negative-number case is settled, and the null sentinel
    /// must be recognised even when a unit is still glued to it ("--- (pm)" occurs 32 times).</para>
    /// </summary>
    public static class ValueParser
    {
        /// <summary>
        /// Sentinels the data uses for "no value". Compared after trimming and lowercasing.
        ///
        /// <para>Deliberately excludes "na": it is sodium's symbol, and treating it as a sentinel
        /// silently erased sodium from every symbol lookup. "n/a" alone covers the real absent
        /// marker.</para>
        /// </summary>
        private static readonly HashSet<string> NullTokens = new(StringComparer.Ordinal)
        {
            "", "---", "--", "n/a", "none", "null", "?", "???", "unknown",
        };

        /// <summary>Leading markers meaning the number is not exact.</summary>
        private static readonly char[] ApproxPrefixes = ['~', '≈', '<', '>', '≲', '≳'];

        /// <summary>[room temperature], [Graphite], [27°C] - a qualifier, not part of the number.</summary>
        private static readonly Regex BracketNote = JavaRegex.Compile(@"\[([^\[\]]*)]");

        /// <summary>"Br2: 3.1028 (g/cm^3)", "White: 317 (K)" - allotrope or molecular form.</summary>
        private static readonly Regex FormPrefix = JavaRegex.Compile(@"^([A-Za-z][A-Za-z0-9]{0,7})\s*:\s*");

        /// <summary>"0.479 (Cl2) (kJ/mol)" - the diatomic form annotated mid-string.</summary>
        private static readonly Regex MoleculeInfix = JavaRegex.Compile(@"\((?:Cl|F|H|I|N|O|Br)\d\)\s*");

        /// <summary>"7.17(24)×10^5" - a glued uncertainty on the last digits.</summary>
        private static readonly Regex GluedUncertainty = JavaRegex.Compile(@"(\d)\((\d+)\)");

        /// <summary>"8.0 × 10^4", "5.0×10^−21", "570*10^-24". Note U+2212 and U+00D7.</summary>
        private static readonly Regex Scientific =
            JavaRegex.Compile(@"^([+-]?\d+(?:[.,]\d+)?)\s*[×xX*]\s*10\s*\^?\s*([+\-−–]?\d+)");

        /// <summary>A bare power such as "10^-8", with no mantissa.</summary>
        private static readonly Regex BarePower = JavaRegex.Compile(@"^10\s*\^\s*([+\-−–]?\d+)");

        /// <summary>"160-350 (MPa)", "0.2-0.4". Only applied to fields that genuinely carry ranges.</summary>
        private static readonly Regex Range = JavaRegex.Compile(@"^(\d+(?:\.\d+)?)\s*[-–—]\s*(\d+(?:\.\d+)?)");

        /// <summary>A signed decimal at the start, possibly with thousands separators.</summary>
        private static readonly Regex LeadingNumber =
            JavaRegex.Compile(@"^([+\-−]?\d{1,3}(?:,\d{3})+(?:\.\d+)?|[+\-−]?\d*\.?\d+)");

        private static readonly Regex Whitespace = JavaRegex.Compile(@"\s+");

        /// <summary>
        /// Parse one raw value against its field's expectations.
        /// </summary>
        /// <param name="raw">The value as it came out of the data: string, int, map, or null.</param>
        /// <param name="spec">Field metadata; drives enum handling and whether ranges are meaningful.</param>
        public static FieldValue Parse(object? raw, FieldSpec spec)
        {
            // Rule 1 - absent. Handles bare sentinels and unit-suffixed ones like "--- (pm)".
            if (raw is null) return FieldValue.Missing;

            if (raw is int i)
            {
                return new FieldValue.Num(new Quantity(
                    i, Unit: spec.CanonicalUnit,
                    Display: i.ToString(CultureInfo.InvariantCulture)));
            }

            // Rule 2 - nested structures (lattice_constants, debye_temperature).
            if (raw is IReadOnlyDictionary<string, string> map)
            {
                var parts = new Dictionary<string, Quantity>(StringComparer.Ordinal);
                foreach (var (k, v) in map)
                {
                    if (Parse(v, spec.AsScalar()) is FieldValue.Num num)
                        parts[k] = num.Quantity;
                }
                return parts.Count == 0 ? FieldValue.Missing : new FieldValue.Struct(parts);
            }

            var original = raw.ToString() ?? string.Empty;
            var s = original.Trim();
            if (IsNullToken(s)) return FieldValue.Missing;

            // Multi-valued cells separate allotropes with a newline; take the first, keep the rest
            // as a note.
            string? extraNote = null;
            if (s.Contains('\n', StringComparison.Ordinal))
            {
                var lines = new List<string>();
                foreach (var line in s.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length > 0) lines.Add(t);
                }
                if (lines.Count > 1) extraNote = string.Join("; ", lines.GetRange(1, lines.Count - 1));
                if (lines.Count == 0) return FieldValue.Missing;
                s = lines[0];
            }

            // Rule 3 - bracketed qualifier, e.g. "5000 (m/s) [room temperature]".
            string? note = null;
            var bracket = BracketNote.Match(s);
            if (bracket.Success)
            {
                var inner = bracket.Groups[1].Value.Trim();
                note = inner.Length == 0 ? null : inner;
                s = BracketNote.Replace(s, " ").Trim();
            }

            // Rule 4 - allotrope / molecular-form prefix, e.g. "Br2: 332.0 (K)".
            var form = FormPrefix.Match(s);
            if (form.Success)
            {
                var candidate = form.Groups[1].Value;
                // Only a form marker when a number follows; otherwise it is prose.
                var rest = s[form.Value.Length..].TrimStart();
                if (rest.Length > 0 &&
                    (char.IsDigit(rest[0]) || Array.IndexOf(ApproxPrefixes, rest[0]) >= 0 || rest[0] == '-'))
                {
                    note = note is null ? candidate : candidate + ", " + note;
                    s = rest;
                }
            }

            s = MoleculeInfix.Replace(s, string.Empty).Trim();
            if (IsNullToken(s)) return FieldValue.Missing;

            // Rule 5 - closed vocabularies never go through numeric parsing.
            if (spec.Kind == FieldKind.Enum)
            {
                var canonical = spec.Canonicalise(original.Trim());
                return canonical is null
                    ? FieldValue.Missing
                    : new FieldValue.Enum(canonical, original.Trim());
            }

            // Rule 6 - an unquantified presence.
            if (string.Equals(s, "trace", StringComparison.OrdinalIgnoreCase))
                return FieldValue.Trace;

            if (spec.Kind is FieldKind.Text or FieldKind.Id or FieldKind.Link)
                return new FieldValue.Text(original.Trim());

            // Rule 7 - strip a glued uncertainty: "7.17(24)×10^5" -> "7.17×10^5".
            s = GluedUncertainty.Replace(s, m => m.Groups[1].Value);

            // Rule 8 - approximation markers.
            var approximate = false;
            while (s.Length > 0 && Array.IndexOf(ApproxPrefixes, s[0]) >= 0)
            {
                approximate = true;
                s = s[1..].TrimStart();
            }

            note = Join(note, extraNote);

            // Rule 9 - scientific notation, before the plain-number rule so the exponent survives.
            var sci = Scientific.Match(s);
            if (sci.Success)
            {
                var mantissa = ParseDouble(sci.Groups[1].Value.Replace(',', '.'));
                var exponent = ParseInt(NormaliseSign(sci.Groups[2].Value));
                if (mantissa is { } m0 && exponent is { } e0)
                {
                    var unit = UnitFrom(s[sci.Value.Length..], spec);
                    return new FieldValue.Num(new Quantity(
                        m0 * Pow10(e0), null, unit.Unit, original.Trim(), approximate, note, unit.Authored));
                }
            }

            var bare = BarePower.Match(s);
            if (bare.Success)
            {
                var exponent = ParseInt(NormaliseSign(bare.Groups[1].Value));
                if (exponent is { } e1)
                {
                    var unit = UnitFrom(s[bare.Value.Length..], spec);
                    return new FieldValue.Num(new Quantity(
                        Pow10(e1), null, unit.Unit, original.Trim(), approximate, note, unit.Authored));
                }
            }

            // Rule 10 - ranges, only where the field genuinely carries them. Elsewhere a hyphen is
            // a minus sign (melting points go negative) and treating it as a range would be
            // silently wrong.
            if (spec.AllowsRange)
            {
                var range = Range.Match(s);
                if (range.Success)
                {
                    var low = ParseDouble(range.Groups[1].Value);
                    var high = ParseDouble(range.Groups[2].Value);
                    if (low is { } l && high is { } h)
                    {
                        var unit = UnitFrom(s[range.Value.Length..], spec);
                        return new FieldValue.Num(new Quantity(
                            l, h, unit.Unit, original.Trim(), approximate, note, unit.Authored));
                    }
                }
            }

            // Rule 11 - a leading decimal, with thousands separators and percent handled.
            var lead = LeadingNumber.Match(s);
            if (lead.Success)
            {
                var numeric = ParseDouble(
                    NormaliseSign(lead.Groups[1].Value).Replace(",", string.Empty, StringComparison.Ordinal));
                if (numeric is { } n)
                {
                    var remainder = s[lead.Value.Length..];
                    // A percent sign is glued to the number rather than spaced off it, so it never
                    // reaches NormaliseUnit; it is authored either way.
                    var unit = remainder.TrimStart().StartsWith('%')
                        ? new UnitSource("%", true)
                        : UnitFrom(remainder, spec);
                    return new FieldValue.Num(new Quantity(
                        n, null, unit.Unit, original.Trim(), approximate, note, unit.Authored));
                }
            }

            // Rule 12 - no number in there at all.
            return new FieldValue.Text(original.Trim());
        }

        /// <summary>True when the string is one of the absent-value sentinels, unit suffix and all.</summary>
        public static bool IsNullToken(string value)
        {
            var trimmed = value.Trim();
            if (NullTokens.Contains(trimmed.ToLowerInvariant())) return true;

            // "--- (pm)", "--- J/(g·K)", "---  (K)": a sentinel that kept its unit.
            if (trimmed.StartsWith("---", StringComparison.Ordinal))
            {
                var rest = trimmed[3..].Trim();
                return rest.Length == 0 || rest[0] == '(' || char.IsLetter(rest[0]);
            }
            return false;
        }

        /// <summary>
        /// Normalise a trailing unit: drop wrapping parentheses, collapse whitespace, and render
        /// caret powers as superscripts so "(g/cm^3)" and "g/cm³" compare equal.
        /// </summary>
        public static string? NormaliseUnit(string raw)
        {
            var u = raw.Trim().Normalize(NormalizationForm.FormKC).Trim();
            if (u.Length == 0) return null;

            if (u.StartsWith('(') && u.EndsWith(')') && Count(u, '(') == Count(u, ')'))
                u = u[1..^1].Trim();

            u = Whitespace.Replace(u, " ")
                .Replace("^3", "³", StringComparison.Ordinal)
                .Replace("^2", "²", StringComparison.Ordinal)
                .Replace("^-1", "⁻¹", StringComparison.Ordinal);

            return u.Length == 0 ? null : u;
        }

        /// <summary>
        /// A unit and where it came from.
        ///
        /// <para>The distinction matters downstream: a unit read off the value is already inside
        /// <see cref="Quantity.Display"/>, while one taken from the registry is not and has to be
        /// added by whoever renders the value.</para>
        /// </summary>
        private readonly record struct UnitSource(string? Unit, bool Authored);

        private static UnitSource UnitFrom(string remainder, FieldSpec spec) =>
            NormaliseUnit(remainder) is { } u
                ? new UnitSource(u, true)
                : new UnitSource(spec.CanonicalUnit, false);

        /// <summary>Map the Unicode minus and dashes onto ASCII so parsing accepts them.</summary>
        private static string NormaliseSign(string raw) => raw
            .Replace('−', '-').Replace('–', '-').Replace('—', '-')
            .Replace("+", string.Empty, StringComparison.Ordinal);

        private static double? ParseDouble(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

        private static int? ParseInt(string s) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

        /// <summary>
        /// Repeated multiplication rather than <see cref="Math.Pow"/>, matching the Kotlin. The
        /// two differ in the last bits for some exponents, and the corpus expectations were
        /// produced by this one.
        /// </summary>
        private static double Pow10(int exponent)
        {
            var result = 1.0;
            for (var i = 0; i < Math.Abs(exponent); i++)
                result *= 10.0;
            return exponent < 0 ? 1.0 / result : result;
        }

        private static string? Join(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? null : b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "; " + b;
        }

        private static int Count(string s, char c)
        {
            var n = 0;
            foreach (var ch in s)
                if (ch == c)
                    n++;
            return n;
        }
    }
}
