using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Atomic.Core.Ai.Data
{
    /// <summary>NFPA 704 diamond ratings.</summary>
    public sealed record Nfpa(int? Health, int? Flammability, int? Instability, string? Special);

    /// <summary>
    /// One element, fully parsed and typed.
    ///
    /// <para><see cref="Period"/> and <see cref="GroupNumber"/> are derived from
    /// <see cref="AtomicNumber"/> rather than read from the JSON, which has no such keys.</para>
    /// </summary>
    public sealed record ElementRecord(
        string Key,
        string Symbol,
        int AtomicNumber,
        int Period,
        int? GroupNumber,
        SeriesId Series,
        Block Block,
        string? Phase,
        bool Radioactive,
        IReadOnlyDictionary<string, FieldValue> Values,
        IReadOnlyList<Isotope> Isotopes,
        Nfpa? Nfpa)
    {
        /// <summary>True when the element does not occur naturally in appreciable amounts.</summary>
        public bool Synthetic => FieldRegistry.IsSynthetic(AtomicNumber);

        public FieldValue Value(string fieldId) =>
            Values.TryGetValue(fieldId, out var value) ? value : FieldValue.Missing;

        public Quantity? Quantity(string fieldId) => Value(fieldId).AsQuantity();

        public int StableIsotopeCount => Isotopes.Count(i => i.Stable);
    }

    /// <summary>The seven fields that differ between language files, for one language.</summary>
    public sealed record LocalizedElement(
        string Name,
        string Description,
        string SeriesName,
        string Appearance,
        string Phase,
        string ElectricalType,
        string MagneticType);

    /// <summary>A language's display overlay on top of the shared numeric index.</summary>
    public sealed class LocalizedView
    {
        public LocalizedView(string language, IReadOnlyDictionary<string, LocalizedElement> elements)
        {
            Language = language;
            Elements = elements;
        }

        public string Language { get; }

        public IReadOnlyDictionary<string, LocalizedElement> Elements { get; }

        public string? Name(string key) =>
            Elements.TryGetValue(key, out var element) ? element.Name : null;

        /// <summary>
        /// A translated value for one of the seven overlaid fields, or null when this language has
        /// none.
        ///
        /// <para>The numeric index is parsed once from English, which is correct for every measured
        /// value but wrong for the handful of fields that are prose. Without this, a Swedish
        /// question about gold's phase or appearance was answered <c>Solid</c> and
        /// <c>Metallic Yellow</c> - in English - even though <c>elements_sv.json</c> holds a
        /// translation.</para>
        /// </summary>
        public string? Text(string key, string fieldId)
        {
            if (!Elements.TryGetValue(key, out var element)) return null;

            var value = fieldId switch
            {
                "name" => element.Name,
                "description" => element.Description,
                "series" => element.SeriesName,
                "appearance" => element.Appearance,
                "phase" => element.Phase,
                "electrical_type" => element.ElectricalType,
                "magnetic_type" => element.MagneticType,
                _ => null,
            };

            return value is not null && value.Trim().Length > 0 && !ValueParser.IsNullToken(value)
                ? value
                : null;
        }
    }

    /// <summary>
    /// The agent's typed view of the element data.
    ///
    /// <para>Only seven of the 124 non-isotope fields differ between the twelve language files
    /// (<c>element</c>, <c>element_group</c>, <c>description</c>, <c>element_appearance</c>,
    /// <c>element_phase</c>, <c>electrical_type</c>, <c>magnetic_type</c>); everything numeric and
    /// structural is identical. So the expensive parse runs once against English and each language
    /// costs only a 118-row string overlay, instead of holding twelve fully parsed ~1 MB
    /// tables.</para>
    /// </summary>
    public sealed class KnowledgeStore
    {
        private KnowledgeStore(
            IReadOnlyList<ElementRecord> elements,
            IReadOnlyDictionary<string, ElementRecord> byKey,
            IReadOnlyDictionary<string, ElementRecord> bySymbol,
            IReadOnlyDictionary<int, ElementRecord> byAtomicNumber,
            IReadOnlyDictionary<string, int> coverage)
        {
            Elements = elements;
            ByKey = byKey;
            BySymbolMap = bySymbol;
            ByAtomicNumber = byAtomicNumber;
            Coverage = coverage;
        }

        public IReadOnlyList<ElementRecord> Elements { get; }

        public IReadOnlyDictionary<string, ElementRecord> ByKey { get; }

        private IReadOnlyDictionary<string, ElementRecord> BySymbolMap { get; }

        public IReadOnlyDictionary<int, ElementRecord> ByAtomicNumber { get; }

        /// <summary>Field id to how many of the 118 elements actually have a value, for honesty notes.</summary>
        public IReadOnlyDictionary<string, int> Coverage { get; }

        public int Size => Elements.Count;

        public ElementRecord? Element(string? key) =>
            key is not null && ByKey.TryGetValue(key.ToLowerInvariant(), out var r) ? r : null;

        public ElementRecord? BySymbol(string? symbol) =>
            symbol is not null && BySymbolMap.TryGetValue(symbol.ToLowerInvariant(), out var r) ? r : null;

        public ElementRecord? ByNumber(int? z) =>
            z is { } number && ByAtomicNumber.TryGetValue(number, out var r) ? r : null;

        /// <summary>Elements in a series, using the canonicalised classification.</summary>
        public IReadOnlyList<ElementRecord> InSeries(SeriesId series) =>
            Elements.Where(e => e.Series == series).ToList();

        /// <summary>How many of the 118 elements have a value for a field.</summary>
        public int CoverageOf(string fieldId) =>
            Coverage.TryGetValue(fieldId, out var n) ? n : 0;

        /// <summary>
        /// A field's value converted into a requested unit where possible. Returns the unconverted
        /// quantity when no conversion applies.
        /// </summary>
        public Quantity? QuantityIn(ElementRecord record, string fieldId, string? targetUnit)
        {
            if (!FieldRegistry.ById.TryGetValue(fieldId, out var spec)) return null;

            // Temperature ships all three scales, so prefer the stored one over converting.
            if (targetUnit is not null && spec.JsonKeys.Count > 1)
            {
                var wanted = UnitConverter.Canonical(targetUnit);
                var stored = record.Values
                    .FirstOrDefault(e => string.Equals(e.Key, $"{fieldId}@{wanted}", StringComparison.Ordinal))
                    .Value?.AsQuantity();
                if (stored is not null) return stored;
            }

            var mainValue = record.Quantity(fieldId);
            if (mainValue is null) return null;
            if (targetUnit is null) return mainValue;
            return UnitConverter.Convert(mainValue, targetUnit) ?? mainValue;
        }

        /// <summary>Language whose table carries the canonical numeric and structural values.</summary>
        public const string BaseLanguage = "en";

        private static readonly object BuildLock = new();
        private static readonly ConcurrentDictionary<string, LocalizedView> Overlays =
            new(StringComparer.OrdinalIgnoreCase);
        private static volatile KnowledgeStore? _shared;

        /// <summary>
        /// The process-wide index, built once from the base-language table. Safe to call from any
        /// thread; the build runs at most once.
        /// </summary>
        public static KnowledgeStore? Get(IElementSource source)
        {
            if (_shared is { } cached) return cached;
            lock (BuildLock)
            {
                if (_shared is { } inner) return inner;

                var table = source.Load(BaseLanguage);
                if (table is null)
                {
                    foreach (var language in source.AvailableLanguages())
                    {
                        table = source.Load(language);
                        if (table is not null) break;
                    }
                }
                if (table is null) return null;

                _shared = Build(table);
                return _shared;
            }
        }

        /// <summary>The display overlay for a language, built and cached on first use.</summary>
        public static LocalizedView? Overlay(IElementSource source, string language)
        {
            if (Overlays.TryGetValue(language, out var cached)) return cached;
            var table = source.Load(language);
            if (table is null) return null;
            var view = BuildOverlay(language, table);
            Overlays[language] = view;
            return view;
        }

        /// <summary>Drop all cached state. Used by tests and by a language-data change.</summary>
        public static void Clear()
        {
            lock (BuildLock) { _shared = null; }
            Overlays.Clear();
        }

        /// <summary>Build an index directly from a table, bypassing the cache. Used by tests.</summary>
        public static KnowledgeStore Build(ElementTable table)
        {
            var records = new List<ElementRecord>(table.Count);
            var coverage = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var (key, row) in table.Rows)
            {
                var values = new Dictionary<string, FieldValue>(
                    FieldRegistry.All.Count * 2, StringComparer.Ordinal);

                foreach (var spec in FieldRegistry.All)
                {
                    if (spec.OrdinalRange is { } range)
                    {
                        // Banked fields: store every populated slot under "id#n", plus slot 1 as
                        // the default.
                        for (var slot = range.Min; slot <= range.Max; slot++)
                        {
                            var parsed = ValueParser.Parse(Raw(row, spec.JsonKeyForOrdinal(slot)), spec);
                            if (!parsed.IsMissing) values[$"{spec.Id}#{slot}"] = parsed;
                        }
                        if (values.TryGetValue($"{spec.Id}#1", out var first)) values[spec.Id] = first;
                    }
                    else if (spec.JsonKeys.Count > 1)
                    {
                        // Multi-scale fields: keep each stored scale so a unit request is a pure
                        // lookup, but always normalise the primary to the canonical unit. Taking
                        // whichever scale happened to be present first would let one element's
                        // Celsius be ranked against another's Kelvin.
                        var primary = FieldValue.Missing;
                        var fallback = FieldValue.Missing;
                        foreach (var jsonKey in spec.JsonKeys)
                        {
                            var parsed = ValueParser.Parse(Raw(row, jsonKey), spec);
                            if (parsed.IsMissing) continue;
                            var unit = UnitConverter.Canonical(parsed.AsQuantity()?.Unit);
                            if (unit is not null) values[$"{spec.Id}@{unit}"] = parsed;
                            if (string.Equals(unit, spec.CanonicalUnit, StringComparison.Ordinal)) primary = parsed;
                            if (fallback.IsMissing) fallback = parsed;
                        }
                        if (primary.IsMissing && !fallback.IsMissing)
                        {
                            // No canonical-unit reading stored; convert whatever we do have.
                            var converted = fallback.AsQuantity() is { } q && spec.CanonicalUnit is { } target
                                ? UnitConverter.Convert(q, target)
                                : null;
                            primary = converted is not null ? new FieldValue.Num(converted) : fallback;
                        }
                        if (!primary.IsMissing) values[spec.Id] = primary;
                    }
                    else
                    {
                        var parsed = ValueParser.Parse(Raw(row, spec.JsonKeys[0]), spec);
                        if (!parsed.IsMissing) values[spec.Id] = parsed;
                    }

                    if (values.ContainsKey(spec.Id)) Count(coverage, spec.Id);
                }

                var atomicNumber = (int)(values.TryGetValue("atomic_number", out var z)
                    ? z.AsQuantity()?.Value ?? 0
                    : 0);

                // Period and group are computed, not stored, so they are filled in here rather
                // than by the parse loop above.
                var derivedPeriod = FieldRegistry.PeriodOf(atomicNumber);
                if (derivedPeriod > 0)
                {
                    values["period"] = new FieldValue.Num(NumberQuantity(derivedPeriod));
                    Count(coverage, "period");
                }
                if (FieldRegistry.GroupOf(atomicNumber) is { } group)
                {
                    values["group_number"] = new FieldValue.Num(NumberQuantity(group));
                    Count(coverage, "group_number");
                }
                if (FieldRegistry.ValenceElectrons(atomicNumber) is { } valence)
                {
                    values["valence_electrons"] = new FieldValue.Num(NumberQuantity(valence));
                    Count(coverage, "valence_electrons");
                }

                var synthetic = FieldRegistry.IsSynthetic(atomicNumber);
                values["synthetic"] = new FieldValue.Enum(
                    synthetic ? "YES" : "NO",
                    synthetic ? "Synthetic" : "Naturally occurring");
                Count(coverage, "synthetic");

                var nfpa = new Nfpa(
                    Health: IntOf(values, "nfpa_health"),
                    Flammability: IntOf(values, "nfpa_flammability"),
                    Instability: IntOf(values, "nfpa_instability"),
                    Special: values.TryGetValue("nfpa_special", out var special)
                        ? (special as FieldValue.Text)?.Raw
                        : null);

                records.Add(new ElementRecord(
                    Key: key,
                    Symbol: values.TryGetValue("symbol", out var sym)
                        ? (sym as FieldValue.Text)?.Raw ?? string.Empty
                        : string.Empty,
                    AtomicNumber: atomicNumber,
                    Period: FieldRegistry.PeriodOf(atomicNumber),
                    GroupNumber: FieldRegistry.GroupOf(atomicNumber),
                    Series: SeriesCanon.Series(Text(row, "element_group")),
                    Block: SeriesCanon.Block(Text(row, "element_block")),
                    Phase: SeriesCanon.Phase(Text(row, "element_phase")),
                    Radioactive: SeriesCanon.Radioactive(Text(row, "radioactive")),
                    Values: values,
                    Isotopes: IsotopeParser.Parse(row),
                    Nfpa: nfpa is { Health: null, Flammability: null, Instability: null, Special: null }
                        ? null
                        : nfpa));
            }

            records.Sort((a, b) => a.AtomicNumber.CompareTo(b.AtomicNumber));

            return new KnowledgeStore(
                elements: records,
                byKey: records.ToDictionary(r => r.Key.ToLowerInvariant(), StringComparer.Ordinal),
                bySymbol: records.Where(r => r.Symbol.Length > 0)
                    // Two rows claiming one symbol would throw; the last wins instead, matching
                    // Kotlin's associateBy. The merged table has no such collision, but a future
                    // data edit should not be able to crash the engine at startup.
                    .Aggregate(new Dictionary<string, ElementRecord>(StringComparer.Ordinal),
                        (map, r) => { map[r.Symbol.ToLowerInvariant()] = r; return map; }),
                byAtomicNumber: records
                    .Aggregate(new Dictionary<int, ElementRecord>(),
                        (map, r) => { map[r.AtomicNumber] = r; return map; }),
                coverage: coverage);
        }

        private static LocalizedView BuildOverlay(string language, ElementTable table)
        {
            var map = new Dictionary<string, LocalizedElement>(table.Count * 2, StringComparer.Ordinal);
            foreach (var (key, row) in table.Rows)
            {
                map[key] = new LocalizedElement(
                    Name: Text(row, "element") ?? string.Empty,
                    Description: Text(row, "description") ?? string.Empty,
                    SeriesName: Text(row, "element_group") ?? string.Empty,
                    Appearance: Text(row, "element_appearance") ?? string.Empty,
                    Phase: Text(row, "element_phase") ?? string.Empty,
                    ElectricalType: Text(row, "electrical_type") ?? string.Empty,
                    MagneticType: Text(row, "magnetic_type") ?? string.Empty);
            }
            return new LocalizedView(language, map);
        }

        private static object? Raw(ElementRow row, string jsonKey) =>
            row.TryGetValue(jsonKey, out var value) ? value : null;

        private static string? Text(ElementRow row, string jsonKey) =>
            Raw(row, jsonKey)?.ToString();

        private static void Count(Dictionary<string, int> coverage, string fieldId) =>
            coverage[fieldId] = coverage.TryGetValue(fieldId, out var n) ? n + 1 : 1;

        private static int? IntOf(IReadOnlyDictionary<string, FieldValue> values, string fieldId) =>
            values.TryGetValue(fieldId, out var value) && value.AsQuantity() is { } q
                ? (int)q.Value
                : null;

        /// <summary>
        /// A derived integer as a quantity. The display string is invariant because it is composed
        /// into answer text, where a locale-specific digit shape would be wrong in every language
        /// but one.
        /// </summary>
        private static Quantity NumberQuantity(int value) =>
            new(value, Display: value.ToString(CultureInfo.InvariantCulture));
    }
}
