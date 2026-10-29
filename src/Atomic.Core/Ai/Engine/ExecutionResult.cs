using System.Collections.Generic;
using Atomic.Core.Ai.Data;

namespace Atomic.Core.Ai.Engine
{
    /// <summary>Where an answer's information came from, so it can be shown and linked.</summary>
    /// <param name="FromTable">True when the source is one of the app's tables rather than the element data.</param>
    public sealed record Citation(
        string Label,
        string Source,
        DeepLinkTarget DeepLink,
        IReadOnlyDictionary<string, string>? Args = null,
        bool FromTable = false)
    {
        public IReadOnlyDictionary<string, string> Args { get; init; } =
            Args ?? new Dictionary<string, string>();
    }

    /// <summary>One element paired with the value that was asked about.</summary>
    public sealed record ValuedElement(ElementRecord Element, Quantity? Quantity, string Display);

    /// <summary>
    /// What running a <see cref="QueryPlan"/> produced.
    ///
    /// <para><see cref="NoData"/> is as important as the success cases. When a field is resolved but
    /// the element has no value for it, the engine reports that explicitly instead of printing the
    /// <c>"---"</c> sentinel or falling through to an unrelated keyword branch, which is what the
    /// previous agent did.</para>
    /// </summary>
    public abstract record ExecutionResult
    {
        private ExecutionResult() { }

        public abstract IReadOnlyList<Citation> Citations { get; init; }

        /// <summary>
        /// A single field of a single element.
        /// </summary>
        /// <param name="Rank">
        /// Where this value sits among all elements that have the field, 1 being the highest. Null
        /// when the field is not rankable. Gives a bare number context: 19.3 g/cm³ means little on
        /// its own, "the 6th densest of 105" means something.
        /// </param>
        /// <param name="RankedOutOf">How many elements have a recorded value for the field.</param>
        public sealed record Property(
            ElementRecord Element,
            string FieldId,
            Quantity? Quantity,
            string Display,
            IReadOnlyList<Citation> Citations,
            int? Rank = null,
            int RankedOutOf = 0) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>How two elements stand relative to one another on one property.</summary>
        /// <param name="ClaimHolds">Set for a yes/no question: whether the claim as asked is true.</param>
        /// <param name="Ratio">Set when the question asked by how much.</param>
        public sealed record Comparative(
            ElementRecord Winner,
            ElementRecord Loser,
            string FieldId,
            Quantity WinnerValue,
            Quantity LoserValue,
            bool? ClaimHolds,
            double? Ratio,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>The element adjacent to another in atomic-number order.</summary>
        public sealed record Neighbour(
            ElementRecord From,
            ElementRecord To,
            bool Forward,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>Several elements compared across one or more fields.</summary>
        public sealed record Comparison(
            IReadOnlyList<ElementRecord> Elements,
            IReadOnlyList<string> FieldIds,
            IReadOnlyDictionary<string, IReadOnlyList<ValuedElement>> Values,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>
        /// A ranked or filtered set of elements.
        /// </summary>
        /// <param name="Matched">How many elements satisfied the filters.</param>
        /// <param name="Missing">How many matched but had no value for the sorted field.</param>
        /// <param name="RankOffset">How far down the ranking the shown rows start; non-zero for "the third densest".</param>
        /// <param name="RunnerUp">The next element down, so a single-element answer has something to be measured against.</param>
        public sealed record ElementList(
            IReadOnlyList<ValuedElement> Results,
            string? FieldId,
            int Matched,
            int Missing,
            bool Descending,
            IReadOnlyList<Citation> Citations,
            int RankOffset = 0,
            ValuedElement? RunnerUp = null) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>A statistic computed over a set of elements.</summary>
        public sealed record Aggregate(
            Aggregation Aggregation,
            string FieldId,
            double Value,
            string? Unit,
            IReadOnlyList<ValuedElement> Contributors,
            int Missing,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>
        /// An element's isotopes.
        /// </summary>
        /// <param name="Shown">The isotopes rendered, longest-lived first.</param>
        /// <param name="Total">How many the element has in total, so a truncated list can say so.</param>
        public sealed record Isotopes(
            ElementRecord Element,
            IReadOnlyList<Isotope> Shown,
            int Total,
            int StableCount,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>An element's NFPA 704 hazard ratings.</summary>
        public sealed record Safety(
            ElementRecord Element,
            Nfpa Nfpa,
            bool Radioactive,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>
        /// An element's emission spectrum.
        ///
        /// <para>Carries only the element, because that is genuinely all the app has: the spectrum
        /// exists as a rendered image per element and there are no wavelengths, intensities or line
        /// names anywhere in the data. Modelling it as a result of its own - rather than as a field
        /// with no value - is what lets the answer show the picture while saying plainly that it
        /// cannot name a line.</para>
        /// </summary>
        public sealed record EmissionSpectrum(
            ElementRecord Element,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>A parsed chemical formula with its molar mass and composition.</summary>
        /// <param name="WantsComposition">Whether the question asked for the percentage breakdown.</param>
        public sealed record Formula(
            FormulaResult Result,
            bool WantsComposition,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>One nuclide with everything known about it, for side-by-side display.</summary>
        public sealed record NuclideFacts(
            ElementRecord Element,
            int MassNumber,
            int Protons,
            int Neutrons,
            Isotope? Isotope);

        /// <summary>Two nuclides compared.</summary>
        public sealed record IsotopeComparison(
            NuclideFacts Left,
            NuclideFacts Right,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>Nucleon counts for a specific nuclide.</summary>
        public sealed record Nuclide(
            ElementRecord Element,
            int MassNumber,
            int Protons,
            int Neutrons,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>A mole/particle conversion.</summary>
        /// <param name="Grams">
        /// The mass the question supplied, when it asked the other way round. Null for "how many
        /// atoms are in 2.5 moles of iron"; set for "if I have 12 grams of carbon-12, how many
        /// moles is that".
        /// </param>
        public sealed record MoleConversion(
            double? Moles,
            double Particles,
            string? Substance,
            bool ToParticles,
            double? Grams = null,
            IReadOnlyList<Citation>? Citations = null) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations ?? [];
        }

        /// <summary>
        /// A value the user supplied, expressed in another unit.
        /// </summary>
        /// <param name="Bridged">
        /// True when the conversion crossed a dimension through a physical constant - eV to kJ/mol -
        /// so the answer can name the constant rather than presenting the two as interchangeable
        /// quantities, which they are not.
        /// </param>
        public sealed record UnitConversion(
            double Value,
            string FromUnit,
            double Converted,
            string ToUnit,
            bool Bridged,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>
        /// A chemical equation, balanced or honestly declined.
        ///
        /// <para><paramref name="Reason"/> being non-null is a first-class answer rather than an
        /// error. An equation that cannot balance with positive integers, or that has several
        /// independent balances, has to be <i>said</i> - answering with one of the possibilities and
        /// no warning is worse than not answering.</para>
        /// </summary>
        /// <param name="Tally">
        /// The per-element proof: how many atoms each side ends up with. A balancer that prints only
        /// coefficients asks to be trusted; one that shows both counts can be checked.
        /// </param>
        /// <param name="Detail">
        /// What the failure was about - for a one-sided element, which element. The solver has
        /// always known; it used to be dropped here, so every failure was reported with the same
        /// general sentence.
        /// </param>
        public sealed record BalancedEquation(
            IReadOnlyList<Term> Reactants,
            IReadOnlyList<Term> Products,
            IReadOnlyList<ElementTally> Tally,
            EquationBalancer.Reason? Reason,
            IReadOnlyList<Citation> Citations,
            string? Detail = null) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>Which unknown a solution question asked for.</summary>
        public enum SolutionKind { MassNeeded, Moles, Molarity, Volume, Dilution }

        /// <summary>
        /// A solution-chemistry calculation: n = c·V, a mass from a concentration, or a dilution.
        /// </summary>
        /// <param name="Kind">
        /// Which unknown the question asked for, which decides the sentence the answer leads with.
        /// The other fields are filled in regardless, because the working is shown.
        /// </param>
        /// <param name="Substance">
        /// The compound as written, or as resolved from a common name. Null for a bare dilution.
        /// </param>
        public sealed record SolutionCalc(
            SolutionKind Kind,
            string? Substance,
            double? MolarMass,
            double? Moles,
            double? Molarity,
            double? Litres,
            double? Grams,
            Dilution? Dilution,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>
        /// Which of the three "no number" outcomes a decay question hit.
        ///
        /// <para>They are not the same thing: the nuclide is stable, the app does not list it, or it
        /// is listed with a half-life the data records as unknown. Collapsing them would tell a user
        /// the app has no data when what it has is the opposite - a positive statement that nothing
        /// decays.</para>
        /// </summary>
        public enum DecayMode { Remaining, Elapsed, HalfLives, Stable, NoHalfLife }

        /// <summary>Exponential decay of a nuclide over time.</summary>
        /// <param name="AmountUnit">The unit the amounts were written in, echoed back rather than converted.</param>
        /// <param name="ElapsedUnit">The unit the elapsed time was written in, so the answer can reply in it.</param>
        public sealed record Decay(
            ElementRecord Element,
            int MassNumber,
            double? HalfLifeSeconds,
            string HalfLifeDisplay,
            bool Stable,
            DecayMode Mode,
            double? InitialAmount,
            double? FinalAmount,
            string? AmountUnit,
            double? ElapsedSeconds,
            string? ElapsedUnit,
            double? HalfLivesElapsed,
            double? FractionRemaining,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>One field across a set of elements, in atomic-number order (roadmap 2.3).</summary>
        /// <param name="Points">The elements with a value, in atomic-number order - the order a trend is read in.</param>
        /// <param name="Missing">Elements in the set with no value for the field, so the answer can say so.</param>
        /// <param name="WholeTable">True when no subset was named: the answer is a map of the table, not a line.</param>
        public sealed record Trend(
            string FieldId,
            IReadOnlyList<ValuedElement> Points,
            int Missing,
            bool WholeTable,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>
        /// A reviewed explanation (roadmap 2.4), with the data it explains: a trend over a set or the
        /// whole table, or one element's ionization series.
        /// </summary>
        /// <param name="Language">The language <paramref name="Text"/> is in, so an English fallback can be disclosed.</param>
        public sealed record Explanation(
            string Key,
            string Text,
            string Language,
            Trend? ExplainedTrend,
            ElementRecord? Ionization,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>A row from one of the app's tables.</summary>
        public sealed record Dataset(
            Data.DatasetRow Row,
            IReadOnlyList<Citation> Citations) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations;
        }

        /// <summary>
        /// The question was understood but the app has no value for it.
        /// </summary>
        /// <param name="Coverage">
        /// How many of the 118 elements do have this field, so the answer can say how sparse it is
        /// rather than implying the data should have been there.
        /// </param>
        public sealed record NoData(
            string FieldId,
            ElementRecord? Element,
            int Coverage,
            IReadOnlyList<Citation>? Citations = null) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations ?? [];
        }

        /// <summary>
        /// The question was understood, but its answer is behind PRO or PRO+.
        ///
        /// <para>A distinct case rather than a variant of <see cref="NoData"/>, because the two say
        /// opposite things: NoData means the app does not have the value, Locked means it has it and
        /// is not showing it. Conflating them would have the agent claim ignorance of data the
        /// element screen visibly holds.</para>
        ///
        /// <para>Produced by the executor rather than the composer, so the value never reaches a
        /// rendering layer at all - a filtered list or an aggregate over a gated field would
        /// otherwise leak it in aggregate even if the sentence never named it.</para>
        /// </summary>
        /// <param name="FieldIds">The gated fields that caused this.</param>
        public sealed record Locked(
            Tier Tier,
            IReadOnlyList<string> FieldIds,
            ElementRecord? Element,
            IReadOnlyList<Citation>? Citations = null) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations ?? [];
        }

        /// <summary>The filters were understood but nothing satisfied them.</summary>
        public sealed record Empty(
            IReadOnlyList<string> DescribedFilters,
            IReadOnlyList<Citation>? Citations = null) : ExecutionResult
        {
            public override IReadOnlyList<Citation> Citations { get; init; } = Citations ?? [];
        }
    }
}
