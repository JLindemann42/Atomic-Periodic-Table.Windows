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
    /// Quiz runs from chat: scoped, counted, answered with chips, and earning XP (roadmap 2.5).
    /// Mirrors Android's <c>QuizRunTest</c>.
    /// </summary>
    public class QuizRunTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(int seed, string language = "en", Action<int>? onXp = null) =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language,
                new Random(seed), onXp);

        [Theory]
        [InlineData("quiz me on the halogens")]
        [InlineData("quiz me on period 3")]
        [InlineData("five isotope questions on uranium")]
        [InlineData("quiz me on gold")]
        public void AScopedRequestStartsARun(string query)
        {
            var chat = Chat(1);
            var reply = chat.Ask(query);
            Assert.True(chat.QuizRun is not null, reply.Text);
            Assert.True(reply.Suggestions.Count >= 2);
            Assert.Contains(chat.QuizRun!.Current.CorrectAnswer, reply.Suggestions);
        }

        [Fact]
        public void ABareQuizMeIsStillOneQuestion()
        {
            var chat = Chat(2);
            chat.Ask("quiz me");
            Assert.Null(chat.QuizRun);
            Assert.NotNull(chat.State.PendingQuizAnswer);
        }

        [Fact]
        public void TheCountIsRead()
        {
            Assert.Equal(5, Started(3, "five isotope questions on uranium").Questions.Count);
            Assert.Equal(3, Started(3, "give me 3 quiz questions").Questions.Count);
            Assert.Equal(QuizRun.DefaultCount, Started(3, "quiz me on period 3").Questions.Count);
        }

        [Fact]
        public void PeriodThreeIsAScopeNotACount() =>
            Assert.Equal(
                new[] { "aluminium", "argon", "chlorine", "magnesium", "phosphorus", "silicon", "sodium", "sulfur" },
                Started(4, "quiz me on period 3").ScopeKeys!.OrderBy(k => k, StringComparer.Ordinal));

        [Fact]
        public void IsotopeQuestionsAreAboutTheNamedElement()
        {
            foreach (var question in Started(5, "five isotope questions on uranium").Questions)
                Assert.True(question.Question.Contains("U-") || question.Alternatives.All(a => a.StartsWith("U-")), question.Question);
        }

        [Fact]
        public void RightAnswersEarnXpAndTheRunEnds()
        {
            var earned = 0;
            var chat = Chat(6, onXp: xp => earned += xp);
            chat.Ask("quiz me on the noble gases");
            var run = chat.QuizRun!;
            var last = "";
            for (var i = 0; i < run.Questions.Count; i++) last = chat.Ask(chat.QuizRun!.Current.CorrectAnswer).Text;
            Assert.Null(chat.QuizRun);
            Assert.Equal(run.Questions.Sum(q => q.BaseXp), earned);
            Assert.Contains($"{run.Questions.Count} of {run.Questions.Count}", last);
        }

        [Fact]
        public void AWrongAnswerSaysTheRightOne()
        {
            var chat = Chat(7);
            chat.Ask("quiz me on period 2");
            var question = chat.QuizRun!.Current;
            var text = chat.Ask(question.Alternatives.First(a => a != question.CorrectAnswer)).Text;
            Assert.Contains("Not quite", text);
            Assert.Contains(question.CorrectAnswer, text);
            Assert.Equal(1, chat.QuizRun!.Index);
        }

        [Fact]
        public void StopEndsTheRun()
        {
            var chat = Chat(8);
            chat.Ask("quiz me on the alkali metals");
            var text = chat.Ask("stop").Text;
            Assert.Null(chat.QuizRun);
            Assert.Contains("stopped", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AnUnrelatedQuestionLeavesTheRun()
        {
            var chat = Chat(9);
            chat.Ask("quiz me on period 4");
            var text = chat.Ask("what is the density of gold").Text;
            Assert.Null(chat.QuizRun);
            Assert.DoesNotContain("Not quite", text);
            Assert.Contains("19.3", text);
        }

        [Fact]
        public void ARunInSwedish()
        {
            var chat = Chat(10, "sv");
            var reply = chat.Ask("frågesport om ädelgaserna");
            Assert.True(chat.QuizRun is not null, reply.Text);
            Assert.Contains("Fråga 1 av", reply.Text);
        }

        private static QuizRunState Started(int seed, string query)
        {
            var chat = Chat(seed);
            chat.Ask(query);
            return chat.QuizRun ?? throw new InvalidOperationException($"no run for '{query}'");
        }
    }
}
