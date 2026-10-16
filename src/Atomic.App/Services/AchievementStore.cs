using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Progression;

namespace Atomic.App.Services
{
    /// <summary>
    /// Where achievement and statistic progress lives, and the one place that advances it.
    ///
    /// <para>Progress is stored per id rather than derived from a shared counter, which is what
    /// Android does and what the sync requires: the cloud carries each achievement's own progress,
    /// and a merge can raise one without there being any local counter to raise alongside it.</para>
    ///
    /// <para>Keys mirror Android's <c>achievement_{id}</c> and <c>statistic_{id}</c> so the shapes
    /// stay recognisable across the two apps, even though the stores themselves do not meet.</para>
    /// </summary>
    public static class AchievementStore
    {
        /// <summary>Raised after any change, so an open account page can redraw.</summary>
        public static event EventHandler? Changed;

        private static string AchievementKey(string id) => "Achievement_" + id;

        private static string StatisticKey(string id) => "Statistic_" + id;

        public static int Progress(string id) => SettingsStore.Get(AchievementKey(id), 0);

        public static int Statistic(string id) => SettingsStore.Get(StatisticKey(id), 0);

        /// <summary>
        /// Record that something happened: advance every achievement it feeds, and its statistic.
        /// </summary>
        /// <remarks>
        /// One call per user action, exactly as Android does it - opening an element advances four
        /// achievements and one statistic, and they are separate stored values that merely move
        /// together.
        /// </remarks>
        public static void Record(AchievementTrigger trigger, int amount = 1)
        {
            if (amount <= 0) return;

            var moved = false;

            foreach (var definition in Achievements.For(trigger))
                moved |= Advance(definition, amount);

            if (UsageStatistics.For(trigger) is { } statistic)
            {
                SettingsStore.Set(StatisticKey(statistic), Statistic(statistic) + amount);
                moved = true;
            }

            if (moved) Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Move one achievement forward, clamped at its goal.
        /// </summary>
        /// <remarks>
        /// Clamped because the goal is what the account page draws the bar against, and because
        /// Android clamps - an unclamped value would sync over and show as "12000 / 10000".
        /// </remarks>
        private static bool Advance(AchievementDefinition definition, int amount)
        {
            var current = Progress(definition.Id);
            if (current >= definition.Goal) return false;

            SettingsStore.Set(AchievementKey(definition.Id), Math.Min(current + amount, definition.Goal));
            return true;
        }

        /// <summary>
        /// Raise stored progress to match a merge result. Never lowers anything.
        /// </summary>
        /// <remarks>
        /// The merge is already monotonic, but writing the comparison out means a future change on
        /// either side cannot quietly start moving a user's achievements backwards - the one
        /// failure here that a user would experience as lost work.
        /// </remarks>
        public static void ApplyMerged(
            IReadOnlyList<AchievementProgressValue> achievements,
            IReadOnlyDictionary<string, int> statistics)
        {
            var moved = false;

            foreach (var (id, progress) in achievements)
            {
                if (progress <= Progress(id)) continue;

                // Clamped against this build's goal, not the cloud's: an id this build does not
                // know has no goal to clamp to, and storing it would create a phantom the account
                // page cannot draw. It still survives the round trip - see CloudSync.
                var goal = Achievements.ById(id)?.Goal;
                SettingsStore.Set(AchievementKey(id), goal is { } g ? Math.Min(progress, g) : progress);
                moved = true;
            }

            foreach (var (id, value) in statistics)
            {
                if (value <= Statistic(id)) continue;
                SettingsStore.Set(StatisticKey(id), value);
                moved = true;
            }

            if (moved) Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Every known achievement's stored progress, for the sync snapshot.</summary>
        public static IReadOnlyList<AchievementProgressValue> Snapshot() =>
            Achievements.All.Select(a => new AchievementProgressValue(a.Id, Progress(a.Id))).ToList();

        /// <summary>The three usage counters, for the sync snapshot.</summary>
        public static IReadOnlyDictionary<string, int> StatisticsSnapshot() =>
            UsageStatistics.All.ToDictionary(id => id, Statistic, StringComparer.Ordinal);

        /// <summary>
        /// Back-fill the flashcard achievements from the counters that predate them.
        /// </summary>
        /// <remarks>
        /// <para>Games played and perfect games were counted long before achievements had their own
        /// storage, so an existing user has a real history that the per-id keys know nothing about.
        /// Without this their flashcard achievements would read zero and then sync that zero
        /// alongside a level 37 - which is the kind of disagreement a user reads as data loss.</para>
        ///
        /// <para>Raises only, and so is safe to run on every launch.</para>
        /// </remarks>
        public static void BackfillFromLegacyCounters()
        {
            var played = SettingsStore.TotalGamesPlayed;
            var perfect = SettingsStore.PerfectGames;

            var merged = new List<AchievementProgressValue>();

            foreach (var definition in Achievements.For(AchievementTrigger.GamePlayed))
                merged.Add(new AchievementProgressValue(definition.Id, played));

            foreach (var definition in Achievements.For(AchievementTrigger.PerfectGame))
                merged.Add(new AchievementProgressValue(definition.Id, perfect > 0 ? definition.Goal : 0));

            ApplyMerged(merged, new Dictionary<string, int>(StringComparer.Ordinal));
        }
    }

    /// <summary>An id and its progress. Deliberately without the goal, which the catalog owns.</summary>
    public readonly record struct AchievementProgressValue(string Id, int Progress);
}
