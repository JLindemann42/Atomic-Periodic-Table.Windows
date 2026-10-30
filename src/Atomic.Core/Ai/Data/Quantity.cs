using System.Collections.Generic;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// A parsed numeric value from the element data, with its unit and its original rendering.
    ///
    /// <para>The element JSON stores everything as unit-suffixed strings (<c>"2743 (K)"</c>,
    /// <c>"160-350 (MPa)"</c>, <c>"8.0 × 10^4"</c>). <see cref="ValueParser"/> turns those into a
    /// Quantity so the agent can compare, rank and aggregate. <see cref="Display"/> always keeps
    /// the authored string, so a plain lookup can echo the source verbatim rather than
    /// round-tripping through a formatter.</para>
    /// </summary>
    /// <param name="Value">The value, or the low end when <paramref name="High"/> is set.</param>
    /// <param name="High">The high end of a range; null when the value is a scalar.</param>
    /// <param name="Unit">Normalised unit, e.g. K, g/cm³, MPa; null when dimensionless.</param>
    /// <param name="Display">The original authored string, rendered as-is for verbatim answers.</param>
    /// <param name="Approximate">True when the source marked the value with ~, ≈, &lt; or &gt;.</param>
    /// <param name="Note">
    /// A qualifier the source attached in brackets or as a prefix, e.g. "room temperature",
    /// "Graphite", "Br2".
    /// </param>
    /// <param name="UnitAuthored">
    /// Whether <paramref name="Unit"/> was read off the value itself rather than supplied by the
    /// field's canonical unit. This is the difference between "8400 mg/kg", where
    /// <paramref name="Display"/> already states the unit, and "80500", where a caller has to add
    /// it - and without it, the only way to tell them apart is to guess from the string.
    /// </param>
    public sealed record Quantity(
        double Value,
        double? High = null,
        string? Unit = null,
        string Display = "",
        bool Approximate = false,
        string? Note = null,
        bool UnitAuthored = false)
    {
        /// <summary>True when this represents a range rather than a single value.</summary>
        public bool IsRange => High is not null;

        /// <summary>
        /// The value to sort and aggregate on: the midpoint for ranges, the value itself
        /// otherwise. Ranking by midpoint is a deliberate choice; callers should surface
        /// <see cref="IsRange"/> to the user.
        /// </summary>
        public double Mid => High is { } h ? (Value + h) / 2 : Value;
    }

    /// <summary>
    /// The typed result of reading one field off one element.
    ///
    /// <para><see cref="Missing"/> is the crucial case: the element JSON uses <c>"---"</c>
    /// (sometimes with a unit still attached, as in <c>"--- (pm)"</c>), <c>"N/A"</c> and empty
    /// strings as its absent-value sentinels, and roughly a third of the fields are populated for
    /// fewer than half the elements. Modelling absence explicitly is what lets the agent say it has
    /// no data instead of printing a sentinel or silently falling through to an unrelated
    /// answer.</para>
    /// </summary>
    public abstract record FieldValue
    {
        private FieldValue() { }

        /// <summary>The element has no value for this field.</summary>
        public sealed record MissingValue : FieldValue
        {
            internal MissingValue() { }
        }

        /// <summary>A parsed numeric value.</summary>
        public sealed record Num(Quantity Quantity) : FieldValue;

        /// <summary>Free text that carries no number, e.g. "Deep Antiquity", "Au^+, Au^3+".</summary>
        public sealed record Text(string Raw) : FieldValue;

        /// <summary>
        /// A value from a closed vocabulary.
        /// </summary>
        /// <param name="Canonical">Language-invariant key used for filtering and comparison.</param>
        /// <param name="Localized">The value as authored in the active language, used for display.</param>
        public sealed record Enum(string Canonical, string Localized) : FieldValue;

        /// <summary>A composite value, e.g. lattice_constants or debye_temperature.</summary>
        public sealed record Struct(IReadOnlyDictionary<string, Quantity> Parts) : FieldValue;

        /// <summary>The source recorded a presence too small to quantify ("trace").</summary>
        public sealed record TraceValue : FieldValue
        {
            internal TraceValue() { }
        }

        /// <summary>The singleton absent value. Kotlin spells this <c>object Missing</c>.</summary>
        public static readonly FieldValue Missing = new MissingValue();

        /// <summary>The singleton trace value.</summary>
        public static readonly FieldValue Trace = new TraceValue();

        public bool IsMissing => this is MissingValue;

        /// <summary>
        /// The <see cref="Data.Quantity"/> if this is a <see cref="Num"/>, else null.
        /// Convenience for ranking and filtering.
        /// </summary>
        public Quantity? AsQuantity() => (this as Num)?.Quantity;
    }
}
