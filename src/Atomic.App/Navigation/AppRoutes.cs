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
        public const string Account = "AccountPage";

        public static readonly IReadOnlyList<RouteDescriptor> All = new RouteDescriptor[]
        {
            // ---- Main -------------------------------------------------------------------
            new()
            {
                Tag = PeriodicTable,
                PageType = typeof(PeriodicTablePage),
                TitleKey = "nav_periodic_table",
                Group = NavGroup.Main,
                Symbol = Microsoft.UI.Xaml.Controls.Symbol.Home,
                WideContent = true,
            },

            // ---- Tables -----------------------------------------------------------------
            new() { Tag = "IsotopePage",         PageType = typeof(IsotopePage),         TitleKey = "isotopes_title",               Group = NavGroup.Tables },
            new() { Tag = "phPage",              PageType = typeof(phPage),              TitleKey = "ph_title",          Group = NavGroup.Tables },
            new() { Tag = "ElectrochemicalPage", PageType = typeof(ElectrochemicalPage), TitleKey = "electrochemical_series", Group = NavGroup.Tables },
            new() { Tag = "EquationsPage",       PageType = typeof(FormulasPage),        TitleKey = "nav_equations",              Group = NavGroup.Tables },
            new() { Tag = "IonizationPage",      PageType = typeof(IonizationPage),      TitleKey = "table_ionization",    Group = NavGroup.Tables },
            new() { Tag = "SolubilityPage",      PageType = typeof(SolubilityPage),      TitleKey = "nav_solubility",             Group = NavGroup.Tables },
            new() { Tag = "PoissonPage",         PageType = typeof(PoissonPage),         TitleKey = "poissons_ratio",        Group = NavGroup.Tables },
            new() { Tag = "NuclidePage",         PageType = typeof(NuclidePage),         TitleKey = "nuclide_table_title",          Group = NavGroup.Tables, WideContent = true },
            new() { Tag = "ConstantsPage",       PageType = typeof(ConstantsPage),       TitleKey = "nav_constants",              Group = NavGroup.Tables },
            new() { Tag = "GeologyPage",         PageType = typeof(GeologyPage),         TitleKey = "table_geology",          Group = NavGroup.Tables },
            new() { Tag = "EmissionPage",        PageType = typeof(EmissionPage),        TitleKey = "nav_emission",     Group = NavGroup.Tables },
            new() { Tag = "AlloyPage",           PageType = typeof(AlloyPage),           TitleKey = "table_alloy",           Group = NavGroup.Tables },

            // ---- Tools ------------------------------------------------------------------
            new() { Tag = "DictionaryPage",    PageType = typeof(DictionaryPage),    TitleKey = "dictionary",     Group = NavGroup.Tools, Glyph = "", WideContent = true },
            new() { Tag = "CalculatorPage",    PageType = typeof(CalculatorPage),    TitleKey = "calculator_title",     Group = NavGroup.Tools, Symbol = Microsoft.UI.Xaml.Controls.Symbol.Calculator },
            new() { Tag = "UnitConverterPage", PageType = typeof(UnitConverterPage), TitleKey = "unit_converter_title", Group = NavGroup.Tools, Glyph = "" },
            new() { Tag = "FlashcardsPage",    PageType = typeof(FlashCardsPage),    TitleKey = "flashcards_title",     Group = NavGroup.Tools, Glyph = "" },
            new() { Tag = "EquationBalancerPage", PageType = typeof(EquationBalancerPage), TitleKey = "balancer_tool_title",      Group = NavGroup.Tools, Symbol = Microsoft.UI.Xaml.Controls.Symbol.Sync },
            new() { Tag = "IdealGasPage",         PageType = typeof(IdealGasPage),         TitleKey = "ideal_gas_calculator_title", Group = NavGroup.Tools, Symbol = Microsoft.UI.Xaml.Controls.Symbol.Repair },
            new() { Tag = "DeveloperPage",        PageType = typeof(Atomic.App.Tools.DeveloperPage), TitleKey = "developer_title", Group = NavGroup.Tools, Symbol = Microsoft.UI.Xaml.Controls.Symbol.Setting },

            // Hidden: the WebView2 host currently points at a placeholder URL.
            new() { Tag = "BlogPage", PageType = typeof(BlogPage), TitleKey = "nav_blog", Symbol = Microsoft.UI.Xaml.Controls.Symbol.Globe },

            // ---- Footer -----------------------------------------------------------------
            new()
            {
                Tag = Pro,
                PageType = typeof(ProPage),
                TitleKey = "pro_version",
                NavLabelKey = "get_pro_title",
                Group = NavGroup.Footer,
                Symbol = Microsoft.UI.Xaml.Controls.Symbol.Add,
            },

            // Footer entries are added ahead of NavigationView's own settings item, so this one
            // sits directly above it - the position the account entry holds on Android.
            //
            // Labelled with user_title, not account: "account" has no German or Swedish
            // translation, and a pane that reads "Account" between "Skaffa PRO" and
            // "Installningar" is exactly the half-translated seam P2e was about.
            new()
            {
                Tag = Account,
                PageType = typeof(AccountPage),
                TitleKey = "user_title",
                Group = NavGroup.Footer,
                Symbol = Microsoft.UI.Xaml.Controls.Symbol.Contact,
            },

            // ---- Reached by navigation, not from the pane --------------------------------
            new() { Tag = "SettingsPage",        PageType = typeof(SettingsPage),        TitleKey = "settings_title" },
            new() { Tag = "ElementDetailsPage",  PageType = typeof(ElementDetailsPage),  TitleKey = "nav_element_details",                 ParentTag = PeriodicTable },
            new() { Tag = "PoissonDetailsPage",  PageType = typeof(PoissonDetailsPage),  TitleKey = "nav_poisson_details",         ParentTag = "PoissonPage" },
            new() { Tag = "GeologyDetailsPage",  PageType = typeof(GeologyDetailsPage),  TitleKey = "nav_geology_details",           ParentTag = "GeologyPage" },
            new() { Tag = "FormulasDetailsPage", PageType = typeof(FormulasDetailsPage), TitleKey = "nav_equations_details",               ParentTag = "EquationsPage" },
            new() { Tag = "EmissionDetailsPage", PageType = typeof(EmissionDetailsPage), TitleKey = "nav_emission_details", ParentTag = "EmissionPage" },

            // Both of these are reachable from more than one parent, so the crumb is resolved
            // against the back stack rather than a fixed chain.
            new()
            {
                Tag = "IsotopeDetailsPage",
                PageType = typeof(IsotopeDetailsPage),
                TitleKey = "nav_isotope_details",
                ParentTag = "IsotopePage",
                WideContent = true,
                BreadcrumbOverride = frame => LastPage(frame) switch
                {
                    // "..." is not a resource key; the provider returns an unknown key verbatim,
                    // which is exactly what an elision needs.
                    var t when t == typeof(ElementDetailsPage) => ["...", "nav_element_details", "nav_isotope_details"],
                    var t when t == typeof(NuclidePage)        => ["nuclide_table_title", "nav_isotope_details"],
                    _ => null,
                },
            },
            new()
            {
                Tag = "IonizationDetailsPage",
                PageType = typeof(IonizationDetailsPage),
                TitleKey = "nav_ionization_details",
                ParentTag = "IonizationPage",
                BreadcrumbOverride = frame => LastPage(frame) == typeof(ElementDetailsPage)
                    ? ["...", "nav_element_details", "nav_ionization_details"]
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
        /// Breadcrumb resource keys for a page, root first. Uses the route's override when it has one,
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
                crumbs.Insert(0, current.TitleKey);
                current = ByTag(current.ParentTag);
            }
            return [.. crumbs];
        }

        private static Type? LastPage(Frame frame) =>
            frame.BackStack.Count > 0 ? frame.BackStack[^1].SourcePageType : null;
    }
}
