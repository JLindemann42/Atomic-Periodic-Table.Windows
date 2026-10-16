using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Atomic.Core.Text
{
    /// <summary>
    /// Compiles a <c>java.util.regex</c> pattern so it behaves on .NET the way it does on the JVM.
    ///
    /// <para>Kotlin's <c>Regex</c> is <c>java.util.regex</c> with default flags, where
    /// <c>\d</c>, <c>\w</c>, <c>\s</c> and <c>\b</c> are <b>ASCII-only</b>. .NET's are
    /// Unicode-aware. The difference is not cosmetic, and it fails silently in both directions:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><c>\d</c> on .NET matches ٥ (Arabic-Indic five) and ५ (Devanagari five). The value,
    /// isotope and quantity parsers all use <c>\d</c> and all run against Hindi and Urdu text, so
    /// a number that Java refused to parse suddenly parses - into a different number.</item>
    /// <item><c>\b</c> on .NET is derived from its Unicode <c>\w</c>. For the mole-quantity
    /// pattern <c>mol(?:e|es|i|s)?\b</c> against "5 molé": Java treats é as a non-word character,
    /// finds a boundary, and matches. .NET treats é as a word character, finds no boundary, and
    /// <b>does not match, with no error</b>.</item>
    /// </list>
    ///
    /// <para>Every regex ported from the engine must be built through here rather than with
    /// <c>new Regex(...)</c>.</para>
    /// </summary>
    public static class JavaRegex
    {
        // ASCII definitions, straight from java.util.regex.Pattern's documentation.
        private const string Digit = "0-9";
        private const string Word = "0-9A-Za-z_";
        private const string Space = @" \t\n\x0B\f\r";

        // A boundary between an ASCII word character and anything else, expressed with lookaround
        // so it means the same thing at either end of the input.
        private const string Boundary =
            @"(?:(?<![0-9A-Za-z_])(?=[0-9A-Za-z_])|(?<=[0-9A-Za-z_])(?![0-9A-Za-z_]))";

        private const string NotBoundary =
            @"(?:(?<=[0-9A-Za-z_])(?=[0-9A-Za-z_])|(?<![0-9A-Za-z_])(?![0-9A-Za-z_]))";

        private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> Cache = new();

        /// <summary>
        /// Translates and compiles a Java pattern.
        /// <see cref="RegexOptions.CultureInvariant"/> is always added, so case-insensitive
        /// matching cannot change meaning under a Turkish locale.
        /// </summary>
        public static Regex Compile(string javaPattern, RegexOptions extra = RegexOptions.None)
        {
            var options = extra | RegexOptions.CultureInvariant | RegexOptions.Compiled;
            return Cache.GetOrAdd((javaPattern, options),
                key => new Regex(Translate(key.Pattern), key.Options));
        }

        /// <summary>
        /// Rewrites the ASCII-only shorthand classes. Exposed for testing.
        ///
        /// <para><c>\p{L}</c>, <c>\p{M}</c> and the other Unicode property escapes are left alone:
        /// .NET agrees with Java on those.</para>
        /// </summary>
        public static string Translate(string javaPattern)
        {
            var sb = new StringBuilder(javaPattern.Length + 32);
            var inClass = false;
            var i = 0;

            while (i < javaPattern.Length)
            {
                var c = javaPattern[i];

                if (c == '\\' && i + 1 < javaPattern.Length)
                {
                    var next = javaPattern[i + 1];
                    // Inside a character class the shorthands are members, so they expand to their
                    // contents rather than to a bracketed class. \b means backspace there, and \B
                    // is not valid, so neither is touched.
                    switch (next)
                    {
                        case 'd': sb.Append(inClass ? Digit : "[" + Digit + "]"); i += 2; continue;
                        case 'D': sb.Append(inClass ? "^" + Digit : "[^" + Digit + "]"); i += 2; continue;
                        case 'w': sb.Append(inClass ? Word : "[" + Word + "]"); i += 2; continue;
                        case 'W': sb.Append(inClass ? "^" + Word : "[^" + Word + "]"); i += 2; continue;
                        case 's': sb.Append(inClass ? Space : "[" + Space + "]"); i += 2; continue;
                        case 'S': sb.Append(inClass ? "^" + Space : "[^" + Space + "]"); i += 2; continue;
                        case 'b' when !inClass: sb.Append(Boundary); i += 2; continue;
                        case 'B' when !inClass: sb.Append(NotBoundary); i += 2; continue;
                        default:
                            // Any other escape passes through untouched, including \\ - consuming
                            // both characters is what stops a trailing backslash being misread.
                            sb.Append(c).Append(next);
                            i += 2;
                            continue;
                    }
                }

                if (c == '[' && !inClass) inClass = true;
                else if (c == ']' && inClass) inClass = false;

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Kotlin's <c>matchEntire</c>: the pattern must consume the whole input.
        /// <see cref="Regex.Match(string)"/> succeeds on a partial match, so the two are not
        /// interchangeable.
        /// </summary>
        public static Match? MatchEntire(this Regex regex, string input)
        {
            var m = regex.Match(input);
            return m.Success && m.Index == 0 && m.Length == input.Length ? m : null;
        }

        /// <summary>Kotlin's <c>Regex.matches</c>, which is entire-input rather than partial.</summary>
        public static bool MatchesEntire(this Regex regex, string input) =>
            regex.MatchEntire(input) is not null;
    }
}
