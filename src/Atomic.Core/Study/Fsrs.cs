using System;

namespace Atomic.Core.Study
{
    /// <summary>
    /// One card's schedule (roadmap 2.6), as it crosses the wire in <c>study.cards</c>. Mirrors
    /// Android's <c>CardState</c>.
    /// </summary>
    /// <param name="Stability">Days until recall falls to 90%.</param>
    /// <param name="Difficulty">1 to 10.</param>
    /// <param name="Due">When the card is next due, epoch ms.</param>
    /// <param name="Last">When it was last reviewed, epoch ms.</param>
    public sealed record CardState(double Stability, double Difficulty, long Due, long Last, int Reps, int Lapses);

    /// <summary>
    /// FSRS-5 with its published default parameters, rated Again or Good, since a quiz answer is
    /// only right or wrong. Specified in the shared data's <c>sync/study.md</c>;
    /// <c>tools/fsrs_golden.py</c> is the reference, and <c>golden/fsrs.tsv</c> pins this and
    /// Android's <c>Fsrs.kt</c> to it.
    /// </summary>
    public static class Fsrs
    {
        public const int Again = 1;
        public const int Good = 3;
        public const long DayMs = 86_400_000L;

        private static readonly double[] W =
        [
            0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575, 0.1192,
            1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655, 0.6621,
        ];

        private const double Decay = -0.5;
        private const double Factor = 19.0 / 81.0;
        private const double Retention = 0.9;
        private const double MinStability = 0.01;
        private const int MaxInterval = 36_500;

        /// <summary>The card after a review rated <paramref name="rating"/> at <paramref name="now"/>, or a new card's first schedule.</summary>
        public static CardState Review(CardState? card, int rating, long now)
        {
            double stability, difficulty;
            if (card is null)
            {
                stability = InitialStability(rating);
                difficulty = InitialDifficulty(rating);
            }
            else
            {
                var elapsed = (double)(now - card.Last) / DayMs;
                stability = elapsed < 1
                    ? card.Stability * Math.Exp(W[17] * (rating - 3 + W[18]))
                    : rating > 1
                        ? RecallStability(card.Difficulty, card.Stability, Retrievability(elapsed, card.Stability), rating)
                        : ForgetStability(card.Difficulty, card.Stability, Retrievability(elapsed, card.Stability));
                difficulty = NextDifficulty(card.Difficulty, rating);
            }

            var s = Math.Max(stability, MinStability);
            return new CardState(
                s, difficulty, now + IntervalDays(s) * DayMs, now,
                (card?.Reps ?? 0) + 1,
                (card?.Lapses ?? 0) + (rating == Again ? 1 : 0));
        }

        /// <summary>The scheduled interval for a stability, in whole days, rounded half up.</summary>
        public static long IntervalDays(double stability)
        {
            var raw = stability / Factor * (Math.Pow(Retention, 1 / Decay) - 1);
            return (long)Math.Min(Math.Max(Math.Floor(raw + 0.5), 1.0), MaxInterval);
        }

        private static double ClampDifficulty(double d) => Math.Min(Math.Max(d, 1.0), 10.0);

        private static double InitialStability(int g) => Math.Max(W[g - 1], MinStability);

        private static double InitialDifficulty(int g) => ClampDifficulty(W[4] - Math.Exp(W[5] * (g - 1)) + 1);

        private static double Retrievability(double elapsedDays, double s) => Math.Pow(1 + Factor * elapsedDays / s, Decay);

        private static double NextDifficulty(double d, int g)
        {
            var damped = d + (-W[6] * (g - 3)) * (10 - d) / 9;
            return ClampDifficulty(W[7] * InitialDifficulty(4) + (1 - W[7]) * damped);
        }

        private static double RecallStability(double d, double s, double r, int g)
        {
            var hard = g == 2 ? W[15] : 1.0;
            var easy = g == 4 ? W[16] : 1.0;
            return s * (Math.Exp(W[8]) * (11 - d) * Math.Pow(s, -W[9]) * (Math.Exp(W[10] * (1 - r)) - 1) * hard * easy + 1);
        }

        private static double ForgetStability(double d, double s, double r) =>
            Math.Min(W[11] * Math.Pow(d, -W[12]) * (Math.Pow(s + 1, W[13]) - 1) * Math.Exp(W[14] * (1 - r)), s);
    }
}
