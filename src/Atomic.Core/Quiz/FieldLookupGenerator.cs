using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Data;

namespace Atomic.Core.Quiz
{
    /// <summary>
    /// "What is the density of gold?" - one element's value for one field, against three other
    /// elements' values for the same field.
    ///
    /// <para><b>This is the improvement over Android.</b> There, only 14 of the 47 categories have a
    /// generator; the other 33 run a ~300-line <c>when</c> block inside the quiz activity that
    /// reads raw JSON strings. This class serves all of them from
    /// <see cref="FieldRegistry"/>, which already knows each field's label, unit, tier and
    /// formatting - so a new field becomes a new category with one registry line rather than
    /// another branch.</para>
    ///
    /// <para>Distractors are always <i>real values other elements have</i>, never invented numbers.
    /// A fabricated distractor can accidentally be correct - two elements genuinely share a phase,
    /// a block, an NFPA rating - and it can also be absurd in a way that gives the answer away.</para>
    /// </summary>
    public sealed class FieldLookupGenerator : IQuestionGenerator
    {
        /// <summary>Most questions show four cards; a closed vocabulary may offer fewer.</summary>
        private const int PreferredOptions = 4;

        /// <summary>Below this there is no question left to ask.</summary>
        private const int MinimumOptions = 2;

        private readonly string _fieldId;
        private readonly string _questionKey;
        private readonly string? _unit;

        /// <param name="unit">
        /// The unit to ask in, when it differs from the field's canonical one. Melting and boiling
        /// point are single multi-key fields carrying all three temperature scales, so asking "in
        /// °C" is a conversion rather than a different field.
        /// </param>
        public FieldLookupGenerator(
            string categoryKey, int baseXp, string fieldId, string questionKey, string? unit = null)
        {
            CategoryKey = categoryKey;
            BaseXp = baseXp;
            _fieldId = fieldId;
            _questionKey = questionKey;
            _unit = unit;
        }

        public string CategoryKey { get; }

        public int BaseXp { get; }

        public IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count)
        {
            // Displayed text, not the parsed value: the answer is matched by string, and two
            // elements with the same display must collapse to one option rather than to two cards
            // where picking the "wrong" identical one is marked incorrect.
            var pool = context.Store.Elements
                .Select(e => (Element: e, Display: Display(e)))
                .Where(p => p.Display is not null)
                .Select(p => (p.Element, Display: p.Display!))
                .ToList();

            var distinctValues = pool
                .Select(p => p.Display)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // A closed vocabulary is allowed to offer fewer cards rather than none. Phase has
            // three values in the whole table and radioactivity has two, so demanding four options
            // would silently drop three legitimate categories - which is exactly the failure the
            // curriculum wiring test exists to catch.
            var optionCount = Math.Min(PreferredOptions, distinctValues.Count);
            if (pool.Count < MinimumOptions || optionCount < MinimumOptions) return [];

            var questions = new List<QuizQuestion>(count);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var attempts = count * 8;

            while (questions.Count < count && attempts-- > 0)
            {
                var (element, correct) = pool.Pick(context.Random);
                if (!used.Add(element.Key)) continue;

                var wrongs = distinctValues
                    .Where(v => !string.Equals(v, correct, StringComparison.Ordinal))
                    .Shuffled(context.Random)
                    .Take(optionCount - 1)
                    .ToList();
                if (wrongs.Count < optionCount - 1) continue;

                questions.Add(new QuizQuestion(
                    Question: context.String(_questionKey, context.Name(element)),
                    CorrectAnswer: correct,
                    Alternatives: wrongs.Append(correct).Shuffled(context.Random),
                    BaseXp: BaseXp));
            }

            return questions;
        }

        /// <summary>
        /// The element's value for this field as the quiz shows it, or null when it has none.
        /// </summary>
        private string? Display(ElementRecord element)
        {
            var value = element.Value(_fieldId);
            if (value.IsMissing) return null;

            var text = value switch
            {
                FieldValue.Enum e => e.Localized,
                FieldValue.Text t => t.Raw,
                // The authored display, not a reformat of the parsed number: it already carries the
                // significant figures and any unit the source attached, and re-rendering would make
                // two elements that agree in the data disagree on screen. The exception is a
                // requested unit, which has to be computed.
                FieldValue.Num n => _unit is null ? n.Quantity.Display : Converted(n.Quantity),
                // Struct and Trace are deliberately not asked about. A lattice triple has no single
                // answer, and "trace" is the same string for every element that has it.
                _ => null,
            };

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        /// <summary>
        /// The value in the unit this category asks about, or null when it cannot be converted.
        /// </summary>
        /// <remarks>
        /// Rounded to a whole degree. The three temperature scales are asked as separate categories,
        /// and a converted Fahrenheit value carries false precision - 1337.33 K is "1947.49 °F",
        /// which claims two decimals the source never had.
        /// </remarks>
        private string? Converted(Quantity quantity)
        {
            if (quantity.IsRange) return null;

            var value = UnitConverter.Convert(quantity.Value, quantity.Unit ?? "K", _unit);
            if (value is not { } converted || double.IsNaN(converted)) return null;

            return Math.Round(converted).ToString("0", CultureInfo.InvariantCulture) + " " + _unit;
        }
    }
}
