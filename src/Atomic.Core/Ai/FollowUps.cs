using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Localization;
using Atomic.Core.Text;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// Follow-up chips that fit the answer just given (roadmap 2.1). Mirrors Android's
    /// <c>FollowUps</c>.
    ///
    /// <para>The fixed chips under a reply - "Uses of gold", "Atomic mass of gold" - say what the
    /// assistant can do, but not what to ask next. These are built from the answer's own plan: after
    /// "the density of gold", the same field for gold's neighbour in its group, the element with the
    /// highest density, and gold's density in another unit; after a ranking, the other end of it;
    /// after a list, how many there are.</para>
    ///
    /// <para>A chip is only offered if the engine would answer it: <c>answers</c> is asked with each
    /// candidate as the next turn of the conversation, and a chip it would decline, or one that asks
    /// the question just answered, is dropped. That is what keeps the templates honest in twelve
    /// languages - a phrasing the planner cannot read in one of them is never shown there, rather
    /// than leading to "I don't know".</para>
    /// </summary>
    public sealed class FollowUps
    {
        /// <summary>
        /// Units a field can be shown in besides its own, by dimension, in the order offered: how the
        /// chip writes it, and the unit that is. Only units <see cref="UnitConverter"/> converts to;
        /// eV to kJ/mol is one of its bridges, which apply when a conversion is asked for outright, as
        /// a chip asks. Ångström is spelled out because a bare "Å" folds to "a", too short to be read
        /// as a unit.
        /// </summary>
        private static readonly Dictionary<Dimension, (string Written, string Unit)[]> OtherUnits = new()
        {
            [Dimension.Temperature] = [("°F", "°F"), ("°C", "°C")],
            [Dimension.Density] = [("kg/m³", "kg/m³"), ("g/cm³", "g/cm³")],
            [Dimension.Length] = [("ångström", "Å"), ("pm", "pm")],
            [Dimension.Energy] = [("kJ/mol", "kJ/mol"), ("eV", "eV")],
            [Dimension.EnergyPerMol] = [("eV", "eV"), ("kJ/mol", "kJ/mol")],
        };

        private readonly KnowledgeStore _store;
        private readonly Func<string, QueryPlan?> _answers;
        private readonly Func<string, string> _name;

        /// <param name="store">The elements.</param>
        /// <param name="answers">The plan the engine would answer a chip with as the next turn, or null.</param>
        /// <param name="name">An element's name in the reader's language.</param>
        public FollowUps(KnowledgeStore store, Func<string, QueryPlan?> answers, Func<string, string> name)
        {
            _store = store;
            _answers = answers;
            _name = name;
        }

        /// <summary>Up to <paramref name="limit"/> chips for the answer <paramref name="plan"/> produced, most specific first.</summary>
        public IReadOnlyList<string> After(QueryPlan plan, IStringProvider strings, int limit)
        {
            var chips = new List<string>();
            foreach (var candidate in Candidates(plan, strings))
            {
                if (chips.Count >= limit) break;
                if (_answers(candidate) is not { } next || AsksTheSame(next, plan)) continue;
                if (!chips.Contains(candidate, StringComparer.Ordinal)) chips.Add(candidate);
            }
            return chips;
        }

        /// <summary>Whether <paramref name="chip"/> would ask <paramref name="plan"/> again, so offering it would only repeat the answer.</summary>
        public bool Repeats(string chip, QueryPlan plan) => _answers(chip) is { } next && AsksTheSame(next, plan);

        private IEnumerable<string> Candidates(QueryPlan plan, IStringProvider strings)
        {
            var field = plan.FieldIds.FirstOrDefault() is { } id && FieldRegistry.ById.TryGetValue(id, out var spec) &&
                        spec.Kind == FieldKind.Numeric ? spec : null;
            var label = field is null ? null : Label(field, strings);

            switch (plan.Intent)
            {
                case Intent.PropertyLookup:
                    if (plan.ElementKeys.FirstOrDefault() is not { } key || _store.Element(key) is not { } element ||
                        field is null || label is null) yield break;
                    if (Partner(element, field.Id) is { } partner)
                        yield return Format(strings, S.ai_follow_compare, label, _name(element.Key), _name(partner.Key));
                    yield return Format(strings, S.ai_follow_highest, label);
                    if (OtherUnit(field.Dimension, plan.TargetUnit) is { } unit)
                        yield return Format(strings, S.ai_follow_unit, label, _name(element.Key), unit);
                    break;
                // The pair is answered; what is left is where the field tops out.
                case Intent.Comparison:
                case Intent.Comparative:
                    if (label is not null) yield return Format(strings, S.ai_follow_highest, label);
                    break;
                // A ranking over the whole table invites its other end. Within a subset the chip would
                // have to name the subset, which it cannot do in every language, so it is left out.
                case Intent.Superlative:
                    if (label is not null && plan.Filters.Count == 0)
                        yield return Format(strings, plan.SortDescending ? S.ai_follow_lowest : S.ai_follow_highest, label);
                    break;
                case Intent.FilterList:
                    if (plan.Filters.Count > 0) yield return strings.Get(S.ai_follow_count);
                    break;
                case Intent.Aggregate:
                    if (plan.Filters.Count > 0 && plan.Aggregation != Aggregation.Count)
                        yield return strings.Get(S.ai_follow_count);
                    break;
            }
        }

        /// <summary>
        /// The element to compare with: the nearest in the same group that has a value for the field,
        /// and for the lanthanides and actinides, which have no group, the nearest in the series. A
        /// natural element is preferred to a synthetic one, and on a tie the lighter: gold's partner
        /// is silver, not roentgenium, whose values are estimates.
        /// </summary>
        private ElementRecord? Partner(ElementRecord element, string fieldId)
        {
            var candidates = _store.Elements.Where(e =>
                e.Key != element.Key && !e.Value(fieldId).IsMissing && (element.Synthetic || !e.Synthetic));
            return element.GroupNumber is { } group
                ? candidates.Where(e => e.GroupNumber == group)
                    .OrderBy(e => Math.Abs(e.Period - element.Period)).ThenBy(e => e.Period).FirstOrDefault()
                : candidates.Where(e => e.GroupNumber is null && e.Series == element.Series)
                    .OrderBy(e => Math.Abs(e.AtomicNumber - element.AtomicNumber)).ThenBy(e => e.AtomicNumber)
                    .FirstOrDefault();
        }

        /// <summary>A unit the converter really converts the dimension to, other than the one just shown, as written.</summary>
        private static string? OtherUnit(Dimension? dimension, string? shown) =>
            dimension is { } d && OtherUnits.TryGetValue(d, out var units)
                ? units.Where(u => u.Unit != shown).Select(u => u.Written).FirstOrDefault()
                : null;

        /// <summary>The field as the composer names it in a sentence, lower-cased where the language has case.</summary>
        private static string? Label(FieldSpec spec, IStringProvider strings)
        {
            var raw = Try.OrNull(() => strings.Get(spec.SentenceLabel()));
            if (raw is null || raw.StartsWith("str:", StringComparison.Ordinal)) raw = Try.OrNull(() => strings.Get(spec.LabelKey));
            if (raw is null || raw.StartsWith("str:", StringComparison.Ordinal)) return null;
            var label = raw.Replace(":", string.Empty, StringComparison.Ordinal)
                .Replace("：", string.Empty, StringComparison.Ordinal).Trim();
            // The labels are table headings, "Atomic Mass". German capitalises every noun; elsewhere a
            // label inside a question is lower-case, except an acronym ("NFPA").
            if (strings.Language == "de") return label;
            return string.Join(' ', label.Split(' ').Select(word =>
                word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c))
                    ? word
                    : word.ToLowerInvariant()));
        }

        private static string Format(IStringProvider strings, string key, params object?[] args)
        {
            var text = strings.Get(key, args);
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
        }

        /// <summary>The same question: the same reading of the same elements and fields.</summary>
        private static bool AsksTheSame(QueryPlan next, QueryPlan previous) =>
            next.Intent == previous.Intent && next.ElementKeys.SequenceEqual(previous.ElementKeys) &&
            next.FieldIds.SequenceEqual(previous.FieldIds) && next.Filters.SequenceEqual(previous.Filters) &&
            next.SortDescending == previous.SortDescending && next.TargetUnit == previous.TargetUnit;
    }
}
