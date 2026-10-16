using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.App.Tables;
using Atomic.App.Tools;
using Microsoft.UI.Xaml.Controls;

namespace Atomic.App.Navigation
{
    /// <summary>
    /// Every navigable destination in the app, in pane order.
    ///
    /// <para>Adding a page used to mean four edits - a NavigationViewItem in MainWindow.xaml, an
    /// entry in a tuple array, and an arm in each of two switch statements - with nothing to catch
    /// a half-finished registration. Now it is one entry here.</para>
    /// </summary>
    public static class AppRoutes
    {
        public const string PeriodicTable = "PeriodicTablePage";
        public const string Settings = "SettingsPage";
        public const string Pro = "ProPage";

        public static readonly IReadOnlyList<RouteDescriptor> All = new RouteDescriptor[]
        {
            // ---- Main -------------------------------------------------------------------
            new()
            {
                Tag = PeriodicTable,
                PageType = typeof(PeriodicTablePage),
                Title = "Periodic Table",
                Group = NavGroup.Main,
                Symbol = Microsoft.UI.Xaml.Controls.Symbol.Home,
                WideContent = true,
            },

            // ---- Tables -----------------------------------------------------------------
            new() { Tag = "IsotopePage",         PageType = typeof(IsotopePage),         Title = "Isotopes",               Group = NavGroup.Tables },
            new() { Tag = "phPage",              PageType = typeof(phPage),              Title = "pH-Indicators",          Group = NavGroup.Tables },
            new() { Tag = "ElectrochemicalPage", PageType = typeof(ElectrochemicalPage), Title = "Electrochemical Series", Group = NavGroup.Tables },
            new() { Tag = "EquationsPage",       PageType = typeof(FormulasPage),        Title = "Equations",              Group = NavGroup.Tables },
            new() { Tag = "IonizationPage",      PageType = typeof(IonizationPage),      Title = "Ionization Energies",    Group = NavGroup.Tables },
            new() { Tag = "SolubilityPage",      PageType = typeof(SolubilityPage),      Title = "Solubility",             Group = NavGroup.Tables },
            new() { Tag = "PoissonPage",         PageType = typeof(PoissonPage),         Title = "Poisson's Ratio",        Group = NavGroup.Tables },
            new() { Tag = "NuclidePage",         PageType = typeof(NuclidePage),         Title = "Nuclide Table",          Group = NavGroup.Tables, WideContent = true },
            new() { Tag = "ConstantsPage",       PageType = typeof(ConstantsPage),       Title = "Constants",              Group = NavGroup.Tables },
            new() { Tag = "GeologyPage",         PageType = typeof(GeologyPage),         Title = "Geology Table",          Group = NavGroup.Tables },
            new() { Tag = "EmissionPage",        PageType = typeof(EmissionPage),        Title = "Emission Spectrums",     Group = NavGroup.Tables },

            // ---- Tools ------------------------------------------------------------------
            new() { Tag = "DictionaryPage",    PageType = typeof(DictionaryPage),    Title = "Dictionary",     Group = NavGroup.Tools, Glyph = "", WideContent = true },
            new() { Tag = "CalculatorPage",    PageType = typeof(CalculatorPage),    Title = "Calculator",     Group = NavGroup.Tools, Symbol = Microsoft.UI.Xaml.Controls.Symbol.Calculator },
            new() { Tag = "UnitConverterPage", PageType = typeof(UnitConverterPage), Title = "Unit Converter", Group = NavGroup.Tools, Glyph = "" },
            new() { Tag = "FlashcardsPage",    PageType = typeof(FlashCardsPage),    Title = "Flashcards",     Group = NavGroup.Tools, Glyph = "" },

            // Hidden: the WebView2 host currently points at a placeholder URL.
            new() { Tag = "BlogPage", PageType = typeof(BlogPage), Title = "Blog", Symbol = Microsoft.UI.Xaml.Controls.Symbol.Globe },

            // ---- Footer -----------------------------------------------------------------
            new()
            {
                Tag = Pro,
                PageType = typeof(ProPage),
                Title = "PRO Version",
                NavLabel = "Get PRO Version",
                Group = NavGroup.Footer,
                Symbol = Microsoft.UI.Xaml.Controls.Symbol.Add,
            },

            // ---- Reached by navigation, not from the pane --------------------------------
            new() { Tag = "SettingsPage",        PageType = typeof(SettingsPage),        Title = "Settings" },
            new() { Tag = "ElementDetailsPage",  PageType = typeof(ElementDetailsPage),  Title = "Element Details",                 ParentTag = PeriodicTable },
            new() { Tag = "PoissonDetailsPage",  PageType = typeof(PoissonDetailsPage),  Title = "Poisson's Ratio Details",         ParentTag = "PoissonPage" },
            new() { Tag = "GeologyDetailsPage",  PageType = typeof(GeologyDetailsPage),  Title = "Geology Table Details",           ParentTag = "GeologyPage" },
            new() { Tag = "FormulasDetailsPage", PageType = typeof(FormulasDetailsPage), Title = "Equations Details",               ParentTag = "EquationsPage" },
            new() { Tag = "EmissionDetailsPage", PageType = typeof(EmissionDetailsPage), Title = "Emission Spectrum Table Details", ParentTag = "EmissionPage" },

            // Both of these are reachable from more than one parent, so the crumb is resolved
            // against the back stack rather than a fixed chain.
            new()
            {
                Tag = "IsotopeDetailsPage",
                PageType = typeof(IsotopeDetailsPage),
                Title = "Isotope Details",
                ParentTag = "IsotopePage",
                WideContent = true,
                BreadcrumbOverride = frame => LastPage(frame) switch
                {
                    var t when t == typeof(ElementDetailsPage) => ["...", "Element Details", "Isotope Details"],
                    var t when t == typeof(NuclidePage)        => ["Nuclide Table", "Isotope Details"],
                    _ => null,
                },
            },
            new()
            {
                Tag = "IonizationDetailsPage",
                PageType = typeof(IonizationDetailsPage),
                Title = "Ionization Energies Details",
                ParentTag = "IonizationPage",
                BreadcrumbOverride = frame => LastPage(frame) == typeof(ElementDetailsPage)
                    ? ["...", "Element Details", "Ionization Energies Details"]
                    : null,
            },
        };

        private static readonly Dictionary<string, RouteDescriptor> ByTagMap =
            All.ToDictionary(r => r.Tag, StringComparer.Ordinal);

        private static readonly Dictionary<Type, RouteDescriptor> ByTypeMap =
            All.ToDictionary(r => r.PageType);

        public static RouteDescriptor? ByTag(string? tag) =>
            tag is not null && ByTagMap.TryGetValue(tag, out var r) ? r : null;

        public static RouteDescriptor? ByType(Type? pageType) =>
            pageType is not null && ByTypeMap.TryGetValue(pageType, out var r) ? r : null;

        public static IEnumerable<RouteDescriptor> InGroup(NavGroup group) =>
            All.Where(r => r.Group == group);

        /// <summary>
        /// Breadcrumb text for a page, root first. Uses the route's override when it has one,
        /// otherwise walks <see cref="RouteDescriptor.ParentTag"/> up to the root.
        /// </summary>
        public static string[] BreadcrumbFor(Type pageType, Frame frame)
        {
            var route = ByType(pageType);
            if (route is null)
                return [];

            if (route.BreadcrumbOverride?.Invoke(frame) is { } custom)
                return custom;

            var crumbs = new List<string>();
            var current = route;
            var guard = 0;
            while (current is not null && guard++ < 16)
            {
                crumbs.Insert(0, current.Title);
                current = ByTag(current.ParentTag);
            }
            return [.. crumbs];
        }

        private static Type? LastPage(Frame frame) =>
            frame.BackStack.Count > 0 ? frame.BackStack[^1].SourcePageType : null;
    }
}
