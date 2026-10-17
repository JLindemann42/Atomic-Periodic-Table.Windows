using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Retrieval;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// "Tell me more" - one fact about the element in focus that has not been said yet.
    ///
    /// <para>Android's <c>provideNewInformation</c>. It is the other half of the overview: the
    /// overview says the three or four things worth leading with, and this walks the rest of the
    /// element one turn at a time, so a user can keep saying "more" and keep learning something.
    /// Without it the second "more" repeats the first.</para>
    ///
    /// <para>This class only <b>chooses</b> the field. Rendering goes through
    /// <see cref="Engine.AiEngine.FieldAnswerFor"/>, so the sentence, the unit, the citation and
    /// the PRO gate are the ones a direct question would have got.</para>
    /// </summary>
    public static class ElementMoreInfo
    {
        /// <summary>
        /// Android's <c>isMoreRequest</c> list.
        /// </summary>
        /// <remarks>
        /// Bare "more" is deliberately absent, here as on Android: it is the ordinary comparative
        /// ("which has more protons"), and claiming it would turn a question into a change of
        /// subject. Android has it in a second, looser branch below the field lookup; that branch
        /// is unreachable from here because the engine has already had the query.
        /// </remarks>
        private static readonly string[] MoreWords =
        [
            "additional", "extra", "further", "tell me more", "what else", "deep dive",
            "keep going", "next", "continue", "anything else", "more info",
            "mer info", "beratta mer", "mer om", "vidare", "nasta", "fortsatt",
            "plus", "mehr", "weiter", "sonst noch", "mas", "mais", "altro", "ancora",
            "karagdagan", "aur", "aur batao", "और", "और बताओ", "मजीद",
            "更多", "继续", "还有",
        ];

        /// <summary>
        /// The fields "more" draws on, in Android's order.
        ///
        /// <para>Android's json keys, resolved through <see cref="FieldRegistry.ByJsonKey"/> rather
        /// than restated as Windows field ids, so the two lists cannot drift apart silently.</para>
        /// </summary>
        private static readonly string[] CandidateJsonKeys =
        [
            "element_atomicmass", "element_boiling_celsius", "element_melting_celsius",
            "element_density", "element_electron_config", "element_electronegativty",
            "element_ion_charge", "element_ionization_energy1", "magnetic_type",
            "electrical_type", "element_block", "element_protons", "element_neutron_common",
            "element_electrons", "young_modulus", "poisson_ratio", "earth_crust",
            "crystal_structure", "cas_number", "molar_volume",

            // Android asks for "element_thermal_conductivity", which no element has - the key is
            // "thermal_conductivity", so on Android this candidate is always empty and is silently
            // re-rolled. Thermal conductivity is populated for 94 of the 118.
            "thermal_conductivity", "element_year",
        ];

        /// <summary>The same list as field ids, in order. Public so a test can assert each resolves.</summary>
        public static readonly IReadOnlyList<string> CandidateFieldIds =
            CandidateJsonKeys
                .Select(key => FieldRegistry.ByJsonKey.TryGetValue(key, out var spec) ? spec.Id : null)
                .OfType<string>()
                .ToList();

        /// <summary>
        /// What reads naturally after what.
        ///
        /// <para>Android's four rules. A boiling point invites the melting point far more than it
        /// invites a CAS number, and following one with the other is most of the difference
        /// between a tour and a shuffle.</para>
        /// </summary>
        private static readonly (string After, string[] Prefer)[] ReadsWellAfter =
        [
            ("boiling_point", ["melting_point"]),
            ("melting_point", ["boiling_point", "density"]),
            ("protons", ["common_neutrons", "electrons"]),
            ("atomic_mass", ["density", "abundance_earth_crust"]),
        ];

        /// <summary>Whether this asks for another fact about whatever is already being discussed.</summary>
        public static bool Asks(string? normalizedQuery) =>
            !string.IsNullOrWhiteSpace(normalizedQuery)
            && MoreWords.Any(w => TextMatching.ContainsWord(normalizedQuery, w));

        /// <summary>
        /// The candidate fields this element actually has a value for, in order.
        /// </summary>
        /// <remarks>
        /// Filtered up front. Android picks first and checks the value afterwards, and when the
        /// value is absent it calls itself again - so an element missing eight of the twenty-two
        /// can recurse eight deep to say one sentence.
        /// </remarks>
        public static IReadOnlyList<string> CandidatesFor(ElementRecord element) =>
            CandidateFieldIds.Where(id => !element.Value(id).IsMissing).ToList();

        /// <summary>
        /// The next field to talk about, or null when they have all been covered.
        /// </summary>
        /// <param name="lastShared">The most recent field said about this element, for the pacing above.</param>
        public static string? Next(
            IReadOnlyList<string> candidates,
            Func<string, bool> alreadyShared,
            string? lastShared,
            Random random)
        {
            var remaining = candidates.Where(id => !alreadyShared(id)).ToList();
            if (remaining.Count == 0) return null;

            if (lastShared is not null)
            {
                foreach (var (after, prefer) in ReadsWellAfter)
                {
                    if (!string.Equals(after, lastShared, StringComparison.Ordinal)) continue;

                    var next = prefer.FirstOrDefault(p => remaining.Contains(p, StringComparer.Ordinal));
                    if (next is not null) return next;
                }
            }

            return remaining[random.Next(remaining.Count)];
        }
    }
}
