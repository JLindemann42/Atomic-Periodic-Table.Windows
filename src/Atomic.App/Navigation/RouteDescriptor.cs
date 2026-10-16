using System;
using Microsoft.UI.Xaml.Controls;

namespace Atomic.App.Navigation
{
    /// <summary>Where a route appears in the navigation pane.</summary>
    public enum NavGroup
    {
        /// <summary>Not shown in the pane at all - detail pages reached by navigation.</summary>
        None,

        /// <summary>Top-level item, above the Tables group.</summary>
        Main,

        /// <summary>Child of the expandable "Tables" item.</summary>
        Tables,

        /// <summary>Below the "Tools" header.</summary>
        Tools,

        /// <summary>Pinned to the bottom of the pane.</summary>
        Footer,
    }

    /// <summary>
    /// One navigable destination. This is the single place a page is registered: the pane item,
    /// the header, the breadcrumb chain and the back behaviour are all derived from it.
    ///
    /// <para><see cref="Title"/> is the breadcrumb text and <see cref="NavLabel"/> the pane text;
    /// they differ only where the pane needs a call to action ("Get PRO Version" vs "PRO Version").
    /// Both become resource keys in Phase 2 - keep them as the only source of display text so that
    /// swap stays mechanical.</para>
    /// </summary>
    public sealed record RouteDescriptor
    {
        /// <summary>Stable identifier. Also the <c>Tag</c> on the generated pane item.</summary>
        public required string Tag { get; init; }

        public required Type PageType { get; init; }

        /// <summary>Breadcrumb text, and the pane text unless <see cref="NavLabel"/> overrides it.</summary>
        public required string Title { get; init; }

        /// <summary>Pane text, when it needs to differ from <see cref="Title"/>.</summary>
        public string? NavLabel { get; init; }

        /// <summary>
        /// Tag of the route this one sits under. The breadcrumb walks this chain to the root, so
        /// "Element Details" needs only to name its parent rather than repeat the whole path.
        /// </summary>
        public string? ParentTag { get; init; }

        public NavGroup Group { get; init; } = NavGroup.None;

        /// <summary>Built-in symbol icon. Mutually exclusive with <see cref="Glyph"/>.</summary>
        public Symbol? Symbol { get; init; }

        /// <summary>Segoe Fluent Icons code point, e.g. "".</summary>
        public string? Glyph { get; init; }

        /// <summary>
        /// Suppresses the breadcrumb's width cap. Set on pages whose content is genuinely wide
        /// (the table itself, the nuclide chart, the dictionary) so the header can run full bleed.
        /// </summary>
        public bool WideContent { get; init; }

        /// <summary>
        /// Overrides the computed breadcrumb for pages reachable from more than one parent.
        /// Returns the full crumb array, or null to fall back to the <see cref="ParentTag"/> walk.
        /// Two routes need this; everything else is a static chain.
        /// </summary>
        public Func<Frame, string[]?>? BreadcrumbOverride { get; init; }

        public string PaneText => NavLabel ?? Title;
    }
}
