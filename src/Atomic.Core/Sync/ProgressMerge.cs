using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Progression;

namespace Atomic.Core.Sync
{
    /// <summary>
    /// One achievement's progress, as it crosses the wire.
    /// </summary>
    /// <param name="Id">
    /// Android's numeric id as a string. <b>It must stay parseable as an integer</b>: Android reads
    /// the cloud id with <c>toIntOrNull()</c> and drops any entry that fails, so a readable id
    /// would make the achievement disappear on the phone with no error anywhere.
    /// </param>
    public sealed record AchievementProgress(string Id, int Progress, int MaxProgress)
    {
        /// <summary>
        /// The English title, which Android writes and never reads back.
        /// </summary>
        /// <remarks>
        /// Carried so a Windows write does not strip a field the phone put in the document - the
        /// same rule the update mask exists to enforce at the document level. It takes no part in
        /// merging, which is keyed entirely on <see cref="Id"/>.
        /// </remarks>
        public string Title { get; init; } = string.Empty;
    }

    /// <summary>
    /// Everything the two devices exchange.
    /// </summary>
    /// <param name="StreakLastPlay">ISO <c>yyyy-MM-dd</c>, or null.</param>
    /// <param name="Notes">The raw blob; see <see cref="NotesCodec"/>.</param>
    public sealed record ProgressSnapshot(
        int Xp = 0,
        int Level = 0,
        int Streak = 0,
        string? StreakLastPlay = null,
        IReadOnlyList<AchievementProgress>? Achievements = null,
        IReadOnlyDictionary<string, int>? Statistics = null,
        string? Notes = null,
        string? WindowsTier = null,
        string? AndroidTier = null,
        bool Ultimate = false,
        IReadOnlyDictionary<string, Atomic.Core.Study.CardState>? Study = null)
    {
        /// <summary>The study coach's cards, <c>study.cards</c> (roadmap 2.6; shared data <c>sync/study.md</c>).</summary>
        public IReadOnlyDictionary<string, Atomic.Core.Study.CardState> Study { get; init; } =
            Study ?? new Dictionary<string, Atomic.Core.Study.CardState>(StringComparer.Ordinal);

        public IReadOnlyList<AchievementProgress> Achievements { get; init; } = Achievements ?? [];

        public IReadOnlyDictionary<string, int> Statistics { get; init; } =
            Statistics ?? new Dictionary<string, int>(StringComparer.Ordinal);


        public static readonly ProgressSnapshot Empty = new();
    }

    /// <summary>
    /// Combines local and cloud progress.
    ///
    /// <para><b>This is the one place in the project where a bug destroys user data
    /// irrecoverably</b>, so it is pure, it lives in <c>Atomic.Core</c>, and it is tested before
    /// any network code exists to call it. Every rule below is monotonic - a merge can only ever
    /// move a counter forward - which is what makes it safe to run on every launch and safe to run
    /// twice.</para>
    ///
    /// <para>The rules match Android's <c>ProgressSyncManager</c> exactly. A disagreement would not
    /// show up as an error; it would show up as a number that flips back and forth depending on
    /// which device synced last.</para>
    /// </summary>
    public static class ProgressMerge
    {
        public static ProgressSnapshot Merge(ProgressSnapshot local, ProgressSnapshot cloud) =>
            new()
            {
                Xp = Math.Max(local.Xp, cloud.Xp),

                // Derived from the merged XP rather than max(local, cloud). Taking the larger
                // stored level could keep a level its own XP no longer supports - and after P8d
                // both platforms compute it from the same curve, so deriving is now unambiguous.
                Level = XpTable.LevelForXp(Math.Max(local.Xp, cloud.Xp)),

                Streak = Math.Max(local.Streak, cloud.Streak),
                StreakLastPlay = LaterDay(local.StreakLastPlay, cloud.StreakLastPlay),
                Achievements = MergeAchievements(local.Achievements, cloud.Achievements),
                Statistics = MergeStatistics(local.Statistics, cloud.Statistics),
                Notes = LongerNotes(local.Notes, cloud.Notes),

                // Each platform is the authority on its own tier, so the local value wins for
                // Windows and the cloud value is simply carried for Android. Taking a maximum
                // across them would let a PRO+ phone silently unlock a Windows machine that has
                // paid for nothing - which is what the Ultimate product is for, and it says so
                // explicitly rather than by leaking through a merge.
                WindowsTier = local.WindowsTier ?? cloud.WindowsTier,
                AndroidTier = cloud.AndroidTier ?? local.AndroidTier,

                // Ultimate is bought once and honoured everywhere, so it only ever grants.
                Ultimate = local.Ultimate || cloud.Ultimate,

                Study = MergeStudy(local.Study, cloud.Study),
            };

        /// <summary>
        /// Per card, the later review wins; on a tie the one with more reps, then the cloud's.
        /// Cards are never deleted, so a card on either side is kept (shared data <c>sync/study.md</c>).
        /// </summary>
        public static IReadOnlyDictionary<string, Atomic.Core.Study.CardState> MergeStudy(
            IReadOnlyDictionary<string, Atomic.Core.Study.CardState> local,
            IReadOnlyDictionary<string, Atomic.Core.Study.CardState> cloud) =>
            local.Keys.Union(cloud.Keys, StringComparer.Ordinal).ToDictionary(
                key => key,
                key => Atomic.Core.Study.StudyDeck.Newer(local.GetValueOrDefault(key), cloud.GetValueOrDefault(key))!,
                StringComparer.Ordinal);

        /// <summary>
        /// The later of two ISO days.
        /// </summary>
        /// <remarks>
        /// Compared as parsed dates, not as strings. String comparison happens to work for
        /// <c>yyyy-MM-dd</c>, but it also silently "works" for malformed input, ordering "not-a-date"
        /// after every real one - and that value would then be written back to both devices.
        /// </remarks>
        private static string? LaterDay(string? local, string? cloud)
        {
            var localDay = StreakMath.ParseDay(local);
            var cloudDay = StreakMath.ParseDay(cloud);

            if (localDay is null) return cloudDay is null ? null : cloud;
            if (cloudDay is null) return local;

            return cloudDay > localDay ? cloud : local;
        }

        /// <summary>
        /// Per id, the higher progress; the union of ids.
        /// </summary>
        /// <remarks>
        /// An achievement present on one side and absent on the other is <i>kept</i>, never treated
        /// as a reset. Absence means "that device has not heard of it", which happens whenever one
        /// platform ships an achievement before the other.
        /// </remarks>
        private static IReadOnlyList<AchievementProgress> MergeAchievements(
            IReadOnlyList<AchievementProgress> local, IReadOnlyList<AchievementProgress> cloud)
        {
            var merged = new Dictionary<string, AchievementProgress>(StringComparer.Ordinal);

            foreach (var achievement in local.Concat(cloud))
            {
                if (merged.TryGetValue(achievement.Id, out var existing))
                {
                    merged[achievement.Id] = existing with
                    {
                        Progress = Math.Max(existing.Progress, achievement.Progress),
                        // The larger ceiling wins: a platform that has raised an achievement's
                        // target must not have it clamped back by one that has not.
                        MaxProgress = Math.Max(existing.MaxProgress, achievement.MaxProgress),
                        // Whichever side has one. A blank title from the platform that does not
                        // send them must not erase the other's.
                        Title = existing.Title.Length > 0 ? existing.Title : achievement.Title,
                    };
                }
                else
                {
                    merged[achievement.Id] = achievement;
                }
            }

            return merged.Values
                .OrderBy(a => a.Id, StringComparer.Ordinal)
                .ToList();
        }

        private static IReadOnlyDictionary<string, int> MergeStatistics(
            IReadOnlyDictionary<string, int> local, IReadOnlyDictionary<string, int> cloud)
        {
            var merged = new Dictionary<string, int>(local, StringComparer.Ordinal);

            foreach (var (id, value) in cloud)
            {
                merged[id] = merged.TryGetValue(id, out var existing)
                    ? Math.Max(existing, value)
                    : value;
            }

            return merged;
        }

        /// <summary>
        /// The longer blob wins as a whole.
        /// </summary>
        /// <remarks>
        /// Deliberately coarse, and it is not what the app should use: <see cref="NotesCodec.Merge"/>
        /// merges per element, which is strictly better. This exists because the merged snapshot has
        /// to have <i>some</i> value for callers that have no element map to hand, and because it
        /// matches what Android does when it cannot parse either side.
        /// </remarks>
        private static string? LongerNotes(string? local, string? cloud)
        {
            if (string.IsNullOrEmpty(local)) return cloud;
            if (string.IsNullOrEmpty(cloud)) return local;
            return cloud.Length > local.Length ? cloud : local;
        }
    }
}
