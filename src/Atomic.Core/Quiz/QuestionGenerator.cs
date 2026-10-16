using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Localization;

namespace Atomic.Core.Quiz
{
    /// <summary>
    /// One generated multiple-choice question.
    ///
    /// <para>Deliberately separate from the page's own question type: generators are a data concern
    /// and must not depend on a page. The page maps between the two.</para>
    /// </summary>
    public sealed record QuizQuestion(
        string Question,
        string CorrectAnswer,
        IReadOnlyList<string> Alternatives,
        int BaseXp);

    /// <summary>
    /// Everything a generator needs, built once per game.
    ///
    /// <para><see cref="Store"/> is the typed element index from the AI data layer. The old
    /// flashcard screen kept every value as a raw string, so <c>"8.0 × 10^4"</c> and
    /// <c>"160-350 (MPa)"</c> were not comparable at all; questions that rank or compare are only
    /// possible on top of parsed <see cref="Quantity"/>s.</para>
    /// </summary>
    public sealed class GeneratorContext
    {
        private readonly LocalizedView? _names;
        private readonly IStringProvider _strings;
        private readonly Dictionary<string, IReadOnlyList<(ElementRecord Element, Quantity Value)>> _rankable =
            new(StringComparer.Ordinal);

        public GeneratorContext(
            KnowledgeStore store,
            LocalizedView? names,
            IStringProvider strings,
            string difficulty,
            Random? random = null)
        {
            Store = store;
            _names = names;
            _strings = strings;
            Difficulty = difficulty;
            Random = random ?? new Random();
        }

        public KnowledgeStore Store { get; }

        public string Difficulty { get; }

        public Random Random { get; }

        /// <summary>The element's name in the user's language, falling back to the English key.</summary>
        public string Name(ElementRecord element)
        {
            var localized = _names?.Name(element.Key);
            if (!string.IsNullOrWhiteSpace(localized)) return localized;

            return element.Key.Length == 0
                ? element.Key
                : char.ToUpperInvariant(element.Key[0]) + element.Key[1..];
        }

        public string String(string key, params object?[] args) => _strings.Get(key, args);

        /// <summary>
        /// The localized name of an element series.
        ///
        /// <para>Several <see cref="SeriesId"/>s deliberately share a label - transition and
        /// post-transition metals are both "Transition Metal", and both nonmetal series plus
        /// Unknown are all "Nonmetal". Group by this resolved text rather than by the enum, or a
        /// question can end up with two right answers.</para>
        /// </summary>
        public string SeriesLabel(SeriesId series) => _strings.Get(series.LabelKey());

        /// <summary>
        /// Elements carrying a comparable value for a field, memoised for the life of the game.
        ///
        /// <para>Ranges are dropped rather than ranked by midpoint: "which has the higher hardness"
        /// is not a fair question when one candidate is recorded as <c>160-350</c>.</para>
        /// </summary>
        public IReadOnlyList<(ElementRecord Element, Quantity Value)> RankableFor(string fieldId)
        {
            if (_rankable.TryGetValue(fieldId, out var cached)) return cached;

            var pool = Store.Elements
                .Select(e => (Element: e, Value: e.Quantity(fieldId)))
                .Where(p => p.Value is { IsRange: false })
                .Select(p => (p.Element, Value: p.Value!))
                .ToList();

            return _rankable[fieldId] = pool;
        }
    }

    /// <summary>Builds the questions for one flashcard category.</summary>
    public interface IQuestionGenerator
    {
        string CategoryKey { get; }

        int BaseXp { get; }

        IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count);
    }

    /// <summary>Shuffling and drawing, shared by every generator.</summary>
    internal static class RandomExtensions
    {
        /// <summary>Fisher-Yates. The order must depend only on the supplied <see cref="Random"/>.</summary>
        public static List<T> Shuffled<T>(this IEnumerable<T> source, Random random)
        {
            var items = source.ToList();
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
            return items;
        }

        public static T Pick<T>(this IReadOnlyList<T> source, Random random) =>
            source[random.Next(source.Count)];
    }
}
