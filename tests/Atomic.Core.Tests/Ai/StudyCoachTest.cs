using System;
using System.Linq;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Study;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// The study coach from chat: answers become cards, and "review" asks the due ones (roadmap
    /// 2.6). Mirrors Android's <c>StudyCoachTest</c>.
    /// </summary>
    public class StudyCoachTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private long _now = 1_700_000_000_000L;
        private readonly StudyDeck _deck = new();
        private int _saves;

        private ChatOrchestrator Chat(int seed, bool pro = true) =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true),
                () => new Entitlements(IsPro: pro, IsProPlus: pro), null, "en", new Random(seed), null,
                new StudyAccess(_deck, () => _saves++, () => _now));

        [Fact]
        public void TheCoachIsPro()
        {
            var chat = Chat(1, pro: false);
            Assert.Contains("PRO", chat.Ask("review").Text);
            Assert.Null(chat.QuizRun);

            chat.Ask("quiz me on period 3");
            chat.Ask(chat.QuizRun!.Current.CorrectAnswer);
            Assert.Empty(_deck.Snapshot());
        }

        [Fact]
        public void NothingIsDueAtFirst() =>
            Assert.Contains("Nothing is due", Chat(2).Ask("what should I study").Text);

        [Fact]
        public void MissedRunQuestionsComeBackAsAReview()
        {
            var chat = Chat(3);
            chat.Ask("quiz me on period 3");
            var run = chat.QuizRun!;
            for (var i = 0; i < run.Questions.Count; i++)
            {
                var q = chat.QuizRun!.Current;
                chat.Ask(q.Alternatives.First(a => a != q.CorrectAnswer));
            }
            var scheduled = run.Questions
                .Where(q => q.SubjectKey is not null && q.Category is not null && StudyDeck.Skills.Contains(q.Category))
                .Select(q => StudyDeck.Key(q.Category!, q.SubjectKey!))
                .ToHashSet(StringComparer.Ordinal);
            Assert.NotEmpty(scheduled);
            Assert.Equal(scheduled.OrderBy(k => k, StringComparer.Ordinal), _deck.Snapshot().Keys.OrderBy(k => k, StringComparer.Ordinal));
            Assert.True(_saves >= scheduled.Count);
            Assert.All(_deck.Snapshot().Values, c => Assert.Equal(1, c.Lapses));

            _now += 2 * Fsrs.DayMs;
            var reply = chat.Ask("review");
            Assert.True(chat.QuizRun is not null, reply.Text);
            Assert.Contains("Time to review", reply.Text);
            var asked = chat.QuizRun!.Questions.Select(q => StudyDeck.Key(q.Category!, q.SubjectKey!)).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(scheduled.OrderBy(k => k, StringComparer.Ordinal), asked.OrderBy(k => k, StringComparer.Ordinal));
        }

        [Fact]
        public void ARightReviewAnswerPushesTheCardOut()
        {
            _deck.Record("periodic_group", "gold", false, _now);
            _now += 2 * Fsrs.DayMs;
            var chat = Chat(4);
            chat.Ask("review");
            var question = chat.QuizRun!.Current;
            Assert.Equal("gold", question.SubjectKey);
            chat.Ask(question.CorrectAnswer);
            var card = _deck.Card("periodic_group:gold")!;
            Assert.Equal(2, card.Reps);
            Assert.True(card.Due > _now + Fsrs.DayMs);
            Assert.Empty(_deck.Due(_now));
        }

        [Fact]
        public void ReviewInsideALongerQuestionIsThatQuestion()
        {
            var chat = Chat(5);
            chat.Ask("can you review the trend of electronegativity across period 3 for me please");
            Assert.Null(chat.QuizRun);
        }
    }
}
