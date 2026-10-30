namespace Atomic.Core.Text
{
    /// <summary>
    /// Stands in for Java's <c>Character.UnicodeScript</c>, which .NET has no equivalent of.
    ///
    /// <para>The engine's tokenizer depends on it in three places, and each one changes the tokens
    /// that come out: Han detection drives CJK bigram emission, Arabic detection drives the Urdu
    /// orthography fold, and Latin detection decides whether a combining mark is an accent to strip
    /// or a Devanagari matra to keep. Getting a range wrong does not throw - it silently produces a
    /// different index from the one Android built, and recall quietly drops.</para>
    ///
    /// <para>The range table is generated from the JDK rather than written by hand; see
    /// <c>tools/port/DumpUnicodeScripts.java</c> and <c>UnicodeScripts.g.cs</c>.</para>
    /// </summary>
    public static partial class UnicodeScripts
    {
        /// <summary>
        /// The script of a UTF-16 code unit.
        ///
        /// <para>Takes a <see cref="char"/>, not a code point, matching the Kotlin caller which
        /// passes <c>Char.code</c>. Surrogates therefore resolve to
        /// <see cref="UnicodeScript.Unknown"/> exactly as they do on the JVM. Do not "improve" this
        /// by combining surrogate pairs: it would change tokenization and diverge from the shipped
        /// index.</para>
        /// </summary>
        public static UnicodeScript Of(char c)
        {
            var lo = 0;
            var hi = Ranges.Length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                ref readonly var range = ref Ranges[mid];
                if (c < range.Start)
                    hi = mid - 1;
                else if (c > range.End)
                    lo = mid + 1;
                else
                    return range.Script;
            }
            return UnicodeScript.Unknown;
        }

        public static bool IsHan(char c) => Of(c) == UnicodeScript.Han;

        public static bool IsLatin(char c) => Of(c) == UnicodeScript.Latin;

        public static bool IsArabic(char c) => Of(c) == UnicodeScript.Arabic;
    }
}
