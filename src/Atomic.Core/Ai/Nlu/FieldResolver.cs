using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Nlu
{
    /// <summary>A field the query referred to, with how it was recognised.</summary>
    public sealed record FieldMatch(FieldSpec Spec, double Score, string Matched);

    /// <summary>
    /// Works out which element property a query is asking about.
    ///
    /// <para>Aliases come from three layers, in priority order:</para>
    /// <list type="number">
    /// <item><b>The app's own localized labels.</b> Every field's <c>LabelKey</c> points at a
    /// string the app already ships in twelve languages, so resolving those once per language
    /// yields a full set of translated field names for free - no new translation work, and
    /// strictly better coverage than hand-written keyword lists.</item>
    /// <item><b>Colloquial English phrasings</b> that no label covers ("how heavy", "how hot does
    /// it melt").</item>
    /// <item><b>The canonical field id itself</b>, so "atomic_mass" and "atomic mass" both work.</item>
    /// </list>
    /// </summary>
    public sealed class FieldResolver
    {
        private readonly IStringProvider _strings;
        private readonly Lazy<IReadOnlyList<KeyValuePair<string, string>>> _aliasesByLength;
        private readonly Lazy<HashSet<string>> _aliasKeys;
        private readonly Lazy<string[]> _singleWordAliases;

        public FieldResolver(IStringProvider strings)
        {
            _strings = strings;
            // Built once per language, and sorted once: resolveAll matches longest-first, and
            // Android re-sorts the whole ~600-entry table on every query.
            _aliasesByLength = new Lazy<IReadOnlyList<KeyValuePair<string, string>>>(
                () => BuildAliases().OrderByDescending(e => e.Key.Length).ToArray());
            _aliasKeys = new Lazy<HashSet<string>>(
                () => _aliasesByLength.Value.Select(e => e.Key).ToHashSet(StringComparer.Ordinal));
            _singleWordAliases = new Lazy<string[]>(
                () => _aliasKeys.Value.Where(a => !a.Contains(' ', StringComparison.Ordinal) && a.Length >= 5).ToArray());
        }

        /// <summary>
        /// Every field the query mentions, best match first. Longer aliases are matched first so
        /// "atomic radius" is not shadowed by "radius".
        /// </summary>
        public IReadOnlyList<FieldMatch> ResolveAll(string query, int limit = 3)
        {
            var normalized = TextMatching.NormalizeForLookup(query);
            var found = new Dictionary<string, FieldMatch>(StringComparer.Ordinal);
            var order = new List<string>();

            // The two discovery fields share every alias they have, so no alias table can separate
            // them - only the interrogative can. Decided before the scan, and scored above any
            // alias, because otherwise "when was helium discovered" is answered with a person.
            if (DiscoveryField(normalized) is { } discoveryId &&
                FieldRegistry.ById.TryGetValue(discoveryId, out var discoverySpec))
            {
                found[discoveryId] = new FieldMatch(discoverySpec, 0.95, "discovery");
                order.Add(discoveryId);
            }

            // Fields whose words the sentence pulls apart, before the contiguous aliases are tried
            // so the more specific reading wins.
            foreach (var (words, fieldId) in FieldAliases.SPLIT_FRAMES)
            {
                if (found.Count >= limit) break;
                if (found.ContainsKey(fieldId)) continue;
                if (words.Any(w => !TextMatching.ContainsWord(normalized, w))) continue;
                if (!FieldRegistry.ById.TryGetValue(fieldId, out var spec)) continue;

                found[fieldId] = new FieldMatch(spec, 0.9, string.Join(" ", words));
                order.Add(fieldId);
            }

            foreach (var (alias, fieldId) in _aliasesByLength.Value)
            {
                if (found.Count >= limit) break;
                if (found.ContainsKey(fieldId)) continue;
                if (!Mentions(normalized, alias)) continue;
                if (!FieldRegistry.ById.TryGetValue(fieldId, out var spec)) continue;

                // Longer aliases are more specific, so they score higher.
                found[fieldId] = new FieldMatch(spec, 0.6 + Math.Min(alias.Length, 20) / 50.0, alias);
                order.Add(fieldId);
            }

            // Insertion order before the sort, mirroring Kotlin's LinkedHashMap, so equal scores
            // break the same way on both platforms.
            return order.Select(id => found[id])
                .OrderByDescending(m => m.Score)
                .Take(limit)
                .ToList();
        }

        public FieldMatch? Resolve(string query) => ResolveAll(query, 1).FirstOrDefault();

        /// <summary>Every word of every alias, folded, and the fields it names: the speller's field words (roadmap 1.6).</summary>
        public IReadOnlyDictionary<string, HashSet<string>> AliasWords()
        {
            var words = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (alias, fieldId) in _aliasesByLength.Value)
            {
                foreach (var word in alias.Split(' '))
                {
                    if (word.Length == 0) continue;
                    if (!words.TryGetValue(word, out var ids)) words[word] = ids = new HashSet<string>(StringComparer.Ordinal);
                    ids.Add(fieldId);
                }
            }
            return words;
        }

        /// <summary>Whether <paramref name="word"/>, folded, is exactly the name of a field (roadmap 1.5).</summary>
        public bool IsFieldWord(string word) =>
            word.Length >= MinimumLength(word) && _aliasKeys.Value.Contains(word);

        /// <summary>
        /// Whether <paramref name="word"/>, folded, is the name of a field, or one with a grammatical
        /// ending: the words <see cref="Morphology"/> may split a compound into. The compound rule in
        /// <see cref="Inflects"/> is left out, or every compound would vouch for itself.
        /// </summary>
        public bool KnowsWord(string word)
        {
            if (IsFieldWord(word)) return true;
            if (word.Length < 5) return false;
            return _singleWordAliases.Value.Any(alias => Inflects(word, alias, compounds: false));
        }

        /// <summary>
        /// Which discovery field a question is really asking for, or null when it is not asking.
        ///
        /// <para>Requires an interrogative as well as the discovery word, so "where is gold found" -
        /// which is about abundance and shares the word "found" - falls through to the alias table
        /// untouched.</para>
        /// </summary>
        private static string? DiscoveryField(string normalized)
        {
            if (!Lexicon.DISCOVERY_WORDS.Any(w => TextMatching.ContainsWord(normalized, w)))
                return null;
            if (Lexicon.WHEN_WORDS.Any(w => TextMatching.ContainsWord(normalized, w)))
                return "year_discovered";
            if (Lexicon.WHO_WORDS.Any(w => TextMatching.ContainsWord(normalized, w)))
                return "discovered_by";
            return null;
        }

        /// <summary>
        /// Whether an alias appears in the query.
        ///
        /// <para>Also accepts a short grammatical ending on the alias, because the shipped labels
        /// give the base form while users write the inflected one - Swedish "smältpunkten" for the
        /// label "Smältpunkt", German "Dichte" in "der Dichte des Goldes".</para>
        /// </summary>
        private static bool Mentions(string normalizedQuery, string alias)
        {
            if (alias.Length < MinimumLength(alias)) return false;
            if (TextMatching.ContainsToken(normalizedQuery, normalizedQuery, alias)) return true;

            // A multiword alias inflects on its last word: "atomic mass" becomes "atomic masses",
            // "melting point" becomes "melting points". Requiring the whole phrase verbatim meant
            // every plural form of the most-asked fields resolved nothing, which is how "sum of the
            // atomic masses of the halogens" ended up retrieving a dataset row.
            if (alias.Contains(' ', StringComparison.Ordinal))
                return MentionsInflectedPhrase(normalizedQuery, alias);

            if (alias.Length < 5) return false;
            return TextMatching.SplitQueryTokens(normalizedQuery).Any(word => Inflects(word, alias));
        }

        /// <summary>
        /// Whether a word is the alias carrying a grammatical ending.
        ///
        /// <para>Four shapes, because four of the supported languages need four different ones:</para>
        /// <list type="bullet">
        /// <item>a plain suffix ("melting point" -&gt; "melting points", "smältpunkt" -&gt;
        /// "smältpunkten");</item>
        /// <item>the English <c>y</c>/<c>ies</c> plural, without which "and their <b>densities</b>"
        /// resolved no field at all and the follow-up inherited the previous turn's boiling point;</item>
        /// <item>a Germanic compound whose head noun is the field: "medianatommassan" is "median" +
        /// "atommassa" + the definite ending. Nothing matching from the front could see it, so the
        /// aggregation was recognised and the field it applied to was not;</item>
        /// <item>syncope, where an unstressed final vowel is dropped before the ending. Swedish
        /// "atomnummer" becomes "atomnumret", which is the <i>same length</i> as the alias, so the
        /// suffix rule could not see it and every "vad är atomnumret för guld" was declined.</item>
        /// </list>
        /// </summary>
        private static bool Inflects(string word, string alias, bool compounds = true)
        {
            if (word.Length > alias.Length &&
                word.Length - alias.Length <= MaxInflectionSuffix &&
                word.StartsWith(alias, StringComparison.Ordinal))
                return true;

            if (alias.EndsWith('y') &&
                string.Equals(word, alias[..^1] + "ies", StringComparison.Ordinal))
                return true;

            if (compounds && alias.Length >= MinCompoundHead)
            {
                var at = word.LastIndexOf(alias, StringComparison.Ordinal);
                if (at >= MinCompoundModifier &&
                    word.Length - (at + alias.Length) <= MaxInflectionSuffix)
                    return true;
            }

            if (Syncopate(alias) is not { } syncopated) return false;
            return word.Length > syncopated.Length &&
                   word.Length - syncopated.Length <= MaxInflectionSuffix &&
                   word.StartsWith(syncopated, StringComparison.Ordinal);
        }

        /// <summary>The alias with the vowel of its final syllable elided, or null when it has none.</summary>
        private static string? Syncopate(string alias)
        {
            if (alias.Length < MinSyncopeLength) return null;
            var vowelAt = alias.Length - 2;
            if (!IsSyncopeVowel(alias[vowelAt])) return null;
            if (IsSyncopeVowel(alias[^1])) return null;
            if (IsSyncopeVowel(alias[vowelAt - 1])) return null;
            return alias.Remove(vowelAt, 1);
        }

        /// <summary>
        /// A multiword alias whose final word carries a grammatical ending.
        ///
        /// <para>Only one word may inflect, and only by a short suffix, so "melting point" matches
        /// "melting points" without "melting" matching anything it should not.</para>
        /// </summary>
        private static bool MentionsInflectedPhrase(string normalizedQuery, string alias)
        {
            var parts = alias.Split(' ').Where(p => p.Length > 0).ToList();
            if (parts.Count < 2) return false;

            var words = TextMatching.SplitQueryTokens(normalizedQuery);
            for (var start = 0; start <= words.Count - parts.Count; start++)
            {
                var window = words.Skip(start).Take(parts.Count).ToList();
                if (InflectsAt(window, parts, parts.Count - 1)) return true;

                // The Romance languages put the head noun first and inflect *it*: "point de fusion"
                // pluralises to "points de fusion", not "point de fusions". Only the tail was
                // allowed to carry an ending, so "et leurs points de fusion" resolved no melting
                // point and the follow-up silently answered with the previous turn's field.
                if (InflectsAt(window, parts, 0)) return true;
            }
            return false;
        }

        /// <summary>The window matches the alias exactly but for a short ending on the word at index.</summary>
        private static bool InflectsAt(IReadOnlyList<string> window, IReadOnlyList<string> parts, int index)
        {
            var stem = parts[index];
            if (stem.Length < MinInflectableWord) return false;

            for (var i = 0; i < parts.Count; i++)
            {
                if (i == index) continue;
                if (!string.Equals(window[i], parts[i], StringComparison.Ordinal)) return false;
            }

            var actual = window[index];
            // >=, not >: an *exact* window match has to succeed here too. The literal check in
            // Mentions compares strings, so it misses a phrase the user wrote with different
            // punctuation - French writes "énergie d'ionisation" and the alias is spelled with a
            // space, so the whole phrase resolved nothing. Matching over tokens is punctuation-blind.
            return actual.Length >= stem.Length &&
                   actual.Length - stem.Length <= MaxInflectionSuffix &&
                   actual.StartsWith(stem, StringComparison.Ordinal);
        }

        private Dictionary<string, string> BuildAliases()
        {
            var map = new Dictionary<string, string>(512, StringComparer.Ordinal);

            void Add(string alias, string fieldId)
            {
                var key = TextMatching.NormalizeForLookup(alias).Trim();
                if (key.Length < MinimumLength(key)) return;
                // First writer wins, so the localized label outranks the generic fallbacks.
                map.TryAdd(key, fieldId);
            }

            // Layer 0 - the handful of aliases a shipped label claims but does not mean.
            //
            // `series` uses the label "Group:" (as in "Group: Transition metal"), so Layer 1
            // registers the bare word "group" to it and, because the first writer wins, nothing
            // later can correct that. But "what group is gold in" asks for the numbered group.
            // Registering these first is the only way to override a label without changing what the
            // element screen displays.
            foreach (var (alias, fieldId) in FieldAliases.PRIORITY_ALIASES) Add(alias, fieldId);

            // Layer 1 - the app's own localized labels, in whatever language the provider resolves.
            foreach (var spec in FieldRegistry.All)
            {
                var label = Try.OrNull(() => _strings.Get(spec.LabelKey));
                if (label is null) continue;

                var cleaned = label.Replace(":", string.Empty, StringComparison.Ordinal)
                                   .Replace("：", string.Empty, StringComparison.Ordinal)
                                   .Trim();
                if (cleaned.Length > 0 && !cleaned.StartsWith("str:", StringComparison.Ordinal))
                    Add(cleaned, spec.Id);
            }

            // Layer 2 - colloquial phrasings the labels do not cover.
            foreach (var (alias, fieldId) in FieldAliases.COLLOQUIAL) Add(alias, fieldId);

            // Layer 2a - words naming a value of a categorical field, which is how a yes/no
            // question refers to that field.
            foreach (var (alias, fieldId) in FieldAliases.VALUE_WORD_ALIASES) Add(alias, fieldId);

            // Layer 2b - near-cognates for the most-asked fields. Several locales ship an
            // incomplete strings.xml and fall back to the English label, so a user writing their
            // own language would otherwise get nothing; Filipino, for instance, displays "Density".
            foreach (var (alias, fieldId) in FieldAliases.COGNATES) Add(alias, fieldId);

            // Layer 3 - the canonical ids themselves.
            foreach (var spec in FieldRegistry.All)
            {
                Add(spec.Id.Replace('_', ' '), spec.Id);
                Add(spec.Id, spec.Id);
            }

            return map;
        }

        /// <summary>Shortest final word of a phrase that may carry an ending; below this it over-matches.</summary>
        private const int MinInflectableWord = 4;

        /// <summary>Longest grammatical ending accepted on a field label, e.g. "smältpunkt" + "en".</summary>
        private const int MaxInflectionSuffix = 3;

        /// <summary>Shortest alias worth trying to syncopate; below this the result is not a stem.</summary>
        private const int MinSyncopeLength = 5;

        /// <summary>Shortest alias that may be the head of a Germanic compound.</summary>
        private const int MinCompoundHead = 7;

        /// <summary>Shortest modifier that may precede that head, so "density" is not found in "adensity".</summary>
        private const int MinCompoundModifier = 4;

        /// <summary>
        /// A char test rather than <c>"aeiouy".Contains(c)</c>, which the culture analysers reject
        /// - correctly, since a string search here would be ordinal only by accident.
        /// </summary>
        private static bool IsSyncopeVowel(char c) =>
            c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

        /// <summary>
        /// Shortest alias worth indexing, by script.
        ///
        /// <para>Three characters is a sensible floor for alphabetic scripts, but a Han word is
        /// often exactly two characters - 密度 is "density" in full - so the Latin threshold would
        /// discard the entire Chinese label set.</para>
        /// </summary>
        private static int MinimumLength(string alias) => alias.Any(UnicodeScripts.IsHan) ? 2 : 3;
    }
}
