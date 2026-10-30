using System;
using Atomic.Core.Ai;
using Atomic.Core.Licensing;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// The daily chat allowance. Android's numbers: 16 free, 64 PRO, uncounted for PRO+.
    /// </summary>
    public class RateLimitTest
    {
        [Theory]
        [InlineData(EntitlementTier.Free, 16)]
        [InlineData(EntitlementTier.Pro, 64)]
        [InlineData(EntitlementTier.ProPlus, int.MaxValue)]
        public void EachTierGetsAndroidsAllowance(EntitlementTier tier, int expected) =>
            Assert.Equal(expected, RateLimit.DailyLimit(tier));

        [Theory]
        [InlineData(EntitlementTier.Free, 15, true)]
        [InlineData(EntitlementTier.Free, 16, false)]
        [InlineData(EntitlementTier.Free, 99, false)]
        [InlineData(EntitlementTier.Pro, 63, true)]
        [InlineData(EntitlementTier.Pro, 64, false)]
        public void TheLastMessageIsAllowedAndTheNextIsNot(
            EntitlementTier tier, int used, bool canSend) =>
            Assert.Equal(canSend, RateLimit.CanSend(tier, used));

        /// <summary>PRO+ is never counted, however many messages have been sent.</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1000)]
        [InlineData(int.MaxValue - 1)]
        public void ProPlusIsNeverStopped(int used) =>
            Assert.True(RateLimit.CanSend(EntitlementTier.ProPlus, used));

        /// <summary>A count past the limit reports zero left, not a negative number.</summary>
        [Fact]
        public void RemainingNeverGoesNegative() =>
            Assert.Equal(0, RateLimit.Remaining(EntitlementTier.Free, 20));

        /// <summary>
        /// The reset is the next local midnight, not 24 hours out. Someone who runs out at 23:50
        /// gets their messages back in ten minutes.
        /// </summary>
        [Fact]
        public void TheAllowanceComesBackAtMidnightNotInTwentyFourHours()
        {
            var lateEvening = new DateTimeOffset(2026, 3, 14, 23, 50, 0, TimeSpan.FromHours(1));
            var reset = RateLimit.NextReset(lateEvening);

            Assert.Equal(new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.FromHours(1)), reset);
            Assert.True(reset - lateEvening < TimeSpan.FromMinutes(11));
        }

        /// <summary>Midnight itself resets a whole day later, not immediately.</summary>
        [Fact]
        public void MidnightResetsTomorrow()
        {
            var midnight = new DateTimeOffset(2026, 3, 14, 0, 0, 0, TimeSpan.Zero);
            Assert.Equal(
                new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
                RateLimit.NextReset(midnight));
        }

        /// <summary>The offset is the caller's, so the boundary is the user's midnight.</summary>
        [Fact]
        public void TheDayBoundaryIsLocalNotUtc()
        {
            var beforeMidnightInTokyo = new DateTimeOffset(2026, 3, 14, 23, 0, 0, TimeSpan.FromHours(9));
            Assert.Equal(TimeSpan.FromHours(9), RateLimit.NextReset(beforeMidnightInTokyo).Offset);
        }
    }
}