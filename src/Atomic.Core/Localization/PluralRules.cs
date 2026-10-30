using System;

namespace Atomic.Core.Localization
{
    /// <summary>
    /// Which plural form a count takes, per language.
    ///
    /// <para>The converter flattens Android's <c>&lt;plurals&gt;</c> to <c>key.One</c> and
    /// <c>key.Other</c>, so this only has to choose between those two. Choosing by
    /// <c>count == 1</c> everywhere would be wrong in three of the twelve languages the app
    /// ships, and wrong in the quiet way: "0 vie perdue" is correct French and "0 vies perdues"
    /// is not, and nobody who reads only English will ever see it.</para>
    /// </summary>
    public static class PluralRules
    {
        /// <summary>
        /// Whether <paramref name="count"/> takes the <c>one</c> form in <paramref name="language"/>.
        /// </summary>
        /// <param name="language">A base code such as "sv"; a region is tolerated and ignored.</param>
        /// <remarks>
        /// <para>Follows CLDR for the twelve shipped languages:</para>
        /// <list type="bullet">
        /// <item>French, Portuguese and Hindi put <b>zero</b> in the singular alongside one.</item>
        /// <item>Chinese has no singular form at all - every count is <c>other</c>, which is why
        /// its <c>lives_lost_result.One</c> simply does not exist.</item>
        /// <item>Everything else is the familiar <c>count == 1</c>.</item>
        /// </list>
        ///
        /// <para>Filipino is the one approximation. CLDR gives it a broader singular covering 1, 2
        /// and 3 and some digit endings; it is treated as <c>count == 1</c> here, because the rule
        /// is long, the strings it selects between are usually identical, and getting it exactly
        /// right is not something this codebase can verify.</para>
        /// </remarks>
        public static bool IsOne(string? language, int count)
        {
            var code = BaseCode(language);

            return code switch
            {
                "fr" or "pt" or "hi" => count is 0 or 1,

                // No singular form. Guarded here as well as by the missing-key fallback, so the
                // rule is stated rather than left to a lookup happening to fail.
                "zh" or "ja" or "ko" => false,

                _ => count == 1,
            };
        }

        /// <summary>The part before any region: "pt-BR" and "pt" both give "pt".</summary>
        private static string BaseCode(string? language)
        {
            if (string.IsNullOrEmpty(language)) return string.Empty;

            var dash = language.IndexOf('-', StringComparison.Ordinal);
            var code = dash < 0 ? language : language[..dash];

            return code.ToLowerInvariant();
        }

        /// <summary>The resource suffix a count selects: <c>.One</c> or <c>.Other</c>.</summary>
        public static string SuffixFor(string? language, int count) =>
            IsOne(language, count) ? OneSuffix : OtherSuffix;

        public const string OneSuffix = ".One";

        public const string OtherSuffix = ".Other";
    }
}
