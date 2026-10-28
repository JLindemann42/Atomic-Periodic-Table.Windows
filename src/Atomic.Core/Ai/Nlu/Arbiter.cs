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
    /// proposes, and <see cref="Arbiter"/> chooses. In this first stage the choice is still the old
    /// order - this enum's - so no answer changes; the order is simply data now. Later stages replace
    /// it, one conflict at a time, with the evidence each proposal carries.</para>
    /// </summary>
    public enum Branch
    {
        DeferToPersonality,
        OutOfRangeElement,
        UnbackedExplanation,
        UnbackedConcept,
        CountOfAllElements,
        Calculation,
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

    /// <summary>A plan one branch proposes for a query, with the evidence it had when it proposed.</summary>
    public sealed record Proposal(Branch Branch, QueryPlan Plan);

    /// <summary>Chooses among the planner's proposals (roadmap 1.8).</summary>
    public static class Arbiter
    {
        /// <summary>
        /// The proposal to answer with: the branch earliest in <see cref="Branch"/>, and within one
        /// branch the proposal made first - a branch that tries the explicit "X is denser than Y"
        /// before its looser reading proposes both, and the explicit one is the one it meant.
        /// </summary>
        public static Proposal? Pick(IReadOnlyList<Proposal> proposals) =>
            proposals.Count == 0 ? null : proposals.MinBy(p => (int)p.Branch);
    }
}
