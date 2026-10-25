using System;
using System.Linq;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// "Quiz me" - the one handler that spans two turns.
    ///
    /// <para>In <c>must_defer.tsv</c> in five languages, so the engine declines it by design.
    /// Seeded where the question needs to be known, because the element is chosen at random.</para>
    /// </summary>
    public class ElementQuizTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(int? seed = null, string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null,
                language, seed is { } s ? new Random(s) : null);

        private static IStringProvider Strings(string language = "en") =>
            new ReswStringProvider(language, fallBackToEnglish: true);

        [Theory]
        [InlineData("quiz")]
        [InlineData("quiz me")]
        [InlineData("give me a quiz question")]
        [InlineData("quiz mich")]
        public void AskingForAQuizIsNoLongerDeclined(string query)
        {
            var reply = Chat(seed: 1).Ask(query);

            Assert.NotEqual(Strings().Get(S.ai_no_data), reply.Text);
            Assert.NotEmpty(reply.Text);
        }

        /// <summary>A question is asked, and it is one of the three shapes.</summary>
        [Fact]
        public void TheQuestionIsOneOfTheThreeShapes()
        {
            for (var seed = 0; seed < 12; seed++)
            {
                var reply = Chat(seed).Ask("quiz me");

                var isQuestion =
                    reply.Text.Contains("symbol", StringComparison.OrdinalIgnoreCase)
                    || reply.Text.Contains("atomic number", StringComparison.OrdinalIgnoreCase)
                    || reply.Text.Contains("discovered", StringComparison.OrdinalIgnoreCase);

                Assert.True(isQuestion, $"seed {seed} produced: {reply.Text}");
            }
        }

        /// <summary>The right answer is marked right.</summary>
        [Fact]
        public void TheRightAnswerIsAccepted()
        {
            var chat = Chat(seed: 3);
            chat.Ask("quiz me");

            var expected = chat.State.PendingQuizAnswer;
            Assert.NotNull(expected);

            var reply = chat.Ask(expected!);

            Assert.Contains("Correct", reply.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Null(chat.State.PendingQuizAnswer);
        }

        /// <summary>A wrong answer is marked wrong, and names the one that was wanted.</summary>
        [Fact]
        public void AWrongAnswerIsRejectedAndRevealsTheAnswer()
        {
            var chat = Chat(seed: 5);
            chat.Ask("quiz me");

            var expected = chat.State.PendingQuizAnswer;
            Assert.NotNull(expected);

            // Any element that is not the one being asked about.
            var wrong = string.Equals(expected, "gold", StringComparison.Ordinal) ? "iron" : "gold";
            var reply = chat.Ask(wrong);

            Assert.DoesNotContain("Correct", reply.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Null(chat.State.PendingQuizAnswer);
        }

        /// <summary>
        /// The answer is judged, not looked up. "Gold" while a question is open must not come back
        /// as an element overview - the engine would happily answer it that way.
        /// </summary>
        [Fact]
        public void AnElementNameIsJudgedRatherThanLookedUp()
        {
            var chat = Chat(seed: 7);
            chat.Ask("quiz me");

            var reply = chat.Ask("gold");

            Assert.True(
                reply.Text.Contains("Correct", StringComparison.OrdinalIgnoreCase)
                || reply.Text.Contains("Not quite", StringComparison.OrdinalIgnoreCase),
                "expected a verdict, got: " + reply.Text);
        }

        /// <summary>Asking again while a question is open starts a new one rather than judging.</summary>
        [Fact]
        public void AskingAgainStartsANewQuestion()
        {
            var chat = Chat(seed: 9);
            chat.Ask("quiz me");

            var reply = chat.Ask("quiz me again");

            Assert.DoesNotContain("Correct", reply.Text, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(chat.State.PendingQuizAnswer);
        }

        /// <summary>With no question outstanding, a bare element name is an ordinary lookup.</summary>
        [Fact]
        public void WithNoQuestionOpenAnElementNameIsStillALookup()
        {
            var reply = Chat(seed: 11).Ask("gold");

            Assert.DoesNotContain("Correct", reply.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Au", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>
        /// An unrelated request while a question is open is not a wrong guess.
        ///
        /// <para>Android judges any reply of four words or fewer, so "tell me more" came back
        /// "Not quite! The answer was Vanadium" - the request was swallowed and the question
        /// burned. A reply has to name an element to be judged.</para>
        /// </summary>
        [Theory]
        [InlineData("tell me more")]
        [InlineData("what can you do")]
        public void AnUnrelatedRequestIsNotJudgedAsAGuess(string query)
        {
            var chat = Chat(seed: 13);
            chat.Ask("quiz me");

            var reply = chat.Ask(query);

            Assert.DoesNotContain("Not quite", reply.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Correct", reply.Text, StringComparison.OrdinalIgnoreCase);

            // And the question is still waiting, not spent.
            Assert.NotNull(chat.State.PendingQuizAnswer);
        }

        /// <summary>
        /// A history question is only asked about an element that has both a discoverer and a
        /// year; the sentence reads "discovered by --- in ---" otherwise.
        /// </summary>
        /// <remarks>
        /// This used to assert the text did not contain "** in **" - which every filled question
        /// does ("by **X** in **1878**"). It passed only because no history question was ever
        /// asked (D53). It checks for an empty value instead.
        /// </remarks>
        [Fact]
        public void AHistoryQuestionNeverHasBlanks()
        {
            for (var seed = 0; seed < 25; seed++)
            {
                var text = Chat(seed).Ask("quiz me").Text;

                if (text.Contains("discovered", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.DoesNotContain("---", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("****", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("** **", text, StringComparison.Ordinal);
                }
            }
        }

        /// <summary>
        /// The history shape is reachable. It was not: the discoverer and year were read by JSON
        /// key, which <c>Value</c> never finds, so every candidate was filtered out (D53).
        /// </summary>
        [Fact]
        public void TheHistoryQuestionIsAskedForSomeSeed() =>
            Assert.Contains(
                Enumerable.Range(0, 40),
                seed => Chat(seed).Ask("quiz me").Text.Contains("discovered", StringComparison.OrdinalIgnoreCase));
    }
}
