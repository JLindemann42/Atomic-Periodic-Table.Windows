namespace Atomic.Core.Localization
{
    /// <summary>
    /// Plural lookup for any <see cref="IStringProvider"/>.
    /// </summary>
    /// <remarks>
    /// An extension rather than a member of the interface, because a default interface method is
    /// only reachable through the interface type - and every call site here holds the concrete
    /// <c>ResourceStringProvider</c> that <c>For(...)</c> hands back.
    /// </remarks>
    public static class StringProviderExtensions
    {
        /// <summary>
        /// A plural string, choosing between <c>key.One</c> and <c>key.Other</c> by count.
        /// </summary>
        /// <param name="key">
        /// The family stem, without a suffix - <c>lives_lost_result</c>, not
        /// <c>lives_lost_result.One</c>.
        /// </param>
        /// <remarks>
        /// <para>The count is passed to the format as well as used to pick the form, because every
        /// one of these strings contains it: "Lost %d lives" needs both.</para>
        ///
        /// <para>Falls back to <c>.Other</c> when the singular is absent, which is not an error.
        /// Chinese has no singular form, so <c>lives_lost_result.One</c> genuinely does not exist
        /// there - though <see cref="PluralRules"/> already answers "other" for it before the
        /// lookup is attempted.</para>
        /// </remarks>
        public static string Plural(
            this IStringProvider strings, string key, int count, params object?[] args)
        {
            var one = key + PluralRules.OneSuffix;

            var useOne = PluralRules.IsOne(strings.Language, count) && strings.Has(one);
            var chosen = useOne ? one : key + PluralRules.OtherSuffix;

            return strings.Get(chosen, args is { Length: > 0 } ? args : new object?[] { count });
        }
    }
}
