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
        private const string Subtree = "Resources";

        private static readonly ResourceManager Manager = new();
        private static readonly ConcurrentDictionary<string, ResourceStringProvider> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ResourceMap _map;
        private readonly ResourceContext _context;

        private ResourceStringProvider(string language)
        {
            Language = language;
            _map = Manager.MainResourceMap.GetSubtree(Subtree);
            _context = Manager.CreateResourceContext();
            _context.QualifierValues["Language"] = language;
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
            try
            {
                return _map.TryGetValue(key, _context)?.ValueAsString;
            }
            catch (Exception)
            {
                // MRT throws rather than returning null for some malformed keys.
                return null;
            }
        }
    }
}
