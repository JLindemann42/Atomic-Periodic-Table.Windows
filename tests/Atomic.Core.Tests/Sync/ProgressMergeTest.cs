using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Progression;
using Atomic.Core.Sync;
using Xunit;

namespace Atomic.Core.Tests.Sync
{
    public sealed class ProgressMergeTest
    {
        private static ProgressSnapshot Snapshot(
            int xp = 0, int streak = 0, string? lastPlay = null,
            IReadOnlyList<AchievementProgress>? achievements = null,
            IReadOnlyDictionary<string, int>? statistics = null,
            string? notes = null, IReadOnlyList<string>? chats = null) =>
            new()
            {
                Xp = xp,
                Streak = streak,
                StreakLastPlay = lastPlay,
                Achievements = achievements ?? [],
                Statistics = statistics ?? new Dictionary<string, int>(StringComparer.Ordinal),
                Notes = notes,
                Chats = chats ?? [],
            };

        // ---- The property that makes this safe to run on every launch ---------------------------

        /// <summary>
        /// A merge can only move a counter forward. If this ever fails, some device somewhere loses
        /// progress on a sync - which is the failure this whole class is built to avoid.
        /// </summary>
        [Fact]
        public void MergingNeverLosesGroundOnEitherSide()
        {
            var random = new Random(20260814);

            for (var trial = 0; trial < 500; trial++)
            {
                var local = Random(random);
                var cloud = Random(random);
                var merged = ProgressMerge.Merge(local, cloud);

                Assert.True(merged.Xp >= local.Xp && merged.Xp >= cloud.Xp);
                Assert.True(merged.Streak >= local.Streak && merged.Streak >= cloud.Streak);

                foreach (var side in new[] { local, cloud })
                {
                    foreach (var achievement in side.Achievements)
                    {
                        var kept = merged.Achievements.Single(a => a.Id == achievement.Id);
                        Assert.True(kept.Progress >= achievement.Progress);
                    }

                    foreach (var (id, value) in side.Statistics)
                        Assert.True(merged.Statistics[id] >= value);
                }
            }

            static ProgressSnapshot Random(Random r) => Snapshot(
                xp: r.Next(0, 60000),
                streak: r.Next(0, 400),
                achievements: Enumerable.Range(0, r.Next(0, 6))
                    .Select(i => new AchievementProgress($"ach_{i}", r.Next(0, 50), 50))
                    .ToList(),
                statistics: Enumerable.Range(0, r.Next(0, 6))
                    .ToDictionary(i => $"stat_{i}", _ => r.Next(0, 500), StringComparer.Ordinal));
        }

        /// <summary>Merging is order-independent, or two devices would disagree about the result.</summary>
        [Fact]
        public void MergingIsCommutative()
        {
            var local = Snapshot(
                xp: 4000, streak: 6, lastPlay: "2026-08-13",
                achievements: [new("a", 3, 10), new("b", 9, 10)],
                statistics: new Dictionary<string, int> { ["games"] = 12 },
                notes: "<X>local</X>", chats: ["one", "two"]);

            var cloud = Snapshot(
                xp: 9000, streak: 4, lastPlay: "2026-08-14",
                achievements: [new("a", 7, 10), new("c", 1, 5)],
                statistics: new Dictionary<string, int> { ["games"] = 30, ["wins"] = 4 },
                notes: "<X>a longer cloud note</X>", chats: ["two", "three"]);

            var forward = ProgressMerge.Merge(local, cloud);
            var backward = ProgressMerge.Merge(cloud, local);

            Assert.Equal(forward.Xp, backward.Xp);
            Assert.Equal(forward.Level, backward.Level);
            Assert.Equal(forward.Streak, backward.Streak);
            Assert.Equal(forward.StreakLastPlay, backward.StreakLastPlay);
            Assert.Equal(forward.Notes, backward.Notes);
            Assert.Equal(forward.Achievements, backward.Achievements);
            Assert.Equal(
                forward.Statistics.OrderBy(e => e.Key),
                backward.Statistics.OrderBy(e => e.Key));
        }

        /// <summary>Merging an already-merged result changes nothing, so a repeated sync is free.</summary>
        [Fact]
        public void MergingIsIdempotent()
        {
            var local = Snapshot(xp: 4000, streak: 6, lastPlay: "2026-08-13",
                achievements: [new("a", 3, 10)], chats: ["one"]);
            var cloud = Snapshot(xp: 9000, streak: 4, lastPlay: "2026-08-14",
                achievements: [new("a", 7, 10)], chats: ["two"]);

            var once = ProgressMerge.Merge(local, cloud);
            var twice = ProgressMerge.Merge(once, once);

            Assert.Equal(once.Xp, twice.Xp);
            Assert.Equal(once.Achievements, twice.Achievements);
            Assert.Equal(once.Chats, twice.Chats);
        }

        // ---- The awkward pairs the plan names ---------------------------------------------------

        [Fact]
        public void AnEmptyCloudKeepsEverythingLocal()
        {
            var local = Snapshot(xp: 5000, streak: 9, lastPlay: "2026-08-14",
                achievements: [new("a", 4, 10)],
                statistics: new Dictionary<string, int> { ["games"] = 7 },
                notes: "<X>mine</X>", chats: ["one"]);

            var merged = ProgressMerge.Merge(local, ProgressSnapshot.Empty);

            Assert.Equal(5000, merged.Xp);
            Assert.Equal(9, merged.Streak);
            Assert.Equal("2026-08-14", merged.StreakLastPlay);
            Assert.Equal("<X>mine</X>", merged.Notes);
            Assert.Equal(["one"], merged.Chats);
            Assert.Equal(4, merged.Achievements.Single().Progress);
        }

        [Fact]
        public void AnEmptyLocalTakesEverythingFromTheCloud()
        {
            var cloud = Snapshot(xp: 5000, streak: 9, lastPlay: "2026-08-14",
                achievements: [new("a", 4, 10)], notes: "<X>theirs</X>");

            var merged = ProgressMerge.Merge(ProgressSnapshot.Empty, cloud);

            Assert.Equal(5000, merged.Xp);
            Assert.Equal("<X>theirs</X>", merged.Notes);
            Assert.Equal(4, merged.Achievements.Single().Progress);
        }

        [Fact]
        public void EqualValuesChangeNothing()
        {
            var side = Snapshot(xp: 3000, streak: 5, lastPlay: "2026-08-14",
                achievements: [new("a", 4, 10)]);

            var merged = ProgressMerge.Merge(side, side);

            Assert.Equal(3000, merged.Xp);
            Assert.Equal(5, merged.Streak);
            Assert.Equal(4, merged.Achievements.Single().Progress);
        }

        /// <summary>
        /// An achievement one side has never heard of is not a reset. Absence means the other
        /// platform shipped it first, which happens on every staggered release.
        /// </summary>
        [Fact]
        public void AnAchievementMissingFromOneSideIsKeptRatherThanCleared()
        {
            var local = Snapshot(achievements: [new("only_local", 6, 10)]);
            var cloud = Snapshot(achievements: [new("only_cloud", 2, 5)]);

            var merged = ProgressMerge.Merge(local, cloud);

            Assert.Equal(2, merged.Achievements.Count);
            Assert.Equal(6, merged.Achievements.Single(a => a.Id == "only_local").Progress);
            Assert.Equal(2, merged.Achievements.Single(a => a.Id == "only_cloud").Progress);
        }

        /// <summary>A raised ceiling must not be clamped back by a platform that has not raised it.</summary>
        [Fact]
        public void TheLargerAchievementCeilingWins()
        {
            var merged = ProgressMerge.Merge(
                Snapshot(achievements: [new("a", 5, 10)]),
                Snapshot(achievements: [new("a", 5, 25)]));

            Assert.Equal(25, merged.Achievements.Single().MaxProgress);
        }

        // ---- Level ------------------------------------------------------------------------------

        /// <summary>
        /// Level is derived from the merged XP, not taken as the larger of two stored levels.
        ///
        /// <para>A stored level from the old Windows curve was much lower for the same XP; taking
        /// the max of the two would have kept a level the merged XP does not support. Deriving it
        /// is only unambiguous because P8d put both platforms on one curve.</para>
        /// </summary>
        [Fact]
        public void LevelIsDerivedFromMergedXpRatherThanTakenAsTheLarger()
        {
            var merged = ProgressMerge.Merge(
                new ProgressSnapshot { Xp = 10_428, Level = 99 },
                new ProgressSnapshot { Xp = 4_575, Level = 3 });

            Assert.Equal(10_428, merged.Xp);
            Assert.Equal(XpTable.LevelForXp(10_428), merged.Level);
            Assert.NotEqual(99, merged.Level);
        }

        // ---- Dates ------------------------------------------------------------------------------

        [Theory]
        [InlineData("2026-08-13", "2026-08-14", "2026-08-14")]
        [InlineData("2026-08-14", "2026-08-13", "2026-08-14")]
        [InlineData(null, "2026-08-14", "2026-08-14")]
        [InlineData("2026-08-14", null, "2026-08-14")]
        [InlineData(null, null, null)]
        // A year boundary, where a naive comparison of day-of-month would pick the wrong one.
        [InlineData("2025-12-31", "2026-01-01", "2026-01-01")]
        public void TheLaterPlayDateWins(string? local, string? cloud, string? expected) =>
            Assert.Equal(
                expected,
                ProgressMerge.Merge(Snapshot(lastPlay: local), Snapshot(lastPlay: cloud)).StreakLastPlay);

        /// <summary>
        /// A malformed date must not win by sorting late. String comparison would order
        /// "not-a-date" after every real date and then write that value back to both devices.
        /// </summary>
        [Fact]
        public void AMalformedDateNeverBeatsARealOne()
        {
            Assert.Equal("2026-08-14",
                ProgressMerge.Merge(Snapshot(lastPlay: "not-a-date"), Snapshot(lastPlay: "2026-08-14")).StreakLastPlay);
            Assert.Equal("2026-08-14",
                ProgressMerge.Merge(Snapshot(lastPlay: "2026-08-14"), Snapshot(lastPlay: "zzzz-99-99")).StreakLastPlay);
        }

        // ---- Chats ------------------------------------------------------------------------------

        [Fact]
        public void ChatsAreUnionedLocalFirstAndCapped()
        {
            var local = Snapshot(chats: Enumerable.Range(0, 15).Select(i => $"local{i}").ToList());
            var cloud = Snapshot(chats: Enumerable.Range(0, 15).Select(i => $"cloud{i}").ToList());

            var merged = ProgressMerge.Merge(local, cloud);

            Assert.Equal(ProgressMerge.MaxChats, merged.Chats.Count);
            Assert.Equal("local0", merged.Chats[0]);
            Assert.Contains("cloud0", merged.Chats);
        }

        /// <summary>
        /// A thread synced down and back would otherwise appear twice and eat two of twenty slots.
        /// </summary>
        [Fact]
        public void ARoundTrippedChatIsNotDuplicated()
        {
            var merged = ProgressMerge.Merge(
                Snapshot(chats: ["shared", "mine"]),
                Snapshot(chats: ["shared", "theirs"]));

            Assert.Equal(["shared", "mine", "theirs"], merged.Chats);
        }

        [Fact]
        public void BlankChatsAreDropped()
        {
            var merged = ProgressMerge.Merge(
                Snapshot(chats: ["real", "", "   "]),
                Snapshot(chats: [""]));

            Assert.Equal(["real"], merged.Chats);
        }

        // ---- Notes ------------------------------------------------------------------------------

        /// <summary>
        /// The coarse blob-level rule. The app should prefer NotesCodec.Merge, which merges per
        /// element; this only has to be safe, and safe here means never emptying a populated side.
        /// </summary>
        [Theory]
        [InlineData("short", "a longer note", "a longer note")]
        [InlineData("a longer note", "short", "a longer note")]
        [InlineData(null, "cloud", "cloud")]
        [InlineData("local", null, "local")]
        [InlineData("", "cloud", "cloud")]
        [InlineData(null, null, null)]
        public void NotesTakeTheLongerBlob(string? local, string? cloud, string? expected) =>
            Assert.Equal(expected, ProgressMerge.Merge(Snapshot(notes: local), Snapshot(notes: cloud)).Notes);

        /// <summary>
        /// The two notes paths must agree about which side is richer, or a device would write back
        /// a blob the other has just decided to replace.
        /// </summary>
        [Fact]
        public void TheBlobRuleAgreesWithThePerElementRuleWhenOneSideIsEmpty()
        {
            var populated = new NotesDocument(
                new Dictionary<int, string> { [79] = "gold" },
                new Dictionary<string, string>());

            var merged = NotesCodec.Merge(populated, NotesDocument.Empty);
            Assert.Equal("gold", merged.ByAtomicNumber[79]);

            Assert.Equal(
                "<X>gold</X>",
                ProgressMerge.Merge(Snapshot(notes: "<X>gold</X>"), Snapshot(notes: null)).Notes);
        }
    }
}
