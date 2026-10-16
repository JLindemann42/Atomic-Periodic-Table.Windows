using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Ai.Data
{
    /// <summary>How a field's values should be interpreted.</summary>
    public enum FieldKind { Numeric, Enum, Text, Struct, Id, Link }

    /// <summary>
    /// Broad grouping, used to answer "tell me about its thermal properties" and to order
    /// overviews.
    /// </summary>
    public enum FieldCategory
    {
        Identity, Thermo, Atomic, Electromagnetic, Crystal, Mechanical, Nuclear,
        Abundance, Safety, Ids,
    }

    /// <summary>Physical dimension, so the agent knows which units are interchangeable.</summary>
    public enum Dimension
    {
        Temperature, Mass, Density, Length, Energy, EnergyPerMol, Pressure, Power, Time,
        Velocity, Resistivity, Conductivity, VolumePerMol, HeatCapacity, Concentration,
        Area, Dimensionless,

        /// <summary>Litre-based volume. Distinct from <see cref="VolumePerMol"/>, a molar quantity.</summary>
        Volume,

        /// <summary>Amount of substance, base mol.</summary>
        Amount,

        /// <summary>
        /// Molar concentration, base mol/L.
        ///
        /// <para>Deliberately not folded into <see cref="Concentration"/>, which is mg/kg and backs
        /// the nine abundance fields. Sharing a dimension would let a field comparison rank a
        /// molarity against a crustal abundance and call the comparison meaningful.</para>
        /// </summary>
        Molarity,
    }

    /// <summary>
    /// What a user must own to see a field's value.
    ///
    /// <para>The element screens already gate these; the agent has to gate the same set or it
    /// becomes a way around the paywall - "what is the Young's modulus of gold" would answer in
    /// full what the Mechanical section shows as a lock. Declared alongside the field rather than
    /// as a list somewhere else, so the entitlement travels with the thing it protects and a new
    /// field cannot quietly default to free without someone writing it down.</para>
    /// </summary>
    public enum Tier
    {
        Free,

        /// <summary>Behind PRO, matching the element screen's gated blocks.</summary>
        Pro,

        /// <summary>Behind PRO+, matching the compound-analysis gate in the legacy handlers.</summary>
        ProPlus,
    }

    /// <summary>Where a citation for this field should send the user.</summary>
    public enum DeepLinkTarget
    {
        ElementInfo, Nuclide, Emission, Constants, Dictionary, Equations, Poisson, Geology,
        Solubility, Ion, Electrode, Ph, MolarMassCalc, UnitConverter, IdealGas,
        ReactionBalancer, LearningGames, Flashcards, ResendQuery,

        /// <summary>The PRO page, for an answer withheld behind the paywall.</summary>
        ProPage,
    }

    /// <summary>
    /// Everything the agent needs to know about one queryable field.
    /// </summary>
    /// <param name="Id">Canonical snake_case identifier - the only field name used in a query plan.</param>
    /// <param name="JsonKeys">
    /// The JSON key(s) backing it. Multi-key fields such as melting point carry their
    /// Kelvin/Celsius/Fahrenheit variants together so a unit request is a pure lookup.
    /// </param>
    /// <param name="LabelKey">
    /// An existing app string key, which gives free localized aliases in every shipped locale.
    /// </param>
    /// <param name="AllowsRange">Whether a hyphen in this field means "to" rather than "minus".</param>
    /// <param name="Localized">Whether the value differs between language files.</param>
    /// <param name="HigherIsMore">Whether "most"/"highest" means the numerically largest value.</param>
    /// <param name="OrdinalRange">
    /// For banks such as ionization energies, the valid 1-based slot range as (min, max).
    /// </param>
    public sealed record FieldSpec(
        string Id,
        IReadOnlyList<string> JsonKeys,
        FieldKind Kind,
        FieldCategory Category,
        string LabelKey,
        Dimension? Dimension = null,
        string? CanonicalUnit = null,
        DeepLinkTarget DeepLink = DeepLinkTarget.ElementInfo,
        bool AllowsRange = false,
        bool Localized = false,
        bool HigherIsMore = true,
        (int Min, int Max)? OrdinalRange = null,
        IReadOnlyList<string>? EnumValues = null,

        /// <summary>
        /// A label that reads correctly inside a sentence, when <paramref name="LabelKey"/> does
        /// not.
        ///
        /// <para>Reusing the element screens' labels gives free aliases in every locale and is
        /// excellent for <i>parsing</i>. It is wrong for <i>rendering</i>: those labels are table
        /// headings, and the abundance ones are prepositional phrases. Dropping "I jordskorpan"
        /// into the property template produced "Titan:s I jordskorpan är 4500" - two defects in one
        /// sentence. Null means <paramref name="LabelKey"/> already reads as a noun phrase.</para>
        /// </summary>
        string? SentenceLabelKey = null,

        /// <summary>
        /// The unit as a reader should see it, when that differs from
        /// <paramref name="CanonicalUnit"/>.
        ///
        /// <para><paramref name="CanonicalUnit"/> is a language-invariant token chosen so values
        /// can be grouped and converted; it is not always something to put in a sentence. The
        /// abundance fields forced the split: the solar reservoirs are counted in atoms against a
        /// silicon reference, which is prose, not a symbol.</para>
        /// </summary>
        string? UnitLabelKey = null,

        /// <summary>
        /// Whether to state the unit when the authored value did not carry one.
        ///
        /// <para>Off by default, because most of the element data writes its units into the value
        /// ("2743 (K)", "19.3 (g/cm³)") and <see cref="Quantity.Display"/> echoing the source
        /// verbatim is the right behaviour there. The abundance reservoirs are stored as bare
        /// numbers, so without this the agent answers "gold's abundance in sea water is 0.011" and
        /// the figure means nothing.</para>
        /// </summary>
        bool SurfaceUnit = false,

        /// <summary>What the user must own to be told this value.</summary>
        Tier Tier = Tier.Free)
    {
        /// <summary>The label to use inside a sentence.</summary>
        public string SentenceLabel() => SentenceLabelKey ?? LabelKey;

        /// <summary>
        /// The JSON key for a 1-based slot of a banked field, or the primary key when not banked.
        /// </summary>
        public string JsonKeyForOrdinal(int? ordinal)
        {
            if (OrdinalRange is not { } range)
                return JsonKeys[0];

            var slot = Math.Clamp(ordinal ?? 1, range.Min, range.Max);
            var stem = JsonKeys[0];
            if (stem.EndsWith('1'))
                stem = stem[..^1];
            return stem + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Canonicalise an enum value; null when it is one of the absent-value sentinels.</summary>
        public string? Canonicalise(string raw)
        {
            if (ValueParser.IsNullToken(raw)) return null;

            return Id switch
            {
                "series" => SeriesCanon.Series(raw) is var s && s != SeriesId.Unknown ? s.ToString() : null,
                "block" => SeriesCanon.Block(raw) is var b && b != Data.Block.Unknown ? b.ToString() : null,
                "phase" => SeriesCanon.Phase(raw),
                "radioactive" => SeriesCanon.Radioactive(raw) ? "YES" : "NO",
                _ => raw.Trim().ToLowerInvariant() is { Length: > 0 } t ? t : null,
            };
        }

        /// <summary>A copy that parses as a plain scalar, used when recursing into struct parts.</summary>
        public FieldSpec AsScalar() =>
            Kind == FieldKind.Struct ? this with { Kind = FieldKind.Numeric } : this;

        /// <summary>
        /// <see cref="Quantity.Display"/> with its unit stated, for the fields that opt into that.
        ///
        /// <para>Adds nothing when the authored value already carries a unit, so "8400 mg/kg" never
        /// comes back doubled - that was the element screen's long-standing bug, and it is the
        /// reason this decision lives in one place rather than at each call site.</para>
        /// </summary>
        /// <param name="unitLabel">
        /// Resolves a resource key. A delegate rather than an <c>IStringProvider</c> so this layer
        /// stays free of the localization types too.
        /// </param>
        public string DisplayWithUnit(Quantity quantity, Func<string, string> unitLabel)
        {
            if (!SurfaceUnit || quantity.UnitAuthored) return quantity.Display;

            var label = UnitLabelKey is null
                ? null
                : Text.Try.OrNull(() => unitLabel(UnitLabelKey))?.Trim();

            // The test provider resolves an unknown key to "str:<key>"; that must never print.
            if (string.IsNullOrEmpty(label) || label.StartsWith("str:", StringComparison.Ordinal))
                return quantity.Display;

            return quantity.Display + " " + label;
        }
    }
}
