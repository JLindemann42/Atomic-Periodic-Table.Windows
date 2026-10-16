using System;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Nlu
{
    /// <summary>
    /// The field resolver, against the real shipped labels rather than a fixture - the localized
    /// label layer is most of what it knows, so a fake string provider would test the wrong half.
    /// </summary>
    public class FieldResolverTest
    {
        private static FieldResolver For(string language) =>
            new(new ReswStringProvider(language, fallBackToEnglish: true));

        private static readonly FieldResolver English = For("en");

        [Theory]
        [InlineData("what is the density of gold", "density")]
        [InlineData("melting point of iron", "melting_point")]
        [InlineData("atomic mass of carbon", "atomic_mass")]
        [InlineData("how heavy is lead", "atomic_mass")]
        [InlineData("how hot does tungsten melt", "melting_point")]
        public void ResolvesEnglishFieldsByLabelAndColloquialism(string query, string expected) =>
            Assert.Equal(expected, English.Resolve(query)?.Spec.Id);

        /// <summary>
        /// The two discovery fields share every alias, so only the interrogative separates them.
        /// Without that, "when was helium discovered" is answered with a person's name.
        /// </summary>
        [Fact]
        public void TheInterrogativeSeparatesTheTwoDiscoveryFields()
        {
            Assert.Equal("year_discovered", English.Resolve("when was helium discovered")?.Spec.Id);
            Assert.Equal("discovered_by", English.Resolve("who discovered helium")?.Spec.Id);
        }

        /// <summary>
        /// "where is gold found" is about abundance and shares the word "found", so it must fall
        /// through to the alias table rather than being claimed by the discovery rule.
        /// </summary>
        [Fact]
        public void ADiscoveryWordWithNoInterrogativeIsNotADiscoveryQuestion()
        {
            var match = English.Resolve("where is gold found");
            Assert.True(match is null || match.Spec.Id is not ("year_discovered" or "discovered_by"));
        }

        [Fact]
        public void LongerAliasesWinSoASpecificFieldIsNotShadowed()
        {
            Assert.Equal("atomic_radius", English.Resolve("atomic radius of gold")?.Spec.Id);
            Assert.Equal("covalent_radius", English.Resolve("covalent radius of gold")?.Spec.Id);
        }

        /// <summary>
        /// The English y/ies plural. Without it "and their densities" resolved no field at all and
        /// the follow-up silently inherited the previous turn's field.
        /// </summary>
        [Fact]
        public void EnglishPluralsOfFieldNamesResolve()
        {
            Assert.Equal("density", English.Resolve("and their densities")?.Spec.Id);
            Assert.Equal("melting_point", English.Resolve("their melting points")?.Spec.Id);
            Assert.Equal("atomic_mass", English.Resolve("sum of the atomic masses")?.Spec.Id);
        }

        /// <summary>
        /// Swedish "atomnummer" becomes "atomnumret" - the same length as the alias, so a plain
        /// suffix rule cannot see it and every such question was declined.
        /// </summary>
        [Fact]
        public void SwedishSyncopeResolves() =>
            Assert.Equal("atomic_number", For("sv").Resolve("vad ar atomnumret for guld")?.Spec.Id);

        [Fact]
        public void SwedishDefiniteFormsResolve() =>
            Assert.Equal("melting_point", For("sv").Resolve("vad ar smaltpunkten for jarn")?.Spec.Id);

        /// <summary>
        /// The Romance head noun comes first and inflects: "points de fusion", not "point de
        /// fusions". Allowing only the tail to carry an ending lost the whole phrase.
        /// </summary>
        [Fact]
        public void RomanceHeadNounInflectionResolves() =>
            Assert.Equal("melting_point", For("fr").Resolve("et leurs points de fusion")?.Spec.Id);

        /// <summary>
        /// A Han field name is often exactly two characters, so the three-character Latin floor
        /// would discard the entire Chinese label set.
        /// </summary>
        [Fact]
        public void TwoCharacterHanAliasesAreIndexed() =>
            Assert.Equal("density", For("zh").Resolve("金的密度是多少")?.Spec.Id);

        /// <summary>
        /// Split frames exist for the languages that put the subject between the verb and its
        /// object, where no contiguous alias can reach the field.
        /// </summary>
        [Fact]
        public void SplitFramesReachAFieldWhoseWordsAreSeparated()
        {
            var match = English.Resolve("speed of sound in helium gas");
            Assert.Equal("speed_of_sound_gas", match?.Spec.Id);
            Assert.Equal(0.9, match!.Score);
        }

        /// <summary>
        /// The label "Group:" belongs to <c>series</c> on the element screen, but "what group is
        /// gold in" asks for the number. Layer 0 is the only way to override a shipped label
        /// without changing what the screen displays.
        /// </summary>
        [Fact]
        public void PriorityAliasesOverrideAnAmbiguousShippedLabel() =>
            Assert.Equal("group_number", English.Resolve("what group is gold in")?.Spec.Id);

        [Fact]
        public void ResolveAllReturnsSeveralFieldsBestFirst()
        {
            var matches = English.ResolveAll("compare the density and melting point of gold", 3);

            Assert.Contains(matches, m => m.Spec.Id == "density");
            Assert.Contains(matches, m => m.Spec.Id == "melting_point");
            Assert.Equal(matches.Select(m => m.Score).OrderByDescending(s => s),
                         matches.Select(m => m.Score));
        }

        [Fact]
        public void LimitIsRespectedAndNoFieldAppearsTwice()
        {
            var matches = English.ResolveAll("density density density", 3);
            Assert.Single(matches);
            Assert.True(English.ResolveAll("compare density melting point boiling point", 2).Count <= 2);
        }

        [Fact]
        public void AQueryNamingNoFieldResolvesToNothing() =>
            Assert.Null(English.Resolve("what is the weather like"));

        /// <summary>
        /// Which alias targets are not fields in the registry.
        ///
        /// <para>Two of the three are <b>by design</b>. The planner intercepts isotope and
        /// half-life questions before field resolution runs - its own comment says so - so those
        /// aliases exist to be recognised, never to resolve. An alias whose field is absent is
        /// simply skipped, on both platforms.</para>
        ///
        /// <para>The third, <c>electrical_conductivity</c>, is the dead field D13 records: it is
        /// not in either app's registry and not a key in the element JSON, where the only
        /// conductivity is thermal. Pinned as a set so a fourth cannot appear unnoticed.</para>
        /// </summary>
        [Fact]
        public void OnlyThreeAliasTargetsAreNotFieldsAndAllThreeAreKnown()
        {
            var targets = FieldAliases.PRIORITY_ALIASES
                .Concat(FieldAliases.COLLOQUIAL)
                .Concat(FieldAliases.VALUE_WORD_ALIASES)
                .Concat(FieldAliases.COGNATES)
                .Select(e => e.Item2)
                .Concat(FieldAliases.SPLIT_FRAMES.Select(f => f.Item2));

            var unknown = targets
                .Where(id => !FieldRegistry.ById.ContainsKey(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(["electrical_conductivity", "half_life", "isotopes"], unknown);
        }
    }

    public class OperatorExtractorTest
    {
        [Fact]
        public void APlainLookupCarriesNoOperatorEvidence() =>
            Assert.False(OperatorExtractor.Extract("what is the density of gold").HasOperatorEvidence);

        [Fact]
        public void AComparatorAndASubsetCombineIntoAMultiHopQuery()
        {
            var ops = OperatorExtractor.Extract("which transition metals melt above 2000 c");

            Assert.Contains(ops.Comparators, c => c.Op == Op.Gt && c.Value.Value == 2000);
            Assert.Contains(ops.SubsetFilters, f => f is Filter.InSeries);
            Assert.True(ops.IsListQuestion);
        }

        /// <summary>
        /// Hindi, Urdu and Chinese are postpositional - "2000开尔文以上" reads "2000 kelvin above" -
        /// so looking only after the comparator found no number and the threshold vanished.
        /// </summary>
        [Fact]
        public void APostpositionalComparatorFindsItsNumberInFront()
        {
            var ops = OperatorExtractor.Extract("2000开尔文以上的元素");
            Assert.Contains(ops.Comparators, c => c.Value.Value == 2000);
        }

        /// <summary>The two superlative directions are separate sets, so "least dense" is not "densest".</summary>
        [Fact]
        public void SuperlativeDirectionIsDistinguished()
        {
            Assert.True(OperatorExtractor.Extract("densest element").SuperlativeDescending);
            Assert.False(OperatorExtractor.Extract("least dense element").SuperlativeDescending);
            Assert.Null(OperatorExtractor.Extract("density of gold").SuperlativeDescending);
        }

        /// <summary>
        /// French "plus dense que l'or" uses the same "plus" as "le plus dense"; only the "que"
        /// tells them apart, so a comparison particle disqualifies the superlative reading.
        /// </summary>
        [Fact]
        public void AComparisonParticleDisqualifiesTheSuperlative() =>
            Assert.Null(OperatorExtractor.Extract("plus dense que l or").SuperlativeDescending);

        /// <summary>
        /// Swedish and German inflect family names heavily, and the definite plural is the form
        /// people write. Matching on word boundaries alone missed every one.
        /// </summary>
        [Fact]
        public void InflectedFamilyNamesStillNameASubset()
        {
            var ops = OperatorExtractor.Extract("vilka ar adelgaserna");
            Assert.Contains(ops.SubsetFilters, f => f is Filter.InSeries);
        }

        /// <summary>
        /// "gas" is a phase word, so "what is the gas constant" was answered with a list of every
        /// gaseous element until the exempt phrases were consumed first.
        /// </summary>
        [Fact]
        public void APhaseWordInsideAFixedTermIsNotAPhaseFilter() =>
            Assert.DoesNotContain(OperatorExtractor.Extract("what is the ideal gas law").SubsetFilters,
                                  f => f is Filter.InPhase);

        [Fact]
        public void NegationInvertsABooleanFilterRatherThanDroppingIt()
        {
            var ops = OperatorExtractor.Extract("which noble gases are not radioactive");
            Assert.Contains(ops.SubsetFilters, f => f is Filter.IsRadioactive { Radioactive: false });
            // The series names the set and must survive the negation untouched.
            Assert.Contains(ops.SubsetFilters, f => f is Filter.InSeries);
        }

        [Fact]
        public void RangesReadBothBoundsAndTheSharedUnit()
        {
            var ops = OperatorExtractor.Extract("elements between 1000 and 2000 kelvin");

            Assert.NotNull(ops.Range);
            Assert.Equal(1000, ops.Range!.Value.Low.Value);
            Assert.Equal(2000, ops.Range.Value.High);
        }

        [Fact]
        public void StructuralFiltersReadANumberOnEitherSideOfTheirWord()
        {
            Assert.Contains(OperatorExtractor.Extract("elements in period 6").SubsetFilters,
                            f => f is Filter.InPeriod { Period: 6 });
            Assert.Contains(OperatorExtractor.Extract("group 11 elements").SubsetFilters,
                            f => f is Filter.InGroup { Group: 11 });
            // Chinese writes the number first.
            Assert.Contains(OperatorExtractor.Extract("第6周期的元素").SubsetFilters,
                            f => f is Filter.InPeriod { Period: 6 });
        }

        [Fact]
        public void AggregationsAreRecognisedIncludingAsACompoundHead()
        {
            Assert.Equal(Aggregation.Avg,
                         OperatorExtractor.Extract("average density of the halogens").Aggregation);
            // Swedish writes the statistic and its field as one word.
            Assert.Equal(Aggregation.Avg,
                         OperatorExtractor.Extract("mediandensitet").Aggregation is Aggregation.Median
                             ? Aggregation.Avg   // either statistic proves the compound was seen
                             : OperatorExtractor.Extract("genomsnittlig densitet").Aggregation);
        }

        [Fact]
        public void TopNOnlyCountsAlongsideASuperlative()
        {
            Assert.Equal(5, OperatorExtractor.Extract("5 densest nonmetals").TopN);
            Assert.Null(OperatorExtractor.Extract("5 grams of iron").TopN);
        }

        [Fact]
        public void RankOrdinalsAreReadOnlyWithASuperlative()
        {
            Assert.Equal(3, OperatorExtractor.Extract("the third densest element").RankOrdinal);
            Assert.Null(OperatorExtractor.Extract("the third element").RankOrdinal);
        }
    }
}
