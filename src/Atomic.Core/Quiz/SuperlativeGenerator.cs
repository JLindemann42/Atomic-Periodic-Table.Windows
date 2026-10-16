using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Quiz
{
    /// <summary>
    /// A numeric field a superlative question can be asked about, with its own authored wording.
    ///
    /// <para>The wording is per field rather than a generic frame with the field name substituted
    /// in. <c>FieldSpec.LabelKey</c> values are table headings - several end in a colon and carry
    /// units - and dropping one into "Which of these has the highest %s?" reads badly in German
    /// compounds and in Hindi/Urdu word order.</para>
    /// </summary>
    public sealed record RankableField(string FieldId, string HighestKey, string LowestKey);

    /// <summary>
    /// "Which of these has the highest density?"
    ///
    /// <para>Asks which of the <i>drawn</i> elements is the extreme, not which element is the
    /// extreme overall. The latter has exactly one answer per field, so a player memorises the whole
    /// category in a sitting; drawing a fresh set each time keeps the pool effectively
    /// unlimited.</para>
    ///
    /// <para>Also serves the two-option pairwise game - the answer grid hides cards beyond
    /// <c>Alternatives.Count</c>, so the only difference is <paramref name="optionCount"/>.</para>
    /// </summary>
    public sealed class SuperlativeGenerator : IQuestionGenerator
    {
        private readonly IReadOnlyList<RankableField> _fields;
        private readonly bool? _descending;
        private readonly int _optionCount;

        /// <param name="descending">
        /// True for always "highest", false for always "lowest", null to pick per question.
        /// </param>
        public SuperlativeGenerator(
            string categoryKey,
            int baseXp,
            IReadOnlyList<RankableField> fields,
            bool? descending,
            int optionCount = 4)
        {
            CategoryKey = categoryKey;
            BaseXp = baseXp;
            _fields = fields;
            _descending = descending;
            _optionCount = optionCount;
        }

        public string CategoryKey { get; }

        public int BaseXp { get; }

        public IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count)
        {
            var questions = new List<QuizQuestion>(count);
            var asked = new HashSet<string>(StringComparer.Ordinal);
            var margin = SeparationMargin(context.Difficulty);

            // Bounded, because several of the guards below can reject every draw for a field whose
            // pool is degenerate - an unbounded loop would hang the game rather than short it.
            var attempts = count * 12;

            while (questions.Count < count && attempts-- > 0)
            {
                var field = _fields.Pick(context.Random);
                var pool = context.RankableFor(field.FieldId);
                if (pool.Count < _optionCount) continue;

                var draw = pool.Shuffled(context.Random).Take(_optionCount).ToList();
                var wantHighest = _descending ?? context.Random.Next(2) == 0;

                var ordered = draw.OrderByDescending(p => p.Value.Mid).ToList();
                if (!wantHighest) ordered.Reverse();

                // A near-tie is not a fair question: nobody can be expected to separate 7.85 from
                // 7.87.
                if (!ClearlyApart(ordered[0].Value.Mid, ordered[1].Value.Mid, margin)) continue;

                var options = draw.Select(p => context.Name(p.Element)).ToList();
                // Two cards showing the same text would make one of them wrongly count as incorrect.
                if (options.Distinct(StringComparer.Ordinal).Count() != options.Count) continue;

                var text = context.String(wantHighest ? field.HighestKey : field.LowestKey);
                if (!asked.Add(text + "|" + string.Join("|", options.OrderBy(o => o, StringComparer.Ordinal))))
                    continue;

                questions.Add(new QuizQuestion(
                    Question: text,
                    CorrectAnswer: context.Name(ordered[0].Element),
                    Alternatives: options.Shuffled(context.Random),
                    BaseXp: BaseXp));
            }

            return questions;
        }

        private static bool ClearlyApart(double winner, double runnerUp, double margin)
        {
            var scale = Math.Max(Math.Abs(winner), Math.Abs(runnerUp));
            if (scale == 0.0) return false;
            return Math.Abs(winner - runnerUp) / scale >= margin;
        }

        /// <summary>Harder difficulties allow the top two to sit closer together.</summary>
        private static double SeparationMargin(string difficulty) => difficulty switch
        {
            "hard" => 0.05,
            "medium" => 0.12,
            _ => 0.25,
        };
    }
}
