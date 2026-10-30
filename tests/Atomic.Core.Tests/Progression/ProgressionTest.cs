using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Atomic.Core.Progression;
using Xunit;

namespace Atomic.Core.Tests.Progression
{
    public sealed class XpTableTest
    {
        [Fact]
        public void LevelOneCostsNothingAndTheAuthoredTableIsExact()
        {
            Assert.Equal(0, XpTable.XpForLevel(1));
            Assert.Equal(90, XpTable.XpForLevel(2));
            Assert.Equal(1050, XpTable.XpForLevel(10));
            Assert.Equal(4575, XpTable.XpForLevel(21));
        }

        /// <summary>
        /// The curve must never go backwards - a level that costs less than the one below it would
        /// let a player lose a level by gaining XP.
        /// </summary>
        [Fact]
        public void TheCurveIsStrictlyIncreasing()
        {
            for (var level = 2; level <= XpTable.MaxLevel; level++)
                Assert.True(
                    XpTable.XpForLevel(level) > XpTable.XpForLevel(level - 1),
                    $"level {level} costs no more than {level - 1}");
        }

        /// <summary>
        /// The two platforms' authored tables were already identical; only the tail differed. This
        /// pins the adopted tail so a future edit cannot quietly re-diverge - which matters because
        /// ProgressSyncManager merges xp and level take-the-max, and two curves make that merge
        /// inconsistent rather than merely unbalanced. See XpTable.
        /// </summary>
        [Theory]
        [InlineData(22, 5175)]
        [InlineData(30, 10428)]
        [InlineData(40, 18279)]
        [InlineData(50, 27850)]
        public void TheTailIsAndroids(int level, int expected) =>
            Assert.Equal(expected, XpTable.XpForLevel(level));

        [Fact]
        public void LevelAndXpAgreeAtEveryBoundary()
        {
            for (var level = 1; level < XpTable.MaxLevel; level++)
            {
                var cost = XpTable.XpForLevel(level);
                Assert.Equal(level, XpTable.LevelForXp(cost));
                // One short of the next level is still this level.
                Assert.Equal(level, XpTable.LevelForXp(XpTable.XpForLevel(level + 1) - 1));
            }
        }

        [Fact]
        public void LevelIsClampedAtTheTop() =>
            Assert.Equal(XpTable.MaxLevel, XpTable.LevelForXp(int.MaxValue));

        /// <summary>A progress bar divides by the second number, so it must never be zero.</summary>
        [Fact]
        public void ProgressAtMaxLevelIsAFullBarRatherThanADivideByZero()
        {
            var (earned, needed) = XpTable.ProgressInLevel(int.MaxValue);

            Assert.True(needed > 0);
            Assert.Equal(needed, earned);
        }

        [Fact]
        public void ProgressIsRelativeToTheLevelsOwnStart()
        {
            var start = XpTable.XpForLevel(5);
            var (earned, needed) = XpTable.ProgressInLevel(start + 40);

            Assert.Equal(40, earned);
            Assert.Equal(XpTable.XpForLevel(6) - start, needed);
        }

        [Theory]
        [InlineData(100, 1.0, 100)]
        [InlineData(100, 1.05, 105)]
        [InlineData(100, 1.15, 115)]
        // Below the default is clamped up: a multiplier can only ever be a bonus.
        [InlineData(100, 0.5, 100)]
        [InlineData(0, 1.5, 0)]
        [InlineData(-5, 1.5, 0)]
        public void TheRewardMultiplierOnlyEverAdds(int amount, double multiplier, int expected) =>
            Assert.Equal(expected, XpTable.WithBonus(amount, multiplier));
    }

    public sealed class LivesMathTest
    {
        private static readonly DateTimeOffset Now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void MaxLivesFollowsTheTier()
        {
            Assert.Equal(30, LivesMath.MaxLivesFor(isPro: false, isProPlus: false));
            Assert.Equal(60, LivesMath.MaxLivesFor(isPro: true, isProPlus: false));
            Assert.Equal(LivesMath.Unlimited, LivesMath.MaxLivesFor(isPro: true, isProPlus: true));
        }

        [Fact]
        public void OneIntervalGrantsOneRefill()
        {
            var state = LivesMath.Refill(10, Now - LivesMath.RefillInterval, 30, Now);

            Assert.Equal(15, state.Lives);
            Assert.True(state.Changed);
        }

        /// <summary>
        /// The whole reason the baseline is elapsed time rather than an absolute next-refill stamp:
        /// Windows previously awarded at most one refill however long the app had been closed.
        /// </summary>
        [Fact]
        public void AWeekClosedGrantsEveryIntervalAtOnceUpToTheCap()
        {
            var state = LivesMath.Refill(0, Now - TimeSpan.FromDays(7), 30, Now);

            Assert.Equal(30, state.Lives);
        }

        /// <summary>
        /// Partial progress survives a read. Advancing the baseline to `now` would mean a player who
        /// opens the app every nine minutes never refills at all.
        /// </summary>
        [Fact]
        public void ThePartialIntervalIsCarriedRatherThanDiscarded()
        {
            var baseline = Now - LivesMath.RefillInterval - TimeSpan.FromMinutes(9);

            var state = LivesMath.Refill(10, baseline, 30, Now);

            Assert.Equal(15, state.Lives);
            Assert.Equal(baseline + LivesMath.RefillInterval, state.LastRefill);
            // Nine of the next ten minutes are already served.
            Assert.Equal(TimeSpan.FromMinutes(1), LivesMath.NextRefillAt(state.Lives, state.LastRefill, 30, Now) - Now);
        }

        [Fact]
        public void NothingHappensBeforeAFullInterval()
        {
            var state = LivesMath.Refill(10, Now - TimeSpan.FromMinutes(9), 30, Now);

            Assert.Equal(10, state.Lives);
            Assert.False(state.Changed);
        }

        [Fact]
        public void FullLivesNeitherOverflowNorKeepAStaleBaseline()
        {
            var state = LivesMath.Refill(30, Now - TimeSpan.FromDays(1), 30, Now);

            Assert.Equal(30, state.Lives);
            Assert.Equal(Now, state.LastRefill);
            Assert.Null(LivesMath.NextRefillAt(30, Now, 30, Now));
        }

        [Fact]
        public void UnlimitedNeverRefillsAndNeverSchedules()
        {
            var state = LivesMath.Refill(5, null, LivesMath.Unlimited, Now);

            Assert.Equal(5, state.Lives);
            Assert.False(state.Changed);
            Assert.Null(LivesMath.NextRefillAt(5, null, LivesMath.Unlimited, Now));
        }

        /// <summary>
        /// With no baseline and no lives the player is stranded, so one interval is assumed. With
        /// lives in hand there is no honest inference, so the clock simply starts.
        /// </summary>
        [Fact]
        public void AMissingBaselineIsRecoveredWithoutInventingProgress()
        {
            Assert.Equal(5, LivesMath.Refill(0, null, 30, Now).Lives);

            var withLives = LivesMath.Refill(10, null, 30, Now);
            Assert.Equal(10, withLives.Lives);
            Assert.Equal(Now, withLives.LastRefill);
        }

        /// <summary>A clock moving backwards must not award lives or leave a future baseline.</summary>
        [Fact]
        public void AClockThatWentBackwardsAwardsNothing()
        {
            var state = LivesMath.Refill(10, Now + TimeSpan.FromDays(1), 30, Now);

            Assert.Equal(10, state.Lives);
            Assert.Equal(Now, state.LastRefill);
        }
    }

    public sealed class StreakMathTest
    {
        private static DateTimeOffset Day(int year, int month, int day) =>
            new(year, month, day, 18, 0, 0, TimeSpan.Zero);

        [Fact]
        public void AFirstPlayStartsAtOne()
        {
            var state = StreakMath.Record(0, 0, null, Day(2026, 8, 14));

            Assert.Equal(1, state.Current);
            Assert.Equal(1, state.Best);
            Assert.Equal("2026-08-14", state.LastPlayed);
        }

        [Fact]
        public void ConsecutiveDaysExtendAndABreakResets()
        {
            var extended = StreakMath.Record(4, 9, "2026-08-13", Day(2026, 8, 14));
            Assert.Equal(5, extended.Current);
            Assert.Equal(9, extended.Best);

            var broken = StreakMath.Record(4, 9, "2026-08-10", Day(2026, 8, 14));
            Assert.Equal(1, broken.Current);
            Assert.Equal(9, broken.Best);
        }

        [Fact]
        public void PlayingTwiceInADayIsNotALongerStreak()
        {
            var state = StreakMath.Record(5, 9, "2026-08-14", Day(2026, 8, 14));

            Assert.Equal(5, state.Current);
        }

        [Fact]
        public void BestRisesWithTheCurrentStreak() =>
            Assert.Equal(6, StreakMath.Record(5, 5, "2026-08-13", Day(2026, 8, 14)).Best);

        [Fact]
        public void AMalformedLastPlayedResetsRatherThanThrows()
        {
            Assert.Equal(1, StreakMath.Record(7, 7, "not-a-date", Day(2026, 8, 14)).Current);
            Assert.Equal(1, StreakMath.Record(7, 7, "", Day(2026, 8, 14)).Current);
        }

        /// <summary>A stored streak is only true until the day after it was last extended.</summary>
        [Theory]
        [InlineData("2026-08-14", 12)]  // today
        [InlineData("2026-08-13", 12)]  // yesterday, still alive
        [InlineData("2026-08-12", 0)]   // lapsed
        [InlineData("2026-03-01", 0)]
        [InlineData(null, 0)]
        public void AStaleStreakReadsAsZero(string? lastPlayed, int expected) =>
            Assert.Equal(expected, StreakMath.CurrentStreak(12, lastPlayed, Day(2026, 8, 14)));

        /// <summary>
        /// The defect this class exists to avoid.
        ///
        /// <para>Under <c>ur-PK</c>, an ambient-culture <c>DateTime.Parse</c> reads a Hijri calendar
        /// and <c>ToString</c> writes one, so a streak recorded in one culture would never match the
        /// next day's comparison. Every date operation here is explicitly invariant; this runs the
        /// whole sequence under the hostile culture to prove it.</para>
        /// </summary>
        [Theory]
        [InlineData("ur-PK")]
        [InlineData("ar-SA")]
        [InlineData("th-TH")]
        [InlineData("tr-TR")]
        public void TheStreakSurvivesAHostileCalendar(string culture)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                var hostile = new CultureInfo(culture);
                CultureInfo.CurrentCulture = hostile;
                Thread.CurrentThread.CurrentCulture = hostile;

                Assert.Equal("2026-08-14", StreakMath.DayOf(Day(2026, 8, 14)));

                var day1 = StreakMath.Record(0, 0, null, Day(2026, 8, 13));
                var day2 = StreakMath.Record(day1.Current, day1.Best, day1.LastPlayed, Day(2026, 8, 14));

                Assert.Equal(2, day2.Current);
                Assert.Equal(2, StreakMath.CurrentStreak(day2.Current, day2.LastPlayed, Day(2026, 8, 14)));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        /// <summary>A year of daily play must count as a year, across month and year boundaries.</summary>
        [Fact]
        public void AYearOfDailyPlayCountsEveryDay()
        {
            var start = new DateTimeOffset(2025, 12, 20, 18, 0, 0, TimeSpan.Zero);
            var state = new StreakState(0, 0, null!);

            foreach (var offset in Enumerable.Range(0, 400))
            {
                var day = start.AddDays(offset);
                state = StreakMath.Record(state.Current, state.Best, state.LastPlayed, day);
            }

            Assert.Equal(400, state.Current);
            Assert.Equal(400, state.Best);
        }
    }
}
