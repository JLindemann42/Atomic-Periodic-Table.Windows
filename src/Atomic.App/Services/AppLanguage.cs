using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Globalization;

namespace Atomic.App.Services
{
    /// <summary>
    /// The app's UI language: which one is active, which are available, and how switching works.
    ///
    /// <para>Distinct from the language a chat answer is composed in. That is detected per message
    /// and passed to the engine explicitly - see <see cref="Atomic.Core.Localization.IStringProvider"/>.
    /// Nothing here sets <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>, because
    /// doing so would tie the two together and make "Swedish answer in an English UI" impossible.</para>
    /// </summary>
    public static class AppLanguage
    {
        /// <summary>Sentinel meaning "follow Windows".</summary>
        public const string System = "";

        /// <summary>
        /// Shipped languages, as BCP-47 tags matching the Strings/ folders. Regional variants
        /// (es-AR, es-MX, ur-IN) are resolved by MRT from their base and are not offered
        /// separately.
        /// </summary>
        public static readonly IReadOnlyList<(string Tag, string NativeName)> Supported =
        [
            ("en-US",   "English"),
            ("af",      "Afrikaans"),
            ("de",      "Deutsch"),
            ("es",      "Español"),
            ("fil",     "Filipino"),
            ("fr",      "Français"),
            ("hi",      "हिन्दी"),
            ("it",      "Italiano"),
            ("pt-BR",   "Português (Brasil)"),
            ("sv",      "Svenska"),
            ("ur",      "اردو"),
            ("zh-Hans", "简体中文"),
        ];

        /// <summary>
        /// The language resources are currently resolved in, as a base code such as "sv".
        /// Follows <see cref="ApplicationLanguages.PrimaryLanguageOverride"/> when one is set,
        /// otherwise whatever Windows picked.
        /// </summary>
        public static string Current
        {
            get
            {
                var over = ApplicationLanguages.PrimaryLanguageOverride;
                var tag = !string.IsNullOrEmpty(over)
                    ? over
                    : ApplicationLanguages.Languages.FirstOrDefault() ?? "en-US";
                return BaseCode(tag);
            }
        }

        /// <summary>The saved preference: a BCP-47 tag, or <see cref="System"/> to follow Windows.</summary>
        public static string Preference
        {
            get => SettingsStore.Get(SettingsStore.Keys.AppLanguage, System);
            set => SettingsStore.Set(SettingsStore.Keys.AppLanguage, value ?? System);
        }

        /// <summary>
        /// Applies the saved preference. Called once at startup, before the first page is built.
        /// </summary>
        public static void ApplySaved() => Apply(Preference);

        /// <summary>
        /// Switches language. Only the override is set here - repainting is the caller's job,
        /// because MRT resolves a resource once per element and caches it, so text already on
        /// screen does not change until its page is rebuilt.
        /// </summary>
        public static void Apply(string tag)
        {
            ApplicationLanguages.PrimaryLanguageOverride = tag ?? System;
        }

        /// <summary>"sv-SE" -> "sv"; "zh-Hans" stays, since the script is what distinguishes it.</summary>
        public static string BaseCode(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return "en";
            if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return "zh-Hans";
            if (tag.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
                return "pt-BR";

            var dash = tag.IndexOf('-', StringComparison.Ordinal);
            return dash < 0 ? tag : tag[..dash];
        }
    }
}
