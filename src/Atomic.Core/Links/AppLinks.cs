using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Atomic.Core.Links
{
    /// <summary>Where an <c>atomic:</c> link wants to go.</summary>
    /// <param name="RouteTag">
    /// A route tag the host resolves to a page, or null when the link names no page - which is how
    /// <c>atomic://ask</c> says "open the assistant, not a screen".
    /// </param>
    public sealed record AppLink(string? RouteTag, string? Query = null, string? ElementKey = null);

    /// <summary>
    /// Parses the <c>atomic:</c> scheme.
    ///
    /// <para>Three things need a way back into the app from outside it: the scheduled toasts from
    /// P8, chat deep links, and the Widgets Board. All three go through here, so the set of
    /// reachable destinations is one list rather than three parsers that drift.</para>
    ///
    /// <para>In <c>Atomic.Core</c> because it is pure and because that is where it can be tested.
    /// It deals in route <i>tags</i> - strings the host maps to page types - so the layering holds:
    /// this decides what a link means, the app decides what that looks like.</para>
    ///
    /// <para><b>Total by design.</b> An unknown or malformed link resolves to the periodic table
    /// rather than throwing. Activation runs before any window exists, so an exception there kills
    /// the app on launch with no UI to report it.</para>
    /// </summary>
    public static class AppLinks
    {
        public const string Scheme = "atomic";

        /// <summary>The route a link with no better destination lands on.</summary>
        public const string HomeRoute = "PeriodicTablePage";

        /// <summary>A link that opens the app at the periodic table and does nothing else.</summary>
        public static readonly AppLink Home = new(HomeRoute);

        /// <summary>Hosts the scheme understands, and the route tag each maps to.</summary>
        private static readonly FrozenDictionary<string, string> Routes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["table"] = HomeRoute,
                ["flashcards"] = "FlashcardsPage",
                ["account"] = "AccountPage",
                ["balancer"] = "EquationBalancerPage",
                ["dictionary"] = "DictionaryPage",
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        public static AppLink Parse(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Parse(uri) : Home;

        public static AppLink Parse(Uri? uri)
        {
            if (uri is null) return Home;
            if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)) return Home;

            var query = ParseQuery(uri.Query);

            // atomic://ask?q=... - the Quick Ask widget, and "ask about this" from anywhere.
            if (string.Equals(uri.Host, "ask", StringComparison.OrdinalIgnoreCase))
            {
                query.TryGetValue("q", out var question);
                return new AppLink(null, Query: Blank(question) ? null : question);
            }

            // atomic://element?key=gold - Element of the Day, and citations.
            if (string.Equals(uri.Host, "element", StringComparison.OrdinalIgnoreCase))
            {
                query.TryGetValue("key", out var key);
                return Blank(key) ? Home : new AppLink("ElementDetailsPage", ElementKey: key);
            }

            return Routes.TryGetValue(uri.Host, out var route) ? new AppLink(route) : Home;
        }

        private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

        private static Dictionary<string, string> ParseQuery(string? query)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return values;

            foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var split = pair.Split('=', 2);
                if (split.Length != 2) continue;

                // '+' means space in a query string, which UnescapeDataString does not handle - a
                // widget text box sends spaces that way.
                values[split[0]] = Uri.UnescapeDataString(split[1].Replace('+', ' '));
            }

            return values;
        }
    }
}
