using System;

namespace Atomic.Core.Progression
{
    /// <summary>The outcome of recomputing lives against the clock.</summary>
    /// <param name="Lives">Lives after the refill.</param>
    /// <param name="LastRefill">
    /// The new baseline. Advanced by whole intervals only, so partial progress toward the next life
    /// is preserved rather than reset on every read.
    /// </param>
    /// <param name="Changed">Whether anything moved, so a caller can skip a write.</param>
    public sealed record LivesState(int Lives, DateTimeOffset LastRefill, bool Changed);

    /// <summary>
    /// How lives regenerate.
    ///
    /// <para>Pure and computed from <i>elapsed intervals</i> since the last refill, which is the
    /// whole point: Windows previously stored an absolute <c>NextRefillTime</c> and so awarded at
    /// most one refill however long the app had been closed. Deriving from elapsed time is correct
    /// across a restart, a suspend, and a machine that was off for a week.</para>
    ///
    /// <para><b>No background worker.</b> Compute-on-read gives the right count whenever the app is
    /// open, which is the only time it can be spent. Android runs a <c>LivesRefillWorker</c> solely
    /// to fire the "you are low on lives" nudge; on Windows that is a scheduled toast queued at
    /// <see cref="NextRefillAt"/>, which skips the entire background-task apparatus for identical
    /// user-visible behaviour.</para>
    /// </summary>
    public static class LivesMath
    {
        public const int FreeMaxLives = 30;

        public const int ProMaxLives = 60;

        /// <summary>PRO+ has no ceiling; represented as a sentinel rather than a nullable.</summary>
        public const int Unlimited = int.MaxValue;

        public const int RefillAmount = 5;

        public static readonly TimeSpan RefillInterval = TimeSpan.FromMinutes(10);

        public static int MaxLivesFor(bool isPro, bool isProPlus) =>
            isProPlus ? Unlimited : isPro ? ProMaxLives : FreeMaxLives;

        /// <summary>
        /// Bring lives up to date.
        /// </summary>
        /// <param name="lastRefill">
        /// Null when there is no baseline yet - a fresh install, or a profile restored from a sync
        /// that carried lives but not the timestamp.
        /// </param>
        public static LivesState Refill(
            int lives, DateTimeOffset? lastRefill, int maxLives, DateTimeOffset now)
        {
            if (maxLives == Unlimited) return new LivesState(lives, now, Changed: false);

            // Already full: hold the baseline at now, so the countdown starts from the moment a life
            // is actually spent rather than from whenever the last one arrived.
            if (lives >= maxLives) return new LivesState(maxLives, now, lives != maxLives);

            if (lastRefill is not { } baseline)
            {
                // No baseline to measure from. At zero lives, assume an interval has already passed
                // so the player is not stranded by missing bookkeeping; otherwise start the clock
                // now and award nothing, because there is no honest way to infer how long they have
                // been waiting.
                return lives == 0
                    ? Refill(lives, now - RefillInterval, maxLives, now)
                    : new LivesState(lives, now, Changed: true);
            }

            // A clock that moved backwards - a timezone change, a manual correction, a restored
            // backup - must not award lives, and must not leave a baseline in the future either.
            if (now < baseline) return new LivesState(lives, now, Changed: true);

            var intervals = (int)((now - baseline).Ticks / RefillInterval.Ticks);
            if (intervals <= 0) return new LivesState(lives, baseline, Changed: false);

            var refilled = Math.Min(lives + intervals * RefillAmount, maxLives);

            // Advance by whole intervals consumed, not to `now`: resetting to now would discard the
            // remainder every time the app is opened, so a player checking often would never
            // refill at all.
            return new LivesState(refilled, baseline + intervals * RefillInterval, Changed: true);
        }

        /// <summary>
        /// When the next life arrives, or null when lives are full or unlimited.
        /// </summary>
        /// <remarks>
        /// This is what the scheduled toast is queued at, and it is derived from the same baseline
        /// the refill uses - so the notification cannot promise a life the recompute will not grant.
        /// </remarks>
        public static DateTimeOffset? NextRefillAt(
            int lives, DateTimeOffset? lastRefill, int maxLives, DateTimeOffset now)
        {
            if (maxLives == Unlimited || lives >= maxLives) return null;

            var baseline = lastRefill ?? now;
            if (now < baseline) return baseline + RefillInterval;

            var intervals = (int)((now - baseline).Ticks / RefillInterval.Ticks);
            return baseline + (intervals + 1) * RefillInterval;
        }
    }
}
