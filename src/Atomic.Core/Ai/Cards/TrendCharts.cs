using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;

namespace Atomic.Core.Ai.Cards
{
    /// <summary>One element's value on a trend line, in atomic-number order.</summary>
    public sealed record TrendPoint(int AtomicNumber, string Symbol, double Value);

    /// <summary>A field across a set of elements, for the line card (roadmap 2.3). Mirrors Android's <c>TrendSeries</c>.</summary>
    public sealed record TrendSeries(string FieldId, IReadOnlyList<TrendPoint> Points, ChartScale SuggestedScale);

    /// <summary>
    /// Builds a <see cref="TrendSeries"/> from the card's own arguments - the field and the elements,
    /// in order - read back out of the store, so the card can only draw what the answer above it states.
    /// </summary>
    public static class TrendSeriesReducer
    {
        /// <summary>Fewer points than this and the line says nothing the sentence does not.</summary>
        public const int MinPoints = 3;

        public const string FieldArg = "field";
        public const string KeysArg = "keys";

        public static TrendSeries? Of(string fieldId, IEnumerable<string> keys, KnowledgeStore store)
        {
            if (!FieldRegistry.ById.ContainsKey(fieldId)) return null;
            var points = keys
                .Select(store.Element)
                .Where(e => e is not null)
                .Select(e => store.QuantityIn(e!, fieldId, null) is { } q ? new TrendPoint(e!.AtomicNumber, e.Symbol, q.Mid) : null)
                .OfType<TrendPoint>()
                .OrderBy(p => p.AtomicNumber)
                .ToList();
            if (points.Count < MinPoints) return null;
            return new TrendSeries(fieldId, points, DecadeLabels.SuggestedScale(points.Select(p => p.Value).ToList()));
        }

        public static TrendSeries? Of(ChatCard card, KnowledgeStore store) =>
            card.Args.TryGetValue(FieldArg, out var fieldId) && card.Args.TryGetValue(KeysArg, out var keys)
                ? Of(fieldId, keys.Split(',', StringSplitOptions.RemoveEmptyEntries), store)
                : null;
    }

    /// <summary>
    /// One cell of the mini periodic table: where it sits, and its value as a share of the field's span,
    /// or null for an element with no value, which is drawn empty rather than as the lowest colour.
    /// </summary>
    /// <param name="Row">1 to 7 for the periods, 8 and 9 for the lanthanides and actinides set below.</param>
    /// <param name="Column">1 to 18 by group; the f-block rows use 3 to 17.</param>
    public sealed record HeatCell(string Symbol, int Row, int Column, double? Value, double? Fraction);

    /// <summary>A field over the whole table, for the heat-map card (roadmap 2.3). Mirrors Android's <c>HeatMap</c>.</summary>
    public sealed record HeatMap(string FieldId, IReadOnlyList<HeatCell> Cells, double Min, double Max, ChartScale Scale);

    public static class HeatMapReducer
    {
        /// <summary>Fewer values than this and a map of the table is mostly empty squares.</summary>
        public const int MinValues = 10;

        public const string FieldArg = "field";

        private const double MinPositive = 1e-30;

        public static HeatMap? Of(string fieldId, KnowledgeStore store)
        {
            if (!FieldRegistry.ById.ContainsKey(fieldId)) return null;
            var values = store.Elements.ToDictionary(e => e.Key, e => store.QuantityIn(e, fieldId, null)?.Mid, StringComparer.Ordinal);
            var present = values.Values.OfType<double>().ToList();
            if (present.Count < MinValues) return null;
            // A field spanning decades - abundance, half-life - would show one bright cell and a hundred
            // dark ones on a linear scale; the same rule the line chart uses decides.
            var scale = DecadeLabels.SuggestedScale(present);
            double Scaled(double v) => scale == ChartScale.Log10 ? Math.Log10(Math.Max(v, MinPositive)) : v;
            var usable = scale == ChartScale.Log10 ? present.Where(v => v > 0).ToList() : present;
            var low = Scaled(usable.Min());
            var high = Scaled(usable.Max());
            var span = high - low > 0 ? high - low : 1.0;
            var cells = new List<HeatCell>();
            foreach (var element in store.Elements)
            {
                if (Position(element.AtomicNumber, element.Period, element.GroupNumber) is not { } at) continue;
                var value = values[element.Key];
                double? fraction = value is { } v && (scale == ChartScale.Linear || v > 0)
                    ? Math.Clamp((Scaled(v) - low) / span, 0.0, 1.0)
                    : null;
                cells.Add(new HeatCell(element.Symbol, at.Row, at.Column, value, fraction));
            }
            return new HeatMap(fieldId, cells, usable.Min(), usable.Max(), scale);
        }

        public static HeatMap? Of(ChatCard card, KnowledgeStore store) =>
            card.Args.TryGetValue(FieldArg, out var fieldId) ? Of(fieldId, store) : null;

        /// <summary>
        /// Where an element sits in the drawn table. The lanthanides and actinides go on two rows of
        /// their own below it, as every printed table puts them, so the main body stays eighteen
        /// columns wide.
        /// </summary>
        public static (int Row, int Column)? Position(int atomicNumber, int period, int? group) => atomicNumber switch
        {
            >= 57 and <= 71 => (8, 3 + (atomicNumber - 57)),
            >= 89 and <= 103 => (9, 3 + (atomicNumber - 89)),
            _ => group is { } g ? (period, g) : null,
        };
    }
}
