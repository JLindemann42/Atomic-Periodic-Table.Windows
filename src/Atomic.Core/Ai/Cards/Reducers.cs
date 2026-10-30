using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Ai.Data;
using Atomic.Core.Localization;
using Atomic.Core.Models;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Cards
{
    /// <summary>
    /// Where an element's emission-spectrum image lives.
    ///
    /// <para>The app holds no spectral data - no wavelengths, no intensities - only a pre-rendered
    /// image per element on the author's server. That matters for what the agent is allowed to say:
    /// it can show the spectrum, but it cannot name a line, because it does not have one.</para>
    /// </summary>
    public static class EmissionSpectrumUrl
    {
        private const string Base = "https://www.jlindemann.se/atomic/emission_lines/";
        private const string Extension = ".gif";

        /// <param name="symbol">The element symbol as authored, e.g. <c>Fe</c>.</param>
        public static string Of(string symbol) => Base + symbol.Trim() + Extension;
    }

    /// <summary>The four number/description pairs of an NFPA 704 diamond, already localized.</summary>
    public sealed record NfpaLabels(
        string Fire, string FireText,
        string Health, string HealthText,
        string Reactivity, string ReactivityText,
        string Special, string SpecialText);

    /// <summary>
    /// Turns NFPA ratings into localized labels.
    ///
    /// <para>This is the single owner of that mapping. On Android it existed twice - once in the
    /// composer for the text answer, and once inside the element screen as <b>hardcoded English
    /// literals</b> in an app that ships twelve languages.</para>
    /// </summary>
    public static class NfpaLabeller
    {
        public static NfpaLabels Labels(Nfpa nfpa, IStringProvider strings) => new(
            Fire: Rating(nfpa.Flammability),
            FireText: strings.Get(FlammabilityDescription(nfpa.Flammability)),
            Health: Rating(nfpa.Health),
            HealthText: strings.Get(HealthDescription(nfpa.Health)),
            Reactivity: Rating(nfpa.Instability),
            ReactivityText: strings.Get(InstabilityDescription(nfpa.Instability)),
            Special: SpecialCode(nfpa.Special),
            SpecialText: strings.Get(SpecialDescription(nfpa.Special)));

        /// <summary>True when at least one axis has a value, so a diamond would say something.</summary>
        public static bool HasAnyRating(Nfpa? nfpa) =>
            nfpa is not null &&
            (nfpa.Health is not null || nfpa.Flammability is not null ||
             nfpa.Instability is not null || !string.IsNullOrWhiteSpace(nfpa.Special));

        public static string HealthDescription(int? rating) => Clamp(rating) switch
        {
            0 => S.ai_nfpa_health_0,
            1 => S.ai_nfpa_health_1,
            2 => S.ai_nfpa_health_2,
            3 => S.ai_nfpa_health_3,
            4 => S.ai_nfpa_health_4,
            _ => S.ai_nfpa_unrated,
        };

        public static string FlammabilityDescription(int? rating) => Clamp(rating) switch
        {
            0 => S.ai_nfpa_flammable_0,
            1 => S.ai_nfpa_flammable_1,
            2 => S.ai_nfpa_flammable_2,
            3 => S.ai_nfpa_flammable_3,
            4 => S.ai_nfpa_flammable_4,
            _ => S.ai_nfpa_unrated,
        };

        public static string InstabilityDescription(int? rating) => Clamp(rating) switch
        {
            0 => S.ai_nfpa_instability_0,
            1 => S.ai_nfpa_instability_1,
            2 => S.ai_nfpa_instability_2,
            3 => S.ai_nfpa_instability_3,
            4 => S.ai_nfpa_instability_4,
            _ => S.ai_nfpa_unrated,
        };

        /// <summary>The white quadrant's code: "SA", "W", "OX", or a dash when there is none.</summary>
        public static string SpecialCode(string? special)
        {
            var value = special?.Trim() ?? string.Empty;

            if (value.Length == 0 || value == "---") return "–";
            if (value.Contains("asphyxiant", StringComparison.OrdinalIgnoreCase)) return "SA";
            if (string.Equals(value, "W", StringComparison.OrdinalIgnoreCase)) return "W";
            if (string.Equals(value, "OX", StringComparison.OrdinalIgnoreCase)) return "OX";
            return value[..Math.Min(3, value.Length)].ToUpperInvariant();
        }

        public static string SpecialDescription(string? special) => SpecialCode(special) switch
        {
            "SA" => S.ai_nfpa_special_sa,
            "W" => S.ai_nfpa_special_w,
            "OX" => S.ai_nfpa_special_ox,
            _ => S.ai_nfpa_special_none,
        };

        private static int? Clamp(int? rating) => rating is { } r ? Math.Clamp(r, 0, 4) : null;

        /// <summary>An absent rating shows as a dash rather than a misleading zero, which means "no hazard".</summary>
        private static string Rating(int? value) =>
            value is { } v ? Math.Clamp(v, 0, 4).ToString(CultureInfo.InvariantCulture) : "–";
    }

    /// <summary>The range one class of material occupies on the Poisson axis.</summary>
    public sealed record PoissonSpan(string Label, double Low, double High, string Kind);

    /// <summary>
    /// An element's Poisson ratio placed against the materials the app already tabulates.
    ///
    /// <para>A bare "0.29" tells almost nobody anything. Against rock at 0.1-0.4 and the
    /// incompressible limit at 0.5 it becomes a statement about how the material behaves, which is
    /// why this card is a band rather than a number.</para>
    /// </summary>
    /// <param name="Rank">Where the element sits among those with a recorded ratio.</param>
    public sealed record PoissonBand(
        string Symbol,
        double ElementLow,
        double ElementHigh,
        string ElementDisplay,
        IReadOnlyList<PoissonSpan> References,
        double AxisMin,
        double AxisMax,
        int? Rank,
        int RankedOutOf);

    public static class PoissonBandReducer
    {
        /// <summary>The thermodynamic upper bound for an isotropic material. Worth drawing and annotating.</summary>
        public const double IncompressibleLimit = 0.5;

        public static PoissonBand? Of(ElementRecord element, KnowledgeStore store)
        {
            if (element.Quantity("poisson_ratio") is not { } quantity) return null;

            var references = PoissonModel.All
                .GroupBy(p => p.Type, StringComparer.Ordinal)
                .Select(g => new PoissonSpan(
                    Label: g.Key.Length == 0 ? g.Key : char.ToUpperInvariant(g.Key[0]) + g.Key[1..],
                    Low: g.Min(p => p.Start),
                    High: g.Max(p => p.End),
                    Kind: g.Key))
                .OrderBy(s => s.Low)
                .ToList();

            var ranked = store.Elements
                .Select(e => e.Quantity("poisson_ratio")?.Mid)
                .OfType<double>()
                .OrderByDescending(v => v)
                .ToList();

            var index = ranked.FindIndex(v => v <= quantity.Mid);
            int? rank = index >= 0 ? index + 1 : null;

            var low = quantity.Value;
            var high = quantity.High ?? quantity.Value;

            // The axis has to hold every reference span as well as the element, and always the
            // incompressible limit - the annotation at 0.5 is the pedagogical payoff.
            var axisMin = Math.Min(references.Count == 0 ? 0.0 : references.Min(r => r.Low), low) - 0.05;
            var axisMax = Math.Max(
                Math.Max(references.Count == 0 ? 0.0 : references.Max(r => r.High), high),
                IncompressibleLimit) + 0.05;

            return new PoissonBand(
                Symbol: element.Symbol,
                ElementLow: low,
                ElementHigh: high,
                ElementDisplay: quantity.Display,
                References: references,
                AxisMin: axisMin,
                AxisMax: axisMax,
                Rank: rank,
                RankedOutOf: ranked.Count);
        }
    }

    /// <summary>One ionization step: which electron is being removed, and what it costs.</summary>
    public sealed record IonizationStep(int Order, double ElectronVolts, string Display);

    /// <summary>
    /// An element's successive ionization energies.
    /// </summary>
    /// <param name="ShellClosureAfter">
    /// Orders after which the next step jumps by at least <see cref="IonizationSeriesReducer.JumpFactor"/>×.
    /// These are where a closed shell is being broken into, and they are the whole teaching point of
    /// the series - sodium's second ionization costs nearly ten times its first because the first
    /// emptied its outer shell.
    /// </param>
    /// <param name="SuggestedScale">Chosen from the data, not from a hardcoded preference.</param>
    public sealed record IonizationSeries(
        string Symbol,
        IReadOnlyList<IonizationStep> Steps,
        IReadOnlySet<int> ShellClosureAfter,
        ChartScale SuggestedScale);

    public static class IonizationSeriesReducer
    {
        /// <summary>How much larger the next step must be to count as breaking into a closed shell.</summary>
        public const double JumpFactor = 2.0;

        /// <summary>Fewer steps than this and a chart says nothing a sentence does not.</summary>
        public const int MinSteps = 3;

        /// <summary>
        /// Read an element's ionization bank into a plottable series.
        ///
        /// <para><c>ionization_energy</c> is a banked field: thirty JSON keys addressed through one
        /// spec. Iron has 26 recorded steps, hydrogen has one.</para>
        /// </summary>
        public static IonizationSeries? Of(ElementRecord element)
        {
            if (!FieldRegistry.ById.TryGetValue("ionization_energy", out var spec)) return null;
            if (spec.OrdinalRange is not { } range) return null;

            var steps = new List<IonizationStep>();
            for (var order = range.Min; order <= range.Max; order++)
            {
                FieldValue? value = null;
                if (element.Values.TryGetValue($"ionization_energy#{order}", out var slotted))
                    value = slotted;
                else if (order == range.Min && element.Values.TryGetValue("ionization_energy", out var first))
                    value = first;

                if (value?.AsQuantity() is not { } quantity) continue;
                if (quantity.Mid <= 0) continue;
                steps.Add(new IonizationStep(order, quantity.Mid, quantity.Display));
            }

            if (steps.Count < MinSteps) return null;

            // A jump is recorded against the step *before* it, because that is the shell that just
            // emptied - "after removing 1 electron, the next one costs 10× more".
            var closures = new HashSet<int>();
            for (var i = 0; i < steps.Count - 1; i++)
            {
                if (steps[i + 1].ElectronVolts >= steps[i].ElectronVolts * JumpFactor)
                    closures.Add(steps[i].Order);
            }

            return new IonizationSeries(
                Symbol: element.Symbol,
                Steps: steps,
                ShellClosureAfter: closures,
                // Log compresses exactly the jumps the chart exists to show, so it is only used
                // when a linear axis would flatten the early steps onto the baseline.
                SuggestedScale: DecadeLabels.SuggestedScale(steps.Select(s => s.ElectronVolts).ToList()));
        }
    }

    /// <summary>The authored decay modes collapsed to a small, colourable set.</summary>
    public enum DecayGroup
    {
        Alpha, BetaMinus, BetaPlusEc, Isomeric, Fission, NucleonEmission, Other, Stable,
    }

    /// <summary>
    /// One isotope positioned for plotting.
    /// </summary>
    /// <param name="LogSeconds">
    /// log₁₀ of the half-life in seconds, or null when the isotope is stable or its half-life could
    /// not be parsed. Null is meaningful and must survive to the view: a stable isotope has <i>no</i>
    /// finite half-life, and plotting it at the maximum would assert a number the data does not
    /// contain.
    /// </param>
    public sealed record IsotopePoint(
        string Name,
        int MassNumber,
        double? LogSeconds,
        bool Stable,
        DecayGroup DecayGroup,
        string HalfLifeDisplay);

    /// <summary>
    /// An element's isotopes, ordered by mass number so the valley of stability is visible as a peak.
    /// </summary>
    /// <param name="GroupsPresent">So a legend lists only what is actually drawn.</param>
    public sealed record IsotopeSeries(
        string Symbol,
        IReadOnlyList<IsotopePoint> Points,
        IReadOnlySet<int> StableMassNumbers,
        double MinLogSeconds,
        double MaxLogSeconds,
        IReadOnlySet<DecayGroup> GroupsPresent);

    public static class IsotopeSeriesReducer
    {
        /// <summary>Fewer isotopes than this and a chart is noise.</summary>
        public const int MinIsotopes = 3;

        /// <summary>
        /// Build the series from an element's full isotope list.
        ///
        /// <para>Reads the element's isotopes rather than the truncated eight the text answer
        /// shows. The chart's value is precisely that it can show all of them - tin has ten stable
        /// isotopes and the text list never gets near them.</para>
        ///
        /// <para><c>decay_type_N</c> is present in the shipped JSON and read by nothing outside the
        /// agent, so this surfaces data the app has always carried and never displayed.</para>
        /// </summary>
        public static IsotopeSeries? Of(ElementRecord element)
        {
            var points = element.Isotopes
                .Select(isotope => isotope.MassNumber is not { } mass
                    ? null
                    : new IsotopePoint(
                        Name: isotope.Name,
                        MassNumber: mass,
                        LogSeconds: isotope.HalfLifeSeconds is { } s && s > 0 ? Math.Log10(s) : null,
                        Stable: isotope.Stable,
                        DecayGroup: isotope.Stable ? DecayGroup.Stable : Group(isotope.DecayType),
                        HalfLifeDisplay: isotope.HalfLifeDisplay))
                .OfType<IsotopePoint>()
                .OrderBy(p => p.MassNumber)
                .ToList();

            if (points.Count < MinIsotopes) return null;

            var logs = points.Select(p => p.LogSeconds).OfType<double>().ToList();

            return new IsotopeSeries(
                Symbol: element.Symbol,
                Points: points,
                StableMassNumbers: points.Where(p => p.Stable).Select(p => p.MassNumber).ToHashSet(),
                MinLogSeconds: logs.Count == 0 ? 0.0 : logs.Min(),
                MaxLogSeconds: logs.Count == 0 ? 0.0 : logs.Max(),
                GroupsPresent: points.Select(p => p.DecayGroup).ToHashSet());
        }

        /// <summary>
        /// Map an authored <c>decay_type</c> spelling onto a colourable group.
        ///
        /// <para>The data uses a mix of notations - α, A, B-, B+, EC, 2p, SF, IT. Anything
        /// unrecognised lands in <see cref="DecayGroup.Other"/> rather than being dropped, and a
        /// test enumerates every distinct spelling in the real assets so a new one shows up as a
        /// change in that set rather than as a silently grey marker.</para>
        /// </summary>
        public static DecayGroup Group(string? decayType)
        {
            var value = decayType?.Trim().ToLowerInvariant() ?? string.Empty;
            if (value.Length == 0 || value == "---" || value == "n/a") return DecayGroup.Other;

            if (value.StartsWith('α') || value == "a" || value.StartsWith("alpha", StringComparison.Ordinal))
                return DecayGroup.Alpha;

            if (value.StartsWith("β−", StringComparison.Ordinal) ||
                value.StartsWith("β-", StringComparison.Ordinal) ||
                value.StartsWith("b-", StringComparison.Ordinal) ||
                value.StartsWith("beta-", StringComparison.Ordinal))
                return DecayGroup.BetaMinus;

            if (value.StartsWith("β+", StringComparison.Ordinal) ||
                value.StartsWith("b+", StringComparison.Ordinal) ||
                value.StartsWith("beta+", StringComparison.Ordinal) ||
                value.StartsWith("ec", StringComparison.Ordinal) ||
                value.Contains("electron capture", StringComparison.Ordinal))
                return DecayGroup.BetaPlusEc;

            if (value.StartsWith("it", StringComparison.Ordinal) ||
                value.Contains("isomeric", StringComparison.Ordinal))
                return DecayGroup.Isomeric;

            if (value.StartsWith("sf", StringComparison.Ordinal) ||
                value.Contains("fission", StringComparison.Ordinal))
                return DecayGroup.Fission;

            // Direct nucleon emission: "p", "2p", "n", "2n".
            // MatchEntire, not IsMatch: Kotlin's `matches` tests the whole string, and an
            // unanchored search would read the "p" inside "sp" as proton emission.
            if (NucleonEmission.MatchEntire(value) is not null) return DecayGroup.NucleonEmission;

            return DecayGroup.Other;
        }

        private static readonly Regex NucleonEmission = JavaRegex.Compile(@"\d*[pn]");
    }

    /// <summary>
    /// One reservoir's share of an element.
    /// </summary>
    /// <param name="LogMgPerKg">
    /// log₁₀ of the value, or null when the source recorded only a trace. Named for the unit most
    /// reservoirs use; see <see cref="AbundanceGroup"/> for why it is not the only one.
    /// </param>
    /// <param name="Trace">
    /// True when the source said the element is present but too small to quantify. Modelled
    /// explicitly rather than folded into null, because "present but unquantified" is real
    /// information and a reducer that only handled numbers would silently drop those bars.
    /// </param>
    /// <param name="Display">
    /// The value as the source authored it. The unit is <i>not</i> repeated here: it heads the bar's
    /// group, and a narrow card has no width to say it twice.
    /// </param>
    public sealed record AbundanceBar(
        string FieldId,
        string Label,
        double? LogMgPerKg,
        bool Trace,
        string Display,
        string? UnitKey = null);

    /// <summary>
    /// The bars that share a unit, and so can be compared with each other.
    ///
    /// <para>The nine reservoirs are not measured in one unit: the crust, soils, rocks and
    /// meteorites are mg/kg, sea water is µg/l, the sun and solar system are atom counts against
    /// silicon, and the human-body figures are authored as either mg/kg or a percentage. Drawing
    /// all of them as one run of bars - which this card used to do - claims a comparison the data
    /// does not support, so the bars are grouped and each group states its unit.</para>
    /// </summary>
    public sealed record AbundanceGroup(
        string? UnitKey,
        string UnitLabel,
        IReadOnlyList<AbundanceBar> Bars);

    /// <summary>
    /// An element's abundance across the nine reservoirs the app records.
    /// </summary>
    /// <param name="Bars">Every bar, flattened, in <see cref="AbundanceReducer.Order"/>.</param>
    /// <param name="Groups">The same bars partitioned by unit, which is what the card draws.</param>
    /// <param name="FloorLog">
    /// Where the bars start. A log axis has no zero, so bars must be drawn from a real, labelled
    /// floor - otherwise their lengths mean nothing at all.
    /// </param>
    public sealed record AbundanceProfile(
        string Symbol,
        IReadOnlyList<AbundanceBar> Bars,
        IReadOnlyList<AbundanceGroup> Groups,
        double MinLog,
        double MaxLog,
        double FloorLog);

    public static class AbundanceReducer
    {
        /// <summary>Fewer bars than this and the chart adds nothing to the sentence.</summary>
        public const int MinBars = 3;

        /// <summary>
        /// The nine reservoirs in a fixed order, roughly Earth outwards then biology.
        ///
        /// <para>Fixed rather than sorted by value so the same reservoir sits in the same place for
        /// every element, which is what lets two cards be compared by eye. Grouping by unit
        /// reorders within this list but never sorts by magnitude, so that property survives.</para>
        /// </summary>
        public static readonly IReadOnlyList<string> Order =
        [
            "abundance_earth_crust",
            "abundance_crustal_rocks",
            "abundance_earth_soils",
            "abundance_urban_soils",
            "abundance_sea_water",
            "abundance_meteorites",
            "abundance_sun",
            "abundance_solar_system",
            "abundance_human_body",
        ];

        public static AbundanceProfile? Of(ElementRecord element, IStringProvider strings)
        {
            var ordered = new List<AbundanceBar>();

            foreach (var fieldId in Order)
            {
                if (!FieldRegistry.ById.TryGetValue(fieldId, out var spec)) continue;

                switch (element.Value(fieldId))
                {
                    case FieldValue.Num num:
                    {
                        // Log10(0) is negative infinity and would blow the axis out to nothing.
                        var amount = num.Quantity.Mid;
                        if (amount <= 0) break;
                        ordered.Add(new AbundanceBar(
                            FieldId: fieldId,
                            Label: Label(spec, strings),
                            LogMgPerKg: Math.Log10(amount),
                            Trace: false,
                            Display: num.Quantity.Display,
                            UnitKey: UnitConverter.Canonical(num.Quantity.Unit)));
                        break;
                    }
                    case FieldValue.TraceValue:
                        ordered.Add(new AbundanceBar(
                            FieldId: fieldId,
                            Label: Label(spec, strings),
                            LogMgPerKg: null,
                            Trace: true,
                            // Was the value's ToString on Android, which drew the class name and a
                            // hash the moment anything rendered a trace bar's text.
                            Display: strings.Get(S.ai_value_trace),
                            UnitKey: UnitConverter.Canonical(spec.CanonicalUnit)));
                        break;
                }
            }

            if (ordered.Count < MinBars) return null;

            var logs = ordered.Select(b => b.LogMgPerKg).OfType<double>().ToList();
            if (logs.Count == 0) return null;

            // Grouped by unit, groups in order of first appearance so mg/kg - the unit most
            // reservoirs share - leads, and the outliers follow in the usual reservoir order.
            // GroupBy preserves encounter order for both the keys and the values within a key, so
            // reservoirs stay in Order inside each group.
            var groups = ordered
                .GroupBy(b => b.UnitKey, StringComparer.Ordinal)
                .Select(g => new AbundanceGroup(g.Key, UnitLabel(g.First(), g.Key, strings), g.ToList()))
                .ToList();

            var minLog = logs.Min();

            return new AbundanceProfile(
                Symbol: element.Symbol,
                // Flat and strictly in Order. The card draws Groups; Bars is the canonical sequence
                // for anything that wants the reservoirs without the unit partition.
                Bars: ordered,
                Groups: groups,
                MinLog: minLog,
                MaxLog: logs.Max(),
                FloorLog: Math.Floor(minLog) - 0.5);
        }

        /// <summary>
        /// A group's unit as a reader should see it.
        ///
        /// <para>Prefers the field's declared label - "atoms per 10⁶ atoms of silicon" reads far
        /// better than the <c>Si=10^6</c> token that groups it - but only when the group's unit is
        /// the one that field declares. A human-body figure authored as "65% (by mass)" groups under
        /// <c>%</c>, and labelling that group with the field's mg/kg would be worse than showing no
        /// unit at all.</para>
        /// </summary>
        private static string UnitLabel(AbundanceBar first, string? unitKey, IStringProvider strings)
        {
            if (FieldRegistry.ById.TryGetValue(first.FieldId, out var spec) &&
                string.Equals(UnitConverter.Canonical(spec.CanonicalUnit), unitKey, StringComparison.Ordinal) &&
                spec.UnitLabelKey is { } unitLabelKey)
            {
                var declared = Try.OrNull(() => strings.Get(unitLabelKey))?.Trim();
                if (!string.IsNullOrEmpty(declared) &&
                    !declared.StartsWith("str:", StringComparison.Ordinal))
                    return declared;
            }
            return unitKey ?? string.Empty;
        }

        private static string Label(FieldSpec spec, IStringProvider strings)
        {
            var text = Try.OrNull(() => strings.Get(spec.SentenceLabel()));
            if (text is null || text.StartsWith("str:", StringComparison.Ordinal))
            {
                return spec.Id.StartsWith("abundance_", StringComparison.Ordinal)
                    ? spec.Id["abundance_".Length..].Replace('_', ' ')
                    : spec.Id.Replace('_', ' ');
            }
            return text.Replace(":", string.Empty, StringComparison.Ordinal).Trim();
        }
    }
}
