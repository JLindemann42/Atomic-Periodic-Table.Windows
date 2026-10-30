using System;
using System.Globalization;

namespace Atomic.Core.Progression
{
    /// <summary>A streak after recording a day of play.</summary>
    public sealed record StreakState(int Current, int Best, string LastPlayed);

    /// <summary>
    /// Daily streak arithmetic.
    ///
    /// <para>Pure, and every date operation is explicitly invariant. That is not decoration: the app
    /// ships in Urdu, and <c>DateTime.Parse</c> under <c>ur-PK</c> reads a <b>Hijri</b> calendar, so
    /// an ambient-culture parse of "2026-08-14" would land in a different year and silently break
    /// every streak for those users. The same applies to <c>ToString</c> - a Hijri-formatted date
    /// written back would never match tomorrow's comparison.</para>
    ///
    /// <para>Days are compared as ISO <c>yyyy-MM-dd</c> strings rather than as instants, matching
    /// Android and matching what is synced. A player crossing midnight mid-game gets the day they
    /// finished in, which is the behaviour anyone would expect.</para>
    /// </summary>
    public static class StreakMath
    {
        /// <summary>Streaks at or above this earn the evening reminder.</summary>
        public const int ReminderThreshold = 3;

        private const string IsoDate = "yyyy-MM-dd";

        /// <summary>Format a day the way it is stored and compared.</summary>
        public static string DayOf(DateTimeOffset moment) =>
            moment.ToString(IsoDate, CultureInfo.InvariantCulture);

        /// <summary>Parse a stored day, or null when it is absent or malformed.</summary>
        public static DateTime? ParseDay(string? day) =>
            !string.IsNullOrWhiteSpace(day) &&
            DateTime.TryParseExact(
                day, IsoDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;

        /// <summary>
        /// Record that the player played on <paramref name="today"/>.
        /// </summary>
        /// <remarks>
        /// Playing twice in one day is not a longer streak - it returns the state unchanged, which
        /// is what stops a player farming the counter by replaying.
        /// </remarks>
        public static StreakState Record(
            int current, int best, string? lastPlayed, DateTimeOffset today)
        {
            var todayKey = DayOf(today);
            if (string.Equals(lastPlayed, todayKey, StringComparison.Ordinal))
                return new StreakState(current, best, todayKey);

            // A malformed or missing last-played date resets rather than throws. It cannot be
            // trusted to order against today, and guessing would be worse than starting over.
            var streak = ParseDay(lastPlayed) is { } last && last.AddDays(1).Date == today.Date
                ? current + 1
                : 1;

            return new StreakState(streak, Math.Max(best, streak), todayKey);
        }

        /// <summary>
        /// The streak as it stands now, which is not always the stored number.
        /// </summary>
        /// <remarks>
        /// A stored streak is only true until the day after it was last extended. Validating on read
        /// is what stops the screen showing "12 day streak" to someone who last played in March -
        /// the stored value is never wrong, it is just stale, and only the reader knows the date.
        /// </remarks>
        public static int CurrentStreak(int stored, string? lastPlayed, DateTimeOffset today)
        {
            if (stored <= 0) return 0;
            if (ParseDay(lastPlayed) is not { } last) return 0;

            var days = (today.Date - last.Date).Days;

            // Today or yesterday keeps it alive; anything older has lapsed. A negative difference
            // means the clock moved backwards, which is not evidence of a streak either.
            return days is 0 or 1 ? stored : 0;
        }
    }
}
