using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai.Cards
{
    /// <summary>
    /// Runtime switches, kept apart from the mapping so they can be changed without touching it.
    ///
    /// <para>PRO gating is deliberately <b>not</b> here. Cards are selected for every user and
    /// locked at render time, because the upsell only works if the user can see there is something
    /// to unlock - suppressing the card at selection would leave nothing to advertise, and would
    /// also mean an upgrade could not take effect without recomposing the whole answer.</para>
    /// </summary>
    /// <param name="AllowNetworkCards">
    /// False in offline mode. Only the emission spectrum needs the network, and an image that fails
    /// to load silently would leave an empty box with no explanation.
    /// </param>
    public sealed record ChatCardPolicy(bool AllowNetworkCards = true)
    {
        public static readonly ChatCardPolicy Default = new();
    }

    /// <summary>
    /// Chooses the visual that goes with an answer, or none.
    ///
    /// <para><b>The card is derived from the typed <see cref="ExecutionResult"/>, never from the
    /// query text.</b> That is what keeps the picture and the prose in agreement: a card can only
    /// show something the answer has already asserted, and when the engine declines there is no
    /// card at all. Deriving it from the question would let the two drift apart, which is the
    /// failure this whole layer exists to avoid.</para>
    ///
    /// <para>It returns null far more often than not. A wrong card is worse than no card, so every
    /// mapping is guarded by its reducer actually producing data - and a test asserts, over all 118
    /// elements, that no kind is ever emitted whose reducer then returns null.</para>
    /// </summary>
    public static class CardSelector
    {
        public static ChatCard? Select(
            ExecutionResult result,
            QueryPlan plan,
            KnowledgeStore store,
            IStringProvider strings,
            LocalizedView? localized = null,
            ChatCardPolicy? policy = null)
        {
            policy ??= ChatCardPolicy.Default;

            return result switch
            {
                ExecutionResult.Property p => ForField(p.Element, p.FieldId, store, strings, localized),

                ExecutionResult.Safety s => NfpaLabeller.HasAnyRating(s.Nfpa)
                    ? Card(ChatCardKind.NfpaDiamond, s.Element, localized)
                    : null,

                ExecutionResult.Isotopes i => IsotopeSeriesReducer.Of(i.Element) is not null
                    ? Card(ChatCardKind.IsotopeDecay, i.Element, localized)
                    : null,

                // A decay calculation is about one nuclide of one element, so the isotope chart is
                // exactly the right picture - and it is the existing one, guarded by the same
                // reducer. The other three new intents get nothing: a conversion, a balanced
                // equation and a solution have no element subject, and every card kind is
                // per-element.
                ExecutionResult.Decay d => IsotopeSeriesReducer.Of(d.Element) is not null
                    ? Card(ChatCardKind.IsotopeDecay, d.Element, localized)
                    : null,

                // The one card not drawn from the app's own numbers: the spectrum is an image on
                // the author's server, so it is the one the policy can withhold when offline.
                ExecutionResult.EmissionSpectrum e => policy.AllowNetworkCards
                    ? Card(ChatCardKind.EmissionSpectrum, e.Element, localized)
                    : null,

                // A single-element Comparison is what a category lookup produces. More than one
                // element and any single-element card would misattribute half the answer.
                ExecutionResult.Comparison c => c.Elements.Count == 1
                    ? ForCategory(c.Elements[0], c.FieldIds, store, strings, localized)
                    : null,

                // A withheld answer gets the card its unlocked form would have got. This is the case
                // the whole render-time locking scheme exists for: the gate blurs it and offers the
                // upgrade, so the user can see there is something to unlock. Suppressing it here -
                // which is what happened while Locked fell through to null - meant the Poisson band
                // and the hazard diamond, both PRO, were the two cards a free user could never reach
                // by any question.
                ExecutionResult.Locked l => l.Element is null
                    ? null
                    : ForLocked(l.Element, l.FieldIds, store, strings, localized),

                _ => null,
            };
        }

        /// <summary>The card that goes with one field of one element, or none.</summary>
        private static ChatCard? ForField(
            ElementRecord element,
            string fieldId,
            KnowledgeStore store,
            IStringProvider strings,
            LocalizedView? localized) => fieldId switch
        {
            "electron_configuration" or "electron_shells" => ElectronShell(element, localized),

            "crystal_structure" or "lattice_constants" or "space_group_name" or "space_group_number"
                => Crystal(element, localized),

            "poisson_ratio" => PoissonBandReducer.Of(element, store) is not null
                ? Card(ChatCardKind.PoissonBand, element, localized)
                : null,

            "ionization_energy" => IonizationSeriesReducer.Of(element) is not null
                ? Card(ChatCardKind.IonizationSeries, element, localized)
                : null,

            "nfpa_health" or "nfpa_flammability" or "nfpa_instability" or "nfpa_special"
                => Nfpa(element, localized),

            _ => fieldId.StartsWith("abundance_", StringComparison.Ordinal)
                ? Abundance(element, strings, localized)
                : null,
        };

        /// <summary>
        /// The card for an answer held back by the paywall.
        ///
        /// <para>One gated field is a property lookup by another name, so it maps by field; several
        /// are a family - the three NFPA ratings a safety question locks - and map by category.</para>
        /// </summary>
        private static ChatCard? ForLocked(
            ElementRecord element,
            IReadOnlyList<string> fieldIds,
            KnowledgeStore store,
            IStringProvider strings,
            LocalizedView? localized) =>
            // Not a fallback between the two: a single field that maps to no card must stay without
            // one rather than borrowing its family's, which would illustrate a different property
            // than the one the question asked about.
            fieldIds.Count == 1
                ? ForField(element, fieldIds[0], store, strings, localized)
                : ForCategory(element, fieldIds, store, strings, localized);

        private static ChatCard? ForCategory(
            ElementRecord element,
            IReadOnlyList<string> fieldIds,
            KnowledgeStore store,
            IStringProvider strings,
            LocalizedView? localized)
        {
            // The dominant family decides. A category answer is a family of fields, so the card
            // should illustrate the family rather than whichever field happened to sort first.
            var dominant = fieldIds
                .Select(id => FieldRegistry.ById.TryGetValue(id, out var spec) ? spec.Category : (FieldCategory?)null)
                .OfType<FieldCategory>()
                .GroupBy(c => c)
                .OrderByDescending(g => g.Count())
                .Select(g => (FieldCategory?)g.Key)
                .FirstOrDefault();

            return dominant switch
            {
                FieldCategory.Abundance => Abundance(element, strings, localized),
                FieldCategory.Crystal => Crystal(element, localized),
                FieldCategory.Atomic => ElectronShell(element, localized),
                FieldCategory.Safety => Nfpa(element, localized),
                _ => null,
            };
        }

        // ---- Per-kind guards -----------------------------------------------------------------

        private static ChatCard? ElectronShell(ElementRecord element, LocalizedView? localized)
        {
            var shells = (element.Value("electron_shells") as FieldValue.Text)?.Raw;
            if (string.IsNullOrWhiteSpace(shells) || !shells.Any(char.IsDigit)) return null;
            return Card(ChatCardKind.ElectronShell, element, localized);
        }

        private static ChatCard? Crystal(ElementRecord element, LocalizedView? localized)
        {
            var raw = element.Value("crystal_structure") switch
            {
                FieldValue.Enum e => e.Localized,
                FieldValue.Text t => t.Raw,
                _ => null,
            };

            // Null rather than the renderer's silent "Cubic" fallback: in chat the card is the answer.
            return CrystalSystemResolver.Resolve(raw) is null
                ? null
                : Card(ChatCardKind.CrystalStructure, element, localized);
        }

        private static ChatCard? Abundance(
            ElementRecord element, IStringProvider strings, LocalizedView? localized) =>
            AbundanceReducer.Of(element, strings) is not null
                ? Card(ChatCardKind.Abundance, element, localized)
                : null;

        private static ChatCard? Nfpa(ElementRecord element, LocalizedView? localized) =>
            NfpaLabeller.HasAnyRating(element.Nfpa)
                ? Card(ChatCardKind.NfpaDiamond, element, localized)
                : null;

        private static ChatCard Card(ChatCardKind kind, ElementRecord element, LocalizedView? localized)
        {
            var name = localized?.Name(element.Key);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = element.Key.Length == 0
                    ? element.Key
                    : char.ToUpperInvariant(element.Key[0]) + element.Key[1..];
            }
            return new ChatCard(kind, element.Key, name);
        }
    }
}
