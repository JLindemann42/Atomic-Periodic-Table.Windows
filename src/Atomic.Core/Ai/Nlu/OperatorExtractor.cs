using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Nlu
{
    /// <summary>
    /// Everything the extractor found in one query.
    ///
    /// <para><see cref="HasOperatorEvidence"/> is what the planner uses to decide whether a query is
    /// a computation at all. Queries with no operator evidence are plain lookups.</para>
    /// </summary>
    /// <param name="Range">A range, when the query said "between X and Y".</param>
    /// <param name="RankOrdinal">Which place in the ranking was asked for: "the third densest" gives 3.</param>
    public sealed record Operators(
        IReadOnlyList<(Op Op, Quantity Value)>? Comparators = null,
        IReadOnlyList<Filter>? SubsetFilters = null,
        Aggregation Aggregation = Aggregation.None,
        bool? SuperlativeDescending = null,
        int? TopN = null,
        string? TargetUnit = null,
        int? Ordinal = null,
        bool IsListQuestion = false,
        (Quantity Low, double High)? Range = null,
        int? RankOrdinal = null)
    {
        public IReadOnlyList<(Op Op, Quantity Value)> Comparators { get; init; } = Comparators ?? [];

        public IReadOnlyList<Filter> SubsetFilters { get; init; } = SubsetFilters ?? [];

        public bool HasOperatorEvidence =>
            Comparators.Count > 0 || SubsetFilters.Count > 0 ||
            Aggregation != Aggregation.None || SuperlativeDescending is not null ||
            TopN is not null || TargetUnit is not null || Range is not null;
    }

    /// <summary>
    /// Pulls comparators, subsets, aggregations and unit requests out of a query.
    ///
    /// <para>This is what makes multi-hop questions expressible. "Which transition metals melt above
    /// 2000 °C" yields a series filter plus a greater-than comparator, which together drive a scan
    /// over all 118 elements - a shape the previous keyword router had no way to represent.</para>
    /// </summary>
    public static class OperatorExtractor
    {
        /// <summary>A number optionally followed by a unit, e.g. "2000 °C", "19.3 g/cm3", "5000".</summary>
        private static readonly Regex NumberWithUnit = JavaRegex.Compile(
            @"(-?\d+(?:[.,]\d+)?)\s*(°?[a-zA-Zµμ°/³²Ω]+(?:/[a-zA-Z·()]+)?)?");

        private static readonly Regex TopNPattern =
            JavaRegex.Compile(@"\b(?:top|first|best|de|die|los|les|i|as)?\s*(\d{1,3})\s*(?=\p{L})");

        private static readonly Regex ExplicitTopN = JavaRegex.Compile(@"\btop\s+(\d{1,3})\b");

        private static readonly Regex NumberAfterWord = JavaRegex.Compile(@"^\D{0,2}(\d{1,2})");

        private static readonly Regex NumberBeforeWord = JavaRegex.Compile(@"(\d{1,2})\D{0,2}$");

        // Sorted once. Android re-sorts these three tables inside extract(), which runs per query.
        private static readonly KeyValuePair<string, IReadOnlySet<SeriesId>>[] SeriesWordsByLength =
            Lexicon.SERIES_WORDS.OrderByDescending(e => e.Key.Length).ToArray();

        private static readonly KeyValuePair<string, string>[] UnitWordsByLength =
            Lexicon.UNIT_WORDS.OrderByDescending(e => e.Key.Length).ToArray();

        private static readonly KeyValuePair<string, int>[] RankOrdinalsByLength =
            Lexicon.RANK_ORDINALS.OrderByDescending(e => e.Key.Length).ToArray();

        private static readonly string[] DegreeMarkers =
            Lexicon.MOST.Concat(Lexicon.LEAST).ToArray();

        private static readonly string[] MultiWordDegreeMarkers =
            DegreeMarkers.Where(m => m.Contains(' ', StringComparison.Ordinal)).ToArray();

        public static Operators Extract(string rawQuery)
        {
            var query = TextMatching.NormalizeForLookup(rawQuery);

            var comparators = new List<(Op, Quantity)>();
            var subsets = new List<Filter>();

            // ---- Comparators --------------------------------------------------------------
            // The longest phrase wins, so "at least" is not read as a bare "least" superlative.
            var greater = LongestMatch(query, Lexicon.GREATER);
            var less = LongestMatch(query, Lexicon.LESS);

            if (greater is not null &&
                (NumberAfter(query, greater) ?? NumberBefore(query, greater)) is { } gt)
                comparators.Add((Op.Gt, gt));

            if (less is not null &&
                (NumberBefore(query, less) ?? NumberAfter(query, less)) is { } lt)
                comparators.Add((Op.Lt, lt));

            // ---- Subsets ------------------------------------------------------------------
            var series = new HashSet<SeriesId>();

            // Family names are consumed here so their words cannot be read again below: "noble
            // gases" names a group, and treating its "gases" as a phase filter as well would
            // silently exclude oganesson, which is predicted to be solid.
            var remaining = query;
            foreach (var (word, ids) in SeriesWordsByLength)
            {
                if (!MentionsFamily(remaining, word)) continue;
                series.UnionWith(ids);
                remaining = remaining.Replace(word, " ", StringComparison.Ordinal);
            }

            if (series.Count > 0) subsets.Add(new Filter.InSeries(series));
            else if (Lexicon.METAL_WORDS.Any(w => Mentions(query, w))) subsets.Add(new Filter.IsMetal(true));

            foreach (var (word, block) in Lexicon.BLOCK_WORDS)
            {
                if (!Mentions(query, word)) continue;
                subsets.Add(new Filter.InBlock(block));
                break;
            }

            // A phase word inside a fixed term is not a phase. "What is the gas constant" and "what
            // is the ideal gas law" were answered with a list of every gaseous element, because
            // "gas" is a phase word and nothing looked at what it was part of.
            foreach (var phrase in Lexicon.PHASE_EXEMPT_PHRASES)
            {
                if (remaining.Contains(phrase, StringComparison.Ordinal))
                    remaining = remaining.Replace(phrase, " ", StringComparison.Ordinal);
            }
            foreach (var (word, phase) in Lexicon.PHASE_WORDS)
            {
                if (!Mentions(remaining, word)) continue;
                subsets.Add(new Filter.InPhase(phase));
                break;
            }

            if (Lexicon.RADIOACTIVE_WORDS.Any(w => Mentions(query, w)))
                subsets.Add(new Filter.IsRadioactive(true));

            // "synthetic" is checked second: "non-synthetic" contains both, and the natural reading
            // of a query mentioning synthetic at all is the synthetic set unless negated.
            if (Lexicon.NATURAL_WORDS.Any(w => Mentions(query, w)))
                subsets.Add(new Filter.IsSynthetic(false));
            else if (Lexicon.SYNTHETIC_WORDS.Any(w => Mentions(query, w)))
                subsets.Add(new Filter.IsSynthetic(true));

            if (PeriodOrGroup(query) is { } structural) subsets.Add(structural);

            // ---- Aggregation --------------------------------------------------------------
            // Suffix-tolerant for the same reason superlatives are: German "durchschnittliche"
            // carries an adjective ending that a word-boundary match misses entirely.
            var aggregation = Aggregation.None;
            if (Lexicon.AVERAGE.Any(m => MarksStatistic(query, m))) aggregation = Aggregation.Avg;
            else if (Lexicon.MEDIAN.Any(m => MarksStatistic(query, m))) aggregation = Aggregation.Median;
            else if (Lexicon.SUM.Any(m => MentionsFamily(query, m))) aggregation = Aggregation.Sum;
            else if (Lexicon.COUNT.Any(m => MentionsFamily(query, m))) aggregation = Aggregation.Count;

            // ---- Superlative direction ----------------------------------------------------
            // Kept as two distinct sets so "least dense" is not read as "densest". Suffix-tolerant
            // because Swedish and German inflect these ("högst" -> "högsta", "dicht" ->
            // "dichtesten"), and a superlative that goes unrecognised does not merely lose the
            // ranking - it lets the turn inherit a subject and collapse into a plain lookup.
            //
            // A comparison particle disqualifies the superlative reading entirely: "plus dense que
            // l'or" uses the same "plus" as "le plus dense", and only the "que" tells them apart.
            var comparisonParticle = CarriesComparisonParticle(query);
            var most = !comparisonParticle && Lexicon.MOST.Any(m => MarksDegree(query, m));
            var least = !comparisonParticle && Lexicon.LEAST.Any(m => MarksDegree(query, m));

            bool? descending;
            if (least && !most) descending = false;
            else if (most && !least) descending = true;
            else if (most && least)
            {
                // Both directions matched. The earlier marker wins, and at the same position the
                // longer one does - which is what tells 最低 ("lowest") from the bare 最 ("most")
                // that is a prefix of it. Chinese superlatives are built by prefixing a single
                // character, so every "lowest" question also contains a "highest" marker at the
                // same index.
                var leastMarker = FirstMention(query, Lexicon.LEAST)!;
                var mostMarker = FirstMention(query, Lexicon.MOST)!;
                var leastAt = query.IndexOf(leastMarker, StringComparison.Ordinal);
                var mostAt = query.IndexOf(mostMarker, StringComparison.Ordinal);
                descending = leastAt != mostAt ? leastAt > mostAt : leastMarker.Length <= mostMarker.Length;
            }
            else descending = null;

            // ---- Negation -----------------------------------------------------------------
            // "which noble gases are not radioactive" must invert the filter, not drop it.
            var negated = Lexicon.NEGATIONS.Any(n => query.Contains(n, StringComparison.Ordinal));
            var finalSubsets = negated ? subsets.Select(Invert).ToList() : subsets;

            return new Operators(
                Comparators: comparators,
                SubsetFilters: finalSubsets,
                Aggregation: aggregation,
                SuperlativeDescending: descending,
                TopN: TopN(query),
                TargetUnit: TargetUnit(query),
                Ordinal: Ordinal(query),
                // Naming a family in the plural is itself a request for its members, whatever opens
                // the sentence: "wat is die edelgasse" and "ano ang mga noble gas" carry no
                // interrogative the WHICH list knows, so neither counted as a list question.
                IsListQuestion:
                    Lexicon.WHICH.Any(w => query.StartsWith(w + " ", StringComparison.Ordinal) ||
                                           Mentions(query, w)) ||
                    Lexicon.PLURAL_SERIES_WORDS.Any(w => query.Contains(w, StringComparison.Ordinal)),
                Range: RangeOf(query),
                RankOrdinal: RankOrdinal(query));
        }

        /// <summary>
        /// The quantity written <i>before</i> a comparator, for the languages that put it there.
        ///
        /// <para>Hindi, Urdu and Chinese are postpositional: "2000 केल्विन से ऊपर",
        /// "2000 کیلون سے اوپر" and "2000开尔文以上" all read "2000 kelvin above". Looking only after
        /// the comparator found no number at all, so the threshold silently vanished and the query
        /// became an unfiltered list.</para>
        /// </summary>
        private static Quantity? NumberBefore(string query, string phrase)
        {
            var at = query.IndexOf(phrase, StringComparison.Ordinal);
            if (at <= 0) return null;

            var before = query[..at];
            var matches = NumberWithUnit.Matches(before);
            return matches.Count == 0 ? null : QuantityOf(matches[^1]);
        }

        /// <summary>The number following a phrase, with its unit if one is written.</summary>
        private static Quantity? NumberAfter(string query, string phrase)
        {
            var at = query.IndexOf(phrase, StringComparison.Ordinal);
            if (at < 0) return null;

            var tail = query[(at + phrase.Length)..];
            var match = NumberWithUnit.Match(tail);
            return match.Success ? QuantityOf(match) : null;
        }

        /// <summary>A match of the number pattern, read as a value and a canonical unit.</summary>
        private static Quantity? QuantityOf(Match match)
        {
            if (!double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out var value)) return null;

            var rawUnit = match.Groups[2].Value;
            string? unit = null;
            if (!string.IsNullOrWhiteSpace(rawUnit))
            {
                unit = Lexicon.UNIT_WORDS.TryGetValue(rawUnit.ToLowerInvariant(), out var word)
                    ? word
                    : UnitConverter.Canonical(rawUnit);
            }

            return new Quantity(value, Unit: unit, Display: match.Value.Trim());
        }

        /// <summary>
        /// Whether a degree marker appears, allowing for markers that are phrases.
        ///
        /// <para><see cref="MentionsFamily"/> matches one inflected word, which is right for the
        /// Germanic and Romance superlatives it was written for. Urdu builds its superlative out of
        /// two words - "سب سے" plus the adjective - so a single-word matcher found none of them and
        /// every Urdu superlative was read as a plain lookup.</para>
        /// </summary>
        private static bool MarksDegree(string query, string marker)
        {
            if (marker.Contains(' ', StringComparison.Ordinal))
                return query.Contains(marker, StringComparison.Ordinal);
            if (MentionsFamily(query, marker)) return true;

            // German compounds the rank onto the superlative: "das zweitdichteste Element". The
            // marker is the tail of a longer word, which the prefix-tolerant match above cannot
            // see, so the query carried no superlative at all and was declined outright.
            if (marker.Length < MinCompoundMarker) return false;
            return TextMatching.SplitQueryTokens(query)
                .Any(t => t.Length > marker.Length && t.EndsWith(marker, StringComparison.Ordinal));
        }

        /// <summary>
        /// Whether a statistic is asked for, allowing the marker to open a compound word.
        ///
        /// <para><see cref="MentionsFamily"/> tolerates an <i>ending</i> on the marker, which is
        /// enough for "durchschnittliche" and "medianen". It is not enough for Swedish and German,
        /// which write the statistic and its field as one word: "mediandensitet",
        /// "medianatommassan", "Durchschnittsdichte". There the marker is a prefix of a much longer
        /// token, and the whole aggregation was lost - the question became an unfiltered list of
        /// the subset.</para>
        ///
        /// <para>Confined to markers of five characters or more, so the short ones ("avg", "sum",
        /// "media") cannot swallow an unrelated word.</para>
        /// </summary>
        private static bool MarksStatistic(string query, string marker)
        {
            if (MentionsFamily(query, marker)) return true;
            if (marker.Length < MinCompoundMarker || marker.Contains(' ', StringComparison.Ordinal))
                return false;
            return TextMatching.SplitQueryTokens(query)
                .Any(t => t.Length > marker.Length && t.StartsWith(marker, StringComparison.Ordinal));
        }

        /// <summary>Shortest statistic marker that may open a compound.</summary>
        private const int MinCompoundMarker = 5;

        /// <summary>
        /// Whether the query carries a comparison particle that is genuinely comparing two things.
        ///
        /// <para>A bare "is there a than-word anywhere" test cost two whole languages their
        /// superlatives:</para>
        /// <list type="bullet">
        /// <item>Urdu builds its superlative <i>out of</i> the particle - سب سے زیادہ is literally
        /// "than all, more" - so every Urdu superlative contained سے and was demoted to a
        /// comparison. Multi-word degree markers are therefore blanked out before the search.</item>
        /// <item>Spanish and Portuguese spell the interrogative "qué"/"qual" with the same letters
        /// as their comparison particle "que", so "qué elemento tiene el punto de ebullición más
        /// alto" looked like a comparison too. Position settles that one: a Romance comparison
        /// particle follows its degree word ("más denso <b>que</b> el oro"), never precedes it.</item>
        /// </list>
        /// </summary>
        private static bool CarriesComparisonParticle(string query)
        {
            // Blanked rather than removed, so every index still lines up with the original.
            var masked = query;
            foreach (var marker in MultiWordDegreeMarkers)
                masked = masked.Replace(marker, new string(' ', marker.Length), StringComparison.Ordinal);

            int? degreeAt = null;
            foreach (var marker in DegreeMarkers)
            {
                if (!MarksDegree(query, marker)) continue;
                var at = query.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0) continue;
                if (degreeAt is null || at < degreeAt) degreeAt = at;
            }

            foreach (var particle in Lexicon.THAN_WORDS)
            {
                if (!Mentions(masked, particle)) continue;
                if (degreeAt is not { } from) return true;
                if (from <= masked.Length &&
                    masked.IndexOf(particle, from, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Invert a filter for a negated question.
        ///
        /// <para>Only the boolean-shaped filters can be meaningfully inverted. A negated series or
        /// phase would mean "every element that is not a halogen", which is almost never what is
        /// being asked, so those are left alone: in "which noble gases are not radioactive" the
        /// series names the set and only the radioactivity is negated.</para>
        /// </summary>
        private static Filter Invert(Filter filter) => filter switch
        {
            Filter.IsRadioactive f => new Filter.IsRadioactive(!f.Radioactive),
            Filter.IsSynthetic f => new Filter.IsSynthetic(!f.Synthetic),
            Filter.IsMetal f => new Filter.IsMetal(!f.Metal),
            _ => filter,
        };

        /// <summary>"between 1000 and 2000 kelvin" - the two bounds and the unit they share.</summary>
        private static (Quantity Low, double High)? RangeOf(string query)
        {
            var opener = Lexicon.BETWEEN_WORDS.FirstOrDefault(w => Mentions(query, w));
            if (opener is null) return null;

            // Hindi, Urdu and Chinese are postpositional: the bounds come *before* the word that
            // says they are a range - "5 और 10 के बीच", "5 اور 10 کے درمیان", "5到10之间". Reading
            // only what followed the opener found no numbers at all, so every range question in
            // those three languages was declined. Whichever side carries a joined pair is the range.
            var after = SubstringAfter(query, opener);
            var before = SubstringBefore(query, opener);
            var tail = JoinedPairIn(after) is not null ? after : before;

            var joiner = JoinedPairIn(tail);
            if (joiner is null) return null;

            var separator = tail.Contains($" {joiner} ", StringComparison.Ordinal) ? $" {joiner} " : joiner;
            var low = NumberWithUnit.Match(SubstringBefore(tail, separator));
            var high = NumberWithUnit.Match(SubstringAfter(tail, separator));
            if (!low.Success || !high.Success) return null;

            if (!double.TryParse(low.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out var lowValue)) return null;
            if (!double.TryParse(high.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out var highValue)) return null;

            // The unit is usually written once, after the second bound.
            var rawUnit = !string.IsNullOrWhiteSpace(high.Groups[2].Value) ? high.Groups[2].Value
                        : !string.IsNullOrWhiteSpace(low.Groups[2].Value) ? low.Groups[2].Value
                        : null;
            string? unit = null;
            if (rawUnit is not null)
            {
                unit = Lexicon.UNIT_WORDS.TryGetValue(rawUnit.ToLowerInvariant(), out var word)
                    ? word
                    : UnitConverter.Canonical(rawUnit);
            }

            var display = $"{Num(lowValue)}–{Num(highValue)}";
            return (new Quantity(Math.Min(lowValue, highValue), Unit: unit, Display: display),
                    Math.Max(lowValue, highValue));
        }

        /// <summary>
        /// The joiner of a pair of bounds inside the text, or null when it holds no such pair.
        ///
        /// <para>Chinese writes the joiner with no spaces around it (5到10), so both spellings are
        /// tried.</para>
        /// </summary>
        private static string? JoinedPairIn(string text)
        {
            string? best = null;
            var bestAt = int.MaxValue;

            foreach (var joiner in Lexicon.RANGE_JOINERS)
            {
                var spaced = text.IndexOf($" {joiner} ", StringComparison.Ordinal);
                var present = spaced >= 0 ||
                              (Tokenizer.IsUnspacedScript(joiner) &&
                               text.Contains(joiner, StringComparison.Ordinal));
                if (!present) continue;

                var at = spaced >= 0 ? spaced : text.IndexOf(joiner, StringComparison.Ordinal);
                if (at >= bestAt) continue;
                bestAt = at;
                best = joiner;
            }
            return best;
        }

        /// <summary>"the third densest element" - which place in the ranking was asked for.</summary>
        private static int? RankOrdinal(string query)
        {
            if (!HasSuperlative(query)) return null;
            foreach (var (word, n) in RankOrdinalsByLength)
                if (Mentions(query, word)) return n;
            return null;
        }

        private static bool HasSuperlative(string query) =>
            Lexicon.MOST.Any(m => Mentions(query, m)) || Lexicon.LEAST.Any(m => Mentions(query, m));

        private static int? TopN(string query)
        {
            var explicitMatch = ExplicitTopN.Match(query);
            if (explicitMatch.Success &&
                int.TryParse(explicitMatch.Groups[1].Value, NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out var explicitN))
                return Math.Clamp(explicitN, 1, 118);

            if (!HasSuperlative(query)) return null;

            // "5 densest nonmetals" - a bare number only counts alongside a superlative.
            var match = TopNPattern.Match(query);
            if (!match.Success) return null;
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out var n)) return null;
            return n is >= 2 and <= 118 ? n : null;
        }

        /// <summary>A requested output unit, e.g. "in Fahrenheit".</summary>
        private static string? TargetUnit(string query)
        {
            foreach (var (word, unit) in UnitWordsByLength)
            {
                if (word.Length < 2) continue;
                if (!Mentions(query, word)) continue;

                // Require a preposition beside it, so "kelvin" inside a name is not a conversion
                // request. Either side, because Hindi, Urdu and Filipino are postpositional:
                // "सेल्सियस में" puts the preposition after the unit, and looking only in front
                // meant a bare "in celsius" follow-up carried no unit at all in those three.
                var at = query.IndexOf(word, StringComparison.Ordinal);
                var before = SubstringAfterLast(query[..at].TrimEnd(), ' ');
                var after = SubstringBefore(query[(at + word.Length)..].TrimStart(), " ");

                if (Lexicon.UNIT_PREPOSITIONS.Contains(before) ||
                    Lexicon.UNIT_PREPOSITIONS.Contains(after) ||
                    word.Length > 6) return unit;
            }
            return null;
        }

        private static int? Ordinal(string query)
        {
            foreach (var (word, n) in Lexicon.ORDINALS)
                if (Mentions(query, word)) return n;
            return null;
        }

        /// <summary>
        /// "period 6", "group 11", and the same in the other supported languages.
        ///
        /// <para>The number may come after the word or before it, since Chinese writes 第6周期 with
        /// the number first.</para>
        /// </summary>
        private static Filter? PeriodOrGroup(string query)
        {
            if (NumberNear(query, PeriodWords) is { } period && period is >= 1 and <= 7)
                return new Filter.InPeriod(period);
            if (NumberNear(query, GroupWords) is { } group && group is >= 1 and <= 18)
                return new Filter.InGroup(group);
            return null;
        }

        private static int? NumberNear(string query, IReadOnlyList<string> words)
        {
            foreach (var word in words)
            {
                var at = query.IndexOf(word, StringComparison.Ordinal);
                if (at < 0) continue;

                var afterMatch = NumberAfterWord.Match(query[(at + word.Length)..]);
                if (afterMatch.Success &&
                    int.TryParse(afterMatch.Groups[1].Value, NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out var afterN))
                    return afterN;

                var beforeMatch = NumberBeforeWord.Match(query[..at]);
                if (beforeMatch.Success &&
                    int.TryParse(beforeMatch.Groups[1].Value, NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out var beforeN))
                    return beforeN;
            }
            return null;
        }

        /// <summary>Words naming a row of the table. "row" is a period; "column" is a group.</summary>
        private static readonly string[] PeriodWords =
        [
            "period", "periode", "periodo", "row", "rad", "rangee", "fila", "zeile",
            "hang", "पंक्ति", "आवर्त", "周期", "dor", "دور", "panahon", "periodo",
        ];

        private static readonly string[] GroupWords =
        [
            "group", "grupp", "gruppe", "groupe", "grupo", "gruppo", "column", "kolumn",
            "spalte", "colonne", "columna", "colonna", "kolom", "hanay", "hilera",
            "समूह", "族", "gurooh", "groep", "گروپ", "pangkat",
        ];

        private static bool Mentions(string query, string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase)) return false;

            // Multi-word and non-Latin phrases match by containment; single Latin words by boundary.
            return phrase.Contains(' ', StringComparison.Ordinal) || phrase.Any(c => c > 0x2000)
                ? query.Contains(phrase, StringComparison.Ordinal)
                : TextMatching.ContainsWord(query, phrase);
        }

        /// <summary>
        /// Like <see cref="Mentions"/>, but tolerant of a grammatical ending on a family name.
        ///
        /// <para>Swedish and German inflect these heavily, and the definite plural is the form
        /// people actually write: "ädelgaserna" for "ädelgas", "halogenerna", "alkalimetallerna",
        /// "Edelgasen". Matching on word boundaries alone missed every one of them, so "Vilka är
        /// ädelgaserna?" resolved no subset and was answered with a single-element dump.</para>
        ///
        /// <para>Four characters covers "-erna"; the tolerance is confined to family names rather
        /// than applied to every subset word, because a loose match on a phase or a metal word would
        /// silently widen a filter instead of failing to find one.</para>
        /// </summary>
        private static bool MentionsFamily(string query, string phrase)
        {
            if (Mentions(query, phrase)) return true;
            if (phrase.Length < MinFamilyStem || phrase.Contains(' ', StringComparison.Ordinal))
                return false;

            return TextMatching.SplitQueryTokens(query).Any(
                word => word.Length > phrase.Length &&
                        word.Length - phrase.Length <= MaxFamilySuffix &&
                        word.StartsWith(phrase, StringComparison.Ordinal));
        }

        /// <summary>Longest grammatical ending accepted on a family name, e.g. "adelgas" + "erna".</summary>
        private const int MaxFamilySuffix = 4;

        /// <summary>Shortest family name worth matching by stem, so a two-letter token cannot widen a filter.</summary>
        private const int MinFamilyStem = 5;

        private static string? LongestMatch(string query, IReadOnlyList<string> phrases)
        {
            string? best = null;
            foreach (var phrase in phrases)
            {
                if (!Mentions(query, phrase)) continue;
                if (best is null || phrase.Length > best.Length) best = phrase;
            }
            return best;
        }

        private static string? FirstMention(string query, IReadOnlyList<string> phrases)
        {
            string? best = null;
            var bestAt = int.MaxValue;
            foreach (var phrase in phrases)
            {
                if (!MentionsFamily(query, phrase)) continue;
                // IndexOf can be -1 when MentionsFamily matched an *inflected* form rather than the
                // phrase itself. Kotlin's minByOrNull would rank that -1 first; keeping the same
                // ordering matters, because this decides which superlative direction wins.
                var at = query.IndexOf(phrase, StringComparison.Ordinal);
                if (at >= bestAt) continue;
                bestAt = at;
                best = phrase;
            }
            return best;
        }

        // Kotlin's substring helpers, which return the whole string when the delimiter is absent.
        // Reimplemented rather than approximated with Split: the "not found" behaviour is load
        // bearing in RangeOf, where a missing separator must leave the text intact.
        private static string SubstringAfter(string text, string delimiter)
        {
            var at = text.IndexOf(delimiter, StringComparison.Ordinal);
            return at < 0 ? text : text[(at + delimiter.Length)..];
        }

        private static string SubstringBefore(string text, string delimiter)
        {
            var at = text.IndexOf(delimiter, StringComparison.Ordinal);
            return at < 0 ? text : text[..at];
        }

        private static string SubstringAfterLast(string text, char delimiter)
        {
            var at = text.LastIndexOf(delimiter);
            return at < 0 ? text : text[(at + 1)..];
        }

        private static string Num(double value) =>
            value.ToString("0.##########", CultureInfo.InvariantCulture);
    }
}
