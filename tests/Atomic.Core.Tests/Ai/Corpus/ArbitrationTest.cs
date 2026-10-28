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
    /// <para>Besides the invariants, this prints which branches propose together and which wins:
    /// the map of where the order of <see cref="Branch"/> still decides an answer, which the later
    /// stages of 1.8 take apart one conflict at a time.</para>
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
        public void AnEarlierBranchWinsAndWithinOneTheFirstProposal()
        {
            var planner = CorpusHarness.StackFor("en").Planner;
            var proposals = planner.Proposals("which elements are in the same group as gold", new DialogueState());
            var pick = Arbiter.Pick(proposals)!;
            Assert.All(proposals, p => Assert.True(p.Branch >= pick.Branch));
            Assert.Same(proposals.First(p => p.Branch == pick.Branch), pick);
            Assert.Equal(DeclineReason.NoMatch, proposals[^1].Plan.Declined);
        }

        /// <summary>Which branches compete, and how often each wins: a scoreboard, not a gate.</summary>
        [Fact]
        public void ReportsWhereTheOrderStillDecides()
        {
            var contests = new Dictionary<(Branch Winner, Branch Loser), int>();
            var rows = Rows();
            var contested = 0;
            foreach (var corpusCase in rows)
            {
                var proposals = CorpusHarness.StackFor(corpusCase.Lang).Planner
                    .Proposals(corpusCase.Query, new DialogueState())
                    .Where(p => p.Branch != Branch.NoMatch).ToList();
                if (Arbiter.Pick(proposals) is not { } winner) continue;
                var losers = proposals.Select(p => p.Branch).Where(b => b != winner.Branch).Distinct().ToList();
                if (losers.Count > 0) contested++;
                foreach (var loser in losers)
                    contests[(winner.Branch, loser)] = contests.GetValueOrDefault((winner.Branch, loser)) + 1;
            }
            _output.WriteLine($"[arbitration] {rows.Count} rows, {contested} with more than one branch proposing");
            foreach (var ((winner, loser), count) in contests.OrderByDescending(e => e.Value))
                _output.WriteLine($"[arbitration] {winner} over {loser}: {count}");
        }
    }
}
