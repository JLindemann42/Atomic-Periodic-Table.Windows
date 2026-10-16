using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Atomic.Core.Text
{
    /// <summary>
    /// Formats Java/Android format strings - <c>%s</c>, <c>%d</c>, <c>%1$s</c>, <c>%.2f</c>,
    /// <c>%%</c> - so that the <c>.resw</c> values can stay byte-identical to Android's
    /// <c>strings.xml</c>.
    ///
    /// <para>The alternative was rewriting the specifiers to .NET's <c>{0}</c> form during
    /// conversion. There are 5,751 positional references across the twelve languages, up to
    /// <c>%7$s</c>, and translators have reordered arguments in languages nobody here can
    /// proofread. A rewrite that mis-numbers one of them yields a grammatical sentence with the
    /// values swapped, which no test would catch. Byte-identical values also make the next
    /// Android string sync a plain diff.</para>
    ///
    /// <para>Formatting is invariant unless a provider is passed. The engine must produce the same
    /// answer regardless of the user's regional settings - a comma decimal separator leaking into
    /// a chemistry value would be wrong, not merely surprising.</para>
    /// </summary>
    public static class JavaFormat
    {
        /// <summary>Java conversion flags. A span so the lookup is unambiguously ordinal.</summary>
        private static ReadOnlySpan<char> FormatFlags => "-#+ 0,(";

        /// <summary>
        /// Formats <paramref name="template"/> with <paramref name="args"/>.
        ///
        /// <para>Never throws. A malformed template or a missing argument yields the template
        /// unchanged, matching <c>AndroidStrings</c> and <c>FakeStrings</c> in the Kotlin sources:
        /// a visible untranslated string is a better failure than a crash mid-conversation.</para>
        /// </summary>
        public static string Format(string template, params object?[] args) =>
            Format(CultureInfo.InvariantCulture, template, args);

        public static string Format(IFormatProvider provider, string template, params object?[] args)
        {
            if (string.IsNullOrEmpty(template) || !template.AsSpan().Contains('%'))
                return template;

            try
            {
                return Run(provider, template, args ?? Array.Empty<object?>());
            }
            catch (Exception e) when (e is FormatException or IndexOutOfRangeException
                                        or ArgumentException or InvalidCastException)
            {
                return template;
            }
        }

        private static string Run(IFormatProvider provider, string template, IReadOnlyList<object?> args)
        {
            var sb = new StringBuilder(template.Length + 16);
            var nextImplicit = 0;
            var i = 0;

            while (i < template.Length)
            {
                var c = template[i];
                if (c != '%')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                i++;
                if (i >= template.Length)
                {
                    sb.Append('%');
                    break;
                }

                // %% - a literal percent. Common in this resource set (86 occurrences).
                if (template[i] == '%')
                {
                    sb.Append('%');
                    i++;
                    continue;
                }

                if (template[i] == 'n')
                {
                    sb.Append('\n');
                    i++;
                    continue;
                }

                var start = i;

                // Argument index: digits followed by '$'. Without it the argument is positional.
                int? index = null;
                var digits = 0;
                while (i + digits < template.Length && char.IsAsciiDigit(template[i + digits]))
                    digits++;
                if (digits > 0 && i + digits < template.Length && template[i + digits] == '$')
                {
                    index = int.Parse(template.AsSpan(i, digits), CultureInfo.InvariantCulture) - 1;
                    i += digits + 1;
                }

                // Flags.
                var groupThousands = false;
                var leftAlign = false;
                var zeroPad = false;
                var plusSign = false;
                while (i < template.Length && FormatFlags.Contains(template[i]))
                {
                    switch (template[i])
                    {
                        case ',': groupThousands = true; break;
                        case '-': leftAlign = true; break;
                        case '0': zeroPad = true; break;
                        case '+': plusSign = true; break;
                    }
                    i++;
                }

                // Width and precision.
                var width = 0;
                while (i < template.Length && char.IsAsciiDigit(template[i]))
                {
                    width = width * 10 + (template[i] - '0');
                    i++;
                }

                int? precision = null;
                if (i < template.Length && template[i] == '.')
                {
                    i++;
                    var p = 0;
                    while (i < template.Length && char.IsAsciiDigit(template[i]))
                    {
                        p = p * 10 + (template[i] - '0');
                        i++;
                    }
                    precision = p;
                }

                if (i >= template.Length)
                {
                    // Ran off the end mid-specifier: emit what was consumed, verbatim.
                    sb.Append('%').Append(template.AsSpan(start));
                    break;
                }

                var conversion = template[i];
                i++;

                // An explicit index does not consume an implicit one - Java's rule, and the
                // reason "%1$s and %s" reads the *first* argument twice.
                var argIndex = index ?? nextImplicit++;

                if (argIndex < 0 || argIndex >= args.Count)
                {
                    // Not enough arguments. Leave the specifier in place so the gap is visible
                    // rather than silently producing a sentence with a hole in it.
                    sb.Append('%').Append(template.AsSpan(start, i - start));
                    continue;
                }

                var value = args[argIndex];
                var text = Convert(provider, value, conversion, precision, groupThousands, plusSign);

                if (width > 0 && text.Length < width)
                    text = leftAlign
                        ? text.PadRight(width)
                        : text.PadLeft(width, zeroPad && conversion != 's' ? '0' : ' ');

                sb.Append(text);
            }

            return sb.ToString();
        }

        private static string Convert(
            IFormatProvider provider, object? value, char conversion,
            int? precision, bool groupThousands, bool plusSign)
        {
            if (value is null)
                return "null";

            string text;
            switch (conversion)
            {
                case 'd':
                    text = value switch
                    {
                        IFormattable f => f.ToString(groupThousands ? "N0" : "D", provider),
                        _ => value.ToString() ?? string.Empty,
                    };
                    break;

                case 'f':
                    var places = precision ?? 6; // Java's default for %f
                    var fmt = (groupThousands ? "N" : "F") + places.ToString(CultureInfo.InvariantCulture);
                    text = value is IFormattable ff ? ff.ToString(fmt, provider) : value.ToString() ?? string.Empty;
                    break;

                case 'e':
                case 'E':
                    var eFmt = (conversion == 'e' ? "e" : "E") + (precision ?? 6).ToString(CultureInfo.InvariantCulture);
                    text = value is IFormattable ef ? ef.ToString(eFmt, provider) : value.ToString() ?? string.Empty;
                    break;

                case 'x':
                case 'X':
                    text = value is IFormattable xf
                        ? xf.ToString(conversion == 'x' ? "x" : "X", provider)
                        : value.ToString() ?? string.Empty;
                    break;

                case 'b':
                case 'B':
                    text = value is bool bv
                        ? (bv ? "true" : "false")
                        : "true"; // Java: any non-null non-Boolean is "true"
                    if (conversion == 'B') text = text.ToUpperInvariant();
                    break;

                case 'S':
                    text = Stringify(provider, value).ToUpperInvariant();
                    break;

                default: // 's' and anything unrecognised
                    text = Stringify(provider, value);
                    if (precision is { } max && text.Length > max)
                        text = text[..max];
                    break;
            }

            if (plusSign && text.Length > 0 && text[0] != '-')
                text = "+" + text;

            return text;
        }

        private static string Stringify(IFormatProvider provider, object value) =>
            value is IFormattable f ? f.ToString(null, provider) : value.ToString() ?? string.Empty;
    }
}
