using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// "Tell me more" - the next unsaid fact about the element in focus.
    ///
    /// <para>The point of the handler is that repeating "more" keeps teaching, so most of these
    /// assert across a run of turns rather than on a single reply.</para>
    /// </summary>
    public class ElementMoreInfoTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(int? seed = null, string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null,
                language, seed is { } s ? new Random(s) : null);

        private static string NoData => new ReswStringProvider("en", true).Get(S.ai_no_data);

        /// <summary>
        /// Android names twenty-two properties; all twenty-two must survive the json-key lookup.
        /// </summary>
        /// <remarks>
        /// The list is written as Android's json keys and mapped through the registry, and an
        /// unknown key maps to nothing at all. Without this the list could shrink to nineteen and
        /// the only symptom would be a slightly shorter tour.
        /// </remarks>
        [Fact]
        public void EveryCandidatePropertyResolvesToAField()
        {
            Assert.Equal(22, ElementMoreInfo.CandidateFieldIds.Count);
            Assert.Equal(
                ElementMoreInfo.CandidateFieldIds.Count,
                ElementMoreInfo.CandidateFieldIds.Distinct(StringComparer.Ordinal).Count());
        }

        /// <summary>
        /// And each one is a field some element actually has, which is what Android's
        /// <c>element_thermal_conductivity</c> is not - no element carries that key, so Android
        /// offers thermal conductivity to nobody.
        /// </summary>
        [Fact]
        public void EveryCandidatePropertyIsPopulatedForSomeElement()
        {
            var all = Service.Store.Elements;

            var dead = ElementMoreInfo.CandidateFieldIds
                .Where(id => all.All(e => e.Value(id).IsMissing))
                .ToList();

            Assert.Empty(dead);
        }

        /// <summary>The request is answered rather than declined.</summary>
        [Theory]
        [InlineData("tell me more")]
        [InlineData("what else")]
        [InlineData("anything else")]
        [InlineData("keep going")]
        public void TellMeMoreIsNoLongerDeclined(string query)
        {
            var chat = Chat(seed: 1);
            chat.Ask("gold");

            var reply = chat.Ask(query);

            Assert.NotEqual(NoData, reply.Text);
            Assert.NotEmpty(reply.Text);
        }

        /// <summary>Six "more"s in a row say six different things.</summary>
        [Fact]
        public void ItDoesNotRepeatItself()
        {
            var chat = Chat(seed: 2);
            chat.Ask("gold");

            var seen = new List<string>();
            for (var i = 0; i < 6; i++) seen.Add(chat.Ask("tell me more").Text);

            Assert.Equal(seen.Count, seen.Distinct(StringComparer.Ordinal).Count());
        }

        /// <summary>
        /// The overview already states the atomic mass, so the first "more" must not be it.
        /// </summary>
        [Fact]
        public void TheOverviewsOwnFactsAreNotOfferedAsNews()
        {
            for (var seed = 0; seed < 8; seed++)
            {
                var chat = Chat(seed);
                var overview = chat.Ask("tell me about gold");

                Assert.Contains("Au", overview.Text, StringComparison.Ordinal);
                Assert.False(
                    chat.State.SharedFieldIds.Count == 0,
                    "the overview recorded nothing as said");

                Assert.Contains("atomic_mass", chat.State.SharedFieldIds);
            }
        }

        /// <summary>
        /// A melting point invites the boiling point. Android's pacing rules, which are most of
        /// the difference between a tour and a shuffle.
        /// </summary>
        [Fact]
        public void AMeltingPointIsFollowedByABoilingPointOrDensity()
        {
            var chat = Chat(seed: 4);
            chat.Ask("what is the melting point of gold");

            Assert.Contains("melting_point", chat.State.SharedFieldIds);

            chat.Ask("tell me more");

            var next = chat.State.SharedFieldIds.Last();
            Assert.True(
                next is "boiling_point" or "density",
                "expected boiling point or density after a melting point, got " + next);
        }

        /// <summary>
        /// With nothing in focus there is nothing to say more about, so it declines rather than
        /// volunteering a fact about an element the user never mentioned.
        /// </summary>
        [Fact]
        public void WithNoElementInFocusItDeclines()
        {
            var reply = Chat(seed: 5).Ask("tell me more");

            Assert.Equal(NoData, reply.Text);
        }

        /// <summary>Asking repeatedly ends in an admission, not in the tour starting over.</summary>
        [Fact]
        public void EventuallyItSaysItHasCoveredEverything()
        {
            var chat = Chat(seed: 6);
            chat.Ask("gold");

            var last = "";
            for (var i = 0; i < 40; i++) last = chat.Ask("tell me more").Text;

            var covered = new ReswStringProvider("en", true).Get(S.ai_covered_all, "Gold");
            Assert.Contains(covered, last, StringComparison.Ordinal);
        }

        /// <summary>
        /// "More" on its own is the ordinary comparative, not a change of subject - the engine
        /// still owns it.
        /// </summary>
        [Fact]
        public void AComparativeQuestionIsStillAQuestion()
        {
            var chat = Chat(seed: 7);
            chat.Ask("gold");

            var reply = chat.Ask("which has more protons, gold or iron");

            Assert.Equal(ReplySource.Engine, reply.Source);
            Assert.Contains("79", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>Switching element starts a new tour; what was said about gold does not apply.</summary>
        [Fact]
        public void TheTourIsPerElement()
        {
            var chat = Chat(seed: 8);
            chat.Ask("gold");
            chat.Ask("tell me more");

            chat.Ask("iron");

            Assert.DoesNotContain(
                chat.State.SharedFieldIds,
                id => chat.State.HasShared("gold", id));
        }
    }
}
