using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Localization;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// One element's place in one field's ranking (roadmap 2.7): "osmium is the densest of the 99
    /// elements with a recorded density". Mirrors Android's <c>DataFact</c>.
    /// </summary>
    /// <param name="Rank">1 for the extreme, counted from the top when <paramref name="Highest"/> and from the bottom otherwise.</param>
    /// <param name="Of">How many elements have a value for the field.</param>
    public sealed record DataFact(string ElementKey, string FieldId, int Rank, int Of, bool Highest, Quantity Quantity);

    /// <summary>
    /// Facts read off the data rather than written by hand (roadmap 2.7), for the chat's fun facts.
    /// Ten free fields that are worth ranking, over the elements up to fermium: past it most values
    /// are predictions, and seaborgium's predicted density would otherwise make it the densest
    /// element. Mirrors Android's <c>DataFacts</c>.
    /// </summary>
    public sealed class DataFacts
    {
        /// <summary>Free fields worth ranking. Atomic mass is left out: it follows atomic number.</summary>
        public static readonly IReadOnlyList<string> Fields =
        [
            "density", "melting_point", "boiling_point", "electronegativity", "atomic_radius",
            "ionization_energy", "thermal_conductivity", "specific_heat_capacity", "fusion_heat", "vaporization_heat",
        ];

        /// <summary>Fermium. Past it most values in the data are predictions, not measurements.</summary>
        public const int LastMeasured = 100;

        /// <summary>How far from either end a place still makes an element's fact.</summary>
        public const int NotableRank = 3;

        private const int MinRanked = 20;

        private readonly IReadOnlyList<(string FieldId, IReadOnlyList<(ElementRecord Element, Quantity Value)> Ranked)> _rankings;

        public DataFacts(KnowledgeStore store)
        {
            _rankings = Fields
                .Select(fieldId => (FieldId: fieldId, Ranked: (IReadOnlyList<(ElementRecord Element, Quantity Value)>)store.Elements
                    .Where(e => e.AtomicNumber is >= 1 and <= LastMeasured)
                    .Select(e => (Element: e, Value: e.Quantity(fieldId)))
                    .Where(p => p.Value is { IsRange: false })
                    .Select(p => (p.Element, Value: p.Value!))
                    // Ties at either end go to the lighter element, the better measured: caesium and
                    // francium share the lowest electronegativity, and francium's is an estimate.
                    .OrderByDescending(p => p.Value.Mid)
                    .ThenBy(p => p.Element.AtomicNumber)
                    .ToList()))
                .Where(r => r.Ranked.Count >= MinRanked)
                .ToList();
        }

        /// <summary>The top and the bottom of every field: twenty facts, in a fixed order.</summary>
        public IReadOnlyList<DataFact> Extremes() =>
            _rankings.SelectMany(r =>
            {
                var top = r.Ranked[0];
                var lowest = r.Ranked[^1].Value.Mid;
                var bottom = r.Ranked.Where(p => p.Value.Mid == lowest).MinBy(p => p.Element.AtomicNumber);
                return new[]
                {
                    new DataFact(top.Element.Key, r.FieldId, 1, r.Ranked.Count, true, top.Value),
                    new DataFact(bottom.Element.Key, r.FieldId, 1, r.Ranked.Count, false, bottom.Value),
                };
            }).ToList();

        /// <summary>
        /// The element's most striking place, or null when it is nowhere near either end: the
        /// smallest rank from the top or the bottom of any field, up to <see cref="NotableRank"/>.
        /// </summary>
        public DataFact? ForElement(string elementKey) =>
            _rankings.SelectMany(r =>
                {
                    var at = r.Ranked.Select((p, i) => (p, i)).FirstOrDefault(x => x.p.Element.Key == elementKey, (default, -1)).Item2;
                    return at < 0
                        ? []
                        : new[]
                        {
                            new DataFact(elementKey, r.FieldId, at + 1, r.Ranked.Count, true, r.Ranked[at].Value),
                            new DataFact(elementKey, r.FieldId, r.Ranked.Count - at, r.Ranked.Count, false, r.Ranked[at].Value),
                        };
                })
                .Where(f => f.Rank <= NotableRank)
                .OrderBy(f => f.Rank)
                .ThenBy(f => Fields.ToList().IndexOf(f.FieldId))
                .FirstOrDefault();

        /// <summary>The fact for a given day, so a surface shows one fact all day and a new one tomorrow.</summary>
        public DataFact? ForDay(int dayIndex)
        {
            var all = Extremes();
            return all.Count == 0 ? null : all[((dayIndex % all.Count) + all.Count) % all.Count];
        }

        /// <summary>The fact as a sentence in the reader's language.</summary>
        public static string Render(DataFact fact, IStringProvider strings, LocalizedView? names)
        {
            var name = names?.Name(fact.ElementKey) is { Length: > 0 } localized
                ? localized
                : char.ToUpperInvariant(fact.ElementKey[0]) + fact.ElementKey[1..];
            var value = FieldRegistry.ById.TryGetValue(fact.FieldId, out var spec)
                ? spec.DisplayWithUnit(fact.Quantity, key => strings.Get(key))
                : fact.Quantity.Display;
            var label = FieldLabel(fact.FieldId, strings);
            return fact.Rank == 1
                ? strings.Get(fact.Highest ? S.ai_fact_data_highest : S.ai_fact_data_lowest, name, label, fact.Of, value)
                : strings.Get(fact.Highest ? S.ai_fact_data_rank_high : S.ai_fact_data_rank_low, name, fact.Rank, fact.Of, label, value);
        }

        /// <summary>The sentence form of a field's label, lower case as the answers' rank sentence has it, except German nouns.</summary>
        private static string FieldLabel(string fieldId, IStringProvider strings)
        {
            if (!FieldRegistry.ById.TryGetValue(fieldId, out var spec)) return fieldId.Replace('_', ' ');
            var label = Try.OrNull(() => strings.Get(spec.SentenceLabel()));
            if (label is not null && label.StartsWith("str:", StringComparison.Ordinal)) label = null;
            label ??= Try.OrNull(() => strings.Get(spec.LabelKey));
            var text = label is null || label.StartsWith("str:", StringComparison.Ordinal)
                ? fieldId.Replace('_', ' ')
                : label.Replace(":", string.Empty, StringComparison.Ordinal).Replace("：", string.Empty, StringComparison.Ordinal).Trim();
            return strings.Language == "de" ? text : text.ToLowerInvariant();
        }
    }
}
