using System;
using Atomic.Core.Licensing;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// How many chat messages a day each tier is allowed, and what to say when they run out.
    ///
    /// <para>Pure policy: the count and the clock are the host's, because <c>Atomic.Core</c> has
    /// neither storage nor a time zone. Everything here is a function of a tier, a count and a
    /// <see cref="DateTimeOffset"/> the caller supplies.</para>
    /// </summary>
    public static class RateLimit
    {
        /// <summary>A free user's daily allowance, matching Android's <c>AIRateLimiter</c>.</summary>
        public const int FreeDaily = 16;

        /// <summary>A PRO user's daily allowance.</summary>
        public const int ProDaily = 64;

        /// <summary>PRO+ is uncounted. <see cref="int.MaxValue"/> is the "no limit" sentinel, as on Android.</summary>
        public const int Unlimited = int.MaxValue;

        /// <summary>The daily allowance for a tier.</summary>
        public static int DailyLimit(EntitlementTier tier) => tier switch
        {
            EntitlementTier.ProPlus => Unlimited,
            EntitlementTier.Pro => ProDaily,
            EntitlementTier.Free => FreeDaily,
            _ => FreeDaily,
        };

        /// <summary>Whether another message may be sent.</summary>
        public static bool CanSend(EntitlementTier tier, int usedToday) =>
            DailyLimit(tier) == Unlimited || usedToday < DailyLimit(tier);

        /// <summary>Messages left today; <see cref="Unlimited"/> for PRO+.</summary>
        public static int Remaining(EntitlementTier tier, int usedToday)
        {
            var limit = DailyLimit(tier);
            return limit == Unlimited ? Unlimited : Math.Max(0, limit - usedToday);
        }

        /// <summary>
        /// When the allowance comes back: the next local midnight.
        ///
        /// <para>Derived from the same day boundary the counter is keyed by, rather than adding 24
        /// hours - Android's comment makes the point and it is the reason for telling the user at
        /// all. Someone who runs out at 23:50 gets their messages back in ten minutes, not tomorrow
        /// evening.</para>
        /// </summary>
        public static DateTimeOffset NextReset(DateTimeOffset now) =>
            new DateTimeOffset(now.Date, now.Offset).AddDays(1);

        /// <summary>
        /// The badge under the composer - "12/16", or the localized "Unlimited".
        ///
        /// <para>PRO+ reads "Unlimited" rather than the badge disappearing, which said nothing about
        /// why it had gone.</para>
        /// </summary>
        public static string Badge(IStringProvider strings, EntitlementTier tier, int usedToday)
        {
            var limit = DailyLimit(tier);
            if (limit == Unlimited)
                return strings.Has(S.ai_limit_unlimited) ? strings.Get(S.ai_limit_unlimited) : "\u221E";

            return Remaining(tier, usedToday).ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + "/"
                   + limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The refusal, with the time the allowance returns appended when there is one.
        ///
        /// <para>"You've reached your daily limit of 16 messages" is a dead end on its own; the one
        /// thing the user needs next is when they can carry on.</para>
        /// </summary>
        /// <param name="resetAt">
        /// Already formatted for display by the host, which owns the user's 12/24-hour preference.
        /// </param>
        public static string Reached(IStringProvider strings, EntitlementTier tier, string? resetAt)
        {
            var text = strings.Get(S.ai_rate_limit_reached, DailyLimit(tier));
            if (!string.IsNullOrWhiteSpace(resetAt))
                text += "\n" + strings.Get(S.ai_rate_limit_resets, resetAt);

            return text;
        }
    }
}