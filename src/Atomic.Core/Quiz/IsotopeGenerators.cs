using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Localization;

namespace Atomic.Core.Quiz
{
    /// <summary>An isotope together with the label the quiz shows for it.</summary>
    internal sealed record Nuclide(string Label, Isotope Isotope)
    {
        public int? Neutrons => Isotope.Neutrons;

        public bool Stable => Isotope.Stable;

        public double? HalfLifeSeconds => Isotope.HalfLifeSeconds;

        public string? DecayType => Isotope.DecayType;
    }

    internal static class IsotopePool
    {
        /// <summary>
        /// Every usable isotope in the corpus, labelled as symbol-mass.
        ///
        /// <para>The authored <c>iso_N</c> names cannot be shown as-is. 571 of 3,111 are the full
        /// English element name ("Actinium-203"), which would put English in the middle of a Swedish
        /// quiz; a few are reversed ("127-Sb"); and ten names appear twice, which would make an
        /// answer ambiguous. Rebuilding the label from the element's symbol and
        /// <see cref="Isotope.Nucleons"/> fixes all three at once and is the standard notation
        /// anyway.</para>
        ///
        /// <para><c>Isotope.MassNumber</c> is digit-filtered out of the mass string and is not the
        /// mass number, so <see cref="Isotope.Nucleons"/> (<c>iso_A_N</c>) is used. Rows whose
        /// Z/N/A disagree are dropped.</para>
        /// </summary>
        public static IReadOnlyList<Nuclide> Of(GeneratorContext context) =>
            context.Store.Elements
                .Where(context.InScope)
                .SelectMany(element => element.Isotopes.Select(iso => (element, iso)))
                .Select(pair =>
                {
                    var (element, iso) = pair;
                    if (iso.Protons is not { } protons) return null;
                    if (iso.Neutrons is not { } neutrons) return null;
                    if (iso.Nucleons is not { } nucleons) return null;
                    if (protons + neutrons != nucleons) return null;
                    if (protons != element.AtomicNumber) return null;

                    return new Nuclide(
                        $"{element.Symbol}-{nucleons.ToString(CultureInfo.InvariantCulture)}", iso);
                })
                .Where(n => n is not null)
                .Select(n => n!)
                // A label that resolves to two records would make the question unanswerable.
                .GroupBy(n => n.Label, StringComparer.Ordinal)
                .Where(g => g.Count() == 1)
                .Select(g => g.Single())
                .ToList();
    }

    /// <summary>"How many neutrons does C-14 have?"</summary>
    public sealed class IsotopeNeutronGenerator : IQuestionGenerator
    {
        public IsotopeNeutronGenerator(string categoryKey, int baseXp)
        {
            CategoryKey = categoryKey;
            BaseXp = baseXp;
        }

        public string CategoryKey { get; }

        public int BaseXp { get; }

        public IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count)
        {
            var pool = IsotopePool.Of(context);
            if (pool.Count < 4) return [];

            var questions = new List<QuizQuestion>(count);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var attempts = count * 8;

            while (questions.Count < count && attempts-- > 0)
            {
                var nuclide = pool.Pick(context.Random);
                if (!used.Add(nuclide.Label)) continue;
                if (nuclide.Neutrons is not { } correct) continue;

                // Near misses: a plausible question is one where counting actually matters.
                var wrongs = Enumerable.Range(correct - 4, 9)
                    .Where(n => n >= 0 && n != correct)
                    .Shuffled(context.Random)
                    .Take(3)
                    .ToList();
                if (wrongs.Count < 3) continue;

                questions.Add(new QuizQuestion(
                    Question: context.String(S.question_isotope_neutrons, nuclide.Label),
                    CorrectAnswer: Text(correct),
                    Alternatives: wrongs.Append(correct).Select(Text).Shuffled(context.Random),
                    BaseXp: BaseXp));
            }

            return questions;
        }

        private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>"Which of these isotopes is stable?" - one stable nuclide against three that decay.</summary>
    public sealed class IsotopeStabilityGenerator : IQuestionGenerator
    {
        public IsotopeStabilityGenerator(string categoryKey, int baseXp)
        {
            CategoryKey = categoryKey;
            BaseXp = baseXp;
        }

        public string CategoryKey { get; }

        public int BaseXp { get; }

        public IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count)
        {
            var pool = IsotopePool.Of(context);
            var stable = pool.Where(n => n.Stable).ToList();
            var unstable = pool.Where(n => !n.Stable).ToList();
            if (stable.Count == 0 || unstable.Count < 3) return [];

            var questions = new List<QuizQuestion>(count);
            var asked = new HashSet<string>(StringComparer.Ordinal);
            var attempts = count * 10;

            while (questions.Count < count && attempts-- > 0)
            {
                var correct = stable.Pick(context.Random);
                var others = unstable.Shuffled(context.Random).Take(3);
                var options = others.Append(correct).Select(n => n.Label).ToList();

                if (options.Distinct(StringComparer.Ordinal).Count() != options.Count) continue;
                if (!asked.Add(string.Join("|", options.OrderBy(o => o, StringComparer.Ordinal))))
                    continue;

                questions.Add(new QuizQuestion(
                    Question: context.String(S.question_isotope_stable),
                    CorrectAnswer: correct.Label,
                    Alternatives: options.Shuffled(context.Random),
                    BaseXp: BaseXp));
            }

            return questions;
        }
    }

    /// <summary>"Which of these isotopes has the longest half-life?"</summary>
    public sealed class IsotopeHalfLifeGenerator : IQuestionGenerator
    {
        /// <summary>
        /// How many times longer the winner must be.
        ///
        /// <para>Half-lives span forty orders of magnitude, so a factor of two is a wide enough gap
        /// to be fair while still leaving most draws usable.</para>
        /// </summary>
        private const double MinRatio = 2.0;

        public IsotopeHalfLifeGenerator(string categoryKey, int baseXp)
        {
            CategoryKey = categoryKey;
            BaseXp = baseXp;
        }

        public string CategoryKey { get; }

        public int BaseXp { get; }

        public IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count)
        {
            var pool = IsotopePool.Of(context)
                .Where(n => !n.Stable && n.HalfLifeSeconds is > 0.0)
                .ToList();
            if (pool.Count < 4) return [];

            var questions = new List<QuizQuestion>(count);
            var asked = new HashSet<string>(StringComparer.Ordinal);
            var attempts = count * 12;

            while (questions.Count < count && attempts-- > 0)
            {
                var draw = pool.Shuffled(context.Random).Take(4).ToList();
                var longest = context.Random.Next(2) == 0;

                var ordered = draw.OrderByDescending(n => n.HalfLifeSeconds!.Value).ToList();
                if (!longest) ordered.Reverse();

                var winner = ordered[0].HalfLifeSeconds!.Value;
                var runnerUp = ordered[1].HalfLifeSeconds!.Value;
                var ratio = longest ? winner / runnerUp : runnerUp / winner;
                if (ratio < MinRatio) continue;

                var options = draw.Select(n => n.Label).ToList();
                if (options.Distinct(StringComparer.Ordinal).Count() != options.Count) continue;

                var text = context.String(longest
                    ? S.question_isotope_longest_half_life
                    : S.question_isotope_shortest_half_life);

                if (!asked.Add(text + "|" + string.Join("|", options.OrderBy(o => o, StringComparer.Ordinal))))
                    continue;

                questions.Add(new QuizQuestion(
                    text, ordered[0].Label, options.Shuffled(context.Random), BaseXp));
            }

            return questions;
        }
    }

    /// <summary>"How does C-14 decay?"</summary>
    public sealed class IsotopeDecayGenerator : IQuestionGenerator
    {
        public IsotopeDecayGenerator(string categoryKey, int baseXp)
        {
            CategoryKey = categoryKey;
            BaseXp = baseXp;
        }

        public string CategoryKey { get; }

        public int BaseXp { get; }

        public IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count)
        {
            var pool = IsotopePool.Of(context)
                .Select(n => (Nuclide: n, Mode: DecayModes.Of(n.DecayType)))
                .Where(p => p.Mode is not null)
                .Select(p => (p.Nuclide, Mode: p.Mode!.Value))
                .ToList();
            if (pool.Count == 0) return [];

            var allModes = Enum.GetValues<DecayMode>();
            var questions = new List<QuizQuestion>(count);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var attempts = count * 10;

            while (questions.Count < count && attempts-- > 0)
            {
                var (nuclide, mode) = pool.Pick(context.Random);
                if (!used.Add(nuclide.Label)) continue;

                // Checked against every option already chosen, not just the correct one: beta-plus
                // and electron capture must not meet even when neither of them is the answer.
                var wrongs = new List<DecayMode>(3);
                foreach (var candidate in allModes.Shuffled(context.Random))
                {
                    if (wrongs.Count == 3) break;
                    if (candidate == mode) continue;
                    if (wrongs.Append(mode).Any(chosen => DecayModes.Confusable(chosen, candidate)))
                        continue;
                    wrongs.Add(candidate);
                }
                if (wrongs.Count < 3) continue;

                var options = wrongs.Append(mode).Select(m => context.String(m.LabelKey())).ToList();
                if (options.Distinct(StringComparer.Ordinal).Count() != options.Count) continue;

                questions.Add(new QuizQuestion(
                    Question: context.String(S.question_isotope_decay, nuclide.Label),
                    CorrectAnswer: context.String(mode.LabelKey()),
                    Alternatives: options.Shuffled(context.Random),
                    BaseXp: BaseXp));
            }

            return questions;
        }
    }
}
