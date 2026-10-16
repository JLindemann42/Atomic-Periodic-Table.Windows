using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Ai.Data;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Nlu
{
    /// <summary>
    /// One <c>&lt;number&gt;&lt;unit&gt;</c> pair found in a query, with the span it occupied.
    /// </summary>
    /// <param name="Value">The number as written.</param>
    /// <param name="Unit">Canonical unit, as <see cref="UnitConverter.KnownUnit"/> resolved it.</param>
    /// <param name="Dimension">What that unit measures.</param>
    /// <param name="Start">
    /// Where in the raw query it was found, so a caller can tell two quantities apart by position -
    /// "dilute 50 mL of 2 M to 0.5 M" needs to know which molarity came first.
    /// </param>
    public sealed record ScannedQuantity(double Value, string Unit, Dimension Dimension, int Start, int Length)
    {
        public int End => Start + Length;

        /// <summary>This value expressed in the target unit, or null when the units do not convert.</summary>
        public double? InUnit(string target) => UnitConverter.Convert(Value, Unit, target);
    }

    /// <summary>
    /// Finds the quantities a user wrote in a query.
    ///
    /// <para>Shared by the conversion, solution and decay intents so the three cannot disagree
    /// about what "250 mL" means. That sharing is the point: when only the planner knew how to read
    /// a mole quantity and the executor had its own idea, a query planned correctly and then
    /// executed to nothing, which the user experiences as the assistant shrugging at its own
    /// answer.</para>
    ///
    /// <para>Scans the <b>raw</b> query, never the normalised one.
    /// <c>TextMatching.NormalizeForLookup</c> lowercases, and <c>M</c> and <c>m</c> - molar and
    /// metre - differ only by case.</para>
    /// </summary>
    public static class QuantityScanner
    {
        /// <summary>
        /// A number followed by a unit.
        ///
        /// <para>The unit group stays optional so a bare number is consumed rather than leaving its
        /// digits to be re-matched as part of the next token.</para>
        /// </summary>
        private static readonly Regex NumberWithUnit = JavaRegex.Compile(
            // Unicode letters, not `a-z`: Spanish writes "años", and an ASCII-only class matched
            // the leading "a" of it - which the converter reads as the angstrom, so "11460 años"
            // scanned as a length and the decay question lost its duration.
            // Digits after the letters, so `g/cm3` and `kg/m3` survive as written; the denominator
            // needs them for the same reason.
            @"(-?\d+(?:[.,]\d+)?(?:\s*[×x*]\s*10\s*\^?\s*-?\d+)?)" +
            @"\s*(°?[\p{L}µμÅ°³²Ω]+[0-9³²]*(?:\s*/\s*[\p{L}³²·()]+[0-9³²]*)?)?");

        /// <summary>A number followed by a unit written in Devanagari, Arabic or Han script.</summary>
        private static readonly Regex NumberThenScript =
            JavaRegex.Compile(@"(-?\d+(?:[.,]\d+)?)\s*([^\s\d]{1,8})");

        /// <summary>
        /// Script unit words, longest first.
        ///
        /// <para>Sorted once rather than per match. Android re-sorts the whole table inside the
        /// scan loop, which is a sort per number in the query.</para>
        /// </summary>
        private static readonly KeyValuePair<string, string>[] ScriptUnitsByLength =
            Lexicon.SCRIPT_UNIT_WORDS.OrderByDescending(e => e.Key.Length).ToArray();

        /// <summary>Every quantity in the query, in the order written.</summary>
        public static IReadOnlyList<ScannedQuantity> Scan(string rawQuery)
        {
            var found = new Dictionary<int, ScannedQuantity>();

            foreach (Match match in NumberWithUnit.Matches(rawQuery))
            {
                var unitToken = match.Groups[2].Value;
                if (string.IsNullOrWhiteSpace(unitToken)) continue;
                if (NumberOf(match.Groups[1].Value) is not { } value) continue;
                if (UnitConverter.KnownUnit(unitToken) is not { } unit) continue;
                if (UnitConverter.DimensionOf(unit) is not { } dimension) continue;

                found[match.Index] = new ScannedQuantity(value, unit, dimension, match.Index, match.Length);
            }

            // Scripts that write no space before the unit and no Latin letters in it. The Latin
            // pass above matches only letters, so "11460 साल" presents it with nothing to fold.
            foreach (Match match in NumberThenScript.Matches(rawQuery))
            {
                if (found.ContainsKey(match.Index)) continue;
                if (NumberOf(match.Groups[1].Value) is not { } value) continue;

                var tail = match.Groups[2].Value;
                var unit = ScriptUnitsByLength
                    .FirstOrDefault(e => tail.StartsWith(e.Key, StringComparison.Ordinal))
                    .Value;
                if (unit is null) continue;
                if (UnitConverter.DimensionOf(unit) is not { } dimension) continue;

                found[match.Index] = new ScannedQuantity(value, unit, dimension, match.Index, match.Length);
            }

            return found.Values.OrderBy(q => q.Start).ToList();
        }

        /// <summary>Every quantity of a given dimension, in the order written.</summary>
        public static IReadOnlyList<ScannedQuantity> Of(string rawQuery, Dimension dimension) =>
            Scan(rawQuery).Where(q => q.Dimension == dimension).ToList();

        /// <summary>The first quantity of a given dimension, or null when there is none.</summary>
        public static ScannedQuantity? FirstOf(string rawQuery, Dimension dimension) =>
            Of(rawQuery, dimension).FirstOrDefault();

        /// <summary>
        /// A unit requested as the output, introduced by a preposition: "to °C", "in pm", "साल में".
        ///
        /// <para>Distinct from the quantities above, which each carry a number. A target has none -
        /// that is what makes it the target - so it cannot be told apart from a stray word by shape
        /// alone, and the preposition is the only evidence available. Checked on both sides because
        /// Hindi, Urdu and Filipino put it after.</para>
        /// </summary>
        public static string? TargetUnit(string rawQuery)
        {
            var tokens = TokenPattern.Matches(rawQuery).Select(m => m.Value).ToList();

            // A preposition in front is the ordinary shape and wins outright. The trailing form is
            // checked only afterwards, because Hindi, Urdu and Filipino write it the other way
            // round and would otherwise never resolve a target at all.
            for (var index = 0; index < tokens.Count; index++)
            {
                if (IsSourceQuantity(tokens, index)) continue;
                if (UnitAt(tokens, index) is not { } unit) continue;
                if (IsPreposition(tokens, index - 1)) return unit;
            }
            for (var index = 0; index < tokens.Count; index++)
            {
                if (IsSourceQuantity(tokens, index)) continue;
                if (UnitAt(tokens, index) is not { } unit) continue;
                if (IsPreposition(tokens, index + 1)) return unit;
            }
            return null;
        }

        /// <summary>
        /// A unit sitting right after a number is the value being converted, not the answer being
        /// asked for.
        ///
        /// <para>Without this, "2 eV in kJ/mol" resolves eV - which has "in" on its right and so
        /// satisfies the postpositional rule perfectly - and the conversion runs backwards.</para>
        /// </summary>
        private static bool IsSourceQuantity(IReadOnlyList<string> tokens, int index) =>
            index - 1 >= 0 && NumberPattern.IsMatch(tokens[index - 1]);

        private static string? UnitAt(IReadOnlyList<string> tokens, int index)
        {
            var token = tokens[index];

            // Only a bare number is not a unit. Rejecting anything containing a digit would throw
            // away `kg/m3`, `cm3` and `g/cm3` - the spellings people actually type.
            if (NumberPattern.IsMatch(token)) return null;

            // UNIT_WORDS last: it is the list the property-lookup path already accepts as a
            // conversion target, and it carries the transliterated names - "सेल्सियस", "开尔文" -
            // that neither the converter's aliases nor the script table spell.
            if (UnitConverter.KnownUnit(token) is { } known) return known;
            if (Lexicon.SCRIPT_UNIT_WORDS.TryGetValue(token, out var script)) return script;
            return Lexicon.UNIT_WORDS.TryGetValue(token.ToLowerInvariant(), out var word) ? word : null;
        }

        private static bool IsPreposition(IReadOnlyList<string> tokens, int index) =>
            index >= 0 && index < tokens.Count &&
            Lexicon.UNIT_PREPOSITIONS.Contains(tokens[index].ToLowerInvariant());

        private static readonly Regex TokenPattern = JavaRegex.Compile(@"[^\s,;()]+");

        /// <summary>
        /// A bare number. Anchored, because Kotlin's <c>Regex.matches</c> tests the whole string
        /// while .NET's <c>IsMatch</c> searches - unanchored, "2M" would count as a number and the
        /// unit behind it would be discarded.
        /// </summary>
        private static readonly Regex NumberPattern = JavaRegex.Compile(@"^-?\d+(?:[.,]\d+)?$");

        private static readonly Regex ScientificNotation =
            JavaRegex.Compile(@"^(-?[\d.]+)[×x*]10\^?(-?\d+)$");

        /// <summary>Reads <c>12</c>, <c>12.5</c>, <c>12,5</c> and <c>3.5 × 10^-21</c>.</summary>
        private static double? NumberOf(string raw)
        {
            var text = raw.Replace(",", ".", StringComparison.Ordinal)
                          .Replace(" ", string.Empty, StringComparison.Ordinal);

            var exponent = ScientificNotation.Match(text);
            if (exponent.Success)
            {
                if (!double.TryParse(exponent.Groups[1].Value, NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out var mantissa)) return null;
                if (!int.TryParse(exponent.Groups[2].Value, NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out var power)) return null;
                return mantissa * Math.Pow(10.0, power);
            }

            // InvariantCulture is not optional: the comma has already been rewritten to a point, so
            // a comma-decimal ambient culture would fail to parse the very numbers this normalises.
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
    }
}
