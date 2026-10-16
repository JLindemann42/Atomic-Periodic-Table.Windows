using System.Collections.Generic;
using System.Linq;
using Atomic.App.Services;
using Atomic.Core.Progression;

namespace Atomic.App.Tools
{
    /// <summary>
    /// The achievements as the pages draw them.
    ///
    /// <para>The definitions live in <see cref="Achievements"/> and the progress in
    /// <see cref="AchievementStore"/>; this only joins them into the view model the two screens
    /// bind to. It held a hand-written list of four until the account page needed the same four,
    /// and then the full twelve had to match Android's ids to sync at all.</para>
    ///
    /// <para>The text is English in every language. Android keeps these titles as Kotlin literals
    /// rather than in <c>strings.xml</c>, so there is nothing to borrow and nobody has translated
    /// them on either platform.</para>
    /// </summary>
    public static class AchievementCatalog
    {
        /// <summary>
        /// Every achievement, completed first and then by how close it is.
        /// </summary>
        /// <remarks>
        /// Sorted here rather than at each call site so the flashcards page and the account page
        /// cannot present the same achievements in two different orders.
        /// </remarks>
        public static List<Achievement> Build() =>
            Achievements.All
                .Select(definition => new Achievement
                {
                    Id = definition.Id,
                    Icon = IconFor(definition.Trigger),
                    Header = definition.Title,
                    Description = definition.Description,
                    Progress = AchievementStore.Progress(definition.Id),
                    Goal = definition.Goal,
                })
                .OrderByDescending(a => a.IsCompleted)
                .ThenByDescending(a => a.Goal > 0 ? (double)a.ClampedProgress / a.Goal : 0)
                .ToList();

        /// <summary>
        /// A glyph per kind of achievement, so the list is scannable rather than twelve identical rows.
        /// </summary>
        /// <remarks>
        /// Completed achievements override this with a trophy - see <see cref="Achievement.DisplayIcon"/>.
        /// </remarks>
        private static string IconFor(AchievementTrigger trigger) => trigger switch
        {
            AchievementTrigger.ElementOpened => "\uE8A5",    // document
            AchievementTrigger.TableOpened => "\uE80A",      // grid
            AchievementTrigger.DictionaryOpened => "\uE82D", // book
            AchievementTrigger.RandomElement => "\uE8B1",    // shuffle
            AchievementTrigger.Search => "\uE721",           // magnifier
            AchievementTrigger.Calculation => "\uE8EF",      // calculator
            AchievementTrigger.PerfectGame => "\uEA86",      // the original perfect-game glyph
            _ => "\uE7FC",                                   // the original games glyph
        };
    }
}
