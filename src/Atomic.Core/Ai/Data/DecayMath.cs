using System;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// Exponential decay over a known half-life.
    ///
    /// <para>Half-lives arrive from <see cref="IsotopeParser"/>, which has already reconciled the
    /// forty-odd unit spellings, scientific notations and corruptions the <c>iso_half_N</c> data is
    /// authored in. Nothing here re-reads that field: a second parser would be a second set of edge
    /// cases to keep in step, and the two would drift.</para>
    /// </summary>
    public static class DecayMath
    {
        /// <summary>
        /// Past this many half-lives, less than one part in 10^18 is left.
        ///
        /// <para>Reported as "effectively none" rather than as a denormal, because printing
        /// 3.2e-25 g implies a measurement nobody could make and no data in this app
        /// supports.</para>
        /// </summary>
        public const double EffectivelyGone = 60.0;

        /// <summary>How many half-lives fit in an interval. Null when the half-life is not positive.</summary>
        public static double? HalfLives(double elapsedSeconds, double halfLifeSeconds)
        {
            if (halfLifeSeconds <= 0.0 || !double.IsFinite(halfLifeSeconds)) return null;
            if (elapsedSeconds < 0.0 || !double.IsFinite(elapsedSeconds)) return null;
            return elapsedSeconds / halfLifeSeconds;
        }

        /// <summary>The fraction of a sample left after n half-lives: 2^-n.</summary>
        public static double FractionRemaining(double halfLives) => Math.Pow(2.0, -halfLives);

        /// <summary>How much of an initial amount is left after an interval.</summary>
        public static double? Remaining(double initial, double elapsedSeconds, double halfLifeSeconds)
        {
            if (HalfLives(elapsedSeconds, halfLifeSeconds) is not { } n) return null;
            return initial * FractionRemaining(n);
        }

        /// <summary>
        /// How long it takes an initial amount to fall to a remaining amount.
        ///
        /// <para>Null when either amount is not positive, or when the remaining amount exceeds the
        /// initial one. Asking how long until a sample grows has no answer, and the logarithm would
        /// happily supply a negative time for it.</para>
        /// </summary>
        public static double? ElapsedFor(double initial, double remaining, double halfLifeSeconds)
        {
            if (halfLifeSeconds <= 0.0 || !double.IsFinite(halfLifeSeconds)) return null;
            if (initial <= 0.0 || remaining <= 0.0) return null;
            if (remaining > initial) return null;
            return halfLifeSeconds * Math.Log(initial / remaining) / Math.Log(2.0);
        }

        /// <summary>The decay constant λ = ln 2 / t½, so the answer can show its working.</summary>
        public static double? DecayConstant(double halfLifeSeconds) =>
            halfLifeSeconds > 0.0 && double.IsFinite(halfLifeSeconds)
                ? Math.Log(2.0) / halfLifeSeconds
                : null;
    }
}
