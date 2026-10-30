using System.Collections.Generic;
using Atomic.App.Services;
using Atomic.Core.Ai.Compose;
using Atomic.Core.Ai.Data;

namespace Atomic.App.Navigation
{
    /// <summary>
    /// Turns a chat citation into a navigation.
    ///
    /// <para>Every grounded answer carries citations, and a citation the user cannot open is just an
    /// assertion. This is the table that makes "melting point of gold - Periodic table" a chip that
    /// opens gold.</para>
    ///
    /// <para>Kept out of <see cref="NavigationService"/> so the route table stays about the pane and
    /// the breadcrumb; this is about one feature's links.</para>
    /// </summary>
    public static class ChatDeepLinks
    {
        /// <summary>The element key a citation points at, when it points at one.</summary>
        private const string ElementArg = "key";

        /// <summary>
        /// Route tag per deep-link target.
        ///
        /// <para><see cref="DeepLinkTarget.Ion"/> means <i>ionization</i>, not ionic species - it
        /// backs the ionization-energy fields and the ionization dataset, so it opens the ionization
        /// table.</para>
        /// </summary>
        private static readonly IReadOnlyDictionary<DeepLinkTarget, string> Routes =
            new Dictionary<DeepLinkTarget, string>
            {
                [DeepLinkTarget.ElementInfo] = "ElementDetailsPage",
                [DeepLinkTarget.Nuclide] = "NuclidePage",
                [DeepLinkTarget.Emission] = "EmissionPage",
                [DeepLinkTarget.Constants] = "ConstantsPage",
                [DeepLinkTarget.Dictionary] = "DictionaryPage",
                [DeepLinkTarget.Equations] = "EquationsPage",
                [DeepLinkTarget.Poisson] = "PoissonPage",
                [DeepLinkTarget.Geology] = "GeologyPage",
                [DeepLinkTarget.Solubility] = "SolubilityPage",
                [DeepLinkTarget.Ion] = "IonizationPage",
                [DeepLinkTarget.Electrode] = "ElectrochemicalPage",
                [DeepLinkTarget.Ph] = "phPage",
                [DeepLinkTarget.MolarMassCalc] = "CalculatorPage",
                [DeepLinkTarget.UnitConverter] = "UnitConverterPage",
                [DeepLinkTarget.IdealGas] = "IdealGasPage",
                [DeepLinkTarget.ReactionBalancer] = "EquationBalancerPage",
                [DeepLinkTarget.Flashcards] = "FlashcardsPage",
                [DeepLinkTarget.ProPage] = AppRoutes.Pro,

                // LearningGames has a page but no route: it is reached from the flashcards page
                // rather than from the pane, so the flashcards route is where a chip should land.
                [DeepLinkTarget.LearningGames] = "FlashcardsPage",

                // ResendQuery is not a place. No citation in the port produces it - it exists in the
                // enum for a chip that re-asks a question, which the panel handles itself.
                // ReportQuestion is not a place either: the panel asks, then sends the question.
            };

        /// <summary>Whether this action leads anywhere.</summary>
        public static bool CanFollow(ChatAction action) => Routes.ContainsKey(action.Target);

        /// <summary>
        /// Navigate to whatever a citation points at.
        /// </summary>
        /// <returns>False when the target has no page, so the caller can leave the chip inert.</returns>
        public static bool Follow(this NavigationService navigation, ChatAction action)
        {
            if (!Routes.TryGetValue(action.Target, out var tag)) return false;

            // The element pages take the Element object, matching every other call site rather than
            // introducing a second parameter shape only chat would use.
            if (action.Target == DeepLinkTarget.ElementInfo)
            {
                if (!action.Args.TryGetValue(ElementArg, out var key)) return false;

                var element = ElementRepository.Get(key);
                if (element is null) return false;

                return navigation.Navigate(tag, element);
            }

            return navigation.Navigate(tag);
        }
    }
}
