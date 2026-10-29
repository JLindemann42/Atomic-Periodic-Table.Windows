using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;

namespace Atomic.Core.Ai.Data
{
    /// <summary>Which way a trend explanation reads the table (roadmap 2.4).</summary>
    public enum ExplanationScope { Group, Period, Table }

    /// <summary>What an explanation is shown with, drawn from the app's own data when it is shown.</summary>
    public abstract record ExplanationCard
    {
        private ExplanationCard() { }

        /// <summary>A field across a set - a line - or, with no filter, the whole table - a heat map (roadmap 2.3).</summary>
        public sealed record Trend(string FieldId, Filter? Filter) : ExplanationCard;

        /// <summary>One element's successive ionization energies.</summary>
        public sealed record Ionization(string ElementKey) : ExplanationCard;
    }

    /// <summary>One reviewed explanation of a mechanism (roadmap 2.4). Mirrors Android's <c>Explanation</c>.</summary>
    /// <param name="FieldId">With <paramref name="Scope"/>, which trend question it answers; null for one reached by its topics alone.</param>
    /// <param name="Language">The language <paramref name="Text"/> is in - English where the reader's language has no row.</param>
    /// <param name="Topics">Trigger phrases, folded; each is a list of parts that must all appear.</param>
    public sealed record Explanation(
        string Key,
        string? FieldId,
        ExplanationScope? Scope,
        ExplanationCard? Card,
        string Text,
        string Language,
        IReadOnlyList<IReadOnlyList<string>> Topics);

    /// <summary>
    /// The explanations for one language, from the shared data's <c>explanations.tsv</c> (what each
    /// explains and the card it is shown with) and <c>explanations_&lt;lang&gt;.tsv</c> (the words),
    /// with English filling any row a language does not have yet. Mirrors Android's <c>Explanations</c>.
    /// </summary>
    public sealed class Explanations
    {
        public static readonly Explanations Empty = new(Array.Empty<Explanation>());

        /// <summary>
        /// Fields a question may name for the one an explanation is written about: "radius" resolves
        /// whichever radius matches best, and every radius shrinks across a period for the same reason.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> Family = new Dictionary<string, string>
        {
            ["atomic_radius_empirical"] = "atomic_radius",
            ["covalent_radius"] = "atomic_radius",
            ["van_der_waals_radius"] = "atomic_radius",
            ["electronegativity_allen"] = "electronegativity",
        };

        private readonly Dictionary<string, Explanation> _byKey;

        public Explanations(IReadOnlyList<Explanation> all)
        {
            All = all;
            _byKey = all.GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.Last());
        }

        public IReadOnlyList<Explanation> All { get; }

        public Explanation? ByKey(string key) => _byKey.TryGetValue(key, out var e) ? e : null;

        /// <summary>
        /// The explanation whose trigger phrase <paramref name="normalized"/> contains, the most
        /// specific first: the one whose parts are longest in total. "Why is fluorine the most
        /// electronegative" names fluorine and electronegativity, and the fluorine explanation is a
        /// closer answer than the group trend.
        /// </summary>
        public Explanation? ForTopic(string normalized)
        {
            Explanation? best = null;
            var bestLength = -1;
            foreach (var explanation in All)
            {
                foreach (var parts in explanation.Topics)
                {
                    if (!parts.All(p => normalized.Contains(p, StringComparison.Ordinal))) continue;
                    var length = parts.Sum(p => p.Length);
                    if (length > bestLength) { best = explanation; bestLength = length; }
                }
            }
            return best;
        }

        /// <summary>The trend explanation for a field in a scope, or the first for that field when no scope was said.</summary>
        public Explanation? ForTrend(string fieldId, ExplanationScope? scope)
        {
            var family = Family.TryGetValue(fieldId, out var f) ? f : fieldId;
            var candidates = All.Where(e => e.FieldId == family).ToList();
            return candidates.FirstOrDefault(e => e.Scope == scope) ?? (scope is null ? candidates.FirstOrDefault() : null);
        }

        /// <summary>
        /// Builds the set for <paramref name="language"/> from the spec and the two text files. A spec
        /// row with no text in either is left out, so a half-written file never shows an empty answer.
        /// </summary>
        public static Explanations Parse(string? spec, string? texts, string? english, string language)
        {
            var local = Rows(texts);
            var fallback = Rows(english);
            var entries = new List<Explanation>();
            foreach (var cells in Lines(spec))
            {
                var key = cells.Count > 0 ? cells[0] : "";
                if (string.IsNullOrWhiteSpace(key)) continue;
                local.TryGetValue(key, out var row);
                fallback.TryGetValue(key, out var fallbackRow);
                var own = row is not null && !string.IsNullOrWhiteSpace(row.Value.Text) ? row : null;
                var text = own?.Text ?? fallbackRow?.Text;
                if (text is null) continue;
                // The reader's own trigger phrases, and the English ones too: "noble gas" is how a
                // Filipino question names the family as often as not.
                var topics = (row?.Topics ?? Array.Empty<IReadOnlyList<string>>())
                    .Concat(fallbackRow?.Topics ?? Array.Empty<IReadOnlyList<string>>())
                    .GroupBy(t => string.Join("+", t)).Select(g => g.First()).ToList();
                var fieldId = Cell(cells, 1);
                var scope = Cell(cells, 2) is { } s && Enum.TryParse<ExplanationScope>(s, ignoreCase: true, out var parsed) ? parsed : (ExplanationScope?)null;
                var card = Cell(cells, 3) is { } c ? Card(c) : null;
                entries.Add(new Explanation(key, fieldId, scope, card, text, own is not null ? language : "en", topics));
            }
            return new Explanations(entries);
        }

        private static string? Cell(IReadOnlyList<string> cells, int index) =>
            cells.Count > index && !string.IsNullOrWhiteSpace(cells[index]) ? cells[index] : null;

        /// <summary>key to (topics, text) from one language's file.</summary>
        private static Dictionary<string, (IReadOnlyList<IReadOnlyList<string>> Topics, string Text)?> Rows(string? text)
        {
            var rows = new Dictionary<string, (IReadOnlyList<IReadOnlyList<string>> Topics, string Text)?>();
            foreach (var cells in Lines(text))
            {
                var topics = (cells.Count > 2 ? cells[2] : "").Split('|')
                    .Select(p => p.Trim()).Where(p => p.Length > 0)
                    .Select(phrase => (IReadOnlyList<string>)phrase.Split('+')
                        .Select(part => TextMatching.NormalizeForLookup(part.Trim())).Where(part => part.Length > 0).ToList())
                    .ToList();
                rows[cells[0]] = (topics, (cells.Count > 3 ? cells[3] : "").Trim());
            }
            return rows;
        }

        /// <summary>The data rows of a TSV file: no comments, no header, no blank lines.</summary>
        private static IEnumerable<IReadOnlyList<string>> Lines(string? text) =>
            (text ?? "").Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#') && !l.StartsWith("key\t", StringComparison.Ordinal))
                .Select(l => (IReadOnlyList<string>)l.Split('\t'));

        /// <summary><c>trend:&lt;field&gt;:group=1</c>, <c>trend:&lt;field&gt;:table</c>, <c>ionization:&lt;element&gt;</c>.</summary>
        private static ExplanationCard? Card(string spec)
        {
            var parts = spec.Trim().Split(':');
            switch (parts[0])
            {
                case "ionization":
                    return parts.Length > 1 ? new ExplanationCard.Ionization(parts[1]) : null;
                case "trend":
                    if (parts.Length < 3) return null;
                    var fieldId = parts[1];
                    if (parts[2] == "table") return new ExplanationCard.Trend(fieldId, null);
                    var kv = parts[2].Split('=');
                    if (kv.Length != 2) return null;
                    Filter? filter = kv[0] switch
                    {
                        "group" => int.TryParse(kv[1], out var g) ? new Filter.InGroup(g) : null,
                        "period" => int.TryParse(kv[1], out var p) ? new Filter.InPeriod(p) : null,
                        "series" => Enum.TryParse<SeriesId>(kv[1], ignoreCase: true, out var series)
                            ? new Filter.InSeries(new HashSet<SeriesId> { series }) : null,
                        _ => null,
                    };
                    return filter is null ? null : new ExplanationCard.Trend(fieldId, filter);
                default:
                    return null;
            }
        }
    }
}
