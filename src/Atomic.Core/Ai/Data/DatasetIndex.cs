using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Atomic.Core.Models;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// A row from one of the app's non-element datasets, flattened into a shape the agent can
    /// search, cite and deep-link uniformly.
    /// </summary>
    /// <param name="Dataset">The dataset id; one of <see cref="DatasetIndex.Datasets"/>.</param>
    /// <param name="Id">Stable identifier within the dataset, used in citations.</param>
    /// <param name="Title">The row's display name.</param>
    /// <param name="Body">Searchable prose; what BM25 indexes.</param>
    /// <param name="Detail">Rendered answer text.</param>
    /// <param name="NumericValue">A comparable value when the row carries one, e.g. a constant or a voltage.</param>
    public sealed record DatasetRow(
        string Dataset,
        string Id,
        string Title,
        string Body,
        string Detail,
        DeepLinkTarget DeepLink,
        double? NumericValue = null,
        string? Unit = null,
        string? Link = null);

    /// <summary>
    /// Every non-element dataset in the app, in one searchable index.
    ///
    /// <para>The legacy agent reached only a fraction of these, through hand-written keyword
    /// cascades. Here all of them become uniformly queryable and citable. The underlying models are
    /// English-only, so answers drawn from them stay English regardless of chat language; the
    /// composer flags that rather than pretending otherwise.</para>
    /// </summary>
    public sealed class DatasetIndex
    {
        public DatasetIndex(IReadOnlyList<DatasetRow> rows)
        {
            Rows = rows;
            ByDataset = rows
                .GroupBy(r => r.Dataset, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DatasetRow>)g.ToList(), StringComparer.Ordinal);
        }

        public IReadOnlyList<DatasetRow> Rows { get; }

        public IReadOnlyDictionary<string, IReadOnlyList<DatasetRow>> ByDataset { get; }

        public IReadOnlyList<DatasetRow> InDataset(string name) =>
            ByDataset.TryGetValue(name, out var rows) ? rows : [];

        public DatasetRow? Row(string dataset, string id) =>
            InDataset(dataset).FirstOrDefault(
                r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The dataset ids. Nested rather than sitting on <see cref="DatasetIndex"/> directly
        /// because names like <c>Dictionary</c>, <c>Equation</c> and <c>Poisson</c> would
        /// otherwise shadow the BCL generic and the record types of the same name inside this
        /// class - silently, since both resolve.
        /// </summary>
        public static class Datasets
        {
            public const string Dictionary = "dictionary";
            public const string Equation = "equation";
            public const string Constant = "constant";
            public const string Poisson = "poisson";
            public const string Geology = "geology";
            public const string Ion = "ion";
            public const string Indicator = "indicator";
            public const string Electrode = "electrode";
            public const string Solubility = "solubility";
            public const string Unit = "unit";
            public const string Alloy = "alloy";

            /// <summary>Every dataset id, so a test can assert the index reaches all of them.</summary>
            public static readonly IReadOnlyList<string> All =
            [
                Dictionary, Equation, Constant, Poisson, Geology, Ion,
                Indicator, Electrode, Solubility, Unit, Alloy,
            ];
        }

        /// <summary>
        /// Build the index from the static reference tables.
        /// </summary>
        /// <param name="elementName">
        /// Resolves a lowercase element key to its display name in the reader's language. Only the
        /// electrochemical series needs it, because that table stores element keys rather than
        /// names. Null falls back to capitalising the key, which is what Android does when it has
        /// no context.
        /// </param>
        /// <remarks>
        /// Runs once and the result is held by the engine, rather than being rebuilt per query.
        /// </remarks>
        public static DatasetIndex Build(Func<string, string?>? elementName = null)
        {
            var rows = new List<DatasetRow>(600);

            foreach (var d in DictionaryModel.All)
            {
                rows.Add(new DatasetRow(
                    Dataset: Datasets.Dictionary, Id: d.Heading, Title: d.Heading,
                    Body: $"{d.Heading} {d.Category} {d.Text}",
                    Detail: d.Text, DeepLink: DeepLinkTarget.Dictionary, Link: d.Wiki));
            }

            foreach (var e in EquationModel.All)
            {
                // "Equivalent Resistance (Series)" has an empty description in both apps - on the
                // page the rendered formula carries the meaning, so nobody noticed. As a dataset
                // row that would answer a question with nothing at all, so the category stands in.
                var detail = e.Description.Trim().Length > 0
                    ? e.Description
                    : $"{e.Title} — {e.Category}";

                rows.Add(new DatasetRow(
                    Dataset: Datasets.Equation, Id: e.Title, Title: e.Title,
                    Body: $"{e.Title} {e.Category} {e.Description}",
                    Detail: detail, DeepLink: DeepLinkTarget.Equations));
            }

            foreach (var c in ConstantModel.All)
            {
                var detail = new StringBuilder(c.Value);
                if (HasUnit(c.Unit)) detail.Append(' ').Append(c.Unit);
                if (c.Info.Trim().Length > 0) detail.Append(" — ").Append(c.Info);

                rows.Add(new DatasetRow(
                    Dataset: Datasets.Constant, Id: c.Name, Title: c.Name,
                    Body: $"{c.Name} {c.Category} {c.Info} {c.Unit}",
                    Detail: detail.ToString(),
                    DeepLink: DeepLinkTarget.Constants,
                    NumericValue: ParseConstant(c.Value),
                    Unit: HasUnit(c.Unit) ? c.Unit : null));
            }

            // Two rocks are quoted over two different ranges and share a name. Android keys the
            // row by the name alone, which makes the second row unreachable through Row(); the id
            // here carries the range so both can be cited. The corpus asserts intent, not ids.
            var poissonNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in PoissonModel.All)
            {
                var seen = poissonNames.TryGetValue(p.Name, out var n) ? n : 0;
                poissonNames[p.Name] = seen + 1;
                var id = seen == 0
                    ? p.Name
                    : $"{p.Name} ({Num(p.Start)}–{Num(p.End)})";

                rows.Add(new DatasetRow(
                    Dataset: Datasets.Poisson, Id: id, Title: p.Name,
                    Body: $"{p.Name} {p.Type} poisson ratio",
                    Detail: $"{Num(p.Start)} – {Num(p.End)} ({p.Type})",
                    DeepLink: DeepLinkTarget.Poisson,
                    NumericValue: (p.Start + p.End) / 2));
            }

            foreach (var g in GeologyModel.All)
            {
                rows.Add(new DatasetRow(
                    Dataset: Datasets.Geology, Id: g.Name, Title: g.Name,
                    Body: $"{g.Name} {g.Type} {g.Group} {g.Color} {g.Streak} {g.Crystal} {g.Luster}",
                    Detail: $"{g.Group} · {g.Color} · Mohs {g.Hardness} · {g.Luster}",
                    DeepLink: DeepLinkTarget.Geology));
            }

            foreach (var i in IonModel.All)
            {
                rows.Add(new DatasetRow(
                    Dataset: Datasets.Ion, Id: i.Name, Title: i.Name,
                    Body: $"{i.Name} {i.Symbol} ionization energy",
                    // Invariant, not interpolated directly: the analysers do not flag int
                    // interpolation, but the value ends up in an answer string and every other
                    // number in this file goes through Num().
                    Detail: $"{i.Count.ToString(CultureInfo.InvariantCulture)} ionization energy values",
                    DeepLink: DeepLinkTarget.Ion,
                    NumericValue: i.Count));
            }

            foreach (var ind in IndicatorModel.All)
            {
                var title = ind.Name.Replace('_', ' ');
                rows.Add(new DatasetRow(
                    Dataset: Datasets.Indicator, Id: ind.Name, Title: title,
                    Body: $"{title} ph indicator {ind.AcidColor} {ind.NeutralColor} {ind.AlkaliColor}",
                    Detail: $"Acid {ind.Acid}: {ind.AcidColor} · Neutral {ind.Neutral}: " +
                            $"{ind.NeutralColor} · Alkali {ind.Alkali}: {ind.AlkaliColor}",
                    DeepLink: DeepLinkTarget.Ph));
            }

            foreach (var s in SeriesModel.All)
            {
                var name = elementName?.Invoke(s.ElementKey) ?? Capitalise(s.ElementKey);
                rows.Add(new DatasetRow(
                    Dataset: Datasets.Electrode, Id: s.Symbol, Title: name,
                    Body: $"{name} {s.Symbol} {s.Charge} electrode potential electrochemical series",
                    Detail: $"{s.Charge}: {Num(s.Voltage)} V",
                    DeepLink: DeepLinkTarget.Electrode,
                    NumericValue: s.Voltage, Unit: "V"));
            }

            foreach (var column in SolubilityData.Columns)
            {
                var pairs = SolubilityData.Anions
                    .Zip(column.Values, (anion, code) => (anion, code))
                    .Where(p => p.code.Trim().Length > 0 && p.code != "---")
                    .ToList();

                rows.Add(new DatasetRow(
                    Dataset: Datasets.Solubility, Id: column.Cation, Title: column.Cation,
                    Body: $"{column.Cation} solubility soluble insoluble " +
                          string.Join(" ", pairs.Select(p => p.anion)),
                    Detail: string.Join(" · ", pairs.Select(
                        p => $"{p.anion} {SolubilityData.Word(p.code)}")),
                    DeepLink: DeepLinkTarget.Solubility));
            }

            foreach (var a in AlloyModel.All)
            {
                rows.Add(new DatasetRow(
                    Dataset: Datasets.Alloy, Id: a.Name, Title: a.Name,
                    Body: $"{a.Name} alloy {a.Base} {a.Composition} {a.Description}",
                    Detail: $"{a.Composition}. {a.Description}",
                    // Alloys have no screen of their own; the base element is the nearest place a
                    // reader can go for related data.
                    DeepLink: DeepLinkTarget.ElementInfo,
                    Link: a.Wiki));
            }

            foreach (var (category, units) in UnitCatalog.Categories)
            {
                foreach (var u in units)
                {
                    rows.Add(new DatasetRow(
                        Dataset: Datasets.Unit, Id: $"{category}/{u.Name}", Title: u.Name,
                        Body: $"{u.Name} {category} unit conversion",
                        Detail: string.Equals(category, UnitCatalog.Temperature, StringComparison.Ordinal)
                            ? category
                            : $"1 {u.Name} = {Num(u.Factor)} (base {category} unit)",
                        DeepLink: DeepLinkTarget.UnitConverter,
                        NumericValue: u.Factor));
                }
            }

            return new DatasetIndex(rows);
        }

        private static bool HasUnit(string unit) => unit.Trim().Length > 0 && unit != "-";

        /// <summary>
        /// A constant's value as a number, or null when it is not one.
        ///
        /// <para>Thousands separators are stripped because several constants are authored with
        /// them. InvariantCulture is not optional: under a comma-decimal culture the invariant
        /// parse is the only one that reads "6.674e-11" correctly, and under tr-TR it is the only
        /// one that reads it at all.</para>
        /// </summary>
        private static double? ParseConstant(string value) =>
            double.TryParse(value.Replace(",", string.Empty, StringComparison.Ordinal),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

        private static string Num(double value) =>
            value.ToString("0.##########", CultureInfo.InvariantCulture);

        private static string Capitalise(string key) =>
            key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..];
    }
}
