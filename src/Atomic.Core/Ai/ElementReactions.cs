using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// "What happens when sodium reacts with water" - what an element does, by where it sits.
    ///
    /// <para>No stored field holds this, so the engine declines it and Android answers it in
    /// <c>handleReactionQuery</c>. There was no Windows equivalent: the question reached the
    /// decline, and for a while it was worse than that - fuzzy keyword matching claimed it as a
    /// request for a list of uses.</para>
    ///
    /// <para>The answers are general chemistry rather than a lookup - alkali metals react
    /// violently with water, noble gases hardly react at all - which is why they are ten authored
    /// strings rather than a table. They are already translated into all twelve languages.</para>
    /// </summary>
    public static class ElementReactions
    {
        private static readonly string[] ReactionWords =
        [
            "react", "mix", "combine", "together", "reaction",
            "reagera", "blanda", "tillsammans", "reaktion",
            "reagieren", "mischen", "zusammen",
            "reagir", "melanger", "ensemble", "reaction",
            "reaccionar", "mezclar", "juntos", "reaccion",
            "reagire", "mescolare", "insieme", "reazione",
            "misturar", "junto", "reacao",
            "प्रतिक्रिया", "अभिक्रिया", "मिलाएं", "साथ",
            "反应", "混合", "结合", "fanying", "hunhe",
            "radd-e-amal", "milana", "reageer", "reaksyon", "paggawa",
        ];

        private static readonly string[] WaterWords =
        [
            "water", "vatten", "wasser", "eau", "agua", "acqua", "agua", "pani", "जल", "पानी", "水",
        ];

        private static readonly string[] OxygenWords =
        [
            "oxygen", "air", "syre", "luft", "sauerstoff", "oxygene", "oxigeno", "ossigeno",
            "ar", "hava", "ऑक्सीजन", "हवा", "氧", "空气",
        ];

        private static readonly string[] AcidWords =
        [
            "acid", "syra", "saure", "acide", "acido", "अम्ल", "तेजाब", "酸",
        ];

        /// <summary>Whether this asks what something reacts with.</summary>
        /// <remarks>
        /// Substring rather than whole-word, as Android has it: the question is almost always
        /// inflected - "reacts", "reaction", "reagerar" - and a word-boundary test matches the
        /// bare stem only, so "what happens when sodium reacts with water" was not recognised.
        /// </remarks>
        public static bool Asks(string? normalizedQuery) =>
            !string.IsNullOrWhiteSpace(normalizedQuery)
            && ReactionWords.Any(w => normalizedQuery.Contains(w, StringComparison.Ordinal));

        /// <summary>
        /// The answer, or null when no element is in play and there is nothing to say.
        /// </summary>
        /// <param name="nameOf">Localizes an element's name for display.</param>
        public static string? Answer(
            IReadOnlyList<ElementRecord> named,
            string normalizedQuery,
            IStringProvider strings,
            Func<ElementRecord, string> nameOf)
        {
            if (named.Count >= 2) return BetweenElements(named[0], named[1], strings, nameOf);
            if (named.Count == 0) return null;

            var element = named[0];
            var name = nameOf(element);

            if (Mentions(normalizedQuery, WaterWords)) return WithWater(element, name, strings);
            if (Mentions(normalizedQuery, OxygenWords)) return strings.Get(S.ai_reaction_oxygen, name);
            if (Mentions(normalizedQuery, AcidWords)) return strings.Get(S.ai_reaction_acid, name);

            return strings.Get(S.ai_reaction_complex, name, strings.Get(element.Series.LabelKey()));
        }

        private static string WithWater(ElementRecord element, string name, IStringProvider strings) =>
            element.Series switch
            {
                SeriesId.AlkaliMetal => strings.Get(S.ai_reaction_alkali_water, name),

                // Magnesium is the exception its own string exists for: slow in cold water,
                // rapid in steam, where the rest of its group react readily.
                SeriesId.AlkalineEarthMetal when string.Equals(element.Key, "magnesium", StringComparison.Ordinal)
                    => strings.Get(S.ai_reaction_magnesium_water, name),

                SeriesId.AlkalineEarthMetal => strings.Get(S.ai_reaction_alkaline_earth_water, name),
                _ => strings.Get(S.ai_reaction_generic_water, name),
            };

        private static string BetweenElements(
            ElementRecord first, ElementRecord second, IStringProvider strings, Func<ElementRecord, string> nameOf)
        {
            var a = nameOf(first);
            var b = nameOf(second);

            var saltFormers =
                (first.Series == SeriesId.AlkaliMetal && IsHalogen(second)) ||
                (second.Series == SeriesId.AlkaliMetal && IsHalogen(first));

            if (saltFormers) return strings.Get(S.ai_reaction_alkali_halogen, a, b);

            // A noble gas in the pair settles it, whichever side it is on.
            if (first.Series == SeriesId.NobleGas) return strings.Get(S.ai_reaction_noble_gas, a);
            if (second.Series == SeriesId.NobleGas) return strings.Get(S.ai_reaction_noble_gas, b);

            return strings.Get(S.ai_reaction_generic_elements, a, b);
        }

        /// <summary>
        /// Group 17, not <see cref="SeriesId.Halogen"/>.
        /// </summary>
        /// <remarks>
        /// The enum has a Halogen member but the shipped element data does not use it - chlorine
        /// is classified ReactiveNonmetal - so matching on the series silently never fired and
        /// sodium and chlorine got the generic answer instead of forming a salt.
        /// </remarks>
        private static bool IsHalogen(ElementRecord element) =>
            element.Series == SeriesId.Halogen || element.GroupNumber == 17;

        private static bool Mentions(string query, IReadOnlyList<string> words) =>
            words.Any(w => TextMatching.ContainsWord(query, w));
    }
}
