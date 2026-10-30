using System;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// What a question means once an element is in focus - pinned as it stands, because it is
    /// <b>shared with Android</b> rather than a porting defect.
    ///
    /// <para>The acceptance corpus cannot see any of this: it runs every row against a fresh
    /// <see cref="DialogueState"/>, while a real conversation carries the previous element. So a
    /// question that is not about that element gets captured by it, and the result is a confident
    /// wrong answer rather than a decline - "what are periodic trends" answered with "Gold's
    /// Period is 6".</para>
    ///
    /// <para><b>Why this is pinned rather than fixed.</b> Both inheritance guards in
    /// <c>QueryPlanner</c> are identical to Android's - the element guard
    /// (<c>fieldIds.isNotEmpty() || continuesTheThread</c>) and the field guard (the
    /// <c>elliptical</c> test). Android therefore answers these the same way. Changing it here
    /// would make the two apps disagree, which is a product decision rather than a bug fix, so
    /// these tests record the behaviour and will fail the moment anyone changes it - at which
    /// point Android has to change too.</para>
    /// </summary>
    public class ContextBleedTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static ChatOrchestrator Chat(string language = "en") =>
            new(Service, lang => new ReswStringProvider(language: lang, fallBackToEnglish: true),
                null, null, language);

        /// <summary>
        /// FIXED. The corpus pins "what is electronegativity" as a dictionary definition
        /// (ds-dict-1, DATASET_LOOKUP), and it now stays one even with gold in focus.
        ///
        /// <para>It used to report gold's value: the dictionary path is guarded by
        /// <c>!hasElement</c>, so the inherited element shut out the better answer that was
        /// already sitting in the dictionary. <c>AsksForADefinition</c> now declines to inherit
        /// when an interrogative opens the question and the term has an entry of its own.</para>
        ///
        /// <para>This is a deliberate divergence from Android, agreed with the user, to be ported
        /// back so the two converge again.</para>
        /// </summary>
        [Fact]
        public void AConceptQuestionIsDefinedRatherThanInheritingTheFocusedElement()
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");

            var reply = chat.Ask("what is electronegativity");

            Assert.DoesNotContain("2.54", reply.Text, StringComparison.Ordinal);
            Assert.Contains("Electronegativity", reply.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A real follow-up is untouched: "and the melting point?" still means gold's.</summary>
        [Fact]
        public void ARealFollowUpStillInheritsTheFocusedElement()
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");

            var reply = chat.Ask("and the melting point?");

            Assert.Contains("Gold", reply.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>And an anaphoric question is still about the element, not the word.</summary>
        [Fact]
        public void AnAnaphoricQuestionStillInheritsTheFocusedElement()
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");

            var reply = chat.Ask("what is its electronegativity");

            Assert.Contains("2.54", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>
        /// FIXED. "Periodic trends" used to resolve the field <c>period</c> and inherit the
        /// focused element, so a question about the periodic table was answered about gold.
        ///
        /// <para>The dictionary entries are titled "Periodic trend: ionization energy", so the
        /// question matches no title whole; the topic before the colon is what it names.</para>
        ///
        /// <para>Asserted on the element rather than on the word "Period", because the correct
        /// answer contains that word too - an earlier version of this test passed by coincidence
        /// while the behaviour was still wrong.</para>
        /// </summary>
        [Fact]
        public void AGeneralQuestionIsDefinedRatherThanAnsweredAboutTheFocusedElement()
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");

            var reply = chat.Ask("what are periodic trends");

            Assert.DoesNotContain("Gold", reply.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Period is 6", reply.Text, StringComparison.Ordinal);
        }

        /// <summary>The same for a property name asked on its own, with no element named.</summary>
        [Fact]
        public void ABarePropertyQuestionIsDefinedRatherThanInherited()
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");

            var reply = chat.Ask("what is the atomic radius");

            Assert.DoesNotContain("Gold", reply.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// FIXED. A question that names an element <i>and</i> states a predicate about it omits
        /// nothing, so it is not an ellipsis and must not inherit the previous field.
        ///
        /// <para>"Is gold a metal" used to answer about Discovered By - the field left over from
        /// a question about radium - because "metal" is a known series word, so the query read as
        /// a continuation. It now declines rather than answering the wrong question; a positive
        /// membership answer would be a new capability rather than a parity fix.</para>
        /// </summary>
        [Fact]
        public void AQuestionWithItsOwnPredicateDoesNotInheritTheField()
        {
            var chat = Chat();
            chat.Ask("who discovered radium");

            var reply = chat.Ask("is gold a metal");

            Assert.DoesNotContain("Discovered By", reply.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A genuine ellipsis still inherits: "and iron?" means iron's density.</summary>
        [Theory]
        [InlineData("and iron?", "Iron")]
        [InlineData("what about silver", "Silver")]
        public void AGenuineEllipsisStillInheritsTheField(string followUp, string expected)
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");

            var reply = chat.Ask(followUp);

            Assert.Contains(expected, reply.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Density", reply.Text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// "Element 79" is short enough to read as a follow-up, so it inherits the last field and
        /// answers with that property rather than identifying the element.
        /// </summary>
        [Fact]
        public void AnAtomicNumberQuestionInheritsTheLastProperty()
        {
            var chat = Chat();
            chat.Ask("what is the density of gold");

            var reply = chat.Ask("element 79");

            Assert.Contains("Density", reply.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
