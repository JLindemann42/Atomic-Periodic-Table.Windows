using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Localization;

namespace Atomic.Core.Quiz
{
    internal static class SeriesGrouping
    {
        /// <summary>
        /// Elements grouped by the <i>text</i> of their series label, with Unknown dropped.
        ///
        /// <para>Grouping by label rather than by <see cref="SeriesId"/> is what makes both
        /// generators below correct: transition and post-transition metals share one label, as do
        /// the two nonmetal series, so an "odd one out" drawn across that boundary would not
        /// actually be odd. Unknown is excluded because it also resolves to "Nonmetal", which would
        /// present an unclassified element as a nonmetal.</para>
        /// </summary>
        public static Dictionary<string, List<ElementRecord>> ByLabel(GeneratorContext context) =>
            context.Store.Elements
                .Where(e => e.Series != SeriesId.Unknown)
                .GroupBy(e => context.SeriesLabel(e.Series), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    /// <summary>
    /// "What group does gold belong to?"
    ///
    /// <para>Replaces the raw-JSON version of this game. <c>element_group</c> is authored
    /// inconsistently - "Other Nonmetal" vs "Other Nonmetals", "Post-transition Metals" vs
    /// "Post-Transition Metals", "Alkaline earth metal" - and the old generator offered those
    /// spellings as separate options, so a question could carry two answers that meant the same
    /// thing. Reading the canonical <see cref="SeriesId"/> instead removes the whole class of
    /// defect.</para>
    /// </summary>
    public sealed class ClassificationGenerator : IQuestionGenerator
    {
        public ClassificationGenerator(string categoryKey, int baseXp)
        {
            CategoryKey = categoryKey;
            BaseXp = baseXp;
        }

        public string CategoryKey { get; }

        public int BaseXp { get; }

        public IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count)
        {
            var byLabel = SeriesGrouping.ByLabel(context);
            if (byLabel.Count < 2) return [];

            var labels = byLabel.Keys.ToList();
            var pool = byLabel.Values.SelectMany(v => v).Where(context.InScope).ToList();
            if (pool.Count == 0) return [];

            var questions = new List<QuizQuestion>(count);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var attempts = count * 8;

            while (questions.Count < count && attempts-- > 0)
            {
                var element = pool.Pick(context.Random);
                if (!used.Add(element.Key)) continue;

                var correct = context.SeriesLabel(element.Series);
                var wrongs = labels
                    .Where(l => !string.Equals(l, correct, StringComparison.Ordinal))
                    .Shuffled(context.Random)
                    .Take(3)
                    .ToList();
                if (wrongs.Count == 0) continue;

                questions.Add(new QuizQuestion(
                    Question: context.String(S.question_element_group, context.Name(element)),
                    CorrectAnswer: correct,
                    Alternatives: wrongs.Append(correct).Shuffled(context.Random),
                    BaseXp: BaseXp));
            }

            return questions;
        }
    }

    /// <summary>"Which of these is NOT a noble gas?" - three from one family, one from another.</summary>
    public sealed class OddOneOutGenerator : IQuestionGenerator
    {
        private readonly int _optionCount;

        public OddOneOutGenerator(string categoryKey, int baseXp, int optionCount = 4)
        {
            CategoryKey = categoryKey;
            BaseXp = baseXp;
            _optionCount = optionCount;
        }

        public string CategoryKey { get; }

        public int BaseXp { get; }

        public IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count)
        {
            var byLabel = SeriesGrouping.ByLabel(context);
            var families = byLabel
                .Where(e => e.Value.Count >= _optionCount - 1)
                .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
            if (families.Count < 2) return [];

            var outsiders = byLabel.Keys.ToDictionary(
                label => label,
                label => byLabel
                    .Where(e => !string.Equals(e.Key, label, StringComparison.Ordinal))
                    .SelectMany(e => e.Value)
                    .ToList(),
                StringComparer.Ordinal);

            var familyLabels = families.Keys.ToList();
            var questions = new List<QuizQuestion>(count);
            var asked = new HashSet<string>(StringComparer.Ordinal);
            var attempts = count * 12;

            while (questions.Count < count && attempts-- > 0)
            {
                var label = familyLabels.Pick(context.Random);
                if (!outsiders.TryGetValue(label, out var pool) || pool.Count == 0) continue;

                var insiders = families[label].Shuffled(context.Random).Take(_optionCount - 1);
                var odd = pool.Pick(context.Random);

                var options = insiders.Append(odd).Select(context.Name).ToList();
                if (options.Distinct(StringComparer.Ordinal).Count() != options.Count) continue;

                var text = context.String(S.question_not_a_series, label);
                if (!asked.Add(text + "|" + string.Join("|", options.OrderBy(o => o, StringComparer.Ordinal))))
                    continue;

                questions.Add(new QuizQuestion(
                    text, context.Name(odd), options.Shuffled(context.Random), BaseXp));
            }

            return questions;
        }
    }
}
