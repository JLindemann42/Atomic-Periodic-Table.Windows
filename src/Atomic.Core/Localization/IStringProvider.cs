using System.Collections.Generic;

namespace Atomic.Core.Localization
{
    /// <summary>
    /// The engine's only route to translated text. Ported from <c>ai/core/StringProvider.kt</c>.
    ///
    /// <para>It is an interface rather than a static because the language is a property of the
    /// <em>message</em>, not of the process. Chat detects the language of each question and answers
    /// in it, so the engine may be formatting Swedish while the UI is in English. That is why
    /// nothing here reads <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>, and why
    /// the Android side deliberately never calls <c>Locale.setDefault</c>.</para>
    /// </summary>
    public interface IStringProvider
    {
        /// <summary>
        /// A string by key, with any <c>%s</c> / <c>%1$s</c> placeholders filled from
        /// <paramref name="args"/>. Missing keys must not throw - see the implementations for
        /// what they return instead.
        /// </summary>
        string Get(string key, params object?[] args);

        /// <summary>
        /// A string array, stored as <c>key.0</c>, <c>key.1</c>, … Returns empty when the first
        /// index is absent.
        /// </summary>
        IReadOnlyList<string> Array(string key);

        /// <summary>Language these strings are in: a base code such as "sv", not "sv-SE".</summary>
        string Language { get; }

        /// <summary>
        /// Whether a key exists.
        /// </summary>
        /// <remarks>
        /// <see cref="Get"/> deliberately never fails, so it cannot answer this - it returns the
        /// key, or a sentinel, and both are indistinguishable from a translation that happens to
        /// look like one. Plural resolution needs a real answer: Chinese has no singular form, so
        /// <c>lives_lost_result.One</c> is genuinely absent there.
        /// </remarks>
        bool Has(string key);
    }
}
