using System;
using System.Globalization;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace Atomic.App.Services
{
    /// <summary>
    /// Typed access to <see cref="ApplicationData.LocalSettings"/>.
    ///
    /// <para>Settings were previously read inline from ~20 files, which meant key names were
    /// string literals repeated across the codebase - and at least one had already drifted
    /// (ProPage read "IsProPlus" where the licence check wrote "IsProPlusUser", so the property
    /// was permanently false). Keys live here now, once.</para>
    ///
    /// <para>Reads are also defensive. The old pattern was <c>(bool)settings["IsProUser"]</c>,
    /// which throws if the stored value is ever not a bool - possible after a schema change or a
    /// partially-written roaming profile. <see cref="Get{T}"/> falls back instead.</para>
    /// </summary>
    public static class SettingsStore
    {
        private static IPropertySet Values => ApplicationData.Current.LocalSettings.Values;

        // -- keys ------------------------------------------------------------------------------

        public static class Keys
        {
            public const string AppTheme = "AppTheme";

            /// <summary>BCP-47 tag, or empty to follow Windows. See <see cref="AppLanguage"/>.</summary>
            public const string AppLanguage = "AppLanguage";

            public const string DefaultUnit = "DefaultUnit";
            public const string WindowWidth = "WindowWidth";
            public const string WindowHeight = "WindowHeight";
            public const string IsProUser = "IsProUser";
            public const string IsProPlusUser = "IsProPlusUser";
            public const string FavoritePropertyKeys = "FavoritePropertyKeys";
            public const string UserXp = "user_xp";
            public const string Lives = "Lives";
            public const string NextRefillTime = "NextRefillTime";
            public const string LastQuizXp = "LastQuizXP";

            // Progression (P8). LastLifeRefill replaces NextRefillTime, which could only ever
            // describe one pending refill; see ProgressStore.
            public const string LastLifeRefill = "LastLifeRefill";
            public const string XpMultiplier = "XpMultiplier";
            public const string Streak = "Streak";
            public const string BestStreak = "BestStreak";
            public const string LastPlayDate = "LastPlayDate";
            public const string QuizDifficulty = "QuizDifficulty";
            public const string TotalGamesPlayed = "TotalGamesPlayed";
            public const string PerfectGames = "PerfectGames";

            /// <summary>Lowercase element key, or empty for "all". See AlloyPage.</summary>
            public const string AlloyBaseFilter = "AlloyBaseFilter";

            /// <summary>Per-element note, keyed by atomic number.</summary>
            public static string ElementNotes(int atomicNumber) =>
                "ElementNotes_" + atomicNumber.ToString(CultureInfo.InvariantCulture);
        }

        // -- primitives ------------------------------------------------------------------------

        public static T Get<T>(string key, T fallback = default!)
        {
            if (!Values.TryGetValue(key, out var raw) || raw is null)
                return fallback;

            if (raw is T typed)
                return typed;

            // LocalSettings round-trips a narrow set of primitives, and a value written by an
            // older build may not match the type expected now. Prefer the fallback over throwing.
            try
            {
                return (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
            {
                return fallback;
            }
        }

        public static void Set<T>(string key, T value) => Values[key] = value;

        public static bool Contains(string key) => Values.ContainsKey(key);

        public static void Remove(string key) => Values.Remove(key);

        // -- appearance ------------------------------------------------------------------------

        public static string AppTheme
        {
            get => Get(Keys.AppTheme, "Default");
            set => Set(Keys.AppTheme, value);
        }

        public static string DefaultUnit
        {
            get => Get(Keys.DefaultUnit, "Celsius");
            set => Set(Keys.DefaultUnit, value);
        }

        public static int WindowWidth
        {
            get => Get(Keys.WindowWidth, 1400);
            set => Set(Keys.WindowWidth, value);
        }

        public static int WindowHeight
        {
            get => Get(Keys.WindowHeight, 900);
            set => Set(Keys.WindowHeight, value);
        }

        public static bool HasSavedWindowSize =>
            Contains(Keys.WindowWidth) && Contains(Keys.WindowHeight);

        // -- entitlements ----------------------------------------------------------------------

        public static bool IsProUser
        {
            get => Get(Keys.IsProUser, false);
            set => Set(Keys.IsProUser, value);
        }

        public static bool IsProPlusUser
        {
            get => Get(Keys.IsProPlusUser, false);
            set => Set(Keys.IsProPlusUser, value);
        }

        /// <summary>
        /// True for either paid tier. PRO+ implies PRO, so gates that accept "any paid tier"
        /// should use this rather than testing <see cref="IsProUser"/> alone - a PRO+ purchase
        /// does not necessarily set the PRO flag.
        /// </summary>
        public static bool HasAnyPro => IsProUser || IsProPlusUser;

        // -- element detail --------------------------------------------------------------------

        public static string FavoritePropertyKeys
        {
            get => Get(Keys.FavoritePropertyKeys, string.Empty);
            set => Set(Keys.FavoritePropertyKeys, value);
        }

        public static string GetElementNotes(int atomicNumber) =>
            Get(Keys.ElementNotes(atomicNumber), string.Empty);

        public static void SetElementNotes(int atomicNumber, string notes) =>
            Set(Keys.ElementNotes(atomicNumber), notes);

        // -- progression -----------------------------------------------------------------------

        public static int Xp
        {
            get => Get(Keys.UserXp, 0);
            set => Set(Keys.UserXp, value);
        }

        public static int Lives
        {
            get => Get(Keys.Lives, 0);
            set => Set(Keys.Lives, value);
        }

        public static string LastLifeRefill
        {
            get => Get(Keys.LastLifeRefill, string.Empty);
            set => Set(Keys.LastLifeRefill, value);
        }

        /// <summary>The persistent reward bonus, e.g. 1.05 for +5%.</summary>
        public static double XpMultiplier
        {
            get => Get(Keys.XpMultiplier, 1.0);
            set => Set(Keys.XpMultiplier, value);
        }

        public static int Streak
        {
            get => Get(Keys.Streak, 0);
            set => Set(Keys.Streak, value);
        }

        public static int BestStreak
        {
            get => Get(Keys.BestStreak, 0);
            set => Set(Keys.BestStreak, value);
        }

        /// <summary>The last day played, ISO yyyy-MM-dd. See StreakMath for why it is a string.</summary>
        public static string LastPlayDate
        {
            get => Get(Keys.LastPlayDate, string.Empty);
            set => Set(Keys.LastPlayDate, value);
        }

        /// <summary>
        /// "easy", "medium" or "hard".
        ///
        /// <para>Before P8 the quiz hardcoded "easy" and nothing could change it, so the medium and
        /// hard question counts, timers and fairness margins were all unreachable.</para>
        /// </summary>
        public static string QuizDifficulty
        {
            get => Get(Keys.QuizDifficulty, "easy");
            set => Set(Keys.QuizDifficulty, value);
        }

        public static int LastQuizXp
        {
            get => Get(Keys.LastQuizXp, 0);
            set => Set(Keys.LastQuizXp, value);
        }

        public static int TotalGamesPlayed
        {
            get => Get(Keys.TotalGamesPlayed, 0);
            set => Set(Keys.TotalGamesPlayed, value);
        }

        public static int PerfectGames
        {
            get => Get(Keys.PerfectGames, 0);
            set => Set(Keys.PerfectGames, value);
        }

        /// <summary>
        /// Stored as a round-trip ("o") string. Parsing pins InvariantCulture and
        /// <see cref="DateTimeStyles.RoundtripKind"/>: the plain <c>DateTime.TryParse</c> this
        /// replaces used the ambient culture, which misreads the value under a non-Gregorian
        /// calendar and silently hands back a wrong refill time.
        /// </summary>
        public static DateTime NextRefillTime
        {
            get
            {
                var raw = Get(Keys.NextRefillTime, string.Empty);
                return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                                         DateTimeStyles.RoundtripKind, out var parsed)
                    ? parsed
                    : DateTime.UtcNow;
            }
            set => Set(Keys.NextRefillTime, value.ToString("o", CultureInfo.InvariantCulture));
        }
    }
}
