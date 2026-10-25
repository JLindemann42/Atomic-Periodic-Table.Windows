using System;
using System.Globalization;
using Atomic.Core.Ai;
using Atomic.Core.Licensing;
using Atomic.Core.Localization;

namespace Atomic.App.Services
{
    /// <summary>
    /// The daily chat allowance: how many messages are left today, and when they come back.
    ///
    /// <para>The policy - the numbers, the wording, the reset instant - lives in
    /// <see cref="RateLimit"/> in <c>Atomic.Core</c>. This owns only the two things Core cannot
    /// have: the stored count and the local clock.</para>
    /// </summary>
    public static class AiChatLimiter
    {
        /// <summary>
        /// Keyed by local calendar date, so the count rolls over at local midnight and yesterday's
        /// key is simply never read again.
        /// </summary>
        /// <remarks>
        /// <see cref="CultureInfo.InvariantCulture"/> is not decoration. Under <c>ur-PK</c>,
        /// <c>ToString("yyyy-MM-dd")</c> with the ambient culture formats in the Hijri calendar -
        /// the same trap <c>StreakMath</c> was written around in P8. An Urdu user would get a key
        /// that never matches the one written yesterday, and a fresh 16 messages on every launch.
        /// </remarks>
        private static string TodayKey(DateTimeOffset now) =>
            "AiMessages_" + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static EntitlementTier Tier => SettingsStore.Entitlement.Tier;

        /// <summary>Messages already sent today.</summary>
        public static int UsedToday(DateTimeOffset now) => SettingsStore.Get(TodayKey(now), 0);

        /// <summary>Today's allowance for the user's tier, for the rate-limited event.</summary>
        public static int DailyLimit => RateLimit.DailyLimit(Tier);

        /// <summary>Whether another message may be sent right now.</summary>
        public static bool CanSend() => RateLimit.CanSend(Tier, UsedToday(DateTimeOffset.Now));

        /// <summary>
        /// Count one sent message.
        ///
        /// <para>PRO+ is never counted, so the key is never written for them - there is nothing to
        /// clean up if they later lapse to PRO, and the day they lapse starts at zero.</para>
        /// </summary>
        public static void Increment()
        {
            if (Tier == EntitlementTier.ProPlus) return;

            var now = DateTimeOffset.Now;
            SettingsStore.Set(TodayKey(now), UsedToday(now) + 1);
        }

        /// <summary>The composer badge - "12/16", or "Unlimited".</summary>
        public static string Badge(IStringProvider strings) =>
            RateLimit.Badge(strings, Tier, UsedToday(DateTimeOffset.Now));

        /// <summary>
        /// The reset time in the user's own format, or null for a tier with nothing to reset.
        ///
        /// <para><c>ShortTimePattern</c> for the display culture follows the user's 12/24-hour
        /// preference, which a hardcoded "HH:mm" would not - the same reason Android asks
        /// <c>DateFormat.getTimeFormat</c> rather than formatting it itself.</para>
        /// </summary>
        public static string? ResetDisplay(IStringProvider strings)
        {
            if (Tier == EntitlementTier.ProPlus) return null;

            var culture = Culture(strings.Language);
            return RateLimit.NextReset(DateTimeOffset.Now)
                .ToString(culture.DateTimeFormat.ShortTimePattern, culture);
        }

        /// <summary>The refusal, with the reset time appended.</summary>
        public static string ReachedMessage(IStringProvider strings) =>
            RateLimit.Reached(strings, Tier, ResetDisplay(strings));

        /// <summary>
        /// The chat's language as a culture, falling back to invariant rather than to the machine's
        /// - the sentence this formats a time into is written in the chat's language, not Windows'.
        /// </summary>
        private static CultureInfo Culture(string language)
        {
            try
            {
                return CultureInfo.GetCultureInfo(language);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }
}