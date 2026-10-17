using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Retrieval
{
    /// <summary>What an alias identifies.</summary>
    public enum AliasKind { Name, Symbol, Number }

    /// <summary>A name the resolver knows about, mapped to the English element key it identifies.</summary>
    public sealed record ElementAlias(string Surface, string Key, string Language, AliasKind Kind);

    /// <summary>A resolved element mention, with how confident the match is.</summary>
    public sealed record ElementMatch(string Key, double Score, string Matched);

    /// <summary>
    /// Finds which elements a query mentions, across all twelve languages.
    ///
    /// <para>Two hazards drive the design:</para>
    /// <list type="number">
    /// <item><b>Two-letter symbols collide with ordinary words.</b> Swedish <c>är</c> normalises to
    /// <c>ar</c>, which is argon's symbol; English <c>in</c> is indium; Filipino <c>ng</c> and
    /// <c>sa</c> are common particles.</item>
    /// <item><b>Chinese element names are single characters that recur inside ordinary words.</b>
    /// 金 is gold, but it also opens 金属 ("metal"); 铁 is iron but appears throughout compounds. A
    /// single Han character therefore only counts as an element mention when it stands as the whole
    /// query or is not absorbed into a longer run of Han characters.</item>
    /// </list>
    /// </summary>
    public sealed class EntityResolver
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<ElementAlias>> _aliases;
        private readonly string _activeLanguage;

        /// <summary>
        /// Flattened once at construction rather than per query. Android re-flattens and re-sorts
        /// the whole alias map on every call; with ~3,000 aliases that is a sort per keystroke in a
        /// live-search box.
        /// </summary>
        private readonly ElementAlias[] _byLengthDescending;

        private readonly FrozenSet<char> _singleHanElements;

        public EntityResolver(
            IReadOnlyDictionary<string, IReadOnlyList<ElementAlias>> aliases,
            string activeLanguage = "en")
        {
            _aliases = aliases;
            _activeLanguage = activeLanguage;

            _byLengthDescending = aliases.Values
                .SelectMany(list => list)
                .OrderByDescending(a => a.Surface.Length)
                .ToArray();

            // The Han characters that are themselves a whole element name.
            //
            // They act as boundaries for the same reason the particles do: 金银 is "gold and
            // silver", not a compound word, and treating 银 as compounding meant 比较金银和铜
            // resolved copper alone - a three-way comparison answered about one element. 金属
            // ("metal") is unaffected, because 属 is not an element.
            _singleHanElements = aliases
                .Where(e => e.Key.Length == 1 && Tokenizer.IsHan(e.Key[0]) &&
                            e.Value.Any(a => a.Kind == AliasKind.Name))
                .Select(e => e.Key[0])
                .ToFrozenSet();
        }

        /// <summary>Symbols that are far more likely to be a scientist or unit name than an element.</summary>
        private static readonly FrozenSet<string> SymbolBlocklist = new[]
        {
            "poisson", "planck", "boltzmann", "celsius", "fahrenheit", "kelvin", "newton",
            "pascal", "faraday", "curie", "avogadro", "coulomb", "joule", "watt", "volt",
        }.ToFrozenSet(StringComparer.Ordinal);

        /// <summary>
        /// Resolve every element mentioned in a query, best match first.
        /// </summary>
        /// <param name="limit">Maximum number of distinct elements to return.</param>
        public IReadOnlyList<ElementMatch> ResolveAll(string query, int limit = 4)
        {
            var raw = query.ToLowerInvariant();
            var normalized = TextMatching.NormalizeForLookup(query);
            var words = TextMatching.SplitQueryTokens(normalized);
            var found = new Dictionary<string, ElementMatch>(StringComparer.Ordinal);
            var order = new List<string>();

            // Atomic number, but only when the query frames it as one.
            if (AtomicNumberIn(raw) is { } number)
            {
                var text = number.ToString(CultureInfo.InvariantCulture);
                if (_aliases.TryGetValue(text, out var numberAliases))
                {
                    var alias = numberAliases.FirstOrDefault(a => a.Kind == AliasKind.Number);
                    if (alias is not null)
                    {
                        found[alias.Key] = new ElementMatch(alias.Key, 1.0, text);
                        order.Add(alias.Key);
                    }
                }
            }

            // Longest surface forms first so "carbon dioxide" cannot be shadowed by "carbon".
            foreach (var alias in _byLengthDescending)
            {
                if (found.Count >= limit) break;
                if (found.ContainsKey(alias.Key)) continue;
                if (!Matches(alias, raw, normalized, words, query)) continue;
                found[alias.Key] = new ElementMatch(alias.Key, ScoreOf(alias), alias.Surface);
                order.Add(alias.Key);
            }

            if (found.Count == 0 && FuzzyMatch(words) is { } fuzzy)
            {
                found[fuzzy.Key] = fuzzy;
                order.Add(fuzzy.Key);
            }

            // Insertion order preserved before the sort, mirroring Kotlin's LinkedHashMap, so a
            // score tie resolves the same way on both platforms.
            return order.Select(k => found[k])
                .OrderByDescending(m => m.Score)
                .Take(limit)
                .ToList();
        }

        /// <summary>
        /// An element number the query asks for that the periodic table does not have, or null.
        ///
        /// <para>"Tell me about element 119" resolved nothing, fell through to lexical retrieval
        /// and came back with the mass of an electron. The number is the whole point of the
        /// question, so the honest response needs to know it was out of range rather than merely
        /// unmatched.</para>
        /// </summary>
        public int? OutOfRangeElementNumber(string query)
        {
            var match = SmallNumber.Match(query);
            if (!match.Success) return null;
            if (match.Index > 0 && query[match.Index - 1] == '-') return null;
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                              out var number)) return null;
            if (number is >= 1 and <= 118) return null;

            var lower = query.ToLowerInvariant();
            return ElementNumberFrames.Any(f => lower.Contains(f, StringComparison.Ordinal))
                ? number
                : null;
        }

        /// <summary>The single best element match, or null when the query names none.</summary>
        public ElementMatch? Resolve(string query) => ResolveAll(query, limit: 1).FirstOrDefault();

        /// <summary>
        /// Which language the user is writing in, judged by whose element names they used.
        ///
        /// <para>The alias table already holds every shipped language's names, so the detector is a
        /// tally over it rather than a separate model: "vad är guldets densitet" contains a Swedish
        /// element name and nothing else identifying, which is exactly the evidence available.</para>
        ///
        /// <para>A tie keeps the current language. Spurious switching is worse than not switching -
        /// most element names are shared across several languages, and a thread that flips language
        /// every third turn reads as broken even when each individual guess was defensible.</para>
        /// </summary>
        /// <param name="active">The language in play, returned unchanged when nothing is decisive.</param>
        public string DetectLanguage(string query, string active)
        {
            var lower = query.ToLowerInvariant();
            var normalized = TextMatching.NormalizeForLookup(query);
            var scores = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var word in TextMatching.SplitQueryTokens(lower))
            {
                var folded = TextMatching.NormalizeForLookup(word);
                if (!_aliases.TryGetValue(folded, out var matches) &&
                    !_aliases.TryGetValue(word, out matches)) continue;

                Score(matches, scores);
            }

            // Nothing matched token-by-token, so fall back to scanning the whole query. The scripts
            // that write no spaces need this: 金的密度 is one token to a splitter and three
            // characters to a containment check.
            if (scores.Count == 0)
            {
                foreach (var (surface, matches) in _aliases)
                {
                    if (!TextMatching.ContainsToken(lower, normalized, surface)) continue;
                    Score(matches, scores);
                }
            }

            if (scores.Count == 0) return active;

            var best = scores.Values.Max();
            var leaders = scores.Where(e => e.Value == best).Select(e => e.Key).ToList();

            if (leaders.Contains(active, StringComparer.OrdinalIgnoreCase)) return active;

            // Ordinal, so the choice between two equally-distinctive leaders is at least stable
            // rather than dependent on dictionary iteration order. Android takes whichever the set
            // happens to yield first, which is not.
            leaders.Sort(StringComparer.Ordinal);

            // English loses a tie it is only in by coincidence. We are here because the active
            // language is not among the leaders, so the user is not reading English either, and a
            // name shared by English and one other language is evidence for the other one - which
            // is the rule Android applies. Without this, leaders {en, sv} sorted ordinally answer
            // a Swede in English.
            return leaders.FirstOrDefault(l => !string.Equals(l, "en", StringComparison.Ordinal))
                   ?? leaders[0];
        }

        /// <summary>
        /// Credit a matched surface to the language that actually claims it.
        /// </summary>
        /// <remarks>
        /// <para><b>Only a surface claimed by exactly one language is evidence.</b> Element names
        /// are shared across these twelve far more often than not - "gold" is the English, German
        /// and Afrikaans name, "argon" and "platinum" are near-universal - and a name several
        /// languages share tells you nothing about which one the user is writing in.</para>
        ///
        /// <para>The previous rule scored every claimant and then broke ties by preferring any
        /// non-English leader, on the reasoning that "guld" is Swedish evidence even though English
        /// also lists the key. That is true for a distinctive surface and false for a shared one:
        /// it made "electron configuration of gold" answer in German, because German ties with
        /// English on "gold" and English was then discarded on purpose. Scoring distinctiveness
        /// instead keeps the "guld" case working - Swedish alone claims it - and leaves a shared
        /// name contributing nothing, so the query falls through to the active language.</para>
        /// </remarks>
        private static void Score(
            IReadOnlyList<ElementAlias> matches, Dictionary<string, int> scores)
        {
            string? only = null;

            foreach (var alias in matches)
            {
                if (alias.Kind != AliasKind.Name) continue;

                if (only is null)
                {
                    only = alias.Language;
                    continue;
                }

                // A second language claims the same spelling, so it distinguishes nothing.
                if (!string.Equals(only, alias.Language, StringComparison.Ordinal)) return;
            }

            if (only is null) return;
            scores[only] = scores.TryGetValue(only, out var n) ? n + 1 : 1;
        }

        private bool Matches(
            ElementAlias alias,
            string rawQuery,
            string normalizedQuery,
            IReadOnlyList<string> words,
            string originalQuery)
        {
            var surface = alias.Surface;
            if (string.IsNullOrWhiteSpace(surface)) return false;
            if (SymbolBlocklist.Contains(surface)) return false;

            // Atomic numbers are resolved only by AtomicNumberIn, which checks that the query
            // frames the number as an element number at all. Matching them here as well bypassed
            // that check completely: "if I have 12 grams of carbon-12, how many moles is that"
            // resolved magnesium, because 12 is magnesium's atomic number and a bare token match
            // asks no further questions.
            if (alias.Kind == AliasKind.Number) return false;

            // A name that only appears inside a fixed term naming something else.
            if (alias.Kind == AliasKind.Name &&
                NameExemptPhrases.Any(p => p.Contains(surface, StringComparison.Ordinal) &&
                                           normalizedQuery.Contains(p, StringComparison.Ordinal)))
                return false;

            if (alias.Kind == AliasKind.Symbol)
            {
                if (IsCommonWordCollision(surface, normalizedQuery, words, _activeLanguage)) return false;

                // A short symbol must be *written* as a symbol.
                //
                // Once punctuation is split off, a one- or two-letter surface collides with
                // ordinary words in twelve languages at once: the "s" of "tungsten's" is sulfur,
                // the French "or" is gold inside an English sentence, and the Swedish "är"
                // normalises to "ar", which is argon. Each turned a plain lookup into a two-element
                // comparison.
                //
                // Capitalisation is the signal that separates them, because it is a convention
                // users already follow - nobody writes the element as "fe" mid-sentence, and
                // everybody writes "W". Requiring it also *gains* a case the previous rule lost:
                // "atomic mass of W" was being missed entirely.
                if (surface.Length <= MaxCaseSensitiveSymbol && words.Count > 2 &&
                    !AppearsAsSymbol(surface, originalQuery))
                    return false;

                // A blocklisted word containing the symbol, e.g. "poisson" for S.
                if (SymbolBlocklist.Any(b => b.Contains(surface, StringComparison.Ordinal) &&
                                             normalizedQuery.Contains(b, StringComparison.Ordinal)))
                    return false;
            }

            if (Tokenizer.IsUnspacedScript(surface))
                return MatchesUnspaced(surface, originalQuery);

            var foreign = !string.Equals(alias.Language, _activeLanguage, StringComparison.Ordinal) &&
                          !string.Equals(alias.Language, BaseLanguage, StringComparison.Ordinal);

            // A short name from a language the user is not writing in is a collision waiting to
            // happen: French "or" is gold and appears in every English disjunction, Italian "oro"
            // and Spanish "ne" are the same hazard. An exact match used to bypass the language
            // guard below, so the word "or" in an English question resolved gold and turned a
            // lookup into a comparison. Names only. A chemical symbol belongs to no language -
            // "Au" is gold in all twelve - so applying a foreign-name floor to symbols silently
            // broke every "melting point of Fe".
            if (foreign && alias.Kind == AliasKind.Name && surface.Length < MinForeignName) return false;

            if (TextMatching.ContainsToken(rawQuery, normalizedQuery, surface)) return true;

            // Inflection tolerance is confined to the language being spoken (plus English, which
            // supplies the canonical keys). An exact name from any language still resolves - a
            // Swede writing "gold" is fine - but a *stem* from an unrelated language is how "silver
            // chloride" became two elements: German "Chlor" is within the suffix tolerance of
            // English "chloride", so a compound's name was read as a comparison of its constituents.
            if (foreign) return false;
            return MatchesInflected(surface, words);
        }

        /// <summary>
        /// A name with the vowel of its final syllable elided, or null when it has no such syllable.
        ///
        /// <para>Recognises only the consonant-vowel-consonant ending that actually syncopates
        /// ("kisel", "kvicksilver", German "Wasser"), so names that simply end in a vowel are
        /// untouched.</para>
        /// </summary>
        private static string? Syncopated(string surface)
        {
            if (surface.Length < MinSyncopeLength) return null;
            var vowelAt = surface.Length - 2;
            if (!SyncopeVowels.Contains(surface[vowelAt])) return null;
            if (SyncopeVowels.Contains(surface[^1])) return null;
            if (SyncopeVowels.Contains(surface[vowelAt - 1])) return null;
            return surface.Remove(vowelAt, 1);
        }

        /// <summary>
        /// True when the surface appears in the original query written the way a chemical symbol is
        /// written: a capital first letter, lower-case remainder, standing as its own word.
        ///
        /// <para>Case is read from the <i>original</i> query rather than the normalised one, which
        /// is the only place it survives. A query shouted in capitals ("WHAT IS THE ATOMIC MASS OF
        /// W") would satisfy this for every two-letter word in it, so an all-caps query is treated
        /// as carrying no case information at all and the symbol is declined.</para>
        /// </summary>
        private static bool AppearsAsSymbol(string surface, string originalQuery)
        {
            if (!originalQuery.Any(char.IsLower)) return false;

            var expected = char.ToUpperInvariant(surface[0]) + surface[1..];
            var from = 0;
            while (true)
            {
                var at = originalQuery.IndexOf(expected, from, StringComparison.Ordinal);
                if (at < 0) return false;
                var before = at > 0 ? originalQuery[at - 1] : (char?)null;
                var afterIndex = at + expected.Length;
                var after = afterIndex < originalQuery.Length ? originalQuery[afterIndex] : (char?)null;
                if ((before is null || !char.IsLetterOrDigit(before.Value)) &&
                    (after is null || !char.IsLetterOrDigit(after.Value)))
                    return true;
                from = at + 1;
            }
        }

        /// <summary>
        /// Match an element name carrying a grammatical ending.
        ///
        /// <para>Several supported languages inflect nouns, so a word-boundary match alone misses
        /// the most natural phrasing: Swedish "guldets densitet", German "des Goldes", Italian
        /// "dell'oro". A short suffix on an otherwise exact name is accepted.</para>
        /// </summary>
        private static bool MatchesInflected(string surface, IReadOnlyList<string> words)
        {
            // Three characters, not four: Swedish inflects its shortest element names heavily -
            // "bly" becomes "blyets" in "vad är blyets smältpunkt", which a four-character floor
            // excluded. Two-character names stay out, because those are where symbol collisions live.
            if (surface.Length < MinInflectableName) return false;

            if (words.Any(w => w.Length > surface.Length &&
                               w.Length - surface.Length <= MaxInflectionSuffix &&
                               w.StartsWith(surface, StringComparison.Ordinal)))
                return true;

            // A Germanic compound whose *first* element is the element name: Swedish "uranatom",
            // German "Wasserstoffatom", "kolatomen". The suffix tolerance above allows three
            // characters, and "atom" is four, so "vad väger en uranatom" resolved no element at
            // all. Only these heads qualify - a general compound rule would match far too much.
            if (words.Any(w => CompoundHeads.Any(
                    head => string.Equals(w, surface + head, StringComparison.Ordinal))))
                return true;

            // Syncope: Swedish and German drop the unstressed vowel of a final syllable before an
            // ending, so "kisel" becomes "kislets" and "kvicksilver" becomes "kvicksilvrets". The
            // stem the user wrote is not a prefix of the name, which is why plain suffix tolerance
            // missed these entirely and the whole question was declined.
            if (Syncopated(surface) is { } shortened &&
                words.Any(w => w.Length > shortened.Length &&
                               w.Length - shortened.Length <= MaxInflectionSuffix &&
                               w.StartsWith(shortened, StringComparison.Ordinal)))
                return true;

            // Indic and Arabic-script nouns inflect by changing the final letter rather than adding
            // to it: Urdu gold سونا becomes سونے in an oblique phrase. A single edit on a name of
            // four or more characters is almost certainly the same word. Restricted to non-Latin
            // scripts, where the suffix rule above cannot apply and where a near-miss like
            // "iron"/"iran" is not a risk.
            //
            // The edit has to fall at the *end*, though, or the rule stops modelling inflection and
            // starts modelling the periodic table's own naming: Hindi सोडियम (sodium) and रोडियम
            // (rhodium) are one substitution apart, as are Urdu سوڈیم/روڈیم and ہیلیم/گیلیم - and
            // each pair differs in its first character, which no inflection ever does. Every Hindi
            // and Urdu question about sodium resolved rhodium alongside it and was answered as a
            // comparison.
            if (IsLatin(surface)) return false;
            return words.Any(w => Math.Abs(w.Length - surface.Length) <= 1 &&
                                  TextMatching.Levenshtein(w, surface) <= 1 &&
                                  SharesStem(w, surface));
        }

        /// <summary>
        /// Whether two forms agree on everything but their last character.
        ///
        /// <para>The guard on the single-edit rule above: an inflectional ending changes the tail,
        /// so the stem must survive intact.</para>
        /// </summary>
        private static bool SharesStem(string word, string surface)
        {
            var stem = Math.Min(word.Length, surface.Length) - 1;
            if (stem < MinNonLatinStem) return false;
            return string.CompareOrdinal(word, 0, surface, 0, stem) == 0;
        }

        private static bool IsLatin(string text) =>
            text.All(c => !char.IsLetter(c) || UnicodeScripts.IsLatin(c));

        /// <summary>
        /// A Han surface form counts only when it is not merely part of a longer Han run, unless it
        /// is the entire query. Without this, 金属 ("metal") would resolve to gold on every mention.
        /// </summary>
        private bool MatchesUnspaced(string surface, string query)
        {
            var trimmed = query.Trim();
            if (string.Equals(trimmed, surface, StringComparison.Ordinal)) return true;

            var index = trimmed.IndexOf(surface, StringComparison.Ordinal);
            while (index >= 0)
            {
                var before = index > 0 ? trimmed[index - 1] : (char?)null;
                var afterIndex = index + surface.Length;
                var after = afterIndex < trimmed.Length ? trimmed[afterIndex] : (char?)null;
                var absorbed = IsCompounding(before) || IsCompounding(after);
                // Multi-character names are specific enough to match even inside a longer run.
                if (!absorbed || surface.Length > 1) return true;
                index = trimmed.IndexOf(surface, index + 1, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>
        /// Whether a neighbouring character could make this part of a longer word.
        ///
        /// <para>Grammatical particles do not form compounds, so they act as boundaries: 金的密度 is
        /// "gold's density" and 金 is the element, whereas 金属 is "metal" and 金 is not. Without
        /// this distinction the guard against the second case also blocks the first.</para>
        /// </summary>
        private bool IsCompounding(char? c) =>
            c is { } ch && Tokenizer.IsHan(ch) && !HanParticles.Contains(ch) &&
            !_singleHanElements.Contains(ch);

        private static double ScoreOf(ElementAlias alias) => alias.Kind switch
        {
            AliasKind.Number => 1.0,
            AliasKind.Name => 0.9 + Math.Min(alias.Surface.Length, 10) / 200.0,
            AliasKind.Symbol => 0.7,
            _ => 0.0,
        };

        /// <summary>Last resort for a short query that named nothing exactly - tolerate a typo.</summary>
        private ElementMatch? FuzzyMatch(IReadOnlyList<string> words)
        {
            if (words.Count == 0 || words.Count > 2) return null;

            ElementMatch? best = null;
            foreach (var word in words)
            {
                if (word.Length < 4) continue;

                // A continuation is not a typo. "More" is one edit from the French name for
                // boron, "bore", and "sure" is one from the Swedish for oxygen, "syre" - so a
                // bare "more" after a question about iron answered with boron's melting point,
                // inheriting the field, and moved the conversation to boron for good. These words
                // arrive constantly and name nothing, which is the worst possible thing to guess
                // about. An exact "bore" still resolves; it never reaches this last resort.
                if (Atomic.Core.Text.FuzzyMatch.IsAffirmative(word)) continue;

                var threshold = word.Length > 6 ? 2 : 1;
                foreach (var alias in _byLengthDescending)
                {
                    if (alias.Kind != AliasKind.Name) continue;
                    if (Math.Abs(alias.Surface.Length - word.Length) > threshold) continue;
                    var distance = TextMatching.Levenshtein(word, alias.Surface);
                    if (distance > threshold) continue;
                    var score = 0.5 - distance * 0.1;
                    if (best is null || score > best.Score)
                        best = new ElementMatch(alias.Key, score, alias.Surface);
                }
            }
            return best;
        }

        /// <summary>Read an atomic number out of a query that frames it as one.</summary>
        private static int? AtomicNumberIn(string query)
        {
            // A quantity of moles is not a place in the table. "2 摩尔碳中有多少个原子" and "2 मोल
            // कार्बन में कितने परमाणु हैं" both resolved helium, because 2 is helium's atomic number
            // and the Hindi word for "atom" is one of the frames that says a bare number *is* one.
            if (MoleFrames.Any(f => query.Contains(f, StringComparison.Ordinal))) return null;

            var match = SmallNumber.Match(query);
            if (!match.Success) return null;

            // A mass number is not an atomic number. "Is iron-54 stable" is a question about an
            // isotope of iron; reading the 54 as an element number answered it about xenon, and
            // "yes" at that.
            if (match.Index > 0 && query[match.Index - 1] == '-') return null;
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                              out var number)) return null;
            if (number is < 1 or > 118) return null;

            var framed = ElementNumberFrames.Any(f => query.Contains(f, StringComparison.Ordinal));
            var shortQuery = Whitespace.Split(query.Trim()).Length <= 3;
            return framed || shortQuery ? number : null;
        }

        /// <summary>
        /// Whether a short token is an ordinary word in the active language rather than a symbol.
        /// </summary>
        public static bool IsCommonWordCollision(
            string token, string lowerQuery, IReadOnlyList<string> queryWords, string language)
        {
            if (token.Length > 2) return false;

            var isCommon = language switch
            {
                "en" => Contains(token, "in", "as", "at", "be", "he", "am", "i", "no", "ar"),
                "sv" => Contains(token, "ar", "i", "se", "ne", "na", "be", "es"),  // "ar" for "är"
                "de" => Contains(token, "as", "be", "er", "es", "in", "se", "am", "zu", "an", "um", "du", "so", "da"),
                "fr" => Contains(token, "au", "as", "y", "la", "ca", "ce", "es", "en", "de", "le", "un", "et", "il", "se", "ne"),
                "es" => Contains(token, "y", "la", "ca", "se", "no", "si", "as", "es", "de", "el", "en", "un", "al", "su", "lo", "ya"),
                "it" => Contains(token, "i", "la", "se", "si", "ne", "in", "di", "il", "ed", "un", "ad", "al", "lo", "su", "ma"),
                "hi" or "ur" => Contains(token, "se", "ne", "na", "pa", "ka", "ki", "ke", "ko", "jo", "to", "ab", "vo", "hi"),
                "pt" => Contains(token, "o", "as", "os", "se", "em", "um", "ou", "ao", "do", "da"),
                "af" => Contains(token, "na", "te", "op", "in", "om", "by", "sy", "as", "hy", "is"),
                "fil" => Contains(token, "ng", "sa", "na", "at", "ay", "ko", "mo", "ba", "ka", "pa"),
                _ => false,
            };

            if (isCommon)
            {
                // Only "symbol" counts. "What element has the symbol In" is someone naming a
                // symbol; "wie viele Elemente gibt es" merely mentions elements, and letting that
                // through disabled the guard so German "es" resolved to einsteinium - a count of
                // the whole table became a question about one element. Length cannot separate the
                // two: both are five words.
                if (queryWords.Count > 1 && !lowerQuery.Contains("symbol", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool Contains(string token, params string[] words) =>
            Array.IndexOf(words, token) >= 0;

        /// <summary>Build the alias table from every available language's element names and symbols.</summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<ElementAlias>> BuildAliases(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string Name, string Symbol, string AtomicNumber)>> tables)
        {
            var aliases = new Dictionary<string, List<ElementAlias>>(StringComparer.Ordinal);

            void Put(string surface, ElementAlias alias)
            {
                if (string.IsNullOrWhiteSpace(surface)) return;
                if (!aliases.TryGetValue(surface, out var list))
                    aliases[surface] = list = new List<ElementAlias>(2);

                // Deduped by language as well as by element and kind. Without it, the first
                // language to claim a shared spelling was the only one that kept it: German is
                // indexed before Swedish, so "Uran" was registered as a *German* name and a Swedish
                // query got the foreign-name path, which forbids inflection. "Vad väger en uranatom"
                // therefore resolved no element at all - and so did every other question about a
                // name the two share, which is most of them.
                if (!list.Any(a => string.Equals(a.Key, alias.Key, StringComparison.Ordinal) &&
                                   a.Kind == alias.Kind &&
                                   string.Equals(a.Language, alias.Language, StringComparison.Ordinal)))
                    list.Add(alias);
            }

            // Names a speaker actually writes, where the shipped table gives a transliteration.
            // Urdu ships آئرن for iron and آرسینک for arsenic - English words in Urdu script - so a
            // user writing لوہا or سنکھیا resolved nothing. Registered under the same language so
            // the inflection tolerance applies to them too.
            foreach (var (key, native) in NativeSynonyms)
            {
                foreach (var (language, surface) in native)
                {
                    var normalized = TextMatching.NormalizeForLookup(surface);
                    Put(normalized, new ElementAlias(normalized, key, language, AliasKind.Name));
                }
            }

            foreach (var (language, rows) in tables)
            {
                foreach (var (key, triple) in rows)
                {
                    var (name, symbol, atomicNumber) = triple;

                    var normalizedName = TextMatching.NormalizeForLookup(name);
                    Put(normalizedName, new ElementAlias(normalizedName, key, language, AliasKind.Name));

                    var lowerName = name.ToLowerInvariant();
                    Put(lowerName, new ElementAlias(lowerName, key, language, AliasKind.Name));

                    if (!string.IsNullOrWhiteSpace(symbol))
                    {
                        var lowerSymbol = symbol.ToLowerInvariant();
                        Put(lowerSymbol, new ElementAlias(lowerSymbol, key, language, AliasKind.Symbol));
                    }

                    if (!string.IsNullOrWhiteSpace(atomicNumber))
                        Put(atomicNumber, new ElementAlias(atomicNumber, key, language, AliasKind.Number));
                }
            }

            return aliases.ToDictionary(
                e => e.Key,
                e => (IReadOnlyList<ElementAlias>)e.Value,
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Everyday names the shipped tables miss, by element key and language.
        ///
        /// <para>Deliberately small: only where the shipped name is a transliteration of the
        /// English word and the language has a common word of its own. Everything else comes from
        /// the assets.</para>
        /// </summary>
        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NativeSynonyms =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["iron"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["ur"] = "لوہا" },
                ["arsenic"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["ur"] = "سنکھیا", ["hi"] = "संखिया" },
                ["copper"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["ur"] = "تانبا", ["hi"] = "तांबा" },
                ["sulfur"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["ur"] = "گندھک", ["hi"] = "गंधक" },
                ["mercury"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["ur"] = "پارہ", ["hi"] = "पारा" },
                ["zinc"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["ur"] = "جست", ["hi"] = "जस्ता" },
                ["silver"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["hi"] = "रजत" },
                ["gold"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["hi"] = "स्वर्ण" },
            };

        /// <summary>Wordings that make a bare number a quantity of substance rather than an element number.</summary>
        private static readonly string[] MoleFrames =
        [
            "mole", "moles", "moli", "mols", "mol ", " mol", "avogadro",
            "मोल", "مول", "摩尔",
        ];

        /// <summary>Wordings that frame a bare number as an element's place in the table.</summary>
        private static readonly string[] ElementNumberFrames =
        [
            "element", "atomic number", "number", "grundämne", "atomnummer",
            "ordnungszahl", "número atómico", "numéro atomique", "numero atomico",
            "número atômico", "परमाणु", "جوہری", "原子序数", "atoomgetal", "atomikong",
        ];

        /// <summary>Longest grammatical ending accepted on an element name, e.g. Swedish "guld" + "ets".</summary>
        private const int MaxInflectionSuffix = 3;

        /// <summary>Chinese function characters that separate words rather than joining them.</summary>
        private static readonly FrozenSet<char> HanParticles = new[]
        {
            '的', '是', '有', '和', '與', '与', '在', '中', '了', '嗎', '吗', '呢', '把', '被',
            '為', '为', '之', '或', '及', '而', '就', '都', '也', '很', '多', '少', '個', '个',
            // Question vocabulary. Chinese writes no spaces, so an element character sitting next
            // to one of these is inside a "longer run of Han characters" by the rule above and was
            // silently dropped: 比较金和银 resolved silver but not gold, and 砷危险吗 resolved
            // nothing at all. These characters never begin an element name, so admitting them as
            // boundaries costs nothing and makes the element stand free.
            '比', '較', '较', '危', '險', '险', '更', '最', '種', '种', '元', '素',
            '熔', '點', '点', '密', '度', '沸', '重', '輕', '轻', '致', '些', '哪', '幾', '几',
            '以', '上', '下', '大', '小', '於', '于', '同', '位', '放', '射', '性', '質', '质',
        }.ToFrozenSet();

        /// <summary>Shortest element name that may carry a grammatical ending.</summary>
        private const int MinInflectableName = 3;

        /// <summary>Shortest name accepted from a language other than the active one or English.</summary>
        private const int MinForeignName = 3;

        /// <summary>Shortest stem that has to survive an inflectional edit in a non-Latin script.</summary>
        private const int MinNonLatinStem = 3;

        /// <summary>
        /// Nouns a Germanic language compounds directly onto an element name.
        ///
        /// <para>"Uranatom", "kolatomen", "Wasserstoffkern" - the element is the modifier and one
        /// of these is the head. Written out rather than derived, because any rule permissive
        /// enough to find them by shape would also find "goldfish".</para>
        /// </summary>
        private static readonly string[] CompoundHeads =
        [
            "atom", "atomen", "atomer", "atomet", "atoms", "atome", "atomes",
            "karna", "karnan", "karnor", "kern", "kerne", "kerns",
            "gas", "gasen", "gaser", "molekyl", "molekyler", "molekul", "molekule",
        ];

        /// <summary>
        /// Phrases that contain an element's name without naming the element.
        ///
        /// <para>Spanish calls radium <i>radio</i> and the atomic radius <i>radio atómico</i>, so
        /// every Spanish question about a radius resolved two elements and became a comparison. The
        /// same shape as the symbol blocklist check, and deliberately as short: a phrase earns a
        /// place here only when the collision is total, not merely possible.</para>
        /// </summary>
        public static readonly string[] NameExemptPhrases =
        [
            "radio atomico", "radio ionico", "radio covalente", "radio de van der waals",
            "radio nuclear", "radio metalico",
        ];

        /// <summary>Shortest name worth trying to syncopate; below this the result is not a stem.</summary>
        private const int MinSyncopeLength = 5;

        /// <summary>
        /// Vowels that can be elided from a final syllable.
        ///
        /// <para>A set of chars rather than Android's <c>"aeiouyaaeoau"</c> string, which repeats
        /// four of its letters - the tail is what is left of "åäöáü" after someone folded it to
        /// ASCII. The duplicates never mattered because a string membership test does not care, and
        /// they cannot matter here either: by this point <c>NormalizeForLookup</c> has already
        /// stripped Latin diacritics, so å has become a.</para>
        /// </summary>
        private static readonly FrozenSet<char> SyncopeVowels =
            "aeiouy".ToCharArray().ToFrozenSet();

        /// <summary>
        /// Symbols up to this length must be capitalised to count in a sentence.
        ///
        /// <para>Two, because every collision observed is one or two characters - "s", "or", "ar",
        /// "in", "no". At three the surfaces stop looking like ordinary words in the supported
        /// languages.</para>
        /// </summary>
        private const int MaxCaseSensitiveSymbol = 2;

        /// <summary>The language whose names are the canonical keys, always eligible for inflection.</summary>
        private const string BaseLanguage = "en";

        /// <summary>
        /// A one-to-three digit run standing alone. Compiled through the Java shim so <c>\b</c> and
        /// <c>\d</c> stay ASCII: .NET's Unicode-aware <c>\d</c> matches Arabic-Indic ٥ and
        /// Devanagari ५, which would make a Hindi query's digits parse as an element number.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex SmallNumber =
            JavaRegex.Compile("\\b(\\d{1,3})\\b");

        private static readonly System.Text.RegularExpressions.Regex Whitespace =
            JavaRegex.Compile("\\s+");
    }
}
