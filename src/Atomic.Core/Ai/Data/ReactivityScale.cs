using System;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// A heuristic score for how reactive an element is.
    ///
    /// <para><b>Not a stored field.</b> Reactivity is derived from group and position rather than
    /// measured, which is exactly why <c>Lexicon.UNBACKED_CONCEPTS</c> lists it and the planner
    /// declines questions about it - the engine cannot answer from data it does not have.</para>
    ///
    /// <para>This is the ~40 lines recovered from Android's <c>AIAgentManager</c>, whose 2,664 lines
    /// are otherwise not ported (D1). Everything else that file did is either covered by the engine
    /// or by the orchestrator; this one function was the single capability that would have been
    /// lost, because "which is more reactive, sodium or potassium" has a real answer and no field
    /// behind it.</para>
    ///
    /// <para>The numbers are ordinal only. They order two elements and nothing more - there is no
    /// unit, no measurement, and comparing scores across groups says less than it appears to.</para>
    /// </summary>
    public static class ReactivityScale
    {
        /// <summary>
        /// Score an element. Higher is more reactive.
        /// </summary>
        /// <param name="series">The canonical series, which is what carries most of the signal.</param>
        /// <param name="atomicNumber">
        /// Breaks ties within a group, and carries the trend: reactivity increases down the alkali
        /// metals and decreases down the halogens.
        /// </param>
        /// <param name="key">
        /// The lowercase English element key. Three elements are special-cased because the group
        /// trend alone would rank them wrongly against each other.
        /// </param>
        public static int Of(SeriesId series, int atomicNumber, string key)
        {
            // Fluorine is the most reactive element there is, and the halogen trend below - which
            // decreases with atomic number - would otherwise put it behind nothing at all. Caesium
            // and francium top the alkali metals for the same reason in the other direction.
            if (string.Equals(key, "fluorine", StringComparison.OrdinalIgnoreCase)) return 200;
            if (string.Equals(key, "caesium", StringComparison.OrdinalIgnoreCase) ||
                // Android spells it the American way; the merged element table uses caesium, so both
                // are accepted rather than letting a spelling decide a chemistry answer.
                string.Equals(key, "cesium", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "francium", StringComparison.OrdinalIgnoreCase)) return 190;

            return series switch
            {
                SeriesId.AlkaliMetal => 100 + atomicNumber,        // increases down the group
                SeriesId.AlkalineEarthMetal => 80 + atomicNumber,
                SeriesId.Halogen => 100 - atomicNumber,            // decreases down the group
                SeriesId.NobleGas => 0,
                _ => 50,
            };
        }

        /// <summary>Score an element record.</summary>
        public static int Of(ElementRecord element) =>
            Of(element.Series, element.AtomicNumber, element.Key);

        /// <summary>
        /// Which of two elements is more reactive, or null when the heuristic cannot separate them.
        ///
        /// <para>Null rather than a coin flip: two elements that score the same are two elements
        /// this scale has nothing to say about, and saying so is the honest answer.</para>
        /// </summary>
        public static ElementRecord? MoreReactive(ElementRecord left, ElementRecord right)
        {
            var l = Of(left);
            var r = Of(right);
            if (l == r) return null;
            return l > r ? left : right;
        }
    }
}
