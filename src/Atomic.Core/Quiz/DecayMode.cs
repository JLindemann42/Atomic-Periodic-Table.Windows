using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Atomic.Core.Localization;

namespace Atomic.Core.Quiz
{
    /// <summary>
    /// The decay modes the quiz asks about, canonicalised from the raw <c>decay_type_N</c> strings.
    ///
    /// <para>The data spells the same mode several ways - <c>α</c> / <c>alpha</c> / <c>a</c>, and
    /// <c>EC</c> / <c>ε</c> / <c>e- capture</c> / <c>e-capture</c> - so the raw values cannot be
    /// offered as answer options directly: the same mode would appear twice under two spellings,
    /// and one of them would be marked wrong.</para>
    /// </summary>
    public enum DecayMode
    {
        Alpha,
        BetaMinus,
        BetaPlus,
        ElectronCapture,
        ProtonEmission,
        NeutronEmission,
        SpontaneousFission,
    }

    public static class DecayModes
    {
        public static string LabelKey(this DecayMode mode) => mode switch
        {
            DecayMode.Alpha => S.decay_alpha,
            DecayMode.BetaMinus => S.decay_beta_minus,
            DecayMode.BetaPlus => S.decay_beta_plus,
            DecayMode.ElectronCapture => S.decay_electron_capture,
            DecayMode.ProtonEmission => S.decay_proton_emission,
            DecayMode.NeutronEmission => S.decay_neutron_emission,
            DecayMode.SpontaneousFission => S.decay_spontaneous_fission,
            _ => throw new System.Diagnostics.UnreachableException(),
        };

        /// <summary>
        /// Whether two modes must never be offered in the same question.
        ///
        /// <para>β+ emission and electron capture compete for the same nuclides and the source
        /// records only one of them per isotope. Offering both would mark a physically defensible
        /// answer wrong.</para>
        /// </summary>
        public static bool Confusable(DecayMode a, DecayMode b) =>
            (a is DecayMode.BetaPlus && b is DecayMode.ElectronCapture) ||
            (a is DecayMode.ElectronCapture && b is DecayMode.BetaPlus);

        private static readonly FrozenDictionary<string, DecayMode> Spellings =
            new Dictionary<string, DecayMode>(StringComparer.Ordinal)
            {
                ["α"] = DecayMode.Alpha,
                ["alpha"] = DecayMode.Alpha,
                ["a"] = DecayMode.Alpha,
                ["b-"] = DecayMode.BetaMinus,
                ["β-"] = DecayMode.BetaMinus,
                ["b+"] = DecayMode.BetaPlus,
                ["β+"] = DecayMode.BetaPlus,
                ["ec"] = DecayMode.ElectronCapture,
                ["ε"] = DecayMode.ElectronCapture,
                ["e- capture"] = DecayMode.ElectronCapture,
                ["e-capture"] = DecayMode.ElectronCapture,
                ["p"] = DecayMode.ProtonEmission,
                ["n"] = DecayMode.NeutronEmission,
                ["sf"] = DecayMode.SpontaneousFission,
            }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>
        /// Canonicalise an authored decay string.
        /// </summary>
        /// <returns>
        /// Null for stable isotopes and for the multi-particle variants (<c>2p</c>, <c>3p</c>,
        /// <c>2n</c>, <c>2B-</c>, <c>2EC</c>). Folding those into the single-particle mode would be
        /// wrong physics, and at roughly 30 of 3,131 records they are too rare to deserve their own
        /// answer options.
        /// </returns>
        public static DecayMode? Of(string? raw) =>
            raw is not null && Spellings.TryGetValue(raw.Trim().ToLowerInvariant(), out var mode)
                ? mode
                : null;
    }
}
