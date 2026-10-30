using System;

namespace Atomic.Core.Progression
{
    /// <summary>
    /// The XP curve: how much total XP each level costs, and which level a given XP total is.
    ///
    /// <para>Pure, so it can be tested and so the persistence layer has nothing to get wrong.</para>
    ///
    /// <para><b>This adopts Android's curve, and that is a correctness fix rather than a balance
    /// preference.</b> The two apps' hand-authored tables for levels 1-21 are identical; only the
    /// tail beyond level 21 differed, where Windows used <c>800·(1.10ⁿ−1)/0.10</c> against Android's
    /// <c>600·(1.02ⁿ−1)/0.02</c>. By level 50 that is 123,480 XP against 27,850. <c>ProgressSyncManager</c>
    /// merges <c>xp</c> and <c>level</c> take-the-max, so two curves plus a max-merge is a data
    /// consistency bug: a Windows player would push an <c>xp</c> that Android reads as a much higher
    /// level, and take back a <c>level</c> disagreeing with its own <c>xp</c>.</para>
    ///
    /// <para>The migration is free and strictly favourable. XP is what is persisted and level is
    /// derived, so no player loses progress and everyone above level 21 gains levels.</para>
    /// </summary>
    public static class XpTable
    {
        /// <summary>Levels are 1-based and stop here, matching Android.</summary>
        public const int MaxLevel = 100;

        /// <summary>
        /// Total XP to reach levels 1 through 21, authored by hand for fine control over the early
        /// game. Identical on both platforms.
        /// </summary>
        private static readonly int[] Authored =
        [
            0,     // Level 1
            90,    // Level 2
            180,   // Level 3
            270,   // Level 4
            360,   // Level 5
            495,   // Level 6
            630,   // Level 7
            765,   // Level 8
            900,   // Level 9
            1050,  // Level 10
            1275,  // Level 11
            1500,  // Level 12
            1725,  // Level 13
            1950,  // Level 14
            2175,  // Level 15
            2550,  // Level 16
            2925,  // Level 17
            3300,  // Level 18
            3675,  // Level 19
            4050,  // Level 20
            4575,  // Level 21
        ];

        /// <summary>Growth per level past the authored table.</summary>
        private const double TailRate = 0.02;

        private const double TailScale = 600.0;

        /// <summary>Total XP needed to reach <paramref name="level"/>.</summary>
        public static int XpForLevel(int level)
        {
            if (level <= 1) return 0;
            if (level - 1 < Authored.Length) return Authored[level - 1];

            var extra = level - Authored.Length;
            return (int)Math.Round(
                Authored[^1] + TailScale * (Math.Pow(1 + TailRate, extra) - 1) / TailRate);
        }

        /// <summary>The level a total XP amount has reached.</summary>
        public static int LevelForXp(int xp)
        {
            var level = 1;
            while (level < MaxLevel && XpForLevel(level + 1) <= xp) level++;
            return level;
        }

        /// <summary>
        /// Progress through the current level, as (earned, needed).
        /// </summary>
        /// <remarks>
        /// Both relative to the level's own start, so a progress bar can bind them directly. At
        /// <see cref="MaxLevel"/> there is no next level, and <c>needed</c> would be zero - callers
        /// divide by it, so it is reported as a full bar instead.
        /// </remarks>
        public static (int Earned, int Needed) ProgressInLevel(int xp)
        {
            var level = LevelForXp(xp);
            var start = XpForLevel(level);

            if (level >= MaxLevel) return (1, 1);

            var next = XpForLevel(level + 1);
            return (xp - start, next - start);
        }

        // ---- Reward multiplier -------------------------------------------------------------

        /// <summary>No bonus. Multipliers below this are meaningless and are clamped up.</summary>
        public const double DefaultMultiplier = 1.0;

        /// <summary>
        /// Apply the persistent reward multiplier to XP earned from a game.
        ///
        /// <para>Only game XP is multiplied. Administrative awards - an exam completion bonus, a
        /// migration - go through unmultiplied, or a player could bank a reward and inflate it
        /// later by claiming another.</para>
        /// </summary>
        public static int WithBonus(int amount, double multiplier) =>
            amount <= 0 ? 0 : (int)Math.Round(amount * Math.Max(multiplier, DefaultMultiplier));
    }
}
