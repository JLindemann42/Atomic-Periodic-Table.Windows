using System;
using System.Globalization;
using Atomic.Core.Progression;

namespace Atomic.App.Services
{
    /// <summary>
    /// Persistence for XP, lives and streaks.
    ///
    /// <para>All the arithmetic lives in <see cref="XpTable"/>, <see cref="LivesMath"/> and
    /// <see cref="StreakMath"/>, which are pure and tested. This class only reads and writes, and
    /// supplies the clock - so "what happens after a week offline" is a unit test rather than
    /// something that can only be found by waiting a week.</para>
    /// </summary>
    public static class ProgressStore
    {
        /// <summary>Overridable so tests and a manual check can drive the clock.</summary>
        public static Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

        // ---- XP --------------------------------------------------------------------------------

        public static int Xp
        {
            get => Math.Max(0, SettingsStore.Xp);
            set => SettingsStore.Xp = Math.Max(0, value);
        }

        public static int Level => XpTable.LevelForXp(Xp);

        public static (int Earned, int Needed) LevelProgress => XpTable.ProgressInLevel(Xp);

        /// <summary>
        /// Award XP that did not come from playing - an exam bonus, a migration.
        /// </summary>
        public static void AddXp(int amount)
        {
            if (amount <= 0) return;
            Xp += amount;
        }

        /// <summary>
        /// Award XP for finishing a game, with the persistent reward multiplier applied.
        ///
        /// <para>Split from <see cref="AddXp"/> deliberately: only game XP is multiplied, or a
        /// player could bank an administrative award and inflate it later by claiming a reward.</para>
        /// </summary>
        public static void AddGameXp(int amount) => AddXp(XpTable.WithBonus(amount, XpMultiplier));

        public static double XpMultiplier
        {
            get => Math.Max(SettingsStore.XpMultiplier, XpTable.DefaultMultiplier);
            set => SettingsStore.XpMultiplier = Math.Max(value, XpTable.DefaultMultiplier);
        }

        // ---- Lives -----------------------------------------------------------------------------

        public static int MaxLives =>
            LivesMath.MaxLivesFor(SettingsStore.HasAnyPro, SettingsStore.IsProPlusUser);

        /// <summary>
        /// The current life count, brought up to date first.
        /// </summary>
        /// <remarks>
        /// Reading refills. That is the whole design - there is no background worker, because lives
        /// can only be spent while the app is open, so computing them on read is always sufficient
        /// and always correct across a restart.
        /// </remarks>
        public static int Lives
        {
            get
            {
                Refill();
                return SettingsStore.Lives;
            }
        }

        /// <summary>Spend lives. Returns false when there are not enough, without spending any.</summary>
        public static bool Spend(int count = 1)
        {
            if (count <= 0) return true;
            if (MaxLives == LivesMath.Unlimited) return true;

            Refill();
            if (SettingsStore.Lives < count) return false;

            // The countdown starts from the first life spent below the cap, so a player at full
            // lives does not lose the interval they were never counting down.
            if (SettingsStore.Lives >= MaxLives) LastRefill = Clock();

            SettingsStore.Lives -= count;
            return true;
        }

        /// <summary>When the next life arrives, for the scheduled toast. Null when full or unlimited.</summary>
        public static DateTimeOffset? NextRefillAt() =>
            LivesMath.NextRefillAt(SettingsStore.Lives, LastRefill, MaxLives, Clock());

        private static void Refill()
        {
            var state = LivesMath.Refill(SettingsStore.Lives, LastRefill, MaxLives, Clock());
            if (!state.Changed) return;

            SettingsStore.Lives = state.Lives;
            LastRefill = state.LastRefill;
        }

        /// <summary>
        /// The refill baseline.
        /// </summary>
        /// <remarks>
        /// Replaces the old absolute <c>NextRefillTime</c>, which could only ever describe one
        /// pending refill and so awarded a single life however long the app had been closed. Stored
        /// round-trippable and parsed invariant - the previous key was written round-trip and read
        /// with the ambient culture, which is a bug this key does not reintroduce.
        /// </remarks>
        private static DateTimeOffset? LastRefill
        {
            get => DateTimeOffset.TryParseExact(
                SettingsStore.LastLifeRefill, "o", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
            set => SettingsStore.LastLifeRefill =
                value?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty;
        }

        // ---- Streak ----------------------------------------------------------------------------

        /// <summary>The streak as it stands today, which is not always the stored number.</summary>
        public static int Streak =>
            StreakMath.CurrentStreak(SettingsStore.Streak, SettingsStore.LastPlayDate, Clock());

        public static int BestStreak => SettingsStore.BestStreak;

        /// <summary>Record a day of play. Returns the streak after recording.</summary>
        public static int RecordPlay()
        {
            var state = StreakMath.Record(
                Streak, SettingsStore.BestStreak, SettingsStore.LastPlayDate, Clock());

            SettingsStore.Streak = state.Current;
            SettingsStore.BestStreak = state.Best;
            SettingsStore.LastPlayDate = state.LastPlayed;

            return state.Current;
        }

        /// <summary>Whether the evening reminder should be scheduled.</summary>
        public static bool WantsStreakReminder => Streak >= StreakMath.ReminderThreshold;
    }
}
