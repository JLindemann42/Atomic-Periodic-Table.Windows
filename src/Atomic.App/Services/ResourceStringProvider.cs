using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Atomic.Core.Localization;
using Atomic.Core.Text;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Atomic.App.Services
{
    /// <summary>
    /// <see cref="IStringProvider"/> over the app's <c>.resw</c> resources.
    ///
    /// <para>Each instance is pinned to one language through its own <see cref="ResourceContext"/>.
    /// That is the point: chat answers in the language of the question, so the engine can be
    /// formatting Swedish while the UI is English. Resolving through
    /// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> instead would make the two
    /// impossible to separate.</para>
    ///
    /// <para>Instances are cached per language - building a resource context is not free and the
    /// engine asks for strings per token, not per screen.</para>
    /// </summary>
    public sealed class ResourceStringProvider : IStringProvider
    {
        private const string DefaultLanguage = "en-US";

        /// <summary>
        /// Two resource files ship per language and they have different owners.
        /// <c>Resources</c> is generated from Android's strings.xml and must never be hand-edited;
        /// <c>App</c> holds strings that exist only on Windows, or that Windows words differently.
        /// App is searched first so it can deliberately override an Android string.
        /// </summary>
        private static readonly string[] Subtrees = ["App", "Resources"];

        private static readonly ResourceManager Manager = new();
        private static readonly ConcurrentDictionary<string, ResourceStringProvider> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ResourceMap[] _maps;
        private readonly ResourceContext _context;
        private readonly ResourceContext? _fallbackContext;

        private ResourceStringProvider(string language)
        {
            Language = language;
            _maps = System.Array.ConvertAll(Subtrees, s => Manager.MainResourceMap.GetSubtree(s));

            _context = Manager.CreateResourceContext();
            _context.QualifierValues["Language"] = language;

            // Pinning a single Language qualifier turns MRT's own fallback off, so a key present
            // only in en-US resolves to nothing rather than to English. That is right for the
            // generated resources - a missing translation should be visible - but wrong for the
            // Windows-only strings in App.resw, which are English-only by design. Hence an
            // explicit second context rather than relying on MRT.
            _fallbackContext = IsDefault(language) ? null : Manager.CreateResourceContext();
            if (_fallbackContext is not null)
                _fallbackContext.QualifierValues["Language"] = DefaultLanguage;
        }

        public string Language { get; }

        public static ResourceStringProvider For(string language) =>
            Cache.GetOrAdd(string.IsNullOrWhiteSpace(language) ? "en" : language,
                           lang => new ResourceStringProvider(lang));

        public string Get(string key, params object?[] args)
        {
            var template = Lookup(key);
            if (template is null)
                return key; // Visible, and searchable in the resources. Never throw at the user.

            return args is { Length: > 0 } ? JavaFormat.Format(template, args) : template;
        }

        public IReadOnlyList<string> Array(string key)
        {
            // Arrays are flattened to key.0, key.1, ... by the converter; read until a gap.
            var items = new List<string>();
            for (var i = 0; ; i++)
            {
                var value = Lookup($"{key}.{i}");
                if (value is null)
                    break;
                items.Add(value);
            }
            return items;
        }

        private string? Lookup(string key)
        {
            foreach (var context in new[] { _context, _fallbackContext })
            {
                if (context is null)
                    continue;

                foreach (var map in _maps)
                {
                    try
                    {
                        if (map.TryGetValue(key, context)?.ValueAsString is { } value)
                            return value;
                    }
                    catch (Exception)
                    {
                        // MRT throws rather than returning null for some malformed keys.
                    }
                }
            }
            return null;
        }

        private static bool IsDefault(string language) =>
            language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }
}
