using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Ai.Data
{
    // ElementRow is aliased project-wide in GlobalUsings.cs, mirroring the Kotlin typealias.
    // It is IReadOnlyDictionary<string, object?>: one element's raw fields keyed by JSON key,
    // holding the shapes the JSON actually contains - string for nearly everything, int for the
    // three NFPA diamond fields, and IReadOnlyDictionary<string, string> for the two nested
    // fields, lattice_constants and debye_temperature.

    /// <summary>The whole element table for one language, keyed by lowercase English element name.</summary>
    public sealed class ElementTable
    {
        public ElementTable(IReadOnlyDictionary<string, ElementRow> rows) => Rows = rows;

        public IReadOnlyDictionary<string, ElementRow> Rows { get; }

        public IEnumerable<string> Keys => Rows.Keys;

        public int Count => Rows.Count;

        public ElementRow? this[string key] => Rows.TryGetValue(key, out var row) ? row : null;
    }

    /// <summary>
    /// Where the element tables come from.
    ///
    /// <para>This interface is the boundary that keeps file and package access out of the engine.
    /// Everything downstream of it - parsers, index, retrieval, planner - works on plain
    /// collections and is testable without a host app.</para>
    /// </summary>
    public interface IElementSource
    {
        /// <summary>Language codes with an element table available.</summary>
        IReadOnlyList<string> AvailableLanguages();

        /// <summary>The table for a language, or null when it cannot be loaded.</summary>
        ElementTable? Load(string language);

        /// <summary>
        /// A text file shipped beside the element tables, such as <c>titles_sv.tsv</c> (roadmap
        /// 1.3), or null when there is none.
        /// </summary>
        string? Text(string name) => null;
    }

    /// <summary>In-memory <see cref="IElementSource"/> for unit tests and previews.</summary>
    public sealed class MapElementSource : IElementSource
    {
        private readonly IReadOnlyDictionary<string, ElementTable> _byLanguage;

        public MapElementSource(IReadOnlyDictionary<string, ElementTable> byLanguage)
        {
            _byLanguage = byLanguage;
        }

        public IReadOnlyList<string> AvailableLanguages() =>
            _byLanguage.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList();

        public ElementTable? Load(string language) =>
            _byLanguage.TryGetValue(language, out var table) ? table : null;
    }
}
