using System;
using System.Globalization;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// Where an element's photograph lives.
    ///
    /// <para>Ported from Android's <c>InfoExtension.loadImage</c>. The photos are hosted per element
    /// and named after the <b>English</b> element name, so the URL is composed rather than stored -
    /// which is also why it is the same URL on both platforms without either having to publish a
    /// list.</para>
    ///
    /// <para>Windows previously read an <c>element_image</c> field from the element table instead.
    /// That field is empty for all 118 rows in the shipped data, so the picture never appeared at
    /// all - see D38 in porting-notes.</para>
    /// </summary>
    public static class ElementImages
    {
        public const string BaseUrl = "https://jlindemann.se/atomic/element_images/";

        public const string Extension = ".jpg";

        /// <summary>
        /// The image URL for an element, or null when there can be none.
        /// </summary>
        /// <param name="englishName">
        /// The English element name - <c>Element.OriginalName</c>, not the translated one. A
        /// localized name would ask the server for "Väte.jpg", which does not exist.
        /// </param>
        /// <remarks>
        /// <para>The first letter is capitalised because a few rows in <c>elements_en.json</c> are
        /// authored lower case - "tennessine" among them - while the files on the server are not.
        /// Android does the same, and for the same reason.</para>
        ///
        /// <para><c>"---"</c> is the table's blank marker, not a name. It reaches here whenever a
        /// row has no English name at all, and asking for "---.jpg" would be a guaranteed 404.</para>
        /// </remarks>
        public static string? UrlFor(string? englishName)
        {
            var name = englishName?.Trim();

            if (string.IsNullOrEmpty(name) || name == "---")
                return null;

            return BaseUrl + Capitalise(name) + Extension;
        }

        /// <summary>
        /// First letter upper case, the rest untouched.
        /// </summary>
        /// <remarks>
        /// Invariant, and deliberately not <c>ToTitleCase</c>: the names are file names on a server,
        /// so a Turkish "i" turning into "İ" would ask for a file that is not there. Element names
        /// are single words, so there is no second word to capitalise either.
        /// </remarks>
        private static string Capitalise(string name) =>
            char.ToUpperInvariant(name[0]) + name[1..];
    }
}
