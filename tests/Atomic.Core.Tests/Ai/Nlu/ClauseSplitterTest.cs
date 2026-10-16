using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Nlu;
using Xunit;

namespace Atomic.Core.Tests.Ai.Nlu
{
    /// <summary>
    /// The clause splitter, with a case for every regression its Kotlin comments name. Android has
    /// no unit test for this file - it is covered only through the corpus - so these are new.
    /// </summary>
    public class ClauseSplitterTest
    {
        [Fact]
        public void TwoQuestionsSharingASubjectAreSplit()
        {
            var clauses = ClauseSplitter.Split(
                "What is the density of gold and how does it compare to lead");

            Assert.NotNull(clauses);
            Assert.Equal(2, clauses!.Count);
            Assert.Equal("What is the density of gold", clauses[0]);
            Assert.StartsWith("how does it compare", clauses[1], StringComparison.Ordinal);
        }

        /// <summary>
        /// The whole difficulty of this file. "and" joins two elements, two bounds and two aspects
        /// far more often than it joins two questions, and splitting any of those breaks a query
        /// that works.
        /// </summary>
        [Theory]
        [InlineData("compare gold and silver")]
        [InlineData("elements between 1000 and 2000 kelvin")]
        [InlineData("the difference between gold and silver")]
        [InlineData("gold vs silver density")]
        [InlineData("tell me about the reactivity, density and applications")]
        public void ConjunctionsInsideAPhraseDoNotSplit(string query) =>
            Assert.Null(ClauseSplitter.Split(query));

        /// <summary>
        /// The aspect-list guard is narrower than it looks, and worth knowing about: it holds
        /// because "applications" alone is one token with no linker. Write "…and applications of
        /// sodium" and the tail acquires a linker with a noun on each side, which is the definition
        /// of an ask, so it splits.
        ///
        /// <para>Not a defect - both halves still get answered, and the same is true on Android,
        /// whose comment names the short form. Pinned so the boundary is documented rather than
        /// rediscovered as a regression.</para>
        /// </summary>
        [Fact]
        public void AnAspectListSplitsOnceItsTailCarriesALinker() =>
            Assert.NotNull(ClauseSplitter.Split(
                "tell me about the reactivity, density and applications of sodium"));

        [Fact]
        public void ASentenceBoundaryIsAConjunction()
        {
            var clauses = ClauseSplitter.Split(
                "Tell me about hydrogen. Furthermore, what is the density of iron");

            Assert.NotNull(clauses);
            Assert.Equal("Tell me about hydrogen", clauses![0]);
            // The discourse marker is stripped, so the tail is the question itself.
            Assert.StartsWith("what is the density of iron", clauses[1], StringComparison.Ordinal);
        }

        /// <summary>
        /// A tail with no question word is still an ask when it names a property and an element.
        /// Swedish writes it with no interrogative at all.
        /// </summary>
        [Fact]
        public void APropertyLinkerMakesATailAQuestion()
        {
            var clauses = ClauseSplitter.Split("vad ar densiteten for guld och densitet av vate");

            Assert.NotNull(clauses);
            Assert.Equal("densitet av vate", clauses![1]);
        }

        [Fact]
        public void ABareContinuationStaysJoined() =>
            Assert.Null(ClauseSplitter.Split("what is the density of gold and silver"));

        /// <summary>
        /// Italian normalises its copula "è" onto the same letter as its conjunction "e", so the
        /// query contains " e " twice and the first is too early to split on. Taking only the first
        /// occurrence per conjunction discarded it entirely.
        /// </summary>
        [Fact]
        public void EveryOccurrenceOfAConjunctionIsConsideredNotJustTheFirst()
        {
            var clauses = ClauseSplitter.Split(
                "qual e il numero atomico del ferro e qual e il suo punto di fusione");

            Assert.NotNull(clauses);
            Assert.Equal("qual e il numero atomico del ferro", clauses![0]);
            Assert.StartsWith("qual e il suo punto", clauses[1], StringComparison.Ordinal);
        }

        /// <summary>
        /// A Han character carries about what a whole word does, so the Latin eight-character floor
        /// rejected every Chinese second clause before it was examined.
        /// </summary>
        [Fact]
        public void ChineseClausesAreMeasuredOnAShorterFloor()
        {
            var clauses = ClauseSplitter.Split("金的密度是多少以及它的熔点是多少");

            Assert.NotNull(clauses);
            Assert.Equal(2, clauses!.Count);
            Assert.StartsWith("它的", clauses[1], StringComparison.Ordinal);
        }

        /// <summary>
        /// The offset trap the Kotlin comment describes: normalisation changes the string's length,
        /// so an offset measured on the folded text cannot index the original. German is where it
        /// bites, because "außerdem" folds one character longer.
        /// </summary>
        [Fact]
        public void AFoldThatChangesLengthDoesNotCorruptTheSplit()
        {
            var clauses = ClauseSplitter.Split(
                "Wie ist die Dichte von Gold und außerdem was ist die Dichte von Eisen");

            Assert.NotNull(clauses);
            Assert.Equal("Wie ist die Dichte von Gold", clauses![0]);
            Assert.Contains("Eisen", clauses[1], StringComparison.Ordinal);
        }

        [Fact]
        public void ASingleQuestionIsNotSplit()
        {
            Assert.Null(ClauseSplitter.Split("what is the density of gold"));
            Assert.Null(ClauseSplitter.Split("gold"));
            Assert.Null(ClauseSplitter.Split(""));
        }

        [Fact]
        public void ClausesKeepTheirOriginalCapitalisation()
        {
            // The formula parser and the element resolver both read case, so the clauses must not
            // come back folded.
            var clauses = ClauseSplitter.Split("What is the molar mass of NaCl and what is H2O");

            Assert.NotNull(clauses);
            Assert.Contains("NaCl", clauses![0], StringComparison.Ordinal);
            Assert.Contains("H2O", clauses[1], StringComparison.Ordinal);
        }
    }

    public class QuantityScannerTest
    {
        [Fact]
        public void ReadsANumberAndItsUnit()
        {
            var found = QuantityScanner.Scan("convert 500 K to celsius");

            Assert.Equal(500.0, found[0].Value);
            Assert.Equal(Dimension.Temperature, found[0].Dimension);
        }

        /// <summary>
        /// The converter's Mass dimension is atomic mass units, not laboratory grams, so a gram
        /// quantity is deliberately invisible to this scanner. Pinned because it looks like a bug
        /// until you know: the solution intent reads grams through its own path.
        /// </summary>
        [Fact]
        public void ALaboratoryGramIsNotAScannableQuantity() =>
            Assert.DoesNotContain(QuantityScanner.Scan("12 g of iron"),
                                  q => q.Dimension == Dimension.Mass);

        [Fact]
        public void KeepsQuantitiesInWrittenOrderSoTheCallerCanTellThemApart()
        {
            // "dilute 50 mL of 2 M to 0.5 M" - the two molarities are distinguishable only by
            // position, which is why the span is carried on the record.
            var found = QuantityScanner.Scan("dilute 50 mL of 2 M to 0.5 M");
            var molarities = found.Where(q => q.Dimension == Dimension.Molarity).ToList();

            Assert.Equal(2, molarities.Count);
            Assert.Equal(2.0, molarities[0].Value);
            Assert.Equal(0.5, molarities[1].Value);
            Assert.True(molarities[0].Start < molarities[1].Start);
        }

        // "mol" rather than "g": the converter's Mass dimension is based on the atomic mass unit
        // and has no laboratory gram in it at all, so "12 g" scans to nothing - correctly, and on
        // both platforms. Grams reach the solution intent by a different path.
        [Theory]
        [InlineData("12 mol", 12.0)]
        [InlineData("12.5 mol", 12.5)]
        [InlineData("12,5 mol", 12.5)]
        [InlineData("3.5 × 10^-21 mol", 3.5e-21)]
        public void ReadsEveryNumberSpelling(string query, double expected) =>
            Assert.Equal(expected, QuantityScanner.Scan(query)[0].Value, 1e-30);

        /// <summary>
        /// The scanner reads the raw query on purpose. Folding lowercases, and M and m - molar and
        /// metre - differ only by case.
        /// </summary>
        [Fact]
        public void CaseSeparatesMolarFromMetre()
        {
            var molar = QuantityScanner.Scan("2 M solution");
            var metre = QuantityScanner.Scan("2 m long");

            Assert.Equal(Dimension.Molarity, molar[0].Dimension);
            Assert.Equal(Dimension.Length, metre[0].Dimension);
        }

        /// <summary>
        /// An ASCII letter class matched the leading "a" of Spanish "años", which the converter
        /// reads as the angstrom - so a decay duration was scanned as a length and lost.
        /// </summary>
        [Fact]
        public void ASpanishYearIsNotAnAngstrom()
        {
            var found = QuantityScanner.Scan("11460 años");
            Assert.DoesNotContain(found, q => q.Dimension == Dimension.Length);
        }

        [Fact]
        public void ScriptUnitsWithNoLatinLettersAreRead()
        {
            var hindi = QuantityScanner.Scan("11460 साल");
            Assert.Contains(hindi, q => q.Dimension == Dimension.Time);
        }

        [Fact]
        public void CompoundUnitsSurviveTheirDigits()
        {
            var found = QuantityScanner.Scan("19.3 g/cm3");
            Assert.Contains(found, q => q.Dimension == Dimension.Density);
        }

        /// <summary>
        /// "2 eV in kJ/mol" - eV has "in" on its right and satisfies the postpositional rule
        /// perfectly, so without the source-quantity guard the conversion runs backwards.
        /// </summary>
        [Fact]
        public void AUnitAttachedToANumberIsNeverTheTarget() =>
            Assert.Equal(UnitConverter.KnownUnit("kJ/mol"), QuantityScanner.TargetUnit("2 eV in kJ/mol"));

        [Fact]
        public void TargetUnitIsFoundBeforeOrAfterItsPreposition()
        {
            Assert.NotNull(QuantityScanner.TargetUnit("convert 500 K to celsius"));

            // Hindi puts the preposition after the unit, so the trailing form is checked too.
            Assert.NotNull(QuantityScanner.TargetUnit("500 K को सेल्सियस में"));
        }

        /// <summary>
        /// The other half of the source-quantity guard: in "11460 साल में" the unit belongs to the
        /// number in front of it, so it is what is being converted, not what is being asked for -
        /// even though the trailing preposition matches perfectly.
        /// </summary>
        [Fact]
        public void AUnitWithATrailingPrepositionIsStillTheSourceWhenANumberPrecedesIt() =>
            Assert.Null(QuantityScanner.TargetUnit("11460 साल में"));

        [Fact]
        public void NoTargetUnitWhenTheQueryNamesNone() =>
            Assert.Null(QuantityScanner.TargetUnit("what is the density of gold"));

        [Fact]
        public void FirstOfPicksTheEarliestOfADimension()
        {
            var first = QuantityScanner.FirstOf("dilute 50 mL of 2 M to 0.5 M", Dimension.Molarity);
            Assert.Equal(2.0, first!.Value);
        }

        /// <summary>
        /// The comma-to-point rewrite means a comma-decimal ambient culture would otherwise fail to
        /// parse the very numbers this normalises.
        /// </summary>
        [Fact]
        public void NumbersParseTheSameUnderAHostileCulture()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");
                Assert.Equal(12.5, QuantityScanner.Scan("12.5 mol")[0].Value);
                Assert.Equal(12.5, QuantityScanner.Scan("12,5 mol")[0].Value);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }
}
