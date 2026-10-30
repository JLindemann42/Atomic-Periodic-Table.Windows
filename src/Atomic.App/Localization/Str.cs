using Atomic.App.Services;
using Microsoft.UI.Xaml.Markup;

namespace Atomic.App.Localization
{
    /// <summary>
    /// Resolves a resource key in XAML: <c>Text="{loc:Str Key=density_colon}"</c>.
    ///
    /// <para><b>Why this rather than <c>x:Uid</c>.</b> MRT's <c>x:Uid</c> looks up
    /// <c>&lt;Uid&gt;.Text</c>, <c>&lt;Uid&gt;.Content</c> and so on - one resource entry per
    /// control <i>property</i>. Our <c>.resw</c> files are generated from Android's
    /// <c>strings.xml</c> and are keyed by flat names shared with that app, so every label would
    /// need a second, Windows-only entry duplicating a string that already exists. Two keys for one
    /// string is exactly how the two platforms drift apart, and it would put the duplicates outside
    /// the generator that keeps them in sync.</para>
    ///
    /// <para>Resolved once when the page loads, which is sufficient: a language change rebuilds the
    /// content frame and re-navigates - MRT caches per context, so already-realized elements would
    /// not update however they were bound.</para>
    /// </summary>
    [MarkupExtensionReturnType(ReturnType = typeof(string))]
    public sealed partial class Str : MarkupExtension
    {
        /// <summary>The resource name, matching a constant on <c>Atomic.Core.Localization.S</c>.</summary>
        public string Key { get; set; } = string.Empty;

        protected override object ProvideValue()
        {
            if (Key.Length == 0) return string.Empty;

            return ResourceStringProvider
                .For(AppLanguage.BaseCode(AppLanguage.Current))
                .Get(Key);
        }
    }
}
