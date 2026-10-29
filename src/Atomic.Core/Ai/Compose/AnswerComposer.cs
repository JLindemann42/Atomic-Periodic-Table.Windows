using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Localization;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Compose
{
    /// <summary>
    /// Rendered answer text, the actions it offers, and the visual that goes with it.
    ///
    /// <para><paramref name="Card"/> is null far more often than not - most answers have no visual
    /// worth adding.</para>
    /// </summary>
    public sealed record ComposedAnswer(
        string Text,
        IReadOnlyList<ChatAction> Actions,
        ChatCard? Card = null);

    /// <summary>
    /// Turns an <see cref="ExecutionResult"/> into the markdown the chat panel can actually render.
    ///
    /// <para>The renderer supports only <c>###</c> headings and <c>**bold**</c>, so the vocabulary
    /// here is headings, literal <c>• </c> bullets and emphasis. Links are not renderable at all,
    /// which is why citations become tappable action chips instead of inline URLs.</para>
    /// </summary>
    public sealed class AnswerComposer
    {
        private readonly KnowledgeStore _store;
        private readonly LocalizedView? _localized;
        private readonly IStringProvider _strings;
        private readonly ChatCardPolicy _cardPolicy;
        private readonly Workings _workings;

        public AnswerComposer(
            KnowledgeStore store,
            LocalizedView? localized,
            IStringProvider strings,
            ChatCardPolicy? cardPolicy = null)
        {
            _store = store;
            _localized = localized;
            _strings = strings;
            _cardPolicy = cardPolicy ?? ChatCardPolicy.Default;
            _workings = new Workings(strings);
        }

        /// <summary>
        /// Render without the sources block.
        ///
        /// <para>A compound answer composes each clause separately and then emits one merged Sources
        /// section, rather than repeating the heading after every part.</para>
        /// </summary>
        public ComposedAnswer ComposeBody(ExecutionResult result, QueryPlan plan) =>
            Compose(result, plan, includeCitations: false);

        public ComposedAnswer Compose(ExecutionResult result, QueryPlan plan) =>
            Compose(result, plan, includeCitations: true);

        private ComposedAnswer Compose(ExecutionResult result, QueryPlan plan, bool includeCitations)
        {
            var body = result switch
            {
                ExecutionResult.Property r => Property(r),
                ExecutionResult.Comparison r => Comparison(r) + UncoveredAspects(plan),
                ExecutionResult.ElementList r => ElementList(r, plan),
                ExecutionResult.Aggregate r => Aggregate(r),
                ExecutionResult.Formula r => Formula(r) + Steps(plan, () => _workings.Formula(r.Result)),
                ExecutionResult.Comparative r => Comparative(r),
                ExecutionResult.Neighbour r => Neighbour(r),
                ExecutionResult.Nuclide r => Nuclide(r),
                ExecutionResult.IsotopeComparison r => IsotopeComparison(r),
                ExecutionResult.MoleConversion r => MoleConversion(r) + Steps(plan, () => _workings.Moles(r)),
                ExecutionResult.UnitConversion r => UnitConversion(r),
                ExecutionResult.BalancedEquation r =>
                    StepsInstead(plan, working => BalancedEquation(r, working), () => _workings.Balance(r)),
                ExecutionResult.SolutionCalc r =>
                    StepsInstead(plan, working => SolutionCalc(r, working), () => _workings.Solution(r)),
                ExecutionResult.Decay r => StepsInstead(plan, working => Decay(r, working), () => _workings.Decay(r)),
                ExecutionResult.Isotopes r => Isotopes(r),
                ExecutionResult.Safety r => Safety(r),
                ExecutionResult.EmissionSpectrum r => EmissionSpectrum(r),
                ExecutionResult.Dataset r => Dataset(r),
                ExecutionResult.NoData r => NoData(r),
                ExecutionResult.Locked r => Locked(r),
                ExecutionResult.Empty => _strings.Get(S.ai_filter_none),
                _ => string.Empty,
            };

            var actions = result.Citations
                .Select(c => c.ToAction(_strings))
                // A locked answer offers the way out, using the same chip row as citations rather
                // than a second affordance nobody has seen before.
                .Concat(UpgradeAction(result) is { } upgrade ? [upgrade] : Array.Empty<ChatAction>())
                .DistinctBy(a => a.Label)
                .ToList();

            return new ComposedAnswer(
                Text: includeCitations ? body + CitationBlock(result.Citations) : body,
                Actions: actions,
                // Derived from the typed result, never from the query text - so the picture can only
                // show what the prose above it has already asserted.
                Card: CardSelector.Select(result, plan, _store, _strings, _localized, _cardPolicy));
        }

        /// <summary>A calculation's steps as a numbered section, when the plan asks for them (roadmap 2.2).</summary>
        private string Steps(QueryPlan plan, Func<IReadOnlyList<string>> lines)
        {
            if (!plan.ShowSteps) return string.Empty;
            var steps = lines();
            if (steps.Count == 0) return string.Empty;
            return "\n\n### " + _strings.Get(S.ai_steps_header) + "\n" +
                   string.Join('\n', steps.Select((line, i) => $"{i + 1}. {line}"));
        }

        /// <summary>
        /// An answer whose short working the steps replace rather than repeat: <paramref name="render"/>
        /// is called with whether to include its own working.
        /// </summary>
        private string StepsInstead(QueryPlan plan, Func<bool, string> render, Func<IReadOnlyList<string>> lines)
        {
            var steps = Steps(plan, lines);
            return steps.Length == 0 ? render(true) : render(false) + steps;
        }

        /// <summary>The "Get PRO" chip shown beside a withheld answer, or null when nothing was withheld.</summary>
        private ChatAction? UpgradeAction(ExecutionResult result)
        {
            if (result is not ExecutionResult.Locked locked) return null;

            var label = _strings.Get(
                locked.Tier == Tier.ProPlus ? S.get_pro_plus_button : S.get_pro_button);
            return new ChatAction(label, DeepLinkTarget.ProPage);
        }

        /// <summary>The sources section on its own, for a caller assembling several parts.</summary>
        public string CitationsFor(IReadOnlyList<Citation> citations) => CitationBlock(citations);

        // ---- Per-result rendering ------------------------------------------------------------

        /// <summary>
        /// A property answer, with enough context to mean something.
        ///
        /// <para>A bare figure is not an answer to most people: "19.3 g/cm³" only lands if you
        /// already know the range it sits in. Adding where the value ranks, and what kind of element
        /// this is, turns a lookup into something a reader learns from.</para>
        /// </summary>
        private string Property(ExecutionResult.Property result)
        {
            var builder = new StringBuilder(_strings.Get(
                S.ai_property_is,
                DisplayName(result.Element.Key), FieldLabel(result.FieldId), result.Display));

            if (result.Quantity is { } q)
            {
                if (q.IsRange) builder.Append('\n').Append(_strings.Get(S.ai_value_is_range));
                if (q.Note is { } note) builder.Append('\n').Append(note);
            }

            if (RankSentence(result) is { } rank) builder.Append('\n').Append(rank);
            return builder.ToString();
        }

        /// <summary>"That is the highest of 105 elements…", or where it ranks. Null when not rankable.</summary>
        private string? RankSentence(ExecutionResult.Property result)
        {
            if (result.Rank is not { } rank) return null;
            if (result.RankedOutOf < 2) return null;

            var label = FieldLabel(result.FieldId).ToLowerInvariant();
            if (rank == 1) return _strings.Get(S.ai_rank_highest, result.RankedOutOf, label);
            if (rank == result.RankedOutOf) return _strings.Get(S.ai_rank_lowest, result.RankedOutOf, label);
            return _strings.Get(S.ai_rank_nth, rank, result.RankedOutOf, label);
        }

        private string Comparative(ExecutionResult.Comparative result)
        {
            var winner = DisplayName(result.Winner.Key);
            var loser = DisplayName(result.Loser.Key);
            var label = FieldLabel(result.FieldId).ToLowerInvariant();
            var winnerValue = result.WinnerValue.Display;
            var loserValue = result.LoserValue.Display;

            // A yes/no question gets a yes or a no, before any figures.
            var headline = result.ClaimHolds switch
            {
                true => _strings.Get(S.ai_comparative_yes, winner, winnerValue, loser, loserValue, label),
                false => _strings.Get(S.ai_comparative_no, winner, winnerValue, loser, loserValue, label),
                null => _strings.Get(S.ai_comparative_winner, winner, winnerValue, loser, loserValue, label),
            };

            if (result.Ratio is not { } ratio) return headline;

            return headline + "\n" + _strings.Get(
                S.ai_comparative_ratio,
                winner, Format(ratio), loser,
                $"{winnerValue} {_strings.Get(S.ai_versus)} {loserValue}", label);
        }

        private string Neighbour(ExecutionResult.Neighbour result)
        {
            var sentence = _strings.Get(
                result.Forward ? S.ai_neighbour_after : S.ai_neighbour_before,
                DisplayName(result.To.Key), result.To.AtomicNumber, DisplayName(result.From.Key));

            return sentence + "\n" + ElementContext(result.To);
        }

        /// <summary>One line placing an element: what family it belongs to and which period.</summary>
        private string ElementContext(ElementRecord element)
        {
            string? series = null;
            if (_localized?.Elements.TryGetValue(element.Key, out var view) == true &&
                !string.IsNullOrWhiteSpace(view.SeriesName))
                series = view.SeriesName;

            series ??= element.Series.ToString().ToLowerInvariant().Replace('_', ' ');

            return _strings.Get(S.ai_context_series, DisplayName(element.Key), series, element.Period);
        }

        private string Comparison(ExecutionResult.Comparison result)
        {
            // A single element is a category lookup, not a comparison: render it as a labelled
            // property list rather than "Comparing Gold:".
            if (result.Elements.Count == 1)
            {
                var element = result.Elements[0];
                var single = new StringBuilder("### ").Append(DisplayName(element.Key));

                foreach (var fieldId in result.FieldIds)
                {
                    if (!result.Values.TryGetValue(fieldId, out var rows) || rows.Count == 0) continue;
                    if (string.IsNullOrWhiteSpace(rows[0].Display)) continue;
                    single.Append('\n').Append(
                        _strings.Get(S.ai_list_row, FieldLabel(fieldId), rows[0].Display));
                }
                return single.ToString();
            }

            var names = string.Join(", ", result.Elements.Select(e => DisplayName(e.Key)));
            var builder = new StringBuilder(_strings.Get(S.ai_comparing_title, names));

            foreach (var fieldId in result.FieldIds)
            {
                if (!result.Values.TryGetValue(fieldId, out var rows)) rows = [];
                if (rows.All(r => string.IsNullOrWhiteSpace(r.Display))) continue;

                builder.Append("\n\n### ").Append(FieldLabel(fieldId));
                foreach (var row in rows)
                {
                    var shown = string.IsNullOrWhiteSpace(row.Display)
                        ? _strings.Get(S.ai_no_data_right_now)
                        : row.Display;
                    builder.Append('\n').Append(
                        _strings.Get(S.ai_list_row, DisplayName(row.Element.Key), shown));
                }
            }

            if (Verdict(result) is { } verdict) builder.Append("\n\n").Append(verdict);
            return builder.ToString();
        }

        /// <summary>
        /// Who came out ahead, for a two-element comparison.
        ///
        /// <para>A table of five properties against two elements is data, not an answer - it leaves
        /// the reader to do the comparison the question asked for. Counting the fields where each
        /// element holds the higher value turns it back into an answer, without claiming that higher
        /// is <i>better</i>: the sentence says "has the higher value", which is the only thing the
        /// numbers support.</para>
        ///
        /// <para>Two elements only. With three or more, "leads on 2 of 5" hides more than it says,
        /// and the table really is the right shape for the answer.</para>
        /// </summary>
        private string? Verdict(ExecutionResult.Comparison result)
        {
            if (result.Elements.Count != 2) return null;

            var wins = new Dictionary<string, int>(StringComparer.Ordinal);
            var decided = 0;

            foreach (var fieldId in result.FieldIds)
            {
                if (!result.Values.TryGetValue(fieldId, out var all)) continue;
                var rows = all.Where(r => r.Quantity is not null).ToList();
                if (rows.Count != 2) continue;

                var top = rows.MaxBy(r => r.Quantity!.Value);
                var bottom = rows.MinBy(r => r.Quantity!.Value);
                if (top is null || bottom is null) continue;
                if (top.Quantity!.Value == bottom.Quantity!.Value) continue;

                decided++;
                wins[top.Element.Key] = wins.TryGetValue(top.Element.Key, out var n) ? n + 1 : 1;
            }

            if (decided < MinFieldsToJudge) return null;
            if (wins.Count == 0) return null;

            var leaderCount = wins.Values.Max();
            // An even split has no verdict worth stating.
            if (wins.Values.Count(v => v == leaderCount) > 1) return null;

            var leader = wins.First(e => e.Value == leaderCount);
            return _strings.Get(
                S.ai_comparison_verdict, DisplayName(leader.Key), leader.Value, decided);
        }

        /// <summary>
        /// Name the aspects a multi-part comparison asked for but could not cover.
        ///
        /// <para>"Compare lithium, sodium and potassium on reactivity, density and applications" can
        /// only answer density from stored fields. Saying so is the difference between an incomplete
        /// answer and a misleading one.</para>
        /// </summary>
        private string UncoveredAspects(QueryPlan plan)
        {
            const string prefix = "no field for ";

            var named = plan.Evidence
                .Where(e => e.StartsWith(prefix, StringComparison.Ordinal))
                .Select(e => e[prefix.Length..])
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // Drop terms contained in another, so "application, applications" reads as one aspect.
            var missing = named
                .Where(term => !named.Any(other =>
                    !string.Equals(other, term, StringComparison.Ordinal) &&
                    other.Contains(term, StringComparison.Ordinal)))
                .ToList();

            if (missing.Count == 0) return string.Empty;
            return "\n\n" + _strings.Get(S.ai_aspects_not_compared, string.Join(", ", missing));
        }

        private string ElementList(ExecutionResult.ElementList result, QueryPlan plan)
        {
            if (result.Results.Count == 0) return _strings.Get(S.ai_filter_none);

            // A superlative asks for one element, so answer it as a sentence rather than a list.
            if (result.FieldId is { } superlativeField && plan.Limit == 1)
            {
                var top = result.Results[0];
                var direction = _strings.Get(
                    result.Descending ? S.ai_superlative_highest : S.ai_superlative_lowest);

                string sentence;
                if (result.RankOffset > 0)
                {
                    // "the third densest element" - say which place, or the answer looks wrong.
                    var place = result.RankOffset + 1;
                    sentence = _strings.Get(
                        S.ai_superlative_nth, place, OrdinalSuffix(place),
                        direction, FieldLabel(superlativeField).ToLowerInvariant(),
                        DisplayName(top.Element.Key), top.Display);
                }
                else
                {
                    sentence = _strings.Get(
                        S.ai_superlative_result,
                        direction, FieldLabel(superlativeField), DisplayName(top.Element.Key), top.Display);
                }

                var superlative = new StringBuilder(sentence);

                // Say what the winner won against. A plain property lookup already reports "the 6th
                // densest of 105 elements with a recorded density"; a superlative was answering with
                // a bare winner and no sense of the field it topped. Reuses the same two strings, so
                // this costs no new translation in any locale.
                var withAValue = result.Matched - result.Missing;
                if (withAValue >= MinPoolToReport && result.RankOffset == 0)
                {
                    superlative.Append('\n').Append(_strings.Get(
                        result.Descending ? S.ai_rank_highest : S.ai_rank_lowest,
                        withAValue, FieldLabel(superlativeField).ToLowerInvariant()));
                }

                // The next one down turns a bare winner into a sense of the margin.
                if (result.RunnerUp is { } runnerUp)
                {
                    superlative.Append('\n').Append(_strings.Get(
                        S.ai_runner_up, DisplayName(runnerUp.Element.Key), runnerUp.Display));
                }

                if (result.Missing > 0)
                {
                    superlative.Append('\n').Append(
                        _strings.Get(S.ai_aggregate_missing_note, result.Missing));
                }
                return superlative.ToString();
            }

            // Say plainly when the shown rows are only the top slice of a larger match set.
            var truncated = result.Results.Count < result.Matched;
            var builder = new StringBuilder(
                truncated && result.FieldId is not null
                    ? _strings.Get(S.ai_filter_top, result.Results.Count, result.Matched)
                    : _strings.Get(S.ai_filter_matched, result.Matched));

            foreach (var row in result.Results)
            {
                builder.Append('\n').Append(
                    string.IsNullOrWhiteSpace(row.Display) || result.FieldId is null
                        ? _strings.Get(S.ai_list_row_plain, DisplayName(row.Element.Key))
                        : _strings.Get(S.ai_list_row, DisplayName(row.Element.Key), row.Display));
            }

            var remaining = result.Matched - result.Results.Count;
            if (remaining > 0) builder.Append('\n').Append(_strings.Get(S.ai_list_more, remaining));

            // Give the list a shape: the span it covers is more informative than the rows alone.
            if (result.FieldId is not null && result.Results.Count > 2)
            {
                var values = result.Results
                    .Select(r => r.Display)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .ToList();

                if (values.Count > 2)
                {
                    builder.Append("\n\n").Append(
                        _strings.Get(S.ai_list_spread, values[0], values[^1]));
                }
            }

            if (result.Missing > 0 && result.FieldId is not null)
            {
                builder.Append('\n').Append(_strings.Get(S.ai_aggregate_missing_note, result.Missing));
            }
            return builder.ToString();
        }

        /// <summary>English ordinal suffix: 1st, 2nd, 3rd, 4th, with the teens handled.</summary>
        private string OrdinalSuffix(int n) => _strings.Get(
            n % 100 is >= 11 and <= 13 ? S.ai_ordinal_suffix_th
            : n % 10 == 1 ? S.ai_ordinal_suffix_st
            : n % 10 == 2 ? S.ai_ordinal_suffix_nd
            : n % 10 == 3 ? S.ai_ordinal_suffix_rd
            : S.ai_ordinal_suffix_th);

        /// <summary>
        /// The unit to print beside an aggregate.
        ///
        /// <para>The aggregate carries the field's canonical token, which is chosen so values can be
        /// grouped and converted rather than read - the solar reservoirs canonicalise to
        /// <c>Si=10^6</c>. Where the field declares a reader-facing unit, that wins.</para>
        /// </summary>
        private string? AggregateUnit(ExecutionResult.Aggregate result)
        {
            if (!FieldRegistry.ById.TryGetValue(result.FieldId, out var spec)) return result.Unit;
            if (spec.UnitLabelKey is not { } key) return result.Unit;

            var label = Try.OrNull(() => _strings.Get(key))?.Trim();
            return string.IsNullOrEmpty(label) || label.StartsWith("str:", StringComparison.Ordinal)
                ? result.Unit
                : label;
        }

        private string Aggregate(ExecutionResult.Aggregate result)
        {
            if (result.Aggregation == Aggregation.Count)
            {
                var counted = new StringBuilder(_strings.Get(S.ai_count_result, (int)result.Value));
                foreach (var row in result.Contributors.Take(ListPreview))
                {
                    counted.Append('\n').Append(
                        _strings.Get(S.ai_list_row_plain, DisplayName(row.Element.Key)));
                }

                var left = result.Contributors.Count - ListPreview;
                if (left > 0) counted.Append('\n').Append(_strings.Get(S.ai_list_more, left));
                return counted.ToString();
            }

            var label = result.Aggregation switch
            {
                Aggregation.Median => _strings.Get(S.ai_aggregate_median, FieldLabel(result.FieldId)),
                Aggregation.Sum => _strings.Get(S.ai_aggregate_sum, FieldLabel(result.FieldId)),
                _ => _strings.Get(S.ai_aggregate_average, FieldLabel(result.FieldId)),
            };

            var rendered = UnitConverter.FormatValue(result.Value) +
                           (AggregateUnit(result) is { } unit ? $" {unit}" : string.Empty);
            var builder = new StringBuilder(_strings.Get(S.ai_aggregate_result, label, rendered));

            // An average with no spread is the classic misleading statistic: the mean density of the
            // transition metals is about 10 g/cm³, and nothing in that number hints that the set
            // runs from scandium at 3 to osmium at 22.6. Both ends are already in Contributors, so
            // this costs a scan rather than another pass over the table. Sums are excluded - the
            // endpoints of a total say nothing about it.
            if (result.Aggregation != Aggregation.Sum && result.Contributors.Count >= MinPoolToReport)
            {
                var ordered = result.Contributors
                    .Where(c => c.Quantity is not null)
                    .OrderBy(c => c.Quantity!.Value)
                    .ToList();

                if (ordered.Count > 0)
                {
                    var low = ordered[0];
                    var high = ordered[^1];
                    if (!string.Equals(low.Element.Key, high.Element.Key, StringComparison.Ordinal))
                    {
                        builder.Append('\n').Append(_strings.Get(
                            S.ai_aggregate_spread,
                            low.Display, DisplayName(low.Element.Key),
                            high.Display, DisplayName(high.Element.Key)));
                    }
                }
            }

            // A partial statistic is always disclosed as partial.
            var total = result.Contributors.Count + result.Missing;
            builder.Append('\n').Append(
                _strings.Get(S.ai_aggregate_over, result.Contributors.Count, total));

            if (result.Missing > 0)
            {
                builder.Append(' ').Append(_strings.Get(S.ai_aggregate_missing_note, result.Missing));
            }
            return builder.ToString();
        }

        private string Formula(ExecutionResult.Formula result)
        {
            var r = result.Result;
            var builder = new StringBuilder(
                _strings.Get(S.ai_formula_mass, r.Formula, Format(r.MolarMass)));

            // The per-element breakdown *is* the calculation, so a compound shows it whether or not
            // the word "composition" was used. Asked for the molar mass of Ca(NO3)2 the agent gave
            // the total and nothing else, and the follow-up "show the calculation step by step" then
            // retrieved the dictionary definition of stoichiometry. A single-element formula is
            // excluded: one row restating the total is noise.
            if (result.WantsComposition || r.Parts.Count >= MinPartsToBreakDown)
            {
                builder.Append("\n\n### ").Append(_strings.Get(S.ai_composition_header, r.Formula));
                foreach (var part in r.Parts)
                {
                    builder.Append('\n').Append(_strings.Get(
                        S.ai_composition_row, part.Symbol, part.Count,
                        Format(part.MassContribution), Format(part.Percent)));
                }
            }
            return builder.ToString();
        }

        private string Nuclide(ExecutionResult.Nuclide result)
        {
            var name = DisplayName(result.Element.Key);
            return "### " + _strings.Get(S.ai_nuclide_header, name, result.MassNumber) +
                   "\n" + _strings.Get(S.ai_nuclide_explain, result.MassNumber, result.Protons, result.Neutrons) +
                   "\n" + _strings.Get(S.ai_nuclide_body, result.Protons, result.Neutrons, result.MassNumber);
        }

        private string IsotopeComparison(ExecutionResult.IsotopeComparison result)
        {
            var left = result.Left;
            var right = result.Right;
            var leftName = NuclideName(left);
            var rightName = NuclideName(right);

            var builder = new StringBuilder("### ")
                .Append(_strings.Get(S.ai_isotope_compare_header, leftName, rightName));

            // The interesting fact for two isotopes of one element is that only the neutrons differ,
            // so lead with that rather than repeating the shared proton count as a difference.
            if (string.Equals(left.Element.Key, right.Element.Key, StringComparison.Ordinal))
            {
                builder.Append('\n').Append(_strings.Get(
                    S.ai_isotope_same_element, DisplayName(left.Element.Key), left.Protons));
                builder.Append('\n').Append(_strings.Get(
                    S.ai_isotope_neutron_diff, leftName, left.Neutrons,
                    rightName, right.Neutrons, Math.Abs(left.Neutrons - right.Neutrons)));
            }
            else
            {
                builder.Append('\n').Append(Row(
                    S.ai_label_protons,
                    left.Protons.ToString(CultureInfo.InvariantCulture),
                    right.Protons.ToString(CultureInfo.InvariantCulture)));
            }

            builder.Append('\n').Append(Row(
                S.ai_label_mass_number,
                left.MassNumber.ToString(CultureInfo.InvariantCulture),
                right.MassNumber.ToString(CultureInfo.InvariantCulture)));

            builder.Append('\n').Append(Row(
                S.ai_label_neutrons,
                left.Neutrons.ToString(CultureInfo.InvariantCulture),
                right.Neutrons.ToString(CultureInfo.InvariantCulture)));

            builder.Append('\n').Append(Row(S.ai_label_half_life, HalfLife(left), HalfLife(right)));

            var decayLeft = left.Isotope?.DecayType;
            var decayRight = right.Isotope?.DecayType;
            if (decayLeft is not null || decayRight is not null)
            {
                builder.Append('\n').Append(Row(S.ai_label_decay, decayLeft ?? "—", decayRight ?? "—"));
            }
            return builder.ToString();
        }

        private string Row(string labelKey, string left, string right) =>
            _strings.Get(S.ai_isotope_compare_row, _strings.Get(labelKey), left, right);

        private string NuclideName(ExecutionResult.NuclideFacts facts) =>
            $"{DisplayName(facts.Element.Key)}-{facts.MassNumber.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>The authored half-life, "stable", or an admission that the nuclide is not listed.</summary>
        private string HalfLife(ExecutionResult.NuclideFacts facts)
        {
            if (facts.Isotope is not { } isotope) return _strings.Get(S.ai_isotope_unlisted);
            if (isotope.Stable) return _strings.Get(S.ai_stable_label);
            if (string.IsNullOrWhiteSpace(isotope.HalfLifeDisplay))
                return _strings.Get(S.ai_isotope_unlisted);
            return GroupDigits(isotope.HalfLifeDisplay);
        }

        /// <summary>
        /// Insert thin spaces into long digit runs, so a half-life reads as "703 800 000 years"
        /// rather than an unbroken nine-digit string. Uses spaces rather than a locale separator
        /// because the value is authored text being made legible, not a number being formatted.
        /// </summary>
        private static string GroupDigits(string text) =>
            LongDigitRun.Replace(text, match =>
            {
                var digits = match.Value.Reverse().ToArray();
                var chunks = new List<string>();
                for (var i = 0; i < digits.Length; i += 3)
                    chunks.Add(new string(digits, i, Math.Min(3, digits.Length - i)));
                return new string(string.Join(" ", chunks).Reverse().ToArray());
            });

        private string MoleConversion(ExecutionResult.MoleConversion result)
        {
            if (result.Moles is not { } moles) return string.Empty;
            var particles = Scientific(result.Particles);

            // Mass to moles is the question the other way round, and it is the one users actually
            // ask - they have a mass on a balance. It answers with the mole count rather than a
            // particle count, so the Avogadro note below would be answering something else.
            if (result.Grams is { } grams)
            {
                return _strings.Get(
                    S.ai_grams_to_moles, Format(grams), result.Substance ?? string.Empty, Format(moles));
            }

            var sentence = result.Substance is { } substance
                ? _strings.Get(S.ai_moles_of, Format(moles), substance, particles)
                : _strings.Get(S.ai_moles_to_particles, Format(moles), particles);

            return sentence + "\n" + _strings.Get(S.ai_avogadro_note);
        }

        private string UnitConversion(ExecutionResult.UnitConversion result)
        {
            var body = _strings.Get(
                S.ai_unit_converted,
                Format(result.Value), result.FromUnit, Format(result.Converted), result.ToUnit);

            // A crossed dimension is named rather than glossed over. eV and kJ/mol measure the same
            // energy counted per particle and per mole, and a reader who is not told that is
            // entitled to conclude the two are the same quantity.
            return result.Bridged ? body + "\n\n" + _strings.Get(S.ai_unit_bridge_note) : body;
        }

        private string BalancedEquation(ExecutionResult.BalancedEquation result, bool working = true)
        {
            if (result.Reason is { } reason)
            {
                // A one-sided element is named when the solver identified it. "An element appears on
                // only one side" leaves the reader to find which; saying "O appears on only one
                // side" is the same sentence with the work done.
                return reason == EquationBalancer.Reason.OneSidedElement && result.Detail is { } detail
                    ? _strings.Get(S.ai_equation_one_sided_named, detail)
                    : _strings.Get(ReasonString(reason));
            }

            var left = string.Join(" + ", result.Reactants.Select(Term));
            var right = string.Join(" + ", result.Products.Select(Term));
            var builder = new StringBuilder(_strings.Get(S.ai_equation_balanced, left, right));
            if (!working) return builder.ToString();

            // The tally is the proof. A balancer that prints only coefficients is asking to be
            // trusted; one that shows both sides' atom counts can be checked in a few seconds.
            builder.Append("\n\n### ").Append(_strings.Get(S.ai_equation_check_header));
            foreach (var t in result.Tally)
            {
                builder.Append('\n').Append(
                    _strings.Get(S.ai_equation_tally_row, t.Symbol, t.Left, t.Right));
            }
            return builder.ToString();
        }

        /// <summary>A coefficient of 1 is written, not printed - "1Fe" is how a bug looks, not chemistry.</summary>
        private static string Term(Data.Term t) =>
            t.Coefficient == 1
                ? t.Formula
                : t.Coefficient.ToString(CultureInfo.InvariantCulture) + t.Formula;

        private static string ReasonString(EquationBalancer.Reason reason) => reason switch
        {
            EquationBalancer.Reason.OneSidedElement => S.ai_equation_one_sided,
            EquationBalancer.Reason.Underdetermined => S.ai_equation_underdetermined,
            EquationBalancer.Reason.TooLarge => S.ai_equation_too_large,
            EquationBalancer.Reason.ParseFailed => S.ai_equation_parse_failed,
            EquationBalancer.Reason.NoSolution => S.ai_equation_no_solution,
            _ => S.ai_equation_parse_failed,
        };

        private string SolutionCalc(ExecutionResult.SolutionCalc result, bool working = true)
        {
            var substance = result.Substance ?? string.Empty;

            if (result.Dilution is { } d)
            {
                var added = SolutionMath.SolventToAdd(d.V1, d.V2);
                var dilution = new StringBuilder(_strings.Get(
                    S.ai_dilution_result,
                    Format(d.C1), Format(d.V1 * 1000), Format(d.C2), Format(d.V2 * 1000)));

                if (added > 0)
                {
                    dilution.Append('\n').Append(
                        _strings.Get(S.ai_dilution_add_solvent, Format(added * 1000)));
                }

                if (!working) return dilution.ToString();
                dilution.Append("\n\n### ").Append(_strings.Get(S.ai_solution_working_header));
                dilution.Append('\n').Append(_strings.Get(
                    S.ai_solution_working_c1v1,
                    Format(d.C1), Format(d.V1 * 1000), Format(d.C2), Format(d.V2 * 1000)));
                return dilution.ToString();
            }

            if (result.Moles is not { } moles) return string.Empty;
            if (result.Molarity is not { } molarity) return string.Empty;
            if (result.Litres is not { } litres) return string.Empty;

            var lead = result.Kind switch
            {
                ExecutionResult.SolutionKind.MassNeeded => _strings.Get(
                    S.ai_solution_mass_needed,
                    Format(result.Grams ?? 0.0), substance, Format(litres * 1000), Format(molarity)),
                ExecutionResult.SolutionKind.Molarity =>
                    _strings.Get(S.ai_solution_molarity, Format(molarity), substance),
                ExecutionResult.SolutionKind.Volume =>
                    _strings.Get(S.ai_solution_volume, Format(litres * 1000), Format(molarity)),
                _ => _strings.Get(S.ai_solution_moles, Format(moles), substance),
            };

            // The working is shown for the same reason the composition breakdown is: a number a
            // reader cannot reproduce is a number they have to take on faith.
            var builder = new StringBuilder(lead);
            if (!working) return builder.ToString();
            builder.Append("\n\n### ").Append(_strings.Get(S.ai_solution_working_header));
            builder.Append('\n').Append(_strings.Get(
                S.ai_solution_working_nvc, Format(moles), Format(molarity), Format(litres)));

            if (result.MolarMass is { } mass)
            {
                builder.Append('\n').Append(_strings.Get(
                    S.ai_solution_working_mass, Format(result.Grams ?? 0.0), Format(moles), Format(mass)));
            }
            return builder.ToString();
        }

        private string Decay(ExecutionResult.Decay result, bool working = true)
        {
            var nuclide = $"{DisplayName(result.Element.Key)}-{result.MassNumber.ToString(CultureInfo.InvariantCulture)}";

            switch (result.Mode)
            {
                case ExecutionResult.DecayMode.Stable:
                    return _strings.Get(S.ai_decay_stable, nuclide);

                case ExecutionResult.DecayMode.NoHalfLife:
                    // Two different gaps, and the display string is what tells them apart: an empty
                    // one means the nuclide is not listed at all, a non-empty one means it is listed
                    // with a half-life the source records as unknown.
                    return string.IsNullOrWhiteSpace(result.HalfLifeDisplay)
                        ? _strings.Get(S.ai_decay_unlisted, nuclide)
                        : _strings.Get(S.ai_decay_no_half_life, nuclide, result.HalfLifeDisplay);
            }

            if (result.HalfLifeSeconds is null) return string.Empty;
            if (result.HalfLivesElapsed is not { } n) return string.Empty;

            var elapsedUnit = result.ElapsedUnit ?? "s";
            var elapsedInUnit = result.ElapsedSeconds is { } seconds
                ? UnitConverter.Convert(seconds, "s", elapsedUnit) ?? 0.0
                : 0.0;

            string lead;
            if (n > DecayMath.EffectivelyGone)
            {
                // Past sixty half-lives less than one part in 10^18 survives. Printing 3.2e-25 g
                // would imply a measurement nobody could make from data this app does not have.
                lead = _strings.Get(
                    S.ai_decay_effectively_none, nuclide, Format(elapsedInUnit), elapsedUnit);
            }
            else if (result.Mode == ExecutionResult.DecayMode.Elapsed)
            {
                lead = _strings.Get(
                    S.ai_decay_elapsed,
                    Format(result.InitialAmount ?? 0.0), Format(result.FinalAmount ?? 0.0),
                    nuclide, GroupDigits(Format(elapsedInUnit)), elapsedUnit);
            }
            else if (result.Mode == ExecutionResult.DecayMode.Remaining)
            {
                lead = _strings.Get(
                    S.ai_decay_remaining,
                    Format(result.FinalAmount ?? 0.0), result.AmountUnit ?? string.Empty,
                    Format(result.InitialAmount ?? 0.0), nuclide,
                    GroupDigits(Format(elapsedInUnit)), elapsedUnit);
            }
            else
            {
                lead = _strings.Get(
                    S.ai_decay_half_lives,
                    nuclide, Format(n), Format((result.FractionRemaining ?? 0.0) * 100));
            }

            var builder = new StringBuilder(lead);
            if (!working) return builder.ToString();
            builder.Append("\n\n### ").Append(_strings.Get(S.ai_decay_working_header));
            builder.Append('\n').Append(_strings.Get(
                S.ai_decay_working_half_life, nuclide, GroupDigits(result.HalfLifeDisplay)));
            builder.Append('\n').Append(_strings.Get(
                S.ai_decay_working_n, Format(n), Format((result.FractionRemaining ?? 0.0) * 100)));
            return builder.ToString();
        }

        private static string Format(double value) => UnitConverter.FormatValue(value);

        /// <summary>Renders a large count in scientific notation, e.g. 1.2 × 10²⁴.</summary>
        private static string Scientific(double value)
        {
            if (value == 0.0) return "0";
            var exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));
            var mantissa = value / Math.Pow(10.0, exponent);
            return $"{Format(mantissa)} × 10{Superscript(exponent)}";
        }

        private static string Superscript(int n)
        {
            const string digits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
            var sign = n < 0 ? "⁻" : string.Empty;
            var magnitude = Math.Abs(n).ToString(CultureInfo.InvariantCulture);
            return sign + new string(magnitude.Select(d => digits[d - '0']).ToArray());
        }

        private string Isotopes(ExecutionResult.Isotopes result)
        {
            var builder = new StringBuilder("### ")
                .Append(_strings.Get(S.ai_isotopes_header, DisplayName(result.Element.Key)));

            builder.Append('\n').Append(
                _strings.Get(S.ai_isotopes_summary, result.Total, result.StableCount));

            foreach (var isotope in result.Shown)
            {
                builder.Append('\n').Append(
                    isotope.Stable
                        ? _strings.Get(S.ai_isotope_stable, isotope.Name)
                        : isotope.DecayType is { } decay
                            ? _strings.Get(S.ai_isotope_decays, isotope.Name, decay, isotope.HalfLifeDisplay)
                            : _strings.Get(S.ai_isotope_halflife_only, isotope.Name, isotope.HalfLifeDisplay));
            }

            var remaining = result.Total - result.Shown.Count;
            if (remaining > 0) builder.Append('\n').Append(_strings.Get(S.ai_list_more, remaining));
            return builder.ToString();
        }

        private string Safety(ExecutionResult.Safety result)
        {
            var builder = new StringBuilder("### ")
                .Append(_strings.Get(S.ai_safety_header, DisplayName(result.Element.Key)));

            var nfpa = result.Nfpa;
            var anyRating = false;

            if (nfpa.Health is { } health)
            {
                anyRating = true;
                builder.Append('\n').Append(
                    _strings.Get(S.ai_safety_health, health, _strings.Get(NfpaLabeller.HealthDescription(health))));
            }
            if (nfpa.Flammability is { } fire)
            {
                anyRating = true;
                builder.Append('\n').Append(_strings.Get(
                    S.ai_safety_flammability, fire, _strings.Get(NfpaLabeller.FlammabilityDescription(fire))));
            }
            if (nfpa.Instability is { } instability)
            {
                anyRating = true;
                builder.Append('\n').Append(_strings.Get(S.ai_safety_instability, instability));
            }

            if (!anyRating)
            {
                builder.Append('\n').Append(
                    _strings.Get(S.ai_safety_none_recorded, DisplayName(result.Element.Key)));
            }
            if (result.Radioactive)
            {
                builder.Append('\n').Append(_strings.Get(S.ai_safety_radioactive_note));
            }
            return builder.ToString();
        }

        /// <summary>
        /// The emission spectrum, which is a picture and a disclaimer.
        ///
        /// <para>The sentence says what the card below it shows and, in the same breath, that the app
        /// holds no wavelengths - so the agent is never asked to name a line it does not have.
        /// Offline the card is dropped by the policy, and the text says so rather than describing an
        /// image that is not going to appear, reusing the wording the element screen shows.</para>
        /// </summary>
        private string EmissionSpectrum(ExecutionResult.EmissionSpectrum result)
        {
            var body = _strings.Get(S.ai_emission_spectrum, DisplayName(result.Element.Key));
            return _cardPolicy.AllowNetworkCards
                ? body
                : body + "\n" + _strings.Get(S.go_online_for_emission);
        }

        /// <summary>
        /// A row from one of the app's reference tables.
        ///
        /// <para>Those tables are English-only, which the dataset index documents and says the
        /// composer "flags rather than pretending otherwise". It did not flag it, so a Swedish user
        /// asking what an isotope is got an English definition with no indication why. Said once,
        /// and only when the conversation is not already in English.</para>
        /// </summary>
        private string Dataset(ExecutionResult.Dataset result)
        {
            // The title in the reader's language when the shared data has it (roadmap 1.3), with the
            // English one beside it, because the row's text below is still English.
            var translated = _localized?.DatasetTitlesOf(result.Row).FirstOrDefault();
            var heading = translated is not null && translated != result.Row.Title
                ? $"**{translated}** ({result.Row.Title})"
                : $"**{result.Row.Title}**";
            var body = $"{heading}\n{result.Row.Detail}";
            if (string.Equals(_strings.Language, English, StringComparison.OrdinalIgnoreCase)) return body;

            var note = Try.OrNull(() => _strings.Get(S.ai_dataset_english_note));
            return note is null || note.StartsWith("str:", StringComparison.Ordinal)
                ? body
                : $"{body}\n\n{note}";
        }

        /// <summary>
        /// An answer withheld behind the paywall.
        ///
        /// <para>Deliberately different from <see cref="NoData"/>: this says the app <i>has</i> the
        /// value and is not showing it, rather than claiming not to know. Telling a user "I don't
        /// have that" about a figure their element screen visibly displays as a lock would be a lie,
        /// and a confusing one.</para>
        ///
        /// <para>The fields are named. Knowing <i>what</i> is behind the paywall is not the same as
        /// being told the value, and it is what makes the upsell concrete rather than a blank
        /// refusal.</para>
        /// </summary>
        private string Locked(ExecutionResult.Locked result)
        {
            var labels = string.Join(", ", result.FieldIds
                .Where(id => FieldRegistry.ById.ContainsKey(id))
                .Select(id => FieldLabel(id).ToLowerInvariant()));

            var tierName = _strings.Get(result.Tier == Tier.ProPlus ? S.get_pro_plus : S.get_pro);

            return string.IsNullOrWhiteSpace(labels)
                ? _strings.Get(S.ai_locked_generic, tierName)
                : _strings.Get(S.ai_locked_fields, labels, tierName);
        }

        /// <summary>
        /// The honest failure. Names the field, says it was checked, and reports how sparse it is -
        /// rather than printing the raw sentinel or drifting to an unrelated answer.
        /// </summary>
        private string NoData(ExecutionResult.NoData result)
        {
            var label = FieldLabel(result.FieldId);
            var sentence = result.Element is { } named
                ? _strings.Get(S.ai_no_property_data, DisplayName(named.Key), label)
                : _strings.Get(S.ai_no_data_right_now);

            var builder = new StringBuilder(sentence)
                .Append('\n')
                .Append(_strings.Get(S.ai_coverage_note, label, result.Coverage, _store.Size));

            // Say what *is* known. "Tungsten has no recorded Curie point" is honest and a dead end;
            // the same answer plus the neighbouring properties that are recorded gives the reader
            // somewhere to go, and costs nothing - those values are already loaded.
            //
            // The siblings are filtered on actually having a value. Android's filter reads
            // `element.value(it.id) != null`, and value() returns FieldValue.Missing rather than
            // null, so it is always true and the list can offer properties that are also absent -
            // the opposite of what the paragraph above promises. See docs/porting-notes.md D15.
            if (result.Element is { } element &&
                FieldRegistry.ById.TryGetValue(result.FieldId, out var spec))
            {
                var siblings = FieldRegistry.All
                    .Where(s => s.Category == spec.Category &&
                                !string.Equals(s.Id, result.FieldId, StringComparison.Ordinal))
                    .Where(s => !element.Value(s.Id).IsMissing)
                    .Take(MaxAlternatives)
                    .Select(s => FieldLabel(s.Id))
                    .ToList();

                if (siblings.Count > 0)
                {
                    builder.Append('\n').Append(_strings.Get(
                        S.ai_no_data_alternatives,
                        DisplayName(element.Key), string.Join(", ", siblings)));
                }
            }
            return builder.ToString();
        }

        // ---- Citations --------------------------------------------------------------------------

        private string CitationBlock(IReadOnlyList<Citation> citations)
        {
            if (citations.Count == 0) return string.Empty;

            var lines = citations
                .DistinctBy(c => (c.Label, c.Source))
                .Take(MaxCitations)
                .Select(c => c.FromTable
                    ? _strings.Get(S.ai_source_table, c.Label, c.Source)
                    : _strings.Get(S.ai_source_element_data, c.Label, c.Source));

            return "\n\n### " + _strings.Get(S.ai_sources_header) + "\n" +
                   string.Join("\n", lines.Select(l => "• " + l));
        }

        // ---- Helpers ------------------------------------------------------------------------------

        private string DisplayName(string key)
        {
            var name = _localized?.Name(key);
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..];
        }

        private string FieldLabel(string fieldId)
        {
            if (!FieldRegistry.ById.TryGetValue(fieldId, out var spec)) return fieldId.Replace('_', ' ');

            // The sentence form where one is defined: the shipped labels are table headings, and the
            // abundance ones are prepositional phrases that read as nonsense inside a sentence.
            var label = Try.OrNull(() => _strings.Get(spec.SentenceLabel()));
            if (label is not null && label.StartsWith("str:", StringComparison.Ordinal)) label = null;
            label ??= Try.OrNull(() => _strings.Get(spec.LabelKey));

            return label is null || label.StartsWith("str:", StringComparison.Ordinal)
                ? fieldId.Replace('_', ' ')
                : label.Replace(":", string.Empty, StringComparison.Ordinal)
                       .Replace("：", string.Empty, StringComparison.Ordinal)
                       .Trim();
        }

        private static readonly Regex LongDigitRun = JavaRegex.Compile(@"\d{5,}");

        private const int MaxCitations = 4;
        private const int ListPreview = 10;

        /// <summary>The one language the app's reference tables are written in.</summary>
        private const string English = "en";

        /// <summary>Below this, naming the pool a superlative won against adds noise rather than context.</summary>
        private const int MinPoolToReport = 3;

        /// <summary>Below this many distinct elements a formula breakdown just repeats the total.</summary>
        private const int MinPartsToBreakDown = 2;

        /// <summary>Below this many decided fields, a tally of wins says less than the table above it.</summary>
        private const int MinFieldsToJudge = 2;

        /// <summary>Longest list of neighbouring properties offered beside a missing one.</summary>
        private const int MaxAlternatives = 4;
    }
}
