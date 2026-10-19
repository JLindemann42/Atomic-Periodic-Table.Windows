using System;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// Naming an element and asking nothing in particular.
    ///
    /// <para>"gold", "tell me about gold" and "tell me about iron" are all in
    /// <c>must_defer.tsv</c>: the engine must decline them, and something downstream must answer.
    /// Until now nothing did, so the most basic question in the app replied "Hmm, I couldn't find
    /// information about that."</para>
    /// </summary>
    public class ElementOverviewTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), null, null, language);

        private static IStringProvider Strings(string language = "en") =>
            new ReswStringProvider(language, fallBackToEnglish: true);

        [Theory]
        [InlineData("gold")]
        [InlineData("tell me about gold")]
        [InlineData("tell me about iron")]
        [InlineData("element 79")]
        public void NamingAnElementIsAnswered(string query)
        {
            var reply = Chat().Ask(query);

            Assert.NotEqual(Strings().Get(S.ai_no_data), reply.Text);
            Assert.NotEmpty(reply.Text);
        }

        /// <summary>The atomic number resolves to the right element - 79 is gold, not iron.</summary>
        [Fact]
        public void AnAtomicNumberIdentifiesItsElement()
        {
            var reply = Chat().Ask("element 79");

            Assert.Contains("79", reply.Text, StringComparison.Ordinal);
            Assert.Contains("Au", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>The basic facts are there, with their shipped labels.</summary>
        [Fact]
        public void TheOverviewCarriesTheBasicFacts()
        {
            var reply = Chat().Ask("gold");

            Assert.Contains("Au", reply.Text, StringComparison.Ordinal);
            Assert.Contains("79", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>
        /// A question that asks something specific is still the engine's, not an overview.
        /// </summary>
        [Theory]
        [InlineData("what is the density of gold")]
        [InlineData("compare gold and silver")]
        public void ARealQuestionIsNotTurnedIntoAnOverview(string query)
        {
            var reply = Chat().Ask(query);

            Assert.Equal(ReplySource.Engine, reply.Source);
        }

        /// <summary>Asking about an element puts it in focus, like any other answer.</summary>
        [Fact]
        public void TheOverviewSetsTheFocus()
        {
            var chat = Chat();
            chat.Ask("gold");

            var reply = chat.Ask("what is its density");

            Assert.Contains("19.3", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>A Swedish overview is answered, and in Swedish.</summary>
        [Fact]
        public void ASwedishOverviewIsAnswered()
        {
            var reply = Chat("sv").Ask("berätta om guld");

            Assert.NotEqual(Strings("sv").Get(S.ai_no_data), reply.Text);
            Assert.Contains("Au", reply.Text, StringComparison.Ordinal);
        }

        // ---- In the reader's language ------------------------------------------------------------
        //
        // The overview was parsed from the English record, so in every other language it said
        // "solid" and quoted the English description, though the language files hold both
        // translated. Found by asking the same questions in all twelve languages.

        private static string Overview(string language) =>
            Chat(language).Ask(Service.Localized(language)!.Name("gold")!).Text;

        /// <summary>The phase is the language's own word for it.</summary>
        [Theory]
        [InlineData("sv", "Fast")]
        [InlineData("de", "Fest")]
        [InlineData("fil", "Solido")]
        public void ThePhaseIsInTheReadersLanguage(string language, string phase)
        {
            var text = Overview(language);

            Assert.Contains(phase, text, StringComparison.Ordinal);
            // The whole value, so "Solido" is not mistaken for "solid".
            Assert.DoesNotContain("**: solid\n", text + "\n", StringComparison.Ordinal);
        }

        /// <summary>
        /// A sentence the language file left untranslated is a verbatim copy of the English one, and
        /// is dropped - in Swedish, where only part of the description is translated, and in Hindi,
        /// where none of it is. The facts answer the question on their own.
        /// </summary>
        [Theory]
        [InlineData("sv")]
        [InlineData("hi")]
        public void NoUntranslatedSentenceIsQuoted(string language) =>
            Assert.DoesNotContain("In a pure form", Overview(language), StringComparison.Ordinal);

        /// <summary>English keeps its description; the filter is only for other languages.</summary>
        [Fact]
        public void EnglishKeepsItsDescription() =>
            Assert.Contains("bright, slightly", Overview("en"), StringComparison.Ordinal);

        /// <summary>
        /// Chinese labels end in a full-width colon, which the ASCII strip missed, so every fact
        /// printed with two colons.
        /// </summary>
        // ---- The rich overview -------------------------------------------------------------------
        //
        // Android's provideOverview: the basic facts, then sections from a pool, and all of them for
        // "tell me everything". Its English-only labels, meaningless "notable" isotopes and ungated
        // PRO field are the parts not copied.

        private static ChatOrchestrator Chat(string language, Atomic.Core.Ai.Engine.Entitlements entitlements) =>
            new(Service, lang => new ReswStringProvider(lang, fallBackToEnglish: true), () => entitlements, null, language,
                new Random(7));

        [Fact]
        public void EverythingAboutAnElementGetsEverySection()
        {
            var text = Chat().Ask("tell me everything about gold").Text;
            var strings = Strings();

            Assert.Contains(strings.Get(S.ai_rich_overview_safety, "Gold"), text, StringComparison.Ordinal);
            Assert.Contains(strings.Get(S.ai_rich_overview_advanced, "Gold"), text, StringComparison.Ordinal);
            Assert.Contains(strings.Get(S.ai_rich_overview_isotopes, "Gold"), text, StringComparison.Ordinal);

            // The tip is for an ordinary overview; a full one has already said everything.
            Assert.DoesNotContain(strings.Get(S.ai_pro_tip_hint), text, StringComparison.Ordinal);
        }

        [Fact]
        public void AnOrdinaryOverviewEndsWithTheTip() =>
            Assert.Contains(Strings().Get(S.ai_pro_tip_hint), Chat().Ask("gold").Text, StringComparison.Ordinal);

        /// <summary>
        /// Electron affinity is a PRO field everywhere else, so the overview does not hand it to a free
        /// user. Android shows it to everyone.
        /// </summary>
        [Fact]
        public void APaidFieldFollowsTheEntitlement()
        {
            var label = Strings().Get(S.electron_affinity_colon).TrimEnd(':');
            var free = new Atomic.Core.Ai.Engine.Entitlements(IsPro: false, IsProPlus: false);

            Assert.DoesNotContain(label, Chat("en", free).Ask("tell me everything about gold").Text, StringComparison.Ordinal);
            Assert.Contains(label, Chat("en", Atomic.Core.Ai.Engine.Entitlements.Full).Ask("tell me everything about gold").Text,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// The stable isotopes are the notable ones. Android listed the first three slots, which for gold
        /// are Au-170 to Au-172 - gone in fractions of a second.
        /// </summary>
        [Fact]
        public void TheNotableIsotopesAreTheStableOnes()
        {
            var text = Chat().Ask("tell me everything about gold").Text;

            Assert.Contains("Au-197", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Au-170", text, StringComparison.Ordinal);
        }

        /// <summary>With no stable isotope, the longest-lived stand in - uranium-238 first.</summary>
        [Fact]
        public void WithNoStableIsotopeTheLongestLivedAreNamed()
        {
            var text = Chat().Ask("tell me everything about uranium").Text;
            var isotopes = text[text.IndexOf(Strings().Get(S.ai_rich_overview_isotopes, "Uranium"), StringComparison.Ordinal)..];

            Assert.StartsWith("Uranium-238", isotopes[(isotopes.IndexOf("**", StringComparison.Ordinal) + 2)..], StringComparison.Ordinal);
        }

        /// <summary>The safety lines carry the language's own labels, not Android's English "Health".</summary>
        [Fact]
        public void SafetyIsLabelledInTheReadersLanguage()
        {
            var text = Chat("sv").Ask("berätta allt om guld").Text;
            var health = new ReswStringProvider("sv", fallBackToEnglish: true).Get(S.ai_field_nfpa_health);

            Assert.Contains(char.ToUpperInvariant(health[0]) + health[1..], text, StringComparison.Ordinal);
            Assert.DoesNotContain("**Health", text, StringComparison.Ordinal);
        }

        /// <summary>
        /// A decimal point is not the end of a sentence: "between 159,200 years and 4.5 billion years"
        /// was cut at "4.", and Chinese, which ends sentences with "。", was never split at all.
        /// </summary>
        [Fact]
        public void SentencesAreSplitWhereTheyEnd()
        {
            var latin = ElementNarrative.TranslatedSentences(
                "Its half-lives range between 159,200 years and 4.5 billion years. It is grey.",
                "Its half-lives range between 159,200 years and 4.5 billion years. It is grey.", "en");
            Assert.Equal(2, latin.Count);
            Assert.EndsWith("4.5 billion years.", latin[0], StringComparison.Ordinal);

            var chinese = ElementNarrative.TranslatedSentences("\u91D1\u662F\u5143\u7D20\u3002\u5B83\u662F\u91D1\u5C5E\u3002", "Gold.", "zh");
            Assert.Equal(2, chinese.Count);
            Assert.DoesNotContain("\u3002.", Overview("zh-Hans"), StringComparison.Ordinal);
        }

        [Fact]
        public void ChineseLabelsHaveOneColon() =>
            Assert.DoesNotContain("：**:", Overview("zh-Hans"), StringComparison.Ordinal);
    }
}
