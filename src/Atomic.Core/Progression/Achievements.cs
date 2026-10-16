using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Progression
{
    /// <summary>
    /// The things a user can do that move an achievement or a statistic forward.
    /// </summary>
    /// <remarks>
    /// Android has no such enum - each activity hardcodes the ids it bumps, which is why its
    /// achievements 5 and 8 were declared and then never wired to anything. Naming the events
    /// makes the wiring a lookup, and lets a test assert every declared achievement has a trigger.
    /// </remarks>
    public enum AchievementTrigger
    {
        ElementOpened,
        TableOpened,
        DictionaryOpened,
        RandomElement,
        Search,
        Calculation,
        GamePlayed,
        PerfectGame,
    }

    /// <param name="Id">
    /// Android's numeric id, as a string. <b>It must stay parseable as an integer</b> - Android
    /// reads the cloud id with <c>toIntOrNull()</c> and silently drops any entry that fails, so a
    /// readable id like "perfect_game" would make the achievement vanish on the phone.
    /// </param>
    public sealed record AchievementDefinition(
        string Id,
        string Title,
        string Description,
        int Goal,
        AchievementTrigger Trigger);

    /// <summary>
    /// Every achievement, ported from Android's <c>AchievementModel.getList</c>.
    ///
    /// <para>The titles and descriptions are English on both platforms - Android holds them as
    /// Kotlin literals rather than in <c>strings.xml</c>, so there is nothing to borrow and no
    /// language in which they are translated today.</para>
    ///
    /// <para>Ids 1-8 are the general achievements and 101001-101004 the flashcard ones. The gap is
    /// Android's; it is preserved because the ids are wire identifiers shared with the phone.</para>
    /// </summary>
    public static class Achievements
    {
        public static readonly IReadOnlyList<AchievementDefinition> All =
        [
            new("1", "It's a start!", "Open information about elements 10 times", 10,
                AchievementTrigger.ElementOpened),
            new("2", "Chemical Junior Certified!", "Open information about elements 100 times", 100,
                AchievementTrigger.ElementOpened),
            new("3", "Chemical Professional Certified!", "Open information about elements 1000 times", 1000,
                AchievementTrigger.ElementOpened),
            new("4", "Chemical Obsession Certified!", "Open information about elements 10000 times", 10000,
                AchievementTrigger.ElementOpened),
            new("5", "Master of Table Manners!", "Open different tables a 1000 times", 1000,
                AchievementTrigger.TableOpened),
            new("6", "Starting to get a hang of stuff!", "Enter the dictionary 50 times", 50,
                AchievementTrigger.DictionaryOpened),
            new("7", "Element of Surprise!", "Open a random element from the random button", 1,
                AchievementTrigger.RandomElement),
            new("8", "Your own search engine", "Search 1000 times in the app", 1000,
                AchievementTrigger.Search),

            new("101001", "Perfect Game", "Get all questions correct in a game", 1,
                AchievementTrigger.PerfectGame),
            new("101002", "Quiz Enthusiast", "Play 10 games", 10,
                AchievementTrigger.GamePlayed),
            new("101003", "Quiz Master", "Play 100 games", 100,
                AchievementTrigger.GamePlayed),
            new("101004", "Getting the hang of it", "Play 500 games", 500,
                AchievementTrigger.GamePlayed),
        ];

        /// <summary>The achievements one event advances, in declaration order.</summary>
        public static IReadOnlyList<AchievementDefinition> For(AchievementTrigger trigger) =>
            All.Where(a => a.Trigger == trigger).ToList();

        public static AchievementDefinition? ById(string id) =>
            All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// The usage counters shown on the account screen, keyed as Android keys them.
    /// </summary>
    /// <remarks>
    /// The cloud field is a map of id-string to progress, so these ids are wire identifiers too.
    /// The names beside them are Android's own labels for the three rows.
    /// </remarks>
    public static class UsageStatistics
    {
        /// <summary>Android's <c>element_opened</c>.</summary>
        public const string ElementsOpened = "1";

        /// <summary>Android's <c>calculations</c>.</summary>
        public const string Calculations = "2";

        /// <summary>Android's <c>searches</c>.</summary>
        public const string Searches = "3";

        public static readonly IReadOnlyList<string> All = [ElementsOpened, Calculations, Searches];

        /// <summary>
        /// The statistic an event advances, or null when it advances none.
        /// </summary>
        /// <remarks>
        /// Android increments <c>searches</c> nowhere - the row exists in its model and stays at
        /// zero. Windows does increment it, which the max-merge absorbs safely: the phone reads a
        /// number it never wrote rather than losing one it did.
        /// </remarks>
        public static string? For(AchievementTrigger trigger) => trigger switch
        {
            AchievementTrigger.ElementOpened => ElementsOpened,
            AchievementTrigger.Calculation => Calculations,
            AchievementTrigger.Search => Searches,
            _ => null,
        };
    }
}
