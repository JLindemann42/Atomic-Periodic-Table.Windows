using Atomic.Core.Ai.Nlu;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Nlu
{
    /// <summary>
    /// The three morphology rules (roadmap 1.5), each with the words it must leave alone. Android's
    /// <c>MorphologyTest</c> asserts the same rewrites.
    /// </summary>
    public class MorphologyTest
    {
        private static Morphology For(string language) =>
            new(new FieldResolver(new ReswStringProvider(language, fallBackToEnglish: true)));

        [Fact]
        public void ACompoundIsSplitIntoTheWordsItIsMadeOf()
        {
            var sv = For("sv");
            Assert.Equal("smaltpunkt skillnaden mellan järn och koppar", sv.Expand("smältpunktsskillnaden mellan järn och koppar"));
            Assert.Equal("medel tatheten för halogenerna", sv.Expand("medeltätheten för halogenerna"));
            Assert.Equal("elektronegativitet vardet för fluor?", sv.Expand("elektronegativitetsvärdet för fluor?"));
            Assert.Equal("durchschnitt dichte der Alkalimetalle", For("de").Expand("Durchschnittsdichte der Alkalimetalle"));
        }

        [Fact]
        public void AWordKnownWholeIsNeverTakenApart()
        {
            var sv = For("sv");
            Assert.Equal("vad är atommassan för guld", sv.Expand("vad är atommassan för guld"));
            Assert.Equal("vilka är alkalimetallerna", sv.Expand("vilka är alkalimetallerna"));
            Assert.Equal("molmassan av natriumklorid", sv.Expand("molmassan av natriumklorid"));
        }

        [Fact]
        public void SteIsTheSuperlativeOfItsStem()
        {
            var af = For("af");
            Assert.Equal("watter edelgas is die lightest?", af.Expand("watter edelgas is die ligste?"));
            Assert.Equal("watter element is die most electronegative", af.Expand("watter element is die elektronegatiefste"));
            // Listed whole with its property, so left as it is.
            Assert.Equal("watter metaal is die swaarste", af.Expand("watter metaal is die swaarste"));
            Assert.Equal("die hoogste smeltpunt", af.Expand("die hoogste smeltpunt"));
        }

        [Fact]
        public void PinakaIsTheSuperlativeOfWhatFollowsIt()
        {
            var fil = For("fil");
            Assert.Equal("ano ang pinakamababa punto ng pagkatunaw", fil.Expand("ano ang pinakamababang punto ng pagkatunaw"));
            Assert.Equal("aling elemento ang most electronegative", fil.Expand("aling elemento ang pinakaelectronegative"));
            Assert.Equal("ano ang hardest na metal", fil.Expand("ano ang pinakamatigas na metal"));
            Assert.Equal("ano ang pinakamabigat na elemento", fil.Expand("ano ang pinakamabigat na elemento"));
        }

        [Fact]
        public void FormulasAndNumbersAreLeftAsWritten()
        {
            var en = For("en");
            Assert.Equal("balance Fe + O2 -> Fe2O3", en.Expand("balance Fe + O2 -> Fe2O3"));
            Assert.Equal("molar mass of NaCl", en.Expand("molar mass of NaCl"));
        }
    }
}
