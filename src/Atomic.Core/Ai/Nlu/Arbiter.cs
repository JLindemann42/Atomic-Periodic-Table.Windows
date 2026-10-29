using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Engine;

namespace Atomic.Core.Ai.Nlu
{
    /// <summary>
    /// The planner's branches, each of which may propose a plan for a query (roadmap 1.8). Mirrors
    /// Android's <c>Branch</c>, in the same order.
    ///
    /// <para>The planner used to be a cascade: the first branch to return a plan won, so the order
    /// of ~30 blocks decided every answer and could be read only as control flow. Every branch now
    /// proposes, and <see cref="Arbiter"/> chooses.</para>
    ///
    /// <para>The branches up to <see cref="Explanation"/> recognise what <em>kind</em> of question
    /// this is - chat, a formula, a conversion, a compound, a request for prose, something to
    /// decline - and pre-empt in this order. The rest read the same element-data question in
    /// different ways, and compete on evidence; this order only breaks their ties.</para>
    /// </summary>
    public enum Branch
    {
        DeferToPersonality,
        OutOfRangeElement,
        UnbackedExplanation,
        UnbackedConcept,
        CountOfAllElements,
        Calculation,
        ShowSteps,
        SetOfAnElement,
        UnitConvert,
        Compound,
        NarrativeDecline,
        EtymologyDecline,
        Explanation,
        ComparativeAgainstFocus,
        ComparativeOverPrevious,
        PeriphrasticComparative,
        DirectComparative,
        Aggregate,
        SuperlativeOrFilteredList,
        SubsetList,
        Trend,
        FieldCoverageList,
        CompareWithFocus,
        Neighbour,
        Alloy,
        SelectiveComparison,
        Comparison,
        Isotopes,
        Safety,
        EmissionSpectrum,
        CategoryLookup,
        PropertyLookup,
        DatasetRow,
        NoMatch,
    }

    /// <summary>
    /// What a query said, as the reading branches see it: the elements and fields it named, whether
    /// it asked for a statistic, the unit it asked in, and which filters it did <em>not</em> say but
    /// inherited from the turn before. Everything a plan holds that is not in here came from the
    /// conversation or from a default. Mirrors Android's <c>Said</c>.
    /// </summary>
    public sealed record Said(
        IReadOnlySet<string> Elements,
        IReadOnlySet<string> Fields,
        bool Statistic,
        string? Unit,
        IReadOnlyList<Filter> InheritedFilters);

    /// <summary>
    /// A plan one branch proposes for a query, with the evidence it had when it proposed.
    /// <see cref="Uses"/> are the things the user said that this plan answers with - each named
    /// element and field it uses, each filter the query stated, the statistic, the unit, and the
    /// words that only this reading accounts for ("isotopes", "after", a comparative). Their count
    /// is the proposal's evidence; it is what the arbiter compares.
    /// </summary>
    public sealed record Proposal(Branch Branch, QueryPlan Plan, IReadOnlySet<string> Uses)
    {
        public int Score => Uses.Count;
    }

    /// <summary>Chooses among the planner's proposals (roadmap 1.8).</summary>
    public static class Arbiter
    {
        /// <summary>Whether <paramref name="branch"/> pre-empts by order rather than competing on evidence.</summary>
        public static bool Preempts(this Branch branch) => branch <= Branch.Explanation;

        /// <summary>
        /// The proposal to answer with. A pre-empting branch wins by <see cref="Branch"/> order;
        /// otherwise the reading that accounts for most of what the user said, and on a tie the
        /// branch earlier in <see cref="Branch"/> - and within one branch the proposal made first,
        /// since a branch that tries the explicit "X is denser than Y" before its looser reading
        /// proposes both, and the explicit one is the one it meant.
        /// </summary>
        public static Proposal? Pick(IReadOnlyList<Proposal> proposals)
        {
            if (ByOrder(proposals) is not { } best) return null;
            if (best.Branch.Preempts()) return best;
            foreach (var proposal in proposals)
            {
                if (proposal.Score > best.Score ||
                    (proposal.Score == best.Score && proposal.Branch < best.Branch))
                    best = proposal;
            }
            return best;
        }

        /// <summary>The pick of stage 1, by order alone: kept so the tests can show what evidence changed.</summary>
        public static Proposal? ByOrder(IReadOnlyList<Proposal> proposals) =>
            proposals.Count == 0 ? null : proposals.MinBy(p => (int)p.Branch);

        /// <summary>What <paramref name="plan"/> uses of what the query <paramref name="said"/>, plus the <paramref name="cues"/> only its branch reads.</summary>
        public static IReadOnlySet<string> Uses(QueryPlan plan, Said? said, IEnumerable<string> cues)
        {
            var uses = new HashSet<string>(System.StringComparer.Ordinal);
            if (said is not null)
            {
                foreach (var key in plan.ElementKeys.Where(said.Elements.Contains)) uses.Add($"element {key}");
                foreach (var field in plan.FieldIds.Append(plan.SortField).OfType<string>().Where(said.Fields.Contains))
                    uses.Add($"field {field}");
                var stated = plan.Filters.Where(f => !said.InheritedFilters.Contains(f)).ToList();
                for (var i = 0; i < stated.Count; i++) uses.Add($"filter {i + 1}: {stated[i]}");
                if (plan.Aggregation != Aggregation.None && said.Statistic) uses.Add("statistic");
                if (plan.TargetUnit is not null && plan.TargetUnit == said.Unit) uses.Add("unit");
            }
            uses.UnionWith(cues);
            return uses;
        }
    }
}
