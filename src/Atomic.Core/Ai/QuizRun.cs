using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Quiz;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// A run of multiple-choice questions started from chat (roadmap 2.5): "quiz me on the
    /// halogens", "five isotope questions on uranium". Mirrors Android's <c>QuizRunState</c>.
    /// </summary>
    /// <param name="scopeKeys">The elements the questions are about, or null for the whole table.</param>
    public sealed class QuizRunState(IReadOnlyList<QuizQuestion> questions, IReadOnlySet<string>? scopeKeys)
    {
        public IReadOnlyList<QuizQuestion> Questions { get; } = questions;

        public IReadOnlySet<string>? ScopeKeys { get; } = scopeKeys;

        public int Index { get; set; }

        public int Correct { get; set; }

        public int Xp { get; set; }

        public QuizQuestion Current => Questions[Index];

        public bool Finished => Index >= Questions.Count;
    }

    /// <summary>What a run request asks for.</summary>
    public sealed record QuizRequest(int Count, IReadOnlyList<string> Categories, bool ExplicitCount);

    /// <summary>
    /// Reads a run request, builds its questions from the Learning Games generators, and judges a
    /// pick. Mirrors Android's <c>QuizRun</c>.
    /// </summary>
    public static class QuizRun
    {
        public const int DefaultCount = 5;
        public const int MaxCount = 10;

        /// <summary>
        /// A mix that works for any scope. Generators that cannot fill a question from the scope
        /// return nothing and are skipped: one element has no "which of these is densest".
        /// </summary>
        private static readonly string[] Mixed =
        [
            "element_classifications", "periodic_period", "periodic_group", "valence_electrons",
            "superlative_highest", "superlative_lowest", "compare_pair", "isotope_neutrons",
        ];

        private static readonly string[] Isotopes = ["isotope_neutrons", "isotope_stability", "isotope_half_life", "isotope_decay"];

        private static readonly string[] Superlatives = ["superlative_highest", "superlative_lowest", "compare_pair"];

        private static readonly string[] MolarMass = ["molar_mass"];

        /// <summary>"molar mass" in each language. A formula is not scoped to elements, so this runs table-wide.</summary>
        private static readonly string[] MolarMassWords =
        [
            "molar mass", "molmassa", "molare masse", "masse molaire", "masa molar", "massa molare",
            "massa molar", "molere massa", "molar na masa", "मोलर द्रव्यमान", "مولر ماس", "摩尔质量",
        ];

        /// <summary>Ends a run early, said on its own or in a short reply: "stop", "ok stop the quiz".</summary>
        private static readonly string[] StopWords =
        [
            "stop", "quit", "end quiz", "enough", "sluta", "avbryt", "stopp", "aufhoren", "beenden",
            "arrete", "arreter", "parar", "basta", "fermati", "pare", "hou op", "tama na", "itigil",
            "बस", "रुको", "بس", "روکو", "停", "停止", "结束",
        ];

        /// <summary>Number words up to ten, for "five isotope questions". Digits are read directly.</summary>
        private static readonly IReadOnlyDictionary<string, int> NumberWords = BuildNumberWords();

        /// <summary>"period 3" and "group 17" name a scope, not a count.</summary>
        private static readonly Regex PositionNumber = new(
            @"(?:period|group|periode|period|grupp|gruppe|groupe|periodo|gruppo|grupo|groep|panahon|grupo|आवर्त|समूह|پیریڈ|گروپ|第)\s*\d{1,2}",
            RegexOptions.CultureInvariant);

        private static readonly Regex Digits = new(@"(?<!\d)(\d{1,2})(?!\d)", RegexOptions.CultureInvariant);

        private static Dictionary<string, int> BuildNumberWords()
        {
            var words = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
                ["tre"] = 3, ["fyra"] = 4, ["fem"] = 5, ["sex"] = 6, ["sju"] = 7, ["atta"] = 8, ["nio"] = 9, ["tio"] = 10,
                ["drei"] = 3, ["vier"] = 4, ["funf"] = 5, ["sechs"] = 6, ["sieben"] = 7, ["acht"] = 8, ["neun"] = 9, ["zehn"] = 10,
                ["trois"] = 3, ["quatre"] = 4, ["cinq"] = 5, ["sept"] = 7, ["huit"] = 8, ["neuf"] = 9, ["dix"] = 10,
                ["tres"] = 3, ["cuatro"] = 4, ["cinco"] = 5, ["seis"] = 6, ["siete"] = 7, ["ocho"] = 8, ["nueve"] = 9, ["diez"] = 10,
                ["quattro"] = 4, ["sette"] = 7, ["otto"] = 8, ["nove"] = 9, ["dieci"] = 10,
                ["quatro"] = 4, ["sete"] = 7, ["oito"] = 8, ["dez"] = 10,
                ["drie"] = 3, ["vyf"] = 5, ["ses"] = 6, ["sewe"] = 7, ["agt"] = 8, ["nege"] = 9, ["tien"] = 10,
                ["tatlo"] = 3, ["apat"] = 4, ["lima"] = 5, ["anim"] = 6, ["pito"] = 7, ["walo"] = 8, ["siyam"] = 9, ["sampu"] = 10,
                ["तीन"] = 3, ["चार"] = 4, ["पांच"] = 5, ["पाँच"] = 5, ["छह"] = 6, ["सात"] = 7, ["आठ"] = 8, ["नौ"] = 9, ["दस"] = 10,
                ["تین"] = 3, ["چار"] = 4, ["پانچ"] = 5, ["چھ"] = 6, ["سات"] = 7, ["آٹھ"] = 8, ["نو"] = 9, ["دس"] = 10,
            };
            // A bare 三 is also the 三 of 第三周期, the third period; a count is followed by a measure word.
            var han = new[] { ("三", 3), ("四", 4), ("五", 5), ("六", 6), ("七", 7), ("八", 8), ("九", 9), ("十", 10) };
            foreach (var (word, n) in han)
            {
                words[word + "道"] = n;
                words[word + "个"] = n;
            }
            return words;
        }

        /// <summary>What a quiz request asks for: how many questions, and of which kinds.</summary>
        public static QuizRequest Request(string normalized)
        {
            var count = CountOf(normalized);
            IReadOnlyList<string> categories =
                Lexicon.ISOTOPE_WORDS.Any(w => normalized.Contains(w, StringComparison.Ordinal)) ? Isotopes
                : MolarMassWords.Any(w => normalized.Contains(w, StringComparison.Ordinal)) ? MolarMass
                : Lexicon.MOST.Any(w => normalized.Contains(w, StringComparison.Ordinal)) ||
                  Lexicon.LEAST.Any(w => normalized.Contains(w, StringComparison.Ordinal)) ? Superlatives
                : Mixed;
            return new QuizRequest(Math.Clamp(count ?? DefaultCount, 1, MaxCount), categories, count is not null);
        }

        /// <summary>Whether the request picked a kind of question rather than the default mix.</summary>
        public static bool NamesATopic(QuizRequest request) => !ReferenceEquals(request.Categories, Mixed);

        private static int? CountOf(string normalized)
        {
            var withoutPositions = PositionNumber.Replace(normalized, " ");
            var digits = Digits.Match(withoutPositions);
            if (digits.Success && int.TryParse(digits.Groups[1].Value, out var n) && n > 0) return n;
            foreach (var (word, value) in NumberWords)
                if (TextMatching.ContainsWord(normalized, word)) return value;
            return null;
        }

        /// <summary>
        /// Up to the request's count of questions, taking one from each kind in turn so a run mixes
        /// them, and never the same question twice.
        /// </summary>
        public static IReadOnlyList<QuizQuestion> Questions(GeneratorContext context, QuizRequest request)
        {
            var generators = request.Categories
                .Select(QuestionGenerators.ForCategory)
                .OfType<IQuestionGenerator>()
                .OrderBy(_ => context.Random.Next())
                .ToList();
            var output = new List<QuizQuestion>(request.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (output.Count < request.Count && generators.Count > 0)
            {
                foreach (var generator in generators.ToList())
                {
                    if (output.Count >= request.Count) break;
                    var fresh = generator.Generate(context, 3).FirstOrDefault(q => seen.Add(q.Question + "|" + q.CorrectAnswer));
                    if (fresh is null) generators.Remove(generator);
                    else output.Add(fresh);
                }
            }
            return output;
        }

        public static bool Stops(string normalized) =>
            TextMatching.SplitQueryTokens(normalized).Count <= 4 &&
            StopWords.Any(w => TextMatching.ContainsWord(normalized, w));

        /// <summary>
        /// The option a reply picks, or null when it picks none or more than one. A chip sends the
        /// option itself; a typed reply may say it inside a sentence, "it's sodium".
        /// </summary>
        public static string? Choice(string normalized, IReadOnlyList<string> options)
        {
            var folded = options.Select(o => (Option: o, Folded: TextMatching.NormalizeForLookup(o))).ToList();
            foreach (var (option, text) in folded)
                if (text == normalized) return option;
            var inside = folded.Where(p => p.Folded.Length > 0 && TextMatching.ContainsWord(normalized, p.Folded)).ToList();
            return inside.Count == 1 ? inside[0].Option : null;
        }
    }
}
