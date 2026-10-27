using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Ai.Retrieval;
using Xunit;

namespace Atomic.Core.Tests.Ai.Nlu
{
    /// <summary>
    /// The generated lexicon.
    ///
    /// <para>No Kotlin equivalent - Android trusts the file because a human wrote it. Here it comes
    /// out of a converter, so the invariant that makes it work at all is worth asserting: every
    /// entry has to be in the shape <c>Tokenizer.Normalize</c> produces, or it can never match.</para>
    /// </summary>
    public class LexiconTest
    {
        private static IEnumerable<(string Name, IEnumerable<string> Keys)> AllVocabularies()
        {
            foreach (var field in typeof(Lexicon).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = field.GetValue(null);
                yield return value switch
                {
                    IReadOnlyList<string> list => (field.Name, list),
                    IReadOnlySet<string> set => (field.Name, set),
                    IEnumerable<KeyValuePair<string, string>> m => (field.Name, m.Select(e => e.Key)),
                    _ => (field.Name, KeysOf(value)),
                };
            }
        }

        /// <summary>The keys of any FrozenDictionary&lt;string, T&gt;, whatever T is.</summary>
        private static IEnumerable<string> KeysOf(object? value)
        {
            var keys = value?.GetType().GetProperty("Keys")?.GetValue(value);
            return keys is IEnumerable<string> strings ? strings : [];
        }

        [Fact]
        public void EveryDeclarationSurvivedTheConversion()
        {
            var vocabularies = AllVocabularies().ToList();

            // 80 in Lexicon.kt: 61 lists, 18 maps, 1 set (STOPWORDS joined in roadmap 1.1, the
            // compound vocabulary in 1.2, COMPOUND_HEADS and SUPERLATIVE_STEMS in 1.5).
            Assert.Equal(80, vocabularies.Count);
            Assert.All(vocabularies, v => Assert.NotEmpty(v.Keys));
        }

        /// <summary>
        /// 3,265 entries in Lexicon.kt, 3,246 here (STOPWORDS counts its twelve languages). The nineteen missing are duplicate keys in four
        /// maps - SERIES_WORDS has "noble gas" twice, COMPARATIVE_ADJECTIVES has "tyngre" twice -
        /// and every one maps to the same value, so collapsing them is exactly what Kotlin's mapOf
        /// does. The generator reports them on every run; if a future duplicate ever maps to a
        /// *different* value it is called out as a conflict rather than silently resolved.
        /// </summary>
        [Fact]
        public void TotalEntryCountMatchesTheSourceLessItsDuplicateKeys() =>
            Assert.Equal(3246, AllVocabularies().Sum(v => v.Keys.Count()));

        /// <summary>
        /// The invariant the whole file rests on. Entries are matched against text that has already
        /// been lowercased and had its Latin diacritics stripped, so an accented entry is
        /// unreachable - "större än" can never equal the "storre an" the tokenizer emits.
        ///
        /// <para>Non-Latin scripts are exempt and must stay exactly as written: normalisation
        /// deliberately leaves Devanagari, Arabic and Han alone, because their combining marks are
        /// vowels rather than accents.</para>
        /// </summary>
        /// <summary>
        /// The only entries allowed to carry a Latin diacritic, and the reason they are allowed.
        ///
        /// <para>Both are Romance copulas in <c>YESNO_OPENERS</c>, which
        /// <c>QueryPlanner.ComparativePlan</c> tests against the <b>raw</b> query as well as the
        /// folded one - Italian <c>è</c> ("is") and Portuguese <c>é</c> ("is") differ from the
        /// conjunction <c>e</c> ("and") by nothing but the accent. Fold them and "e le loro
        /// densità" becomes a yes/no question. The corpus proves it: folding these two flips three
        /// conversation rows in <c>it</c> and <c>pt</c> from COMPARISON to COMPARATIVE.</para>
        ///
        /// <para>Android writes five <i>other</i> accented entries that really are dead, because
        /// they are only ever compared against folded text. The generator folds those on the way
        /// out (D13), so they no longer appear here - which is what this test's emptiness now
        /// means.</para>
        /// </summary>
        private static readonly string[] AccentedByDesign =
        [
            "è ",  // it "is" - against "e " ("and")
            "é ",  // pt "is" - against "e " ("and")
        ];

        /// <summary>
        /// The invariant the whole file rests on: every entry is already folded, except the two
        /// that are deliberately matched against unfolded text.
        /// </summary>
        [Fact]
        public void OnlyTheTwoRawTextOpenersCarryALatinDiacritic()
        {
            var offenders = new List<string>();

            foreach (var (name, keys) in AllVocabularies())
            {
                foreach (var key in keys)
                {
                    var folded = TextMatching.NormalizeForLookup(key);
                    if (!string.Equals(folded, key, StringComparison.Ordinal) &&
                        !AccentedByDesign.Contains(key, StringComparer.Ordinal))
                        offenders.Add($"{name}: \"{key}\" folds to \"{folded}\" and can never match");
                }
            }

            Assert.Empty(offenders);
        }

        /// <summary>
        /// The two exempt entries live only in <c>YESNO_OPENERS</c>.
        ///
        /// <para>The exemption is granted per declaration, because it is a fact about how that one
        /// list is used. An accented entry appearing anywhere else is dead vocabulary, and this is
        /// what stops the exemption being read as a licence to spell entries however they came.</para>
        /// </summary>
        [Fact]
        public void TheAccentedEntriesAppearOnlyWhereTheRawQueryIsMatched()
        {
            foreach (var entry in AccentedByDesign)
            {
                Assert.NotEqual(entry, TextMatching.NormalizeForLookup(entry));
                Assert.True(Lexicon.YESNO_OPENERS.Contains(entry), entry);

                foreach (var (name, keys) in AllVocabularies())
                {
                    if (string.Equals(name, nameof(Lexicon.YESNO_OPENERS), StringComparison.Ordinal))
                        continue;
                    Assert.False(keys.Contains(entry, StringComparer.Ordinal),
                        $"{name} carries \"{entry}\", which is folded away before it is compared");
                }
            }
        }

        /// <summary>
        /// The five entries the generator folds, proven reachable in their folded shape.
        ///
        /// <para>Two of them were genuinely lost vocabulary on Android - Portuguese "neutrões" and
        /// the Swedish defer trigger. The other three had a folded twin already sitting in the same
        /// list, so folding them only produced a duplicate; they are kept here because the
        /// assertion is about the vocabulary being reachable, not about which fix mattered.</para>
        /// </summary>
        [Fact]
        public void TheFoldedEntriesAreReachableInTheirFoldedShape()
        {
            Assert.Contains("neutroes", Lexicon.NEUTRON_WORDS);
            Assert.Contains("beratta nagot intressant", Lexicon.DEFER_TO_PERSONALITY);
            Assert.Contains("beratta om", Lexicon.OVERVIEW_WORDS);
            Assert.Contains("mas", Lexicon.AMBIGUOUS_DEGREE);
            Assert.Contains("piu", Lexicon.AMBIGUOUS_DEGREE);
        }

        /// <summary>
        /// A trailing space is deliberate in several entries - NEGATIONS holds "not ", "inte ",
        /// "nicht " so the match is a prefix rather than a substring, which is what keeps "not" out
        /// of "notable". A leading space or an empty entry would be a conversion bug.
        /// </summary>
        [Fact]
        public void NoEntryIsEmptyOrLeadingPadded()
        {
            var offenders = AllVocabularies()
                .SelectMany(v => v.Keys.Select(k => (v.Name, Key: k)))
                .Where(e => e.Key.Length == 0 || e.Key.TrimStart().Length != e.Key.Length)
                .Select(e => $"{e.Name}: \"{e.Key}\"")
                .ToList();

            Assert.Empty(offenders);
        }

        /// <summary>
        /// The separation the legacy agent lacked. It kept superlatives in one bag, so it could not
        /// tell "densest" from "least dense" and re-tested with English Contains checks inside the
        /// handler; an overlap here would put that back.
        /// </summary>
        [Fact]
        public void TheTwoSuperlativeDirectionsDoNotOverlap()
        {
            var both = Lexicon.MOST.Intersect(Lexicon.LEAST, StringComparer.Ordinal).ToList();
            Assert.Empty(both);
        }

        [Fact]
        public void ComparatorsDoNotOverlapEither()
        {
            var both = Lexicon.GREATER.Intersect(Lexicon.LESS, StringComparer.Ordinal).ToList();
            Assert.Empty(both);
        }

        [Fact]
        public void EnumValuedMapsResolvedToRealMembers()
        {
            Assert.Contains(Atomic.Core.Ai.Data.SeriesId.NobleGas, Lexicon.SERIES_WORDS["noble gas"]);
            Assert.Equal(Atomic.Core.Ai.Data.Block.S, Lexicon.BLOCK_WORDS["s-block"]);
            Assert.Equal(Atomic.Core.Ai.Data.FieldCategory.Thermo,
                         Lexicon.CATEGORY_WORDS.First(e => e.Value == Atomic.Core.Ai.Data.FieldCategory.Thermo).Value);
        }

        [Fact]
        public void ComparativeAdjectivesCarryAFieldAndADirection()
        {
            var (field, ascending) = Lexicon.COMPARATIVE_ADJECTIVES["denser"];
            Assert.Equal("density", field);
            Assert.True(ascending);

            var (lighterField, lighterAscending) = Lexicon.COMPARATIVE_ADJECTIVES["lighter"];
            Assert.Equal("density", lighterField);
            Assert.False(lighterAscending);
        }

        /// <summary>
        /// Every field named by a comparative adjective has to exist, or the planner produces a plan
        /// the executor cannot run - and the query is declined for a reason no message explains.
        ///
        /// <para>One entry already fails that: "conducts electricity better" maps to
        /// <c>electrical_conductivity</c>, which is not a field in either app's registry and not a
        /// key in the element JSON, where the only conductivity is thermal. Dead on both platforms;
        /// recorded as D13. Pinned as the single exception so a second cannot be added silently.</para>
        /// </summary>
        [Fact]
        public void OnlyOneComparativeAdjectiveNamesAFieldThatDoesNotExist()
        {
            var unknown = Lexicon.COMPARATIVE_ADJECTIVES
                .Where(e => !Atomic.Core.Ai.Data.FieldRegistry.ById.ContainsKey(e.Value.Field))
                .Select(e => $"{e.Key} -> {e.Value.Field}")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(["conducts electricity better -> electrical_conductivity"], unknown);
        }

        [Fact]
        public void MultiLineMapValuesParsedIntoLists()
        {
            var nuclide = Lexicon.APP_FEATURE_WORDS["nuclide_table"];
            Assert.Contains("nuclide", nuclide);
            Assert.Contains("nuklidkarta", nuclide);
            Assert.Contains("核素表", nuclide);
        }
    }
}
