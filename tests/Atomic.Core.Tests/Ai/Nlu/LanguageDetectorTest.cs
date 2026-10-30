using Atomic.Core.Ai.Nlu;
using Xunit;

namespace Atomic.Core.Tests.Ai.Nlu
{
    /// <summary>
    /// How the language detector reads a message (roadmap 1.4). Android's
    /// <c>LanguageDetectorTest</c> asserts the same features, because both read one model: a trigram
    /// extracted differently is a model that means something else.
    /// </summary>
    public class LanguageDetectorTest
    {
        [Fact]
        public void WordsArePaddedAndLowercasedWithTheirAccents() =>
            Assert.Equal(new[] { " va", "vad", "ad ", " är", "är " }, LanguageDetector.Features("Vad Är?"));

        [Fact]
        public void FormulasAreNoLanguagesWords()
        {
            Assert.Empty(LanguageDetector.Features("Ca(OH)2 + NaCl -> H2O"));
            // A lone capital is a word: the pronoun, or a symbol asked about by name.
            Assert.Equal(new[] { " fe", "fe " }, LanguageDetector.Features("Fe"));
        }

        [Fact]
        public void UnitNamesCarryNoWeight() =>
            Assert.Equal(new[] { " in", "in " }, LanguageDetector.Features("300 K in fahrenheit"));

        [Fact]
        public void AHanCharacterIsAWord()
        {
            Assert.Equal(new[] { " 金 ", " 的 ", " 密 ", " 度 " }, LanguageDetector.Features("金的密度"));
            Assert.Equal(4, LanguageDetector.WordCount("金的密度"));
            Assert.Equal(3, LanguageDetector.WordCount("och i 42 fahrenheit"));
        }

        [Fact]
        public void ASpaceIsWrittenAsAnUnderscoreInTheModel()
        {
            var detector = LanguageDetector.Parse("lang\ttrigram\tcount\nsv\t*\t10\nsv\t_hu\t3\nen\t*\t10\nen\t_ho\t3\n")!;
            Assert.Equal(new[] { "en", "sv" }, detector.Languages);
            Assert.Equal(0.0, detector.Scores("hur")["sv"]);
        }

        [Fact]
        public void NoModelIsNoDetector()
        {
            Assert.Null(LanguageDetector.Parse(null));
            Assert.Null(LanguageDetector.Parse("# nothing\nlang\ttrigram\tcount\n"));
        }

        [Fact]
        public void OneWordIsJudgedByElementNamesAlone()
        {
            var detector = LanguageDetector.Parse("lang\ttrigram\tcount\nsv\t*\t10\nsv\t_ok\t9\nen\t*\t10\n")!;
            Assert.Equal("en", detector.Detect("ok", "en", (_, active) => active));
        }
    }
}
