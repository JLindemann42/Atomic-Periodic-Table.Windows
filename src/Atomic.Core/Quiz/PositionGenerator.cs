using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Localization;

namespace Atomic.Core.Quiz
{
    /// <summary>
    /// What is being asked about an element's place in the table.
    ///
    /// <para>All three are derived from atomic number rather than read from the JSON - the element
    /// files carry no period, group or valence keys at all.</para>
    /// </summary>
    public enum PositionAspect
    {
        Period,

        /// <summary>Null across the f-block, where the concept does not apply.</summary>
        Group,

        /// <summary>Null across the d- and f-blocks, where inner shells are still filling.</summary>
        Valence,
    }

    public static class PositionAspects
    {
        public static string QuestionKey(this PositionAspect aspect) => aspect switch
        {
            PositionAspect.Period => S.question_period_of,
            PositionAspect.Group => S.question_group_of,
            PositionAspect.Valence => S.question_valence_electrons,
            _ => throw new System.Diagnostics.UnreachableException(),
        };

        public static int? ValueOf(this PositionAspect aspect, ElementRecord element) => aspect switch
        {
            PositionAspect.Period => element.Period,
            PositionAspect.Group => element.GroupNumber,
            PositionAspect.Valence => FieldRegistry.ValenceElectrons(element.AtomicNumber),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
    }

    /// <summary>
    /// "Which period is titanium in?" - small integer answers, so no per-field wording is needed.
    /// </summary>
    public sealed class PositionGenerator : IQuestionGenerator
    {
        /// <summary>How many near values to draw the three wrong options from.</summary>
        private const int NearMissPool = 6;

        private readonly PositionAspect _aspect;

        public PositionGenerator(string categoryKey, int baseXp, PositionAspect aspect)
        {
            CategoryKey = categoryKey;
            BaseXp = baseXp;
            _aspect = aspect;
        }

        public string CategoryKey { get; }

        public int BaseXp { get; }

        public IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count)
        {
            var all = context.Store.Elements
                .Select(e => (Element: e, Value: _aspect.ValueOf(e)))
                .Where(p => p.Value is not null)
                .Select(p => (p.Element, Value: p.Value!.Value))
                .ToList();
            var pool = all.Where(p => context.InScope(p.Element)).ToList();

            var values = all.Select(p => p.Value).Distinct().OrderBy(v => v).ToList();
            if (pool.Count == 0 || values.Count < 2) return [];

            var questions = new List<QuizQuestion>(count);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var attempts = count * 8;

            while (questions.Count < count && attempts-- > 0)
            {
                var (element, correct) = pool.Pick(context.Random);
                if (!used.Add(element.Key)) continue;

                // Near misses make a better question than three values from the far end of the
                // table.
                var wrongs = values
                    .Where(v => v != correct)
                    .OrderBy(v => Math.Abs(v - correct))
                    .Take(NearMissPool)
                    .Shuffled(context.Random)
                    .Take(3)
                    .ToList();
                if (wrongs.Count == 0) continue;

                var options = wrongs.Append(correct).Select(Text).ToList();

                questions.Add(new QuizQuestion(
                    Question: context.String(_aspect.QuestionKey(), context.Name(element)),
                    CorrectAnswer: Text(correct),
                    Alternatives: options.Shuffled(context.Random),
                    BaseXp: BaseXp,
                    SubjectKey: element.Key,
                    Category: CategoryKey));
            }

            return questions;
        }

        /// <summary>
        /// Invariant, deliberately: these are the answer strings, and the correct one is matched by
        /// text. A culture that shapes digits differently between two calls would make the right
        /// answer unselectable.
        /// </summary>
        private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
