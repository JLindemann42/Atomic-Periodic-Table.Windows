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

            // 71 in Lexicon.kt: 56 lists, 14 maps, 1 set.
            Assert.Equal(71, vocabularies.Count);
            Assert.All(vocabularies, v => Assert.NotEmpty(v.Keys));
        }

        /// <summary>
        /// 2,867 entries in Lexicon.kt, 2,848 here. The nineteen missing are duplicate keys in four
        /// maps - SERIES_WORDS has "noble gas" twice, COMPARATIVE_ADJECTIVES has "tyngre" twice -
        /// and every one maps to the same value, so collapsing them is exactly what Kotlin's mapOf
        /// does. The generator reports them on every run; if a future duplicate ever maps to a
        /// *different* value it is called out as a conflict rather than silently resolved.
        /// </summary>
        [Fact]
        public void TotalEntryCountMatchesTheSourceLessItsDuplicateKeys() =>
            Assert.Equal(2848, AllVocabularies().Sum(v => v.Keys.Count()));

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
        /// The seven entries in Lexicon.kt that violate the file's own rule and are therefore dead
        /// vocabulary - on both platforms. See docs/porting-notes.md D13.
        ///
        /// <para>Pinned exactly rather than allowed by pattern, so an eighth cannot join them
        /// unnoticed. If they are ever folded upstream this test fails and gets deleted, which is
        /// the outcome it is arguing for.</para>
        /// </summary>
        private static readonly string[] KnownUnreachable =
        [
            "berätta nagot intressant",  // sv, half-folded already: "nagot" is folded, "berätta" is not
            "berätta om",                // sv "tell me about" - the overview trigger
            "neutrões",                  // pt
            "è ",                        // it yes/no opener
            "é ",                        // pt yes/no opener
            "más",                       // es
            "più",                       // it
        ];

        [Fact]
        public void OnlyTheSevenKnownEntriesCarryALatinDiacritic()
        {
            var offenders = new List<string>();

            foreach (var (name, keys) in AllVocabularies())
            {
                foreach (var key in keys)
                {
                    var folded = TextMatching.NormalizeForLookup(key);
                    if (!string.Equals(folded, key, StringComparison.Ordinal) &&
                        !KnownUnreachable.Contains(key, StringComparer.Ordinal))
                        offenders.Add($"{name}: \"{key}\" folds to \"{folded}\" and can never match");
                }
            }

            Assert.Empty(offenders);
        }

        /// <summary>
        /// The other half of the same claim: each known entry really is unreachable, so the list
        /// above is a record of a defect rather than a list of permitted spellings.
        /// </summary>
        [Fact]
        public void TheKnownUnreachableEntriesAreGenuinelyUnreachable()
        {
            foreach (var entry in KnownUnreachable)
                Assert.NotEqual(entry, TextMatching.NormalizeForLookup(entry));
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
