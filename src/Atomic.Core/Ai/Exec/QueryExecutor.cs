using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Localization;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Exec
{
    /// <summary>
    /// Runs a <see cref="QueryPlan"/> against the index.
    ///
    /// <para>Two rules run through everything here:</para>
    /// <list type="bullet">
    /// <item><b>Absent data is reported, never guessed.</b> An element with no value for the
    /// requested field yields <see cref="ExecutionResult.NoData"/> carrying the field's coverage, so
    /// the answer can say how sparse the data is instead of printing a sentinel or drifting to
    /// another topic.</item>
    /// <item><b>Aggregates always disclose what they missed.</b> A mean over the halogens states
    /// how many of them actually had a value, rather than quietly averaging whatever was
    /// present.</item>
    /// </list>
    /// </summary>
    public sealed class QueryExecutor
    {
        private readonly KnowledgeStore _store;
        private readonly DatasetIndex _datasets;
        private readonly LocalizedView? _localized;
        private readonly IStringProvider _strings;
        private readonly Entitlements _entitlements;
        private readonly Explanations _explanations;

        /// <param name="entitlements">
        /// What the user may be told. Defaulted to full access so existing callers and tests are
        /// unaffected; the app supplies the real entitlements.
        /// </param>
        public QueryExecutor(
            KnowledgeStore store,
            DatasetIndex datasets,
            LocalizedView? localized,
            IStringProvider strings,
            Entitlements? entitlements = null,
            Explanations? explanations = null)
        {
            _store = store;
            _datasets = datasets;
            _localized = localized;
            _strings = strings;
            _entitlements = entitlements ?? Entitlements.Full;
            _explanations = explanations ?? Explanations.Empty;
        }

        /// <summary>
        /// Refuse a plan whose answer is behind a paywall, before any value is read.
        ///
        /// <para>Enforced here rather than in the composer because a gated field leaks in more
        /// shapes than a sentence: "which element has the highest Young's modulus" is a ranking over
        /// the whole gated column, and an average over it is the column summarised. Returning
        /// <see cref="ExecutionResult.Locked"/> from one place covers every intent at once.</para>
        ///
        /// <para>A category lookup is the deliberate exception - see <see cref="Category"/>, which
        /// drops the gated fields and answers with the free ones rather than locking the family.</para>
        /// </summary>
        private ExecutionResult? Gate(QueryPlan plan)
        {
            if (plan.Intent == Intent.CategoryLookup) return null;

            var fields = plan.SortField is null
                ? plan.FieldIds
                : [.. plan.FieldIds, plan.SortField];

            if (_entitlements.BlockingTier(fields) is not { } tier) return null;

            return new ExecutionResult.Locked(
                Tier: tier,
                FieldIds: fields.Where(f => !_entitlements.AllowsField(f)).Distinct(StringComparer.Ordinal).ToList(),
                Element: ElementOf(plan));
        }

        public ExecutionResult? Execute(QueryPlan plan)
        {
            if (Gate(plan) is { } locked) return locked;

            return plan.Intent switch
            {
                Intent.PropertyLookup => Property(plan),
                Intent.CategoryLookup => Category(plan),
                Intent.Isotopes => Isotopes(plan),

                // The hazard ratings are behind PRO on the element screen; reading them aloud here
                // would be the same data by another route.
                Intent.Safety => _entitlements.Allows(Tier.Pro)
                    ? Safety(plan)
                    : new ExecutionResult.Locked(
                        Tier.Pro,
                        ["nfpa_health", "nfpa_flammability", "nfpa_instability"],
                        ElementOf(plan)),

                Intent.EmissionSpectrum => EmissionSpectrum(plan),

                Intent.FormulaMass => _entitlements.Allows(Tier.ProPlus)
                    ? FormulaMass(plan)
                    : new ExecutionResult.Locked(Tier.ProPlus, [], null),

                Intent.NuclideCount => Nuclide(plan),
                Intent.IsotopeComparison => IsotopeComparison(plan),
                Intent.Comparative => Comparative(plan),
                Intent.Neighbour => Neighbour(plan),
                Intent.MoleConversion => MoleConversion(plan),

                // Free, and deliberately so: unit conversion is already free in the engine as a
                // target unit on any property lookup, and free in the unit conversion tool. Gating
                // it here would take away something the app already gives.
                Intent.UnitConvert => UnitConvert(plan),

                // Balancing is compound analysis, which the chat already gates at PRO+ for molar
                // mass. Shipping one free beside the other locked would make the paywall look
                // arbitrary.
                Intent.BalanceEquation => _entitlements.Allows(Tier.ProPlus)
                    ? BalanceEquation(plan)
                    : new ExecutionResult.Locked(Tier.ProPlus, [], null),

                // Same gate, same reasoning: every branch but a bare dilution needs a compound
                // molar mass. A split gate would be more generous and would produce the confusing
                // experience of one solution question answering and the next one locking.
                Intent.SolutionCalc => _entitlements.Allows(Tier.ProPlus)
                    ? SolutionCalc(plan)
                    : new ExecutionResult.Locked(Tier.ProPlus, [], null),

                // Free: the isotope intent is ungated, the Table of Nuclides screen is free, and
                // every input here is data the user can already read. Gating arithmetic over free
                // data would be a new paywall rather than a mirror of an existing one.
                Intent.DecayCalc => Decay(plan),

                Intent.Comparison => Comparison(plan),
                Intent.Superlative or Intent.FilterList => ElementList(plan),
                Intent.Aggregate => Aggregate(plan),
                Intent.DatasetLookup => Dataset(plan),
                Intent.Trend => Trend(plan),
                Intent.Explanation => Explanation(plan),
                Intent.Unknown => null,
                _ => null,
            };
        }

        // ---- Property ---------------------------------------------------------------------

        private ExecutionResult? Property(QueryPlan plan)
        {
            if (ElementOf(plan) is not { } element) return null;
            if (plan.PrimaryField is not { } fieldId) return null;
            if (!FieldRegistry.ById.TryGetValue(fieldId, out var spec)) return null;

            // Banked fields such as ionization energy address a specific slot.
            var value = plan.FieldOrdinal is { } ordinal &&
                        element.Values.TryGetValue($"{fieldId}#{ordinal}", out var slotted)
                ? slotted
                : element.Value(fieldId);

            if (value.IsMissing)
                return new ExecutionResult.NoData(fieldId, element, _store.CoverageOf(fieldId));

            var quantity = value.AsQuantity();
            if (quantity is not null && plan.TargetUnit is { } target)
                quantity = _store.QuantityIn(element, fieldId, target) ?? Convert(quantity, target);

            var ranking = quantity is null ? null : RankOf(element, fieldId, quantity);

            return new ExecutionResult.Property(
                Element: element,
                FieldId: fieldId,
                Quantity: quantity,
                Display: Render(value, quantity, plan.TargetUnit, element, fieldId),
                Citations: [CitationFor(spec.Id, element)],
                Rank: ranking?.Rank,
                RankedOutOf: ranking?.OutOf ?? 0);
        }

        /// <summary>
        /// Where an element sits among all elements that have a value for a field, 1 being highest.
        ///
        /// <para>A bare figure carries little meaning on its own - 19.3 g/cm³ says nothing unless
        /// you already know the range. Only computed for numeric fields where ordering is
        /// meaningful: ranking atomic numbers or CAS registry numbers would be noise.</para>
        /// </summary>
        private (int Rank, int OutOf)? RankOf(ElementRecord element, string fieldId, Quantity quantity)
        {
            if (!FieldRegistry.ById.TryGetValue(fieldId, out var spec)) return null;
            if (spec.Kind != FieldKind.Numeric) return null;
            if (UnrankableFields.Contains(fieldId)) return null;

            var values = _store.Elements
                .Select(e => e.Quantity(fieldId)?.Mid)
                .OfType<double>()
                .ToList();

            if (values.Count < MinForRank) return null;
            return (values.Count(v => v > quantity.Mid) + 1, values.Count);
        }

        // ---- Category ---------------------------------------------------------------------

        /// <summary>
        /// Every populated field of one family, for one element.
        ///
        /// <para>Reuses the comparison result shape with a single element, so the composer renders
        /// it as a labelled list without needing a separate branch.</para>
        /// </summary>
        private ExecutionResult? Category(QueryPlan plan)
        {
            if (ElementOf(plan) is not { } element) return null;

            var values = new Dictionary<string, IReadOnlyList<ValuedElement>>(StringComparer.Ordinal);
            var order = new List<string>();
            var withheld = 0;

            foreach (var fieldId in plan.FieldIds)
            {
                // A family is answered with whatever the user is entitled to rather than locked
                // whole: "the mechanical properties of gold" holds both free and PRO fields, and
                // withholding the free ones because a gated one shares the family would be gating
                // more than the app does. The count is carried so the answer can say so.
                if (!_entitlements.AllowsField(fieldId)) { withheld++; continue; }

                var value = element.Value(fieldId);
                if (value.IsMissing) continue;

                var quantity = _store.QuantityIn(element, fieldId, plan.TargetUnit);
                values[fieldId] =
                [
                    new ValuedElement(element, quantity,
                                      Render(value, quantity, plan.TargetUnit, element, fieldId)),
                ];
                order.Add(fieldId);
            }

            if (order.Count == 0)
            {
                // Everything in this family was gated, so there is nothing free left to say.
                if (withheld > 0 && _entitlements.BlockingTier(plan.FieldIds) is { } tier)
                {
                    return new ExecutionResult.Locked(
                        tier,
                        plan.FieldIds.Where(f => !_entitlements.AllowsField(f)).ToList(),
                        element);
                }
                return new ExecutionResult.Empty(DescribeFilters(plan));
            }

            return new ExecutionResult.Comparison(
                Elements: [element],
                FieldIds: order,
                Values: values,
                Citations: [CitationFor(order[0], element)]);
        }

        // ---- Comparatives and neighbours ---------------------------------------------------

        private ExecutionResult? Comparative(QueryPlan plan)
        {
            var elements = plan.ElementKeys.Select(k => _store.Element(k)).OfType<ElementRecord>().ToList();
            if (elements.Count < 2) return null;
            if (plan.PrimaryField is not { } fieldId) return null;

            var first = _store.QuantityIn(elements[0], fieldId, plan.TargetUnit);
            if (first is null)
                return new ExecutionResult.NoData(fieldId, elements[0], _store.CoverageOf(fieldId));

            var second = _store.QuantityIn(elements[1], fieldId, plan.TargetUnit);
            if (second is null)
                return new ExecutionResult.NoData(fieldId, elements[1], _store.CoverageOf(fieldId));

            // The subject of the question is the element named first; "is gold denser than lead"
            // holds when gold is on the winning side of the comparison the adjective describes.
            var firstWins = plan.SortDescending ? first.Mid > second.Mid : first.Mid < second.Mid;

            return new ExecutionResult.Comparative(
                Winner: firstWins ? elements[0] : elements[1],
                Loser: firstWins ? elements[1] : elements[0],
                FieldId: fieldId,
                WinnerValue: firstWins ? first : second,
                LoserValue: firstWins ? second : first,
                ClaimHolds: plan.Limit == 1 ? firstWins : null,
                Ratio: plan.Limit == 3 && (firstWins ? second : first).Mid != 0.0
                    ? (firstWins ? first : second).Mid / (firstWins ? second : first).Mid
                    : null,
                Citations: elements.Select(e => CitationFor(fieldId, e)).ToList());
        }

        private ExecutionResult? Neighbour(QueryPlan plan)
        {
            var keys = plan.ElementKeys;
            if (keys.Count < 2) return null;
            if (_store.Element(keys[0]) is not { } from) return null;
            if (_store.Element(keys[1]) is not { } to) return null;

            return new ExecutionResult.Neighbour(
                from, to, plan.Limit > 0, [CitationFor("atomic_number", to)]);
        }

        // ---- Calculations -------------------------------------------------------------------

        private ExecutionResult? FormulaMass(QueryPlan plan)
        {
            var result = ChemistryMath.ParseFormula(plan.RawQuery, AtomicMass);
            if (result is null) return null;

            return new ExecutionResult.Formula(
                Result: result,
                WantsComposition: plan.FieldIds.Contains("composition", StringComparer.Ordinal),
                Citations: result.Parts
                    .Select(part => _store.BySymbol(part.Symbol))
                    .OfType<ElementRecord>()
                    .Select(e => CitationFor("atomic_mass", e))
                    .Take(3)
                    .ToList());
        }

        /// <summary>
        /// Convert a number the user wrote into the unit they asked for.
        ///
        /// <para>The query is re-scanned here rather than carried on the plan, the same way the
        /// mole branch re-reads its own quantity - and for the same reason the scanner is shared:
        /// two readings of "250 mL" that can disagree eventually will.</para>
        /// </summary>
        private static ExecutionResult? UnitConvert(QueryPlan plan)
        {
            var source = QuantityScanner.Scan(plan.RawQuery).FirstOrDefault();
            if (source is null) return null;
            if (plan.TargetUnit is not { } target) return null;

            var converted = UnitConverter.ConvertAcrossBridges(source.Value, source.Unit, target);
            if (converted is null) return null;

            return new ExecutionResult.UnitConversion(
                Value: source.Value,
                FromUnit: source.Unit,
                Converted: converted.Value,
                ToUnit: target,
                Bridged: UnitConverter.IsBridged(source.Unit, target),
                Citations: []);
        }

        /// <summary>
        /// Balance an equation, or say why it cannot be balanced.
        ///
        /// <para>A failure is returned as a result rather than as null. Declining here would drop
        /// the query back to the legacy router, which answers it by mentioning that the app has a
        /// reaction balancer - advice, where the user asked a question.</para>
        /// </summary>
        private ExecutionResult BalanceEquation(QueryPlan plan)
        {
            var outcome = EquationBalancer.Parse(plan.RawQuery, s => _store.BySymbol(s) is not null);

            if (outcome is EquationBalancer.ParseOutcome.TooManySpeciesOutcome)
                // Read fine, just too long. Reporting this as a parse failure told the user their
                // equation was unreadable when the objection was its size.
                return Failed(EquationBalancer.Reason.TooLarge);

            if (outcome is not EquationBalancer.ParseOutcome.Sides sides)
                return Failed(EquationBalancer.Reason.ParseFailed);

            var equation = string.Join(" + ", sides.Reactants.Select(s => s.Formula)) +
                           " -> " +
                           string.Join(" + ", sides.Products.Select(s => s.Formula));

            return EquationBalancer.Balance(sides.Reactants, sides.Products) switch
            {
                EquationBalancer.Result.Balanced b => new ExecutionResult.BalancedEquation(
                    b.Reactants, b.Products, b.Tally, null, CitationsFor(equation)),
                EquationBalancer.Result.Failed f => Failed(f.Reason, equation, f.Detail),
                _ => Failed(EquationBalancer.Reason.ParseFailed),
            };

            ExecutionResult.BalancedEquation Failed(
                EquationBalancer.Reason reason, string? equation = null, string? detail = null) =>
                new([], [], [], reason, CitationsFor(equation), detail);
        }

        /// <summary>
        /// The chip under a balancer answer.
        ///
        /// <para>The equation passed is what the solver read rather than what the user typed, so
        /// "balance this reaction: Fe + O2 -> Fe2O3" opens the tool on the equation without the
        /// sentence around it. Passing it at all is the point: the chip used to open an empty input
        /// box, asking the user to retype the equation they had just been given the answer to.</para>
        /// </summary>
        private IReadOnlyList<Citation> CitationsFor(string? equation)
        {
            var label = _strings.Get(S.balancer_tool_title);
            return
            [
                new Citation(
                    Label: label,
                    Source: label,
                    DeepLink: DeepLinkTarget.ReactionBalancer,
                    Args: equation is null
                        ? new Dictionary<string, string>()
                        : new Dictionary<string, string>(StringComparer.Ordinal) { [ArgEquation] = equation },
                    FromTable: true),
            ];
        }

        /// <summary>
        /// The deep-link argument carrying an equation to the balancer.
        ///
        /// <para>Declared here rather than on the navigator, which is a Phase 4f file. The navigator
        /// reads the same name.</para>
        /// </summary>
        internal const string ArgEquation = "equation";

        /// <summary>
        /// Work a solution problem: n = c·V, a mass from a concentration, or a dilution.
        ///
        /// <para>The quantities are read back off the raw query in litres, moles and mol/L, so every
        /// branch below is doing arithmetic in one system of units rather than remembering which
        /// input was written in millilitres.</para>
        /// </summary>
        private ExecutionResult? SolutionCalc(QueryPlan plan)
        {
            var raw = plan.RawQuery;
            var molarities = QuantityScanner.Of(raw, Dimension.Molarity)
                .Select(q => q.InUnit("M")).OfType<double>().ToList();
            var volumes = QuantityScanner.Of(raw, Dimension.Volume)
                .Select(q => q.InUnit("L")).OfType<double>().ToList();
            var amounts = QuantityScanner.Of(raw, Dimension.Amount)
                .Select(q => q.InUnit("mol")).OfType<double>().ToList();

            var gramsMatch = Grams.Match(raw.ToLowerInvariant());
            double? grams = gramsMatch.Success && TryNumber(gramsMatch.Groups[1].Value, out var g) ? g : null;

            var compound = plan.Literal;
            var parsed = compound is null ? null : ChemistryMath.ParseFormula(compound, AtomicMass);
            var molarMass = parsed?.MolarMass;

            IReadOnlyList<Citation> citations = parsed is null
                ? []
                : parsed.Parts
                    .Select(part => _store.BySymbol(part.Symbol))
                    .OfType<ElementRecord>()
                    .Select(e => CitationFor("atomic_mass", e))
                    .Take(3)
                    .ToList();

            // Two concentrations is the shape of a dilution and of nothing else, so it is checked
            // before the single-solution branches rather than after.
            if (molarities.Count >= 2)
            {
                var solved = SolutionMath.DilutionOf(
                    molarities[0],
                    volumes.Count > 0 ? volumes[0] : null,
                    molarities[1],
                    volumes.Count > 1 ? volumes[1] : null);
                if (solved is null) return null;

                return new ExecutionResult.SolutionCalc(
                    Kind: ExecutionResult.SolutionKind.Dilution,
                    Substance: compound, MolarMass: molarMass,
                    Moles: null, Molarity: null, Litres: null, Grams: null,
                    Dilution: solved, Citations: citations);
            }

            var molesFromMass = grams is { } mass && molarMass is { } mm
                ? SolutionMath.MolesOf(mass, mm)
                : null;
            var moles = amounts.Count > 0 ? amounts[0] : molesFromMass;

            var completed = SolutionMath.Complete(
                moles,
                molarities.Count > 0 ? molarities[0] : null,
                volumes.Count > 0 ? volumes[0] : null);
            if (completed is not { } done) return null;

            var computedMass = molarMass is { } molar ? SolutionMath.MassOf(done.Moles, molar) : null;

            // What the question was missing is what it was asking for.
            var kind =
                grams is null && amounts.Count == 0 && molarMass is not null
                    ? ExecutionResult.SolutionKind.MassNeeded
                : molarities.Count == 0 ? ExecutionResult.SolutionKind.Molarity
                : volumes.Count == 0 ? ExecutionResult.SolutionKind.Volume
                : ExecutionResult.SolutionKind.Moles;

            return new ExecutionResult.SolutionCalc(
                Kind: kind,
                Substance: compound,
                MolarMass: molarMass,
                Moles: done.Moles,
                Molarity: done.Molarity,
                Litres: done.Litres,
                Grams: computedMass ?? grams,
                Dilution: null,
                Citations: citations);
        }

        /// <summary>
        /// Decay arithmetic over a nuclide's half-life.
        ///
        /// <para>The three ways this can have no number are kept apart. "Stable" is a positive
        /// statement - nothing decays, so nothing is left to compute - while "not in the table" and
        /// "listed with an unrecorded half-life" are two different gaps. Collapsing them into one
        /// "no data" would tell a user the app knows nothing where in fact it knows the
        /// opposite.</para>
        /// </summary>
        private ExecutionResult? Decay(QueryPlan plan)
        {
            if (ElementOf(plan) is not { } element) return null;

            var massNumber = plan.Limit;
            IReadOnlyList<Citation> citations = [CitationFor("common_neutrons", element)];

            ExecutionResult.Decay Result(
                ExecutionResult.DecayMode mode,
                double? halfLife = null,
                string display = "",
                bool stable = false,
                double? initial = null,
                double? final = null,
                string? amountUnit = null,
                double? elapsed = null,
                string? elapsedUnit = null,
                double? halfLivesElapsed = null,
                double? fraction = null) =>
                new(element, massNumber, halfLife, display, stable, mode,
                    initial, final, amountUnit, elapsed, elapsedUnit, halfLivesElapsed, fraction,
                    citations);

            // Nucleons leads because MassNumber overflows for many elements: IsotopeParser
            // digit-filters the atomic mass, so carbon's "14.003241988" becomes an 11-digit integer.
            var isotope = element.Isotopes.FirstOrDefault(
                i => i.Nucleons == massNumber || i.MassNumber == massNumber);
            if (isotope is null) return Result(ExecutionResult.DecayMode.NoHalfLife);

            if (isotope.Stable)
                return Result(ExecutionResult.DecayMode.Stable,
                              display: isotope.HalfLifeDisplay, stable: true);

            if (isotope.HalfLifeSeconds is not { } halfLife)
                return Result(ExecutionResult.DecayMode.NoHalfLife, display: isotope.HalfLifeDisplay);

            var display = isotope.HalfLifeDisplay;
            var time = QuantityScanner.FirstOf(plan.RawQuery, Dimension.Time);
            var masses = Grams.Matches(plan.RawQuery.ToLowerInvariant())
                .Select(m => TryNumber(m.Groups[1].Value, out var v) ? v : (double?)null)
                .OfType<double>()
                .ToList();

            if (time is not null)
            {
                if (time.InUnit("s") is not { } elapsedSeconds) return null;
                if (DecayMath.HalfLives(elapsedSeconds, halfLife) is not { } n) return null;

                double? initial = masses.Count > 0 ? masses[0] : null;
                return Result(
                    // With a starting amount the answer is how much is left; without one, all that
                    // can honestly be said is how many half-lives went by and what fraction survives.
                    initial is not null
                        ? ExecutionResult.DecayMode.Remaining
                        : ExecutionResult.DecayMode.HalfLives,
                    halfLife: halfLife, display: display,
                    initial: initial,
                    final: initial is { } start ? DecayMath.Remaining(start, elapsedSeconds, halfLife) : null,
                    amountUnit: initial is not null ? "g" : null,
                    elapsed: elapsedSeconds,
                    elapsedUnit: time.Unit,
                    halfLivesElapsed: n,
                    fraction: DecayMath.FractionRemaining(n));
            }

            // No duration written, so the duration is the unknown: "how long until 1 g becomes 0.5 g".
            if (masses.Count >= 2)
            {
                if (DecayMath.ElapsedFor(masses[0], masses[1], halfLife) is not { } elapsedSeconds) return null;
                if (DecayMath.HalfLives(elapsedSeconds, halfLife) is not { } n) return null;

                return Result(
                    ExecutionResult.DecayMode.Elapsed,
                    halfLife: halfLife, display: display,
                    initial: masses[0], final: masses[1], amountUnit: "g",
                    elapsed: elapsedSeconds, elapsedUnit: "s",
                    halfLivesElapsed: n,
                    fraction: masses[1] / masses[0]);
            }

            return null;
        }

        private ExecutionResult? Nuclide(QueryPlan plan)
        {
            if (ElementOf(plan) is not { } element) return null;

            var massNumber = plan.Limit;
            if (ChemistryMath.NeutronsIn(massNumber, element.AtomicNumber) is not { } neutrons) return null;

            return new ExecutionResult.Nuclide(
                Element: element,
                MassNumber: massNumber,
                Protons: element.AtomicNumber,
                Neutrons: neutrons,
                Citations: [CitationFor("atomic_number", element)]);
        }

        private ExecutionResult? IsotopeComparison(QueryPlan plan)
        {
            var refs = plan.Entities.OfType<EntityRef.Nuclide>().ToList();
            if (refs.Count < 2) return null;

            if (NuclideFacts(refs[0]) is not { } left) return null;
            if (NuclideFacts(refs[1]) is not { } right) return null;

            var citations = new[]
                {
                    CitationFor("common_neutrons", left.Element),
                    CitationFor("common_neutrons", right.Element),
                }
                .DistinctBy(c => c.Source)
                .ToList();

            return new ExecutionResult.IsotopeComparison(left, right, citations);
        }

        /// <summary>
        /// Everything known about one nuclide.
        ///
        /// <para>Proton and neutron counts are always derivable from the element and the mass
        /// number; the decay mode and half-life come from the isotope table when that nuclide is
        /// listed there.</para>
        /// </summary>
        private ExecutionResult.NuclideFacts? NuclideFacts(EntityRef.Nuclide reference)
        {
            if (_store.Element(reference.Key) is not { } element) return null;
            if (ChemistryMath.NeutronsIn(reference.MassNumber, element.AtomicNumber) is not { } neutrons)
                return null;

            return new ExecutionResult.NuclideFacts(
                Element: element,
                MassNumber: reference.MassNumber,
                Protons: element.AtomicNumber,
                Neutrons: neutrons,
                Isotope: element.Isotopes.FirstOrDefault(
                    i => i.Nucleons == reference.MassNumber || i.MassNumber == reference.MassNumber));
        }

        private ExecutionResult? MoleConversion(QueryPlan plan)
        {
            var lowered = plan.RawQuery.ToLowerInvariant();
            var element = ElementOf(plan);

            // The same pattern the planner matched on. Its own English-only copy meant an Italian,
            // Portuguese, Hindi, Urdu or Chinese mole question was planned correctly and then
            // executed to nothing at all.
            var moleMatch = ChemistryMath.MoleQuantity.Match(lowered);
            if (!moleMatch.Success || !TryNumber(moleMatch.Groups[1].Value, out var moles))
            {
                // Mass to moles, which needs the substance's molar mass and so cannot be answered
                // without knowing what the substance is.
                var gramsMatch = Grams.Match(lowered);
                if (!gramsMatch.Success || !TryNumber(gramsMatch.Groups[1].Value, out var grams)) return null;

                // A compound named in words carries its formula (roadmap 1.2): "10 g of sodium
                // chloride" is converted through NaCl's molar mass, not sodium's atomic mass.
                if (plan.Literal is { } formula)
                {
                    if (ChemistryMath.ParseFormula(formula, AtomicMass) is not { } parsed) return null;
                    if (ChemistryMath.MassToMoles(grams, parsed.MolarMass) is not { } fromFormula) return null;
                    return new ExecutionResult.MoleConversion(
                        Moles: fromFormula,
                        Particles: ChemistryMath.MolesToParticles(fromFormula),
                        Substance: parsed.Formula,
                        ToParticles: false,
                        Grams: grams);
                }

                if (element?.Quantity("atomic_mass")?.Value is not { } molarMass) return null;
                if (ChemistryMath.MassToMoles(grams, molarMass) is not { } converted) return null;

                return new ExecutionResult.MoleConversion(
                    Moles: converted,
                    Particles: ChemistryMath.MolesToParticles(converted),
                    Substance: DisplayName(element),
                    ToParticles: false,
                    Grams: grams,
                    Citations: [CitationFor("atomic_mass", element)]);
            }

            return new ExecutionResult.MoleConversion(
                Moles: moles,
                Particles: ChemistryMath.MolesToParticles(moles),
                Substance: element is null ? null : DisplayName(element),
                ToParticles: true);
        }

        // ---- Isotopes and safety -------------------------------------------------------------

        /// <summary>Isotopes, longest-lived first, with stable ones ahead of everything that decays.</summary>
        private ExecutionResult Isotopes(QueryPlan plan)
        {
            if (ElementOf(plan) is not { } element) return new ExecutionResult.Empty([]);

            var all = element.Isotopes;
            if (all.Count == 0)
            {
                return new ExecutionResult.NoData(
                    "isotopes", element, _store.Elements.Count(e => e.Isotopes.Count > 0));
            }

            var ordered = all
                .OrderByDescending(i => i.Stable)
                .ThenByDescending(i => i.HalfLifeSeconds ?? 0.0)
                .ToList();

            return new ExecutionResult.Isotopes(
                Element: element,
                Shown: ordered.Take(plan.Limit).ToList(),
                Total: all.Count,
                StableCount: all.Count(i => i.Stable),
                Citations: [CitationFor("common_neutrons", element)]);
        }

        private ExecutionResult Safety(QueryPlan plan)
        {
            if (ElementOf(plan) is not { } element) return new ExecutionResult.Empty([]);

            if (element.Nfpa is not { } nfpa)
            {
                // Radioactivity is still a real hazard answer even with no NFPA diamond recorded.
                if (element.Radioactive)
                {
                    return new ExecutionResult.Safety(
                        element, new Nfpa(null, null, null, null),
                        Radioactive: true, Citations: [CitationFor("radioactive", element)]);
                }
                return new ExecutionResult.NoData(
                    "nfpa_health", element, _store.CoverageOf("nfpa_health"));
            }

            return new ExecutionResult.Safety(
                element, nfpa, element.Radioactive, [CitationFor("nfpa_health", element)]);
        }

        /// <summary>
        /// The spectrum an element emits.
        ///
        /// <para>Nothing is read from the element's fields, because there is nothing to read: the
        /// app ships a rendered image per element and no spectral data. The citation points at the
        /// Emission Spectrum table, which is where the same image lives in the rest of the app.</para>
        /// </summary>
        private ExecutionResult EmissionSpectrum(QueryPlan plan)
        {
            if (ElementOf(plan) is not { } element) return new ExecutionResult.Empty([]);

            var raw = Try.OrNull(() => _strings.Get(S.emission_spectrum_colon));
            var label = raw?.Replace(":", string.Empty, StringComparison.Ordinal).Trim();
            if (string.IsNullOrWhiteSpace(label) || label.StartsWith("str:", StringComparison.Ordinal))
                label = "Emission spectrum";

            return new ExecutionResult.EmissionSpectrum(
                Element: element,
                Citations:
                [
                    new Citation(
                        Label: label,
                        Source: DisplayName(element),
                        DeepLink: DeepLinkTarget.Emission,
                        Args: new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = element.Symbol },
                        FromTable: true),
                ]);
        }

        // ---- Comparison -----------------------------------------------------------------------

        private ExecutionResult? Comparison(QueryPlan plan)
        {
            var elements = plan.ElementKeys.Select(k => _store.Element(k)).OfType<ElementRecord>().ToList();
            if (elements.Count < 2) return null;

            var values = new Dictionary<string, IReadOnlyList<ValuedElement>>(StringComparer.Ordinal);
            foreach (var fieldId in plan.FieldIds)
            {
                values[fieldId] = elements.Select(element =>
                {
                    var quantity = _store.QuantityIn(element, fieldId, plan.TargetUnit);
                    return new ValuedElement(
                        element, quantity,
                        Render(element.Value(fieldId), quantity, plan.TargetUnit, element, fieldId));
                }).ToList();
            }

            return new ExecutionResult.Comparison(
                Elements: elements,
                FieldIds: plan.FieldIds,
                Values: values,
                Citations: elements
                    .Select(e => CitationFor(plan.FieldIds.FirstOrDefault() ?? "name", e))
                    .ToList());
        }

        // ---- Superlative and filtered list ------------------------------------------------------

        /// <summary>
        /// One field across the elements the filters leave, in atomic-number order (roadmap 2.3).
        /// Unlike a list, nothing is ranked or cut: a trend is read left to right, and a missing value
        /// is a gap in it rather than a reason to drop the rest.
        /// </summary>
        private ExecutionResult Trend(QueryPlan plan)
        {
            if (plan.FieldIds.FirstOrDefault() is not { } fieldId) return new ExecutionResult.Empty(DescribeFilters(plan));
            // The store's own order is not the table's; a trend is read in atomic-number order.
            var pool = plan.Candidates(_store).OrderBy(e => e.AtomicNumber).ToList();
            var points = pool
                .Select(element => _store.QuantityIn(element, fieldId, plan.TargetUnit) is { } quantity
                    ? new ValuedElement(element, quantity, Render(element.Value(fieldId), quantity, plan.TargetUnit, element, fieldId))
                    : null)
                .OfType<ValuedElement>()
                .ToList();
            if (points.Count == 0)
            {
                return pool.Count == 0
                    ? new ExecutionResult.Empty(DescribeFilters(plan))
                    : new ExecutionResult.NoData(fieldId, null, _store.CoverageOf(fieldId));
            }
            return new ExecutionResult.Trend(
                fieldId, points, pool.Count - points.Count, plan.Filters.Count == 0,
                new[] { points[0], points[^1] }.Distinct().Select(p => CitationFor(fieldId, p.Element)).ToList());
        }

        /// <summary>
        /// A reviewed explanation, with the data it explains computed now from the store (roadmap 2.4):
        /// the trend it describes, or the element whose ionization series shows it. The text says why;
        /// the card shows that it is so.
        /// </summary>
        private ExecutionResult? Explanation(QueryPlan plan)
        {
            if (plan.Literal is not { } key || _explanations.ByKey(key) is not { } explanation) return null;
            var trend = explanation.Card is ExplanationCard.Trend card
                ? Trend(new QueryPlan(Intent.Trend, FieldIds: [card.FieldId], Filters: card.Filter is null ? [] : [card.Filter]))
                    as ExecutionResult.Trend
                : null;
            var ionization = explanation.Card is ExplanationCard.Ionization i ? _store.Element(i.ElementKey) : null;
            return new ExecutionResult.Explanation(
                explanation.Key, explanation.Text, explanation.Language, trend, ionization,
                trend?.Citations ?? []);
        }

        private ExecutionResult ElementList(QueryPlan plan)
        {
            var candidates = plan.Candidates(_store);
            var fieldId = plan.SortField;

            if (fieldId is null)
            {
                // A subset question with no property: just list what matched.
                return new ExecutionResult.ElementList(
                    Results: candidates.Select(e => new ValuedElement(e, null, DisplayName(e))).ToList(),
                    FieldId: null,
                    Matched: candidates.Count,
                    Missing: 0,
                    Descending: plan.SortDescending,
                    Citations: []);
            }

            // How many elements were excluded purely because they have no value for this field. A
            // value comparison already drops them, so counting against the post-filter set would
            // always report zero and the answer could never disclose the gap.
            var subsetOnly = _store.Elements.Where(element =>
                plan.Filters
                    .Where(f => f is not Filter.FieldCompare fc ||
                                !string.Equals(fc.FieldId, fieldId, StringComparison.Ordinal))
                    .All(f => f.Matches(element, _store))).ToList();

            var missing = subsetOnly.Count(e => _store.QuantityIn(e, fieldId, plan.TargetUnit) is null);

            var valued = candidates
                .Select(element =>
                {
                    var quantity = _store.QuantityIn(element, fieldId, plan.TargetUnit);
                    return quantity is null
                        ? null
                        : new ValuedElement(element, quantity,
                            Render(element.Value(fieldId), quantity, plan.TargetUnit, element, fieldId));
                })
                .OfType<ValuedElement>()
                .ToList();

            if (valued.Count == 0)
            {
                return subsetOnly.Count == 0
                    ? new ExecutionResult.Empty(DescribeFilters(plan))
                    : new ExecutionResult.NoData(fieldId, null, _store.CoverageOf(fieldId));
            }

            var sorted = plan.SortDescending
                ? valued.OrderByDescending(v => v.Quantity!.Mid).ToList()
                : valued.OrderBy(v => v.Quantity!.Mid).ToList();

            // "the third densest element" asks for one element, three down the ranking.
            var offset = Math.Max((plan.FieldOrdinal ?? 1) - 1, 0);
            var shown = sorted.Skip(offset).Take(plan.Limit).ToList();
            if (shown.Count == 0) return new ExecutionResult.Empty(DescribeFilters(plan));

            return new ExecutionResult.ElementList(
                Results: shown,
                FieldId: fieldId,
                Matched: candidates.Count,
                Missing: missing,
                Descending: plan.SortDescending,
                Citations: shown.Select(v => CitationFor(fieldId, v.Element)).ToList(),
                RankOffset: offset,
                // The next element down gives a superlative something to be measured against.
                RunnerUp: plan.Limit == 1 && offset + 1 < sorted.Count ? sorted[offset + 1] : null);
        }

        // ---- Aggregate ---------------------------------------------------------------------------

        private ExecutionResult Aggregate(QueryPlan plan)
        {
            var explicitElements = plan.ElementKeys.Select(k => _store.Element(k)).OfType<ElementRecord>().ToList();
            var candidates = explicitElements.Count > 1 ? explicitElements : plan.Candidates(_store);

            if (plan.Aggregation == Aggregation.Count)
            {
                return new ExecutionResult.Aggregate(
                    Aggregation: Aggregation.Count,
                    FieldId: plan.PrimaryField ?? string.Empty,
                    Value: candidates.Count,
                    Unit: null,
                    Contributors: candidates.Select(e => new ValuedElement(e, null, DisplayName(e))).ToList(),
                    Missing: 0,
                    Citations: []);
            }

            if (plan.PrimaryField is not { } fieldId)
                return new ExecutionResult.Empty(DescribeFilters(plan));

            var valued = candidates
                .Select(element =>
                {
                    var quantity = _store.QuantityIn(element, fieldId, plan.TargetUnit);
                    return quantity is null
                        ? null
                        : new ValuedElement(element, quantity,
                            Render(element.Value(fieldId), quantity, plan.TargetUnit, element, fieldId));
                })
                .OfType<ValuedElement>()
                .ToList();

            if (valued.Count == 0)
                return new ExecutionResult.NoData(fieldId, null, _store.CoverageOf(fieldId));

            var numbers = valued.Select(v => v.Quantity!.Mid).OrderBy(n => n).ToList();
            var value = plan.Aggregation switch
            {
                Aggregation.Avg => numbers.Average(),
                Aggregation.Sum => numbers.Sum(),
                Aggregation.Min => numbers[0],
                Aggregation.Max => numbers[^1],
                Aggregation.Median => numbers.Count % 2 == 1
                    ? numbers[numbers.Count / 2]
                    : (numbers[numbers.Count / 2 - 1] + numbers[numbers.Count / 2]) / 2.0,
                Aggregation.Range => numbers[^1] - numbers[0],
                _ => numbers.Average(),
            };

            return new ExecutionResult.Aggregate(
                Aggregation: plan.Aggregation,
                FieldId: fieldId,
                Value: value,
                Unit: valued[0].Quantity?.Unit,
                Contributors: valued,
                // Disclosed in the answer, so a partial mean is never presented as a complete one.
                Missing: candidates.Count - valued.Count,
                Citations: [CitationFor(fieldId, valued[0].Element)]);
        }

        // ---- Dataset ------------------------------------------------------------------------------

        private ExecutionResult? Dataset(QueryPlan plan)
        {
            var reference = plan.Entities.OfType<EntityRef.DatasetRow>().FirstOrDefault();
            if (reference is null) return null;

            var row = _datasets.Row(reference.Dataset, reference.Id);
            if (row is null) return null;

            return new ExecutionResult.Dataset(
                Row: row,
                Citations:
                [
                    new Citation(
                        row.Title, reference.Dataset, row.DeepLink,
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = row.Id },
                        FromTable: true),
                ]);
        }

        // ---- Rendering helpers ---------------------------------------------------------------------

        /// <summary>The element's name in the active language, falling back to its English key.</summary>
        public string DisplayName(ElementRecord element)
        {
            var name = _localized?.Name(element.Key);
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return element.Key.Length == 0
                ? element.Key
                : char.ToUpperInvariant(element.Key[0]) + element.Key[1..];
        }

        /// <summary>
        /// How a value is shown. A value read straight from the data prints exactly as authored;
        /// only a converted value is reformatted, so nothing is lost to rounding on a plain lookup.
        ///
        /// <para>"Exactly as authored" is not enough on its own for the abundance reservoirs, which
        /// the JSON stores as bare numbers - "gold's abundance in sea water is 0.011" says nothing
        /// without its µg/l. Those fields opt in via <c>SurfaceUnit</c> and get their unit appended
        /// here, which is the single funnel every property, category, comparison and superlative
        /// answer runs through.</para>
        /// </summary>
        private string Render(
            FieldValue value,
            Quantity? quantity,
            string? targetUnit,
            ElementRecord? element = null,
            string? fieldId = null)
        {
            if (targetUnit is not null && quantity is not null)
                return UnitConverter.FormatValue(quantity.Value) +
                       (quantity.Unit is { } unit ? $" {unit}" : string.Empty);

            if (quantity is not null) return WithUnit(fieldId, quantity);

            // A prose field is served from the active language's table when it has one. The numeric
            // index is parsed from English, which is right for measured values and wrong for these.
            if (element is not null && fieldId is not null && Translated(element, fieldId) is { } text)
                return text;

            return value switch
            {
                FieldValue.Text t => t.Raw,
                FieldValue.Enum e => e.Localized,
                // "Trace" means present but unquantified. It used to render as ai_abundance_relative
                // - "(relative to H=10¹²)" - which is a unit note under a convention this app does
                // not even use, and read as "iron's abundance in the human body is (relative to
                // H=10¹²)".
                FieldValue.TraceValue => _strings.Get(S.ai_value_trace),
                FieldValue.Struct s => string.Join(", ", s.Parts.Select(p => $"{p.Key}: {p.Value.Display}")),
                _ => string.Empty,
            };
        }

        /// <summary>The authored value, with its unit added for the fields that do not store one.</summary>
        private string WithUnit(string? fieldId, Quantity quantity)
        {
            if (fieldId is null || !FieldRegistry.ById.TryGetValue(fieldId, out var spec))
                return quantity.Display;
            return spec.DisplayWithUnit(quantity, key => _strings.Get(key));
        }

        /// <summary>
        /// The active language's value for a field marked localized, or null.
        ///
        /// <para>Null covers three cases that all mean "use the English table": the field is not one
        /// of the seven that differ between languages, the active language is English, or this
        /// language's file left the value untranslated.</para>
        /// </summary>
        private string? Translated(ElementRecord element, string fieldId)
        {
            if (!FieldRegistry.ById.TryGetValue(fieldId, out var spec) || !spec.Localized) return null;
            return _localized?.Text(element.Key, fieldId);
        }

        private static Quantity Convert(Quantity quantity, string targetUnit) =>
            UnitConverter.Convert(quantity, targetUnit) ?? quantity;

        private Citation CitationFor(string fieldId, ElementRecord element)
        {
            FieldRegistry.ById.TryGetValue(fieldId, out var spec);
            var label = spec is null ? null : Try.OrNull(() => _strings.Get(spec.LabelKey));

            return new Citation(
                Label: (label ?? fieldId).Replace(":", string.Empty, StringComparison.Ordinal).Trim(),
                Source: DisplayName(element),
                DeepLink: spec?.DeepLink ?? DeepLinkTarget.ElementInfo,
                Args: new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = element.Key });
        }

        private static IReadOnlyList<string> DescribeFilters(QueryPlan plan) =>
            plan.Filters.Select(f => f.GetType().Name).ToList();

        private ElementRecord? ElementOf(QueryPlan plan) =>
            plan.ElementKeys.Count == 0 ? null : _store.Element(plan.ElementKeys[0]);

        /// <summary>A symbol's atomic mass, for the formula parser.</summary>
        private double? AtomicMass(string symbol) =>
            _store.BySymbol(symbol)?.Quantity("atomic_mass")?.Value;

        private static bool TryNumber(string raw, out double value) =>
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static readonly Regex Grams =
            JavaRegex.Compile(@"([\d.]+)\s*(?:g|gram|grams|gm|gramm|gramme|grammes)\b");

        /// <summary>Fields where an ordering exists but means nothing to a reader.</summary>
        private static readonly IReadOnlySet<string> UnrankableFields =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "atomic_number", "protons", "electrons", "period", "group_number",
                "space_group_number", "year_discovered", "valence_electrons",
                "nfpa_health", "nfpa_flammability", "nfpa_instability", "common_neutrons",
            };

        /// <summary>Below this many recorded values a rank is more misleading than useful.</summary>
        private const int MinForRank = 20;
    }
}
