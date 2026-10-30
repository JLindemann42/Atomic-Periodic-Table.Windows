using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Nlu;
using Xunit;
using Xunit.Abstractions;

namespace Atomic.Core.Tests.Corpus
{
    /// <summary>
    /// The planner's branches and the arbiter that chooses among them (roadmap 1.8), over every
    /// corpus row, each asked fresh. Mirrors Android's <c>ArbitrationTest</c>.
    ///
    /// <para>Besides the invariants, this prints which branches propose different answers and what
    /// decided each contest - the evidence, or on a tie the order of <see cref="Branch"/> - so the
    /// ties the order still breaks can be taken apart one at a time.</para>
    /// </summary>
    public class ArbitrationTest
    {
        private readonly ITestOutputHelper _output;

        public ArbitrationTest(ITestOutputHelper output) => _output = output;

        private static List<CorpusCase> Rows() =>
            QuestionCorpus.Files.SelectMany(QuestionCorpus.Sessions).SelectMany(s => s).ToList();

        [Fact]
        public void EveryQueryEndsInNoMatchAndThePickIsThePlan()
        {
            Assert.True(QuestionCorpus.Available && CorpusHarness.Available, "corpus or assets not reachable");
            foreach (var corpusCase in Rows())
            {
                var planner = CorpusHarness.StackFor(corpusCase.Lang).Planner;
                var proposals = planner.Proposals(corpusCase.Query, new DialogueState());
                Assert.True(proposals[^1].Branch == Branch.NoMatch, corpusCase.Where);
                var plan = planner.Plan(corpusCase.Query, new DialogueState());
                // The speller may plan a corrected query after a NoMatch; otherwise the plan is the pick.
                if (!plan.Evidence.Any(e => e.StartsWith("spelling: ", System.StringComparison.Ordinal)))
                {
                    var pick = Arbiter.Pick(proposals)!.Plan;
                    Assert.True(pick.Intent == plan.Intent && pick.Declined == plan.Declined &&
                                pick.Evidence.SequenceEqual(plan.Evidence), corpusCase.Where);
                }
            }
        }

        [Fact]
        public void APreemptingBranchWinsByOrder()
        {
            var planner = CorpusHarness.StackFor("en").Planner;
            var proposals = planner.Proposals("which elements are in the same group as gold", new DialogueState());
            var pick = Arbiter.Pick(proposals)!;
            Assert.Equal(Branch.SetOfAnElement, pick.Branch);
            Assert.True(pick.Branch.Preempts());
            Assert.All(proposals, p => Assert.True(p.Branch >= pick.Branch));
            Assert.Equal(DeclineReason.NoMatch, proposals[^1].Plan.Declined);
        }

        [Fact]
        public void OtherwiseTheReadingThatUsesMostOfTheQueryWins()
        {
            var planner = CorpusHarness.StackFor("en").Planner;
            var proposals = planner.Proposals("what is the average electronegativity of the halogens", new DialogueState());
            var pick = Arbiter.Pick(proposals)!;
            Assert.Equal(Branch.Aggregate, pick.Branch);
            Assert.Contains("statistic", pick.Uses);
            Assert.True(pick.Score > proposals.First(p => p.Branch == Branch.SubsetList).Score);
        }

        [Fact]
        public void OnATieTheEarlierBranchAndWithinOneTheFirstProposal()
        {
            var plan = new QueryPlan(Intent.Unknown);
            var a = new Proposal(Branch.Comparison, plan, new HashSet<string> { "element au" });
            var b = new Proposal(Branch.Comparison, plan, new HashSet<string> { "element ag" });
            var c = new Proposal(Branch.DirectComparative, plan, new HashSet<string> { "element pb" });
            Assert.Same(a, Arbiter.Pick([a, b]));
            Assert.Same(c, Arbiter.Pick([a, b, c]));
        }

        /// <summary>Two plans that would give the same answer, whatever their evidence says.</summary>
        private static bool SameAnswer(QueryPlan x, QueryPlan y) =>
            x.Intent == y.Intent && x.Entities.SequenceEqual(y.Entities) && x.FieldIds.SequenceEqual(y.FieldIds) &&
            x.FieldOrdinal == y.FieldOrdinal && x.Filters.SequenceEqual(y.Filters) && x.Aggregation == y.Aggregation &&
            x.SortField == y.SortField && x.SortDescending == y.SortDescending && x.Limit == y.Limit &&
            x.TargetUnit == y.TargetUnit && x.Literal == y.Literal && x.Declined == y.Declined;

        /// <summary>
        /// Which branches compete, and what decided each contest: the evidence, or - on a tie - the
        /// order. A scoreboard, not a gate. Also lists every row where the evidence picks another
        /// branch than the order would have.
        /// </summary>
        [Fact]
        public void ReportsWhereTheOrderStillDecides()
        {
            var byEvidence = new Dictionary<(Branch Winner, Branch Loser), int>();
            var byOrder = new Dictionary<(Branch Winner, Branch Loser), int>();
            var rows = Rows();
            var contested = 0;
            var changed = 0;
            foreach (var corpusCase in rows)
            {
                var proposals = CorpusHarness.StackFor(corpusCase.Lang).Planner
                    .Proposals(corpusCase.Query, new DialogueState())
                    .Where(p => p.Branch != Branch.NoMatch).ToList();
                if (Arbiter.Pick(proposals) is not { } winner) continue;
                // Two branches that reach the same plan agree; only a different answer is a conflict.
                var losers = proposals
                    .Where(p => p.Branch != winner.Branch && !SameAnswer(p.Plan, winner.Plan))
                    .GroupBy(p => p.Branch).ToList();
                if (losers.Count > 0) contested++;
                foreach (var theirs in losers)
                {
                    var tally = winner.Branch.Preempts() || theirs.All(p => p.Score == winner.Score) ? byOrder : byEvidence;
                    tally[(winner.Branch, theirs.Key)] = tally.GetValueOrDefault((winner.Branch, theirs.Key)) + 1;
                    if (tally == byOrder && !winner.Branch.Preempts())
                        _output.WriteLine($"[tie] {winner.Branch}/{theirs.Key}: {corpusCase.Query} [{string.Join(", ", winner.Uses)}] vs [{string.Join(", ", theirs.First().Uses)}]");
                }
                var old = Arbiter.ByOrder(proposals)!;
                if (old.Branch != winner.Branch)
                {
                    changed++;
                    _output.WriteLine($"[arbitration] changed {corpusCase.Where}: {corpusCase.Query} -> {winner.Branch} over {old.Branch}");
                }
            }
            _output.WriteLine($"[arbitration] {rows.Count} rows, {contested} with branches proposing different answers, {changed} picked differently than by order");
            foreach (var (name, tally) in new[] { ("evidence", byEvidence), ("order", byOrder) })
                foreach (var ((winner, loser), count) in tally.OrderByDescending(e => e.Value))
                    _output.WriteLine($"[arbitration] by {name}: {winner} over {loser}: {count}");
        }
    }
}
