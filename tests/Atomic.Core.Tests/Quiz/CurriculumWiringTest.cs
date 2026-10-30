using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Progression;
using Atomic.Core.Quiz;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Quiz
{
    /// <summary>
    /// The curriculum and the generators have to agree.
    ///
    /// <para>This is the test the plan calls for first, and the reason is the 22-to-47 expansion:
    /// a category with no generator is a tile the user can tap that produces an empty game, and
    /// nothing else in the build would say so. Android ships exactly that state - 14 registered
    /// generators against 47 categories - with the gap filled by a <c>when</c> block the compiler
    /// cannot check.</para>
    /// </summary>
    public sealed class CurriculumWiringTest
    {
        private static KnowledgeStore Store() => KnowledgeStore.Build(TestAssets.English);

        private static GeneratorContext Context(string difficulty = "easy", int seed = 12345) =>
            new(Store(), null, new ReswStringProvider("en", fallBackToEnglish: true), difficulty, new Random(seed));

        /// <summary>Every category in the curriculum can actually be played.</summary>
        [Fact]
        public void EveryCategoryHasAGenerator()
        {
            var unwired = FlashcardCatalog.AllCategories
                .Select(c => c.Key)
                .Where(key => QuestionGenerators.ForCategory(key) is null)
                .ToList();

            Assert.Empty(unwired);
        }

        /// <summary>
        /// And nothing is registered that the curriculum never offers - a generator no box lists is
        /// dead code that still has to be maintained.
        /// </summary>
        [Fact]
        public void EveryGeneratorIsReachableFromTheCurriculum()
        {
            var offered = FlashcardCatalog.AllCategories
                .Select(c => c.Key)
                .ToHashSet(StringComparer.Ordinal);

            var orphaned = QuestionGenerators.Keys.Where(k => !offered.Contains(k)).ToList();

            Assert.Empty(orphaned);
        }

        [Fact]
        public void CategoryKeysAreUnique()
        {
            var duplicates = FlashcardCatalog.AllCategories
                .GroupBy(c => c.Key, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.Empty(duplicates);
        }

        /// <summary>
        /// A category built on Tier.Pro fields must be PRO+ in the curriculum, or the quiz hands out
        /// data the AI engine gates - the same values, through a door with no lock on it.
        /// </summary>
        [Fact]
        public void CategoriesRankingProFieldsAreGatedInTheCurriculum()
        {
            var leaks = FlashcardCatalog.AllCategories
                .Where(c => QuestionGenerators.ProFieldKeys.Contains(c.Key) && !c.IsPro)
                .Select(c => c.Key)
                .ToList();

            Assert.Empty(leaks);
        }

        /// <summary>
        /// The level boxes must tile 0-49 without gaps or overlaps: a gap is a level at which the
        /// screen shows nothing, and an overlap puts one category in two boxes.
        /// </summary>
        [Fact]
        public void LevelBoxesTileTheWholeRangeExactlyOnce()
        {
            var covered = new List<int>();
            foreach (var box in FlashcardCatalog.LevelBoxes)
                for (var level = box.Range.First; level <= box.Range.Last; level++)
                    covered.Add(level);

            Assert.Equal(Enumerable.Range(0, 50), covered.OrderBy(l => l));
            Assert.Equal(covered.Count, covered.Distinct().Count());
        }

        /// <summary>Every exam sits at the start of a real box, and covers a widening sweep.</summary>
        [Fact]
        public void EveryExamAnchorsToALevelBox()
        {
            foreach (var exam in FlashcardCatalog.Exams)
            {
                Assert.Contains(FlashcardCatalog.LevelBoxes, b => b.Range.First == exam.UnlockLevel);
                Assert.NotEmpty(FlashcardCatalog.CategoriesForExam(exam));
            }

            var breadths = FlashcardCatalog.Exams
                .Select(e => FlashcardCatalog.CategoriesForExam(e).Count)
                .ToList();

            // Strictly widening: each exam must add something the one before it did not cover, or
            // it is the same test under a new name.
            Assert.Equal(breadths.OrderBy(b => b), breadths);
            Assert.True(breadths.Distinct().Count() == breadths.Count, "two exams cover the same categories");
        }

        /// <summary>
        /// Every exam is PRO+, because each mixes in categories that are PRO+ on their own. A free
        /// exam would hand out paid content by the back door.
        /// </summary>
        [Fact]
        public void EveryExamThatMixesInProCategoriesIsItselfPro()
        {
            foreach (var exam in FlashcardCatalog.Exams)
            {
                var mixesPro = FlashcardCatalog.CategoriesForExam(exam).Any(c => c.IsPro);
                if (mixesPro) Assert.True(exam.IsPro, exam.Key);
            }
        }

        /// <summary>
        /// The point of the whole phase: every category must actually produce questions from the
        /// shipped data.
        ///
        /// <para>Wiring alone is not enough - a generator can be registered, compile, and still
        /// return nothing because its field is too sparse or its pool degenerate. That is
        /// indistinguishable from an unwired category to the user, so it is asserted here rather
        /// than discovered by tapping 47 tiles.</para>
        /// </summary>
        [Fact]
        public void EveryCategoryProducesQuestionsFromTheShippedData()
        {
            var context = Context();
            var barren = new List<string>();

            foreach (var category in FlashcardCatalog.AllCategories)
            {
                var generator = QuestionGenerators.ForCategory(category.Key);
                if (generator is null) continue;

                var questions = generator.Generate(context, 5);
                if (questions.Count == 0) barren.Add(category.Key);
            }

            Assert.Empty(barren);
        }

        /// <summary>
        /// A question with a duplicate option, or whose correct answer is not among its options, is
        /// unanswerable. Checked across every category and all three difficulties, because the
        /// difficulty changes the fairness margins and so which draws survive.
        /// </summary>
        [Theory]
        [InlineData("easy")]
        [InlineData("medium")]
        [InlineData("hard")]
        public void NoCategoryProducesAnUnanswerableQuestion(string difficulty)
        {
            var context = Context(difficulty);
            var problems = new List<string>();

            foreach (var category in FlashcardCatalog.AllCategories)
            {
                var generator = QuestionGenerators.ForCategory(category.Key);
                if (generator is null) continue;

                foreach (var question in generator.Generate(context, 8))
                {
                    if (string.IsNullOrWhiteSpace(question.Question))
                        problems.Add($"{category.Key}: blank question text");

                    if (!question.Alternatives.Contains(question.CorrectAnswer, StringComparer.Ordinal))
                        problems.Add($"{category.Key}: correct answer '{question.CorrectAnswer}' is not an option");

                    if (question.Alternatives.Distinct(StringComparer.Ordinal).Count() != question.Alternatives.Count)
                        problems.Add($"{category.Key}: duplicate option in [{string.Join(", ", question.Alternatives)}]");

                    if (question.Alternatives.Any(string.IsNullOrWhiteSpace))
                        problems.Add($"{category.Key}: blank option");

                    if (question.BaseXp <= 0)
                        problems.Add($"{category.Key}: non-positive XP");
                }
            }

            Assert.Empty(problems);
        }

        /// <summary>
        /// No question may leak a missing-value sentinel or an unresolved resource key.
        ///
        /// <para>The element JSON uses <c>---</c>, <c>N/A</c> and empty strings for absent values,
        /// and a field-lookup generator that offered one as an answer would be asking the player to
        /// pick the element the app knows least about.</para>
        /// </summary>
        [Fact]
        public void NoQuestionShowsASentinelOrAnUnresolvedString()
        {
            var context = Context();
            var leaks = new List<string>();

            foreach (var category in FlashcardCatalog.AllCategories)
            {
                var generator = QuestionGenerators.ForCategory(category.Key);
                if (generator is null) continue;

                foreach (var question in generator.Generate(context, 8))
                {
                    foreach (var text in question.Alternatives.Append(question.Question))
                    {
                        if (text.Contains("---", StringComparison.Ordinal) ||
                            text.Contains("N/A", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains(ReswStringProvider.MissingPrefix, StringComparison.Ordinal))
                        {
                            leaks.Add($"{category.Key}: '{text}'");
                        }
                    }
                }
            }

            Assert.Empty(leaks);
        }
    }
}
