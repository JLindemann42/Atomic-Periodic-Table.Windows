using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Atomic.Core.Tests.Corpus
{
    /// <summary>
    /// The language detector (roadmap 1.4) against every row of the corpus, each of which declares
    /// the language it is asked in. Mirrors Android's <c>LanguageDetectionTest</c>.
    ///
    /// <para><c>language.tsv</c> is the gate for what detection must do. This is the check on what it
    /// must not: the other files are thousands of real questions, short follow-ups and formulas among
    /// them, and the cheapest way to break a thread is to answer one of its turns in another
    /// language.</para>
    /// </summary>
    public class LanguageDetectionTest
    {
        /// <summary>Element names alone recognised 68% of the dev split; the trigram model, 97%.</summary>
        private const double MinRecognised = 0.95;

        private readonly ITestOutputHelper _output;

        public LanguageDetectionTest(ITestOutputHelper output) => _output = output;

        private static List<IReadOnlyList<CorpusCase>> Sessions() =>
            QuestionCorpus.Files.Where(f => f != "language").SelectMany(QuestionCorpus.Sessions).ToList();

        /// <summary>Asked in the language already in play, no row may switch away from it.</summary>
        [Fact]
        public void NoRowIsSwitchedAwayFromItsOwnLanguage()
        {
            Assert.True(QuestionCorpus.Available && CorpusHarness.Available, "corpus or assets not reachable");
            Assert.NotNull(CorpusHarness.LanguageDetector);
            var switched = Sessions().SelectMany(s => s)
                .Select(c => (Case: c, Detected: CorpusHarness.DetectLanguage(c.Query, c.Lang)))
                .Where(r => r.Detected != r.Case.Lang)
                .Select(r => $"{r.Case.Where} '{r.Case.Query}' {r.Case.Lang} -> {r.Detected}")
                .ToList();
            Assert.True(switched.Count == 0, $"{switched.Count} rows switched language:\n  " + string.Join("\n  ", switched));
        }

        /// <summary>
        /// Replayed as conversations that open in English, the app's default, most rows must be
        /// recognised: the first turn of each session starts from English, each later one from the
        /// language the turn before was asked in. A scoreboard with a floor, because a one-word
        /// greeting or a bare element name is rightly not enough to switch on.
        /// </summary>
        [Fact]
        public void MostRowsAreRecognisedInConversationsOpenedInEnglish()
        {
            Assert.True(QuestionCorpus.Available && CorpusHarness.Available, "corpus or assets not reachable");
            var sessions = Sessions();
            var rows = sessions.Sum(s => s.Count);
            var recognised = 0;
            foreach (var session in sessions)
            {
                var active = "en";
                foreach (var corpusCase in session)
                {
                    if (CorpusHarness.DetectLanguage(corpusCase.Query, active) == corpusCase.Lang) recognised++;
                    active = corpusCase.Lang;
                }
            }
            var share = (double)recognised / rows;
            _output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[language] recognised: {0}/{1} ({2:F1}%)", recognised, rows, 100 * share));
            Assert.True(share >= MinRecognised, $"only {recognised} of {rows} rows recognised");
        }
    }
}
