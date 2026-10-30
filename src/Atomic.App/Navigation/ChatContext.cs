using System.Collections.Generic;
using Atomic.Core.Ai;

namespace Atomic.App.Navigation
{
    /// <summary>
    /// Where the chat is being opened from (roadmap 2.9): the screen, as a <see cref="Screens"/> id,
    /// and the element on it, as a symbol. On Android each screen hosts its own panel and names
    /// itself; here one panel serves every page, so it reads the page it is opened over.
    /// </summary>
    public static class ChatContext
    {
        /// <summary>Route tags to screens. A page not listed, the periodic table among them, has no screen.</summary>
        private static readonly IReadOnlyDictionary<string, string> ScreenByTag = new Dictionary<string, string>
        {
            ["IsotopePage"] = Screens.Nuclides,
            ["IsotopeDetailsPage"] = Screens.Nuclides,
            ["NuclidePage"] = Screens.Nuclides,
            ["phPage"] = Screens.Ph,
            ["ElectrochemicalPage"] = Screens.Electrode,
            ["EquationsPage"] = Screens.Equations,
            ["FormulasDetailsPage"] = Screens.Equations,
            ["SolubilityPage"] = Screens.Solubility,
            ["PoissonPage"] = Screens.Poisson,
            ["PoissonDetailsPage"] = Screens.Poisson,
            ["ConstantsPage"] = Screens.Constants,
            ["GeologyPage"] = Screens.Geology,
            ["GeologyDetailsPage"] = Screens.Geology,
            ["EmissionPage"] = Screens.Emission,
            ["EmissionDetailsPage"] = Screens.Emission,
            ["AlloyPage"] = Screens.Alloys,
            ["DictionaryPage"] = Screens.Dictionary,
            ["CalculatorPage"] = Screens.MolarMass,
            ["UnitConverterPage"] = Screens.UnitConverter,
            ["FlashcardsPage"] = Screens.Games,
            ["EquationBalancerPage"] = Screens.Balancer,
            ["IdealGasPage"] = Screens.IdealGas,
        };

        /// <summary>The screen and element for the page <paramref name="navigation"/> is showing.</summary>
        public static (string? Screen, string? Symbol) Of(NavigationService? navigation)
        {
            if (navigation is null) return (null, null);
            var screen = navigation.CurrentRoute is { } route && ScreenByTag.TryGetValue(route.Tag, out var s) ? s : null;
            var symbol = navigation.CurrentContent is ElementDetailsPage page ? page.Element?.Symbol : null;
            return (screen, symbol);
        }
    }
}
