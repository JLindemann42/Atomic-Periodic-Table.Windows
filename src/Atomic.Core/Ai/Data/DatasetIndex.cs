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
        string? Link = null,
        string Key = "");

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
            rows = CanonicalKeys(rows);
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

        /// <summary>A row by its canonical key, e.g. <c>poisson/basalt</c>.</summary>
        public DatasetRow? ByKey(string key) =>
            Rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));

        /// <summary>
        /// Give every row a canonical key: <c>dataset/slug-of-title</c>, with <c>#2</c>, <c>#3</c>...
        /// for a title repeated within a dataset, in source order (roadmap 0.6).
        ///
        /// <para>The same rule as Android's <c>DatasetIndex.canonicalKeys</c>, and both are pinned
        /// by <c>shared/golden/dataset_keys.tsv</c>, so a corpus row's <c>expect_row</c> means the
        /// same row on both platforms. The two used to differ: Android keyed rows by name alone, so
        /// the second Basalt was uncitable, while <see cref="DatasetRow.Id"/> here carries the range
        /// (D11). <c>#</c> rather than <c>-</c>, because titles end in numbers of their own -
        /// <c>Mg^2+</c> is <c>solubility/mg-2</c>. Units are keyed on <c>category/name</c>, since
        /// unit names repeat across categories.</para>
        /// </summary>
        public static IReadOnlyList<DatasetRow> CanonicalKeys(IReadOnlyList<DatasetRow> rows)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var keyed = new List<DatasetRow>(rows.Count);
            foreach (var row in rows)
            {
                var natural = row.Dataset == Datasets.Unit ? row.Id : row.Title;
                var key = row.Dataset + "/" + Slug(natural);
                var n = seen.TryGetValue(key, out var count) ? count + 1 : 1;
                seen[key] = n;
                keyed.Add(row with { Key = n == 1 ? key : key + "#" + n.ToString(CultureInfo.InvariantCulture) });
            }
            return keyed;
        }

        /// <summary>
        /// Lowercase ASCII words joined by hyphens, compatibility-decomposed first so "₂" is "2" and
        /// "é" is "e". Matches Android's <c>DatasetIndex.slug</c>.
        /// </summary>
        public static string Slug(string text)
        {
            var decomposed = text.Normalize(NormalizationForm.FormKD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark) continue;
                sb.Append(c);
            }
            var lowered = sb.ToString().ToLowerInvariant();
            var slug = System.Text.RegularExpressions.Regex.Replace(lowered, "[^a-z0-9]+", "-").Trim('-');
            return slug.Length == 0 ? "row" : slug;
        }

        /// <summary>
        /// The names people use for the anions the solubility table is keyed by. The table writes
        /// "Cl^-"; nobody asks whether silver Cl^- dissolves.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> AnionNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["F^-"] = "fluoride", ["Cl^-"] = "chloride", ["Br^-"] = "bromide", ["I^-"] = "iodide",
                ["OH^-"] = "hydroxide", ["S^2-"] = "sulfide sulphide", ["SO4^2-"] = "sulfate sulphate",
                ["CO3^2-"] = "carbonate", ["NO3^-"] = "nitrate", ["PO4^3-"] = "phosphate",
                ["CrO4^2-"] = "chromate", ["CH3CO2^-"] = "acetate", ["SO3^2-"] = "sulfite sulphite",
                ["SiO3^2-"] = "silicate",
            };

        /// <summary>
        /// Translated row titles from <c>datasets/titles_{lang}.tsv</c> in the shared data (roadmap
        /// 1.3): canonical key, then the title, with other wordings after a <c>|</c> - "Neutronens
        /// massa | neutronmassa". <c>#</c> lines and the header are skipped; a row with no title is
        /// no row. Mirrors Android's <c>DatasetIndex.parseTitles</c>.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseTitles(string? text)
        {
            var output = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(text)) return output;
            foreach (var line in text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') ||
                    line.StartsWith("key\t", StringComparison.Ordinal)) continue;
                var cells = line.TrimEnd('\r').Split('\t');
                var forms = (cells.Length > 1 ? cells[1] : string.Empty).Split('|')
                    .Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
                if (forms.Count > 0) output[cells[0]] = forms;
            }
            return output;
        }

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
                    // "Formula" and "equation", because that is what an equation is asked for
                    // by: "formula for potential energy" (roadmap 1.1).
                    Body: $"{e.Title} {e.Category} formula equation {e.Description}",
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
                    // "Colour", "turn" and "range", because that is how indicators are asked
                    // about: "what colour does phenolphthalein turn" (roadmap 1.1).
                    Body: $"{title} ph indicator ph range colour color turn turns changes " +
                          $"{ind.AcidColor} {ind.NeutralColor} {ind.AlkaliColor}",
                    Detail: $"Acid {ind.Acid}: {ind.AcidColor} · Neutral {ind.Neutral}: " +
                            $"{ind.NeutralColor} · Alkali {ind.Alkali}: {ind.AlkaliColor}",
                    DeepLink: DeepLinkTarget.Ph));
            }

            foreach (var s in SeriesModel.All)
            {
                var name = elementName?.Invoke(s.ElementKey) ?? Capitalise(s.ElementKey);
                rows.Add(new DatasetRow(
                    Dataset: Datasets.Electrode, Id: s.Symbol, Title: name,
                    // "Standard" and "reduction": this is the standard reduction potential table,
                    // and that is what it is asked for by (roadmap 1.1).
                    Body: $"{name} {s.Symbol} {s.Charge} standard electrode potential reduction potential electrochemical series",
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
                    // Anion *names* and "water", because users write "is silver chloride soluble
                    // in water", not "Ag^+ Cl^-" (roadmap 1.1).
                    Body: $"{column.Cation} solubility soluble insoluble dissolve water " +
                          string.Join(" ", pairs.Select(p =>
                              $"{p.anion} {(AnionNames.TryGetValue(p.anion, out var n) ? n : "")}")),
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
                // The unit everything in the category converts through, named on every row: "how
                // many joules in a calorie" names the joule, and the calorie row did not.
                var baseUnit = units.FirstOrDefault(u => u.Factor == 1.0)?.Name ?? "";
                foreach (var u in units)
                {
                    rows.Add(new DatasetRow(
                        Dataset: Datasets.Unit, Id: $"{category}/{u.Name}", Title: u.Name,
                        Body: $"{u.Name} {category} unit conversion {baseUnit}",
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
