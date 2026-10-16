using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Atomic.Core.Ai.Data;

namespace Atomic.Core.Ai.Engine
{
    /// <summary>What the user is asking for.</summary>
    public enum Intent
    {
        /// <summary>One field of one element, optionally in a requested unit.</summary>
        PropertyLookup,

        /// <summary>
        /// A whole family of fields for one element - "the thermal properties of gold". Only fields
        /// the element actually has a value for are returned.
        /// </summary>
        CategoryLookup,

        /// <summary>Two or more elements compared on one or more fields.</summary>
        Comparison,

        /// <summary>The extreme of a field, optionally within a subset: "densest nonmetal".</summary>
        Superlative,

        /// <summary>Every element matching a set of conditions.</summary>
        FilterList,

        /// <summary>A statistic over a subset: "average electronegativity of the halogens".</summary>
        Aggregate,

        /// <summary>An element's isotopes: mass numbers, decay modes and half-lives.</summary>
        Isotopes,

        /// <summary>An element's NFPA 704 hazard ratings.</summary>
        Safety,

        /// <summary>
        /// An element's emission spectrum.
        ///
        /// <para>Not a field lookup: the app holds the spectrum only as a rendered image per
        /// element, so the answer is the picture plus what can honestly be said around it.</para>
        /// </summary>
        EmissionSpectrum,

        /// <summary>Molar mass and percentage composition of a chemical formula.</summary>
        FormulaMass,

        /// <summary>Neutron count of a specific nuclide, e.g. uranium-238.</summary>
        NuclideCount,

        /// <summary>Two nuclides set side by side, e.g. uranium-235 vs uranium-238.</summary>
        IsotopeComparison,

        /// <summary>
        /// A question with a direct answer about two elements: "is gold denser than lead",
        /// "which is heavier, gold or silver", "how much denser is gold than aluminium".
        /// </summary>
        Comparative,

        /// <summary>The element before or after another in atomic-number order.</summary>
        Neighbour,

        /// <summary>Conversion between moles and particle counts.</summary>
        MoleConversion,

        /// <summary>
        /// A standalone unit conversion: "convert 500 K to °C", "96 kJ/mol in eV".
        ///
        /// <para>Distinct from a property lookup carrying a target unit. "The density of gold in
        /// kg/m³" is a value the app holds, expressed differently; this is arithmetic on a number
        /// the user brought.</para>
        /// </summary>
        UnitConvert,

        /// <summary>A chemical equation to balance: "Fe + O2 -&gt; Fe2O3".</summary>
        BalanceEquation,

        /// <summary>Molarity, mass-moles-concentration, and dilution.</summary>
        SolutionCalc,

        /// <summary>How much of a radioisotope is left after a time, or how long a decay takes.</summary>
        DecayCalc,

        /// <summary>A row from one of the app's tables: a constant, equation, mineral, dictionary term.</summary>
        DatasetLookup,

        /// <summary>Nothing recognised. The caller should fall back.</summary>
        Unknown,
    }

    /// <summary>Comparison operators available in a filter.</summary>
    public enum Op { Gt, Gte, Lt, Lte, Eq, Between }

    /// <summary>Statistics available over a filtered set.</summary>
    public enum Aggregation { None, Min, Max, Avg, Sum, Count, Median, Range }

    /// <summary>Something the query referred to.</summary>
    public abstract record EntityRef
    {
        private EntityRef() { }

        public sealed record Element(string Key) : EntityRef;

        public sealed record DatasetRow(string Dataset, string Id) : EntityRef;

        /// <summary>A specific isotope, e.g. uranium-238. MassNumber is the nucleon count.</summary>
        public sealed record Nuclide(string Key, int MassNumber) : EntityRef;
    }

    /// <summary>A condition an element must satisfy.</summary>
    public abstract record Filter
    {
        private Filter() { }

        public sealed record FieldCompare(string FieldId, Op Op, Quantity Value, double? High = null) : Filter;

        public sealed record InSeries(IReadOnlySet<SeriesId> Series) : Filter;

        public sealed record InBlock(Block Block) : Filter;

        public sealed record InPhase(string Phase) : Filter;

        public sealed record IsRadioactive(bool Radioactive) : Filter;

        public sealed record InPeriod(int Period) : Filter;

        public sealed record InGroup(int Group) : Filter;

        public sealed record IsMetal(bool Metal) : Filter;

        /// <summary>Whether the element occurs in nature, as opposed to being made in a reactor.</summary>
        public sealed record IsSynthetic(bool Synthetic) : Filter;

        /// <summary>
        /// Whether an element satisfies this condition.
        ///
        /// <para>An element with no value for the compared field never matches, rather than
        /// defaulting to zero - otherwise "elements denser than 5" would silently include
        /// everything unmeasured.</para>
        /// </summary>
        public bool Matches(ElementRecord element, KnowledgeStore store) => this switch
        {
            FieldCompare f => MatchesCompare(f, element, store),
            InSeries f => f.Series.Contains(element.Series),
            InBlock f => element.Block == f.Block,
            InPhase f => string.Equals(element.Phase, f.Phase, StringComparison.Ordinal),
            IsRadioactive f => element.Radioactive == f.Radioactive,
            InPeriod f => element.Period == f.Period,
            InGroup f => element.GroupNumber == f.Group,
            IsMetal f => element.Series.IsMetal() == f.Metal,
            IsSynthetic f => element.Synthetic == f.Synthetic,
            // C# will not warn about a missing variant the way Kotlin's exhaustive `when` does, so
            // the gap is made loud at runtime instead - inside the test suite, where it is cheap.
            _ => throw new UnreachableException($"unhandled filter {GetType().Name}"),
        };

        private static bool MatchesCompare(FieldCompare f, ElementRecord element, KnowledgeStore store)
        {
            var actual = store.QuantityIn(element, f.FieldId, f.Value.Unit);
            if (actual is null) return false;

            return f.Op switch
            {
                Op.Gt => actual.Mid > f.Value.Value,
                Op.Gte => actual.Mid >= f.Value.Value,
                Op.Lt => actual.Mid < f.Value.Value,
                Op.Lte => actual.Mid <= f.Value.Value,
                Op.Eq => Math.Abs(actual.Mid - f.Value.Value) < 1e-6,
                Op.Between => f.High is { } high && actual.Mid >= f.Value.Value && actual.Mid <= high,
                _ => throw new UnreachableException($"unhandled operator {f.Op}"),
            };
        }
    }

    /// <summary>
    /// A natural-language question turned into something executable.
    ///
    /// <para>This is what makes multi-hop answers possible: "which transition metals melt above
    /// 2000 °C" becomes an <see cref="Filter.InSeries"/> plus a <see cref="Filter.FieldCompare"/>,
    /// which the executor runs across all 118 elements. The previous agent could only match a query
    /// against a fixed keyword branch, so it had no way to express a question of that shape at
    /// all.</para>
    /// </summary>
    /// <param name="Literal">
    /// A token the planner lifted verbatim out of the query - a chemical formula, an equation.
    /// Kept apart from <paramref name="RawQuery"/> because the two are read differently downstream:
    /// RawQuery is re-scanned for the numbers the user wrote, so it has to stay the whole sentence,
    /// while this is the one substring the planner has already decided the answer is about.
    /// </param>
    /// <param name="Confidence">0..1; below the planner's threshold the caller should not use this plan.</param>
    /// <param name="Evidence">Human-readable reasons the planner chose this, for debugging and tests.</param>
    public sealed record QueryPlan(
        Intent Intent,
        IReadOnlyList<EntityRef>? Entities = null,
        IReadOnlyList<string>? FieldIds = null,
        int? FieldOrdinal = null,
        IReadOnlyList<Filter>? Filters = null,
        Aggregation Aggregation = Aggregation.None,
        string? SortField = null,
        bool SortDescending = true,
        int Limit = 1,
        string? TargetUnit = null,
        string? Literal = null,
        double Confidence = 0.0,
        string RawQuery = "",
        IReadOnlyList<string>? Evidence = null)
    {
        // Kotlin defaults these to emptyList(); C# cannot put a collection in a default parameter,
        // so they are normalised on read. Callers still write `new QueryPlan(Intent.Unknown)`.
        public IReadOnlyList<EntityRef> Entities { get; init; } = Entities ?? [];

        public IReadOnlyList<string> FieldIds { get; init; } = FieldIds ?? [];

        public IReadOnlyList<Filter> Filters { get; init; } = Filters ?? [];

        public IReadOnlyList<string> Evidence { get; init; } = Evidence ?? [];

        public string? PrimaryField => FieldIds.Count > 0 ? FieldIds[0] : null;

        public IReadOnlyList<string> ElementKeys =>
            Entities.OfType<EntityRef.Element>().Select(e => e.Key).ToList();

        /// <summary>Elements matching every filter, in atomic-number order.</summary>
        public IReadOnlyList<ElementRecord> Candidates(KnowledgeStore store) =>
            store.Elements.Where(e => Filters.All(f => f.Matches(e, store))).ToList();
    }
}
