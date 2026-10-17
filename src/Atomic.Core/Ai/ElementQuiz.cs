using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// "Quiz me" - one question about an element, then a verdict on the answer.
    ///
    /// <para>In <c>must_defer.tsv</c> in five languages ("quiz me", "quiz", "quiz me again",
    /// "give me a quiz question", "quiz mich"), so the engine declines it and this answers it.
    /// Android does the same in <c>handleQuizQuery</c>; there was no Windows equivalent.</para>
    ///
    /// <para>Unlike every other handler here this one <b>spans two turns</b>: it asks, and the
    /// next message is the answer. The outstanding question lives in
    /// <see cref="Engine.DialogueState.PendingQuizAnswer"/> rather than in a field here, so it is
    /// part of what a host persists and survives the panel closing.</para>
    /// </summary>
    public static class ElementQuiz
    {
        private static readonly string[] QuizWords =
        [
            "quiz", "question", "test", "fragesport", "tavling", "ratsel", "examen",
            "प्रश्नोत्तरी", "测验", "ceyan", "sawal", "toets", "pagsusulit", "tanong",
            "preguntas", "pregunta", "juego", "domanda", "quesito", "interrogazione",
            "frage", "prufung", "testa mig", "interroger", "questionnaire", "concours",
            "questao", "desafio", "testar", "fraga", "सवाल", "امتحان", "vrae", "vraag", "toetsie",
        ];

        /// <summary>
        /// How many words a reply can have and still be read as an answer rather than a new
        /// question. Android's number.
        /// </summary>
        private const int MaxAnswerWords = 4;

        /// <summary>Whether this asks for a quiz.</summary>
        /// <remarks>Substring, as Android has it, so "frågesport" and "questions" both match.</remarks>
        public static bool Asks(string? normalizedQuery) =>
            !string.IsNullOrWhiteSpace(normalizedQuery)
            && QuizWords.Any(w => normalizedQuery.Contains(w, StringComparison.Ordinal));

        /// <summary>
        /// Whether <paramref name="normalizedQuery"/> should be read as an answer to the question
        /// already outstanding, rather than as a request for a new one.
        /// </summary>
        public static bool LooksLikeAnAnswer(string? normalizedQuery, string? pending) =>
            pending is not null
            && !string.IsNullOrWhiteSpace(normalizedQuery)
            && TextMatching.SplitQueryTokens(normalizedQuery).Count(t => t.Trim().Length > 0) <= MaxAnswerWords;

        /// <summary>
        /// Judge a reply against the outstanding question. Clears it either way: one question,
        /// one verdict, and both messages end by offering another.
        /// </summary>
        /// <param name="guessedKey">The element the reply named, if any.</param>
        public static string Judge(
            string normalizedQuery,
            string pending,
            string? guessedKey,
            string pendingDisplayName,
            IStringProvider strings)
        {
            var correct =
                normalizedQuery.Contains(pending, StringComparison.OrdinalIgnoreCase)
                || string.Equals(guessedKey, pending, StringComparison.OrdinalIgnoreCase);

            return strings.Get(correct ? S.ai_quiz_correct : S.ai_quiz_wrong, pendingDisplayName);
        }

        /// <summary>One question, or null when the data cannot supply one.</summary>
        /// <param name="nameOf">Localizes an element's name for display.</param>
        /// <returns>The question text and the element it is about.</returns>
        public static (string Text, ElementRecord About)? Ask(
            IReadOnlyList<ElementRecord> all,
            Random random,
            IStringProvider strings,
            Func<ElementRecord, string> nameOf)
        {
            if (all.Count == 0) return null;

            // Three shapes, as Android has them. The history question needs a discoverer and a
            // year, and roughly a fifth of elements have neither, so the candidates are filtered
            // rather than asked and abandoned - Android re-rolls recursively, which can in
            // principle not terminate.
            var shape = random.Next(3);

            if (shape == 2)
            {
                var withHistory = all.Where(HasHistory).ToList();
                if (withHistory.Count == 0) shape = 0;
                else
                {
                    var pick = withHistory[random.Next(withHistory.Count)];
                    return (strings.Get(S.ai_quiz_history, Raw(pick, "element_discovered_name")!,
                                        Raw(pick, "element_year")!), pick);
                }
            }

            var element = all[random.Next(all.Count)];

            return shape switch
            {
                1 => (strings.Get(S.ai_quiz_atomic,
                                  element.AtomicNumber.ToString(CultureInfo.InvariantCulture)), element),
                _ => (strings.Get(S.ai_quiz_start, element.Symbol), element),
            };
        }

        private static bool HasHistory(ElementRecord element) =>
            Present(Raw(element, "element_discovered_name")) && Present(Raw(element, "element_year"));

        private static bool Present(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value.Trim() != "---";

        private static string? Raw(ElementRecord element, string field) =>
            (element.Value(field) as FieldValue.Text)?.Raw;
    }
}
