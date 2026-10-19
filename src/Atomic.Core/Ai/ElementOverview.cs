using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Localization;
using Atomic.Core.Text;

namespace Atomic.Core.Ai
{
    /// <summary>What a rich overview may include, and how much of it.</summary>
    /// <param name="Full">"Tell me everything about gold": every section, not a selection.</param>
    /// <param name="Random">Chooses the sections for an ordinary overview. Null takes the first ones.</param>
    /// <param name="Entitlements">What the user may see. A PRO field is left out rather than shown.</param>
    public sealed record OverviewOptions(bool Full = false, Random? Random = null, Entitlements? Entitlements = null);

    /// <summary>
    /// What an element <i>is</i>, for a question that names one and asks nothing in particular:
    /// "gold", "tell me about gold", "element 79" - and "tell me everything about gold".
    ///
    /// <para><b>Android's rich overview</b> (<c>provideOverview</c>): the basic facts, then a few
    /// sections drawn from a pool - where it is found, what it is used for, safety, advanced atomic
    /// data, origin, biological role, compounds, notable isotopes - and every section when the user
    /// asks for everything. The headings are Android's shipped strings, translated in all twelve.</para>
    ///
    /// <para>Three things are deliberately not Android's. Its safety and advanced sections label their
    /// lines in English ("Health", "Config") in every language; these use the translated field labels.
    /// Its "notable isotopes" are the first three slots, which for gold are Au-170 to Au-172, gone in
    /// fractions of a second; these are the stable ones, or failing that the longest-lived. And it shows
    /// electron affinity to everyone, though that is a PRO field and is gated everywhere else; here it
    /// follows the entitlement. The sections keep a fixed order when only some are chosen, so two
    /// overviews of the same element read alike even when they say different things.</para>
    ///
    /// <para>Everything is read in the reader's language: prose from the language's own description,
    /// minus any sentence the file left as a copy of the English, with no English fallback.</para>
    /// </summary>
    public static class ElementOverview
    {
        /// <summary>How much of the description leads the overview.</summary>
        private const int LeadSentences = 2;

        /// <summary>
        /// Words that ask for the whole overview: Android's list, plus the native-script forms its
        /// Hindi, Urdu and Chinese entries were missing - they were romanised, and normalisation leaves
        /// those scripts as written, so "sab kuch" could never match "सब कुछ".
        /// </summary>
        private static readonly string[] FullWords =
        [
            "all", "everything", "complete", "full", "detailed", "whole", "total",
            "allt", "hela", "komplett", "fullstandig", "detaljerad",
            "alles", "vollstandig", "detailliert",
            "tout", "complet", "detaille", "entier",
            "todo", "completo", "detallado", "entero",
            "tutto", "dettagliato",
            "lahat", "kumpleto", "detalyado",
            "volledig", "volledige",
            "sab", "sab kuch", "purn", "vistrit", "mukammal", "tafseeli",
            "suoyou", "quanbu", "xiangxi", "quanmian",
            "सब", "सब कुछ", "पूरा", "विस्तृत",
            "سب", "سب کچھ", "مکمل", "تفصیلی",
            "所有", "全部", "详细", "全面",
        ];

        /// <summary>Android's "where is it found" vocabulary, matched within a sentence.</summary>
        private static readonly string[] AbundanceWords =
        [
            "find", "found", "abundance", "crust", "universe", "nature", "meteorite",
            "förekomst", "finns", "natur", "universum",
            "vorkommen", "existiert",
            "trouver", "abondance", "univers",
            "encontrar", "abundancia", "naturaleza", "universo",
            "abundância", "natureza",
            "trovare", "abbondanza", "natura",
            "matatagpuan", "kalikasan", "sansinukob",
            "gevind", "vorkoms", "natuur", "heelal",
        ];

        /// <summary>Whether a word in the question asks for everything.</summary>
        public static bool IsFullWord(string token) => FullWords.Contains(token, StringComparer.Ordinal);

        /// <summary>Whether the question asks for the whole overview.</summary>
        public static bool AsksForEverything(string normalizedQuery) =>
            FullWords.Any(w => Retrieval.TextMatching.ContainsWord(normalizedQuery, w));

        /// <summary>The overview, or null when the element has nothing to say.</summary>
        /// <param name="localized">
        /// The language's own text for the element. The record is parsed once from English, which is
        /// right for every measured value and wrong for prose: reading the phase and the description
        /// off it gave every language "solid" and an English paragraph.
        /// </param>
        public static string? Of(
            ElementRecord element, string displayName, string language, IStringProvider strings,
            LocalizedView? localized = null, OverviewOptions? options = null)
        {
            options ??= new OverviewOptions();
            var key = element.Key;

            var facts = new StringBuilder();
            void Fact(string labelKey, string? value)
            {
                if (string.IsNullOrWhiteSpace(value) || value == "---") return;
                facts.Append("• **").Append(Label(strings, labelKey)).Append("**: ").Append(value.Trim()).Append('\n');
            }

            Fact(S.atomic_number_label, element.AtomicNumber.ToString(CultureInfo.InvariantCulture));
            Fact(S.element_symbols, element.Symbol);
            Fact(S.atomic_mass_colon, Show(element.Value("atomic_mass")));
            Fact(S.phase_stp_colon, localized?.Text(key, "phase") ?? element.Phase);
            Fact(S.type_label, Show(element.Value("element_type")));

            if (facts.Length == 0) return null;

            var english = Show(element.Value("description"));
            var sentences = ElementNarrative.TranslatedSentences(
                localized?.Text(key, "description") ?? english, english, language);

            var text = new StringBuilder();
            text.Append("### ").Append(strings.Get(S.ai_rich_overview_basic_facts, displayName)).Append("\n\n");
            text.Append(facts.ToString().TrimEnd());

            var lead = sentences.Take(LeadSentences).Select(s => s.Trim()).ToList();
            // Joined as written: each sentence keeps its own ending, "." or "。".
            if (lead.Count > 0) text.Append("\n\n").Append(JoinSentences(lead));

            // The lead already said these; a section repeating one of them reads as padding.
            var rest = sentences.Skip(LeadSentences).ToList();

            foreach (var (heading, body) in Choose(Pool(element, displayName, strings, rest, options), options))
                text.Append("\n\n### ").Append(heading).Append('\n').Append(body);

            // A pointer to what else to ask, which the ordinary overview leaves out by design.
            if (!options.Full) text.Append("\n\n\U0001F4A1 ").Append(strings.Get(S.ai_pro_tip_hint));

            return text.ToString();
        }

        // ---- Sections --------------------------------------------------------------------------------

        /// <summary>Every section this element has something for, in reading order.</summary>
        private static List<(string Heading, string Body)> Pool(
            ElementRecord element, string displayName, IStringProvider strings,
            List<string> sentences, OverviewOptions options)
        {
            var pool = new List<(string, string)>();
            void Add(string headingKey, string? body)
            {
                if (!string.IsNullOrWhiteSpace(body)) pool.Add((strings.Get(headingKey, displayName), body.TrimEnd()));
            }

            Add(S.ai_rich_overview_where_found, WhereFound(element, strings, sentences));
            Add(S.ai_rich_overview_applications,
                Bullets(sentences.Where(s => ElementNarrative.IsAbout(ElementNarrative.Topic.Usage, s)), options.Full ? 4 : 2));
            Add(S.ai_rich_overview_safety, Safety(element, strings));
            Add(S.ai_rich_overview_advanced, Advanced(element, strings, options));
            Add(S.ai_rich_overview_etymology,
                Bullets(sentences.Where(s => ElementNarrative.IsAbout(ElementNarrative.Topic.Etymology, s)), 1));
            Add(S.ai_rich_overview_biological,
                Bullets(sentences.Where(s => ElementNarrative.IsAbout(ElementNarrative.Topic.Biological, s)), 1));
            Add(S.ai_rich_overview_compounds, Compounds(sentences, options.Full ? 10 : 5));
            Add(S.ai_rich_overview_isotopes, Isotopes(element, strings, options.Full ? 5 : 3));

            return pool;
        }

        /// <summary>
        /// All of them for a full overview; otherwise three to five, as Android chooses, kept in the
        /// pool's order.
        /// </summary>
        private static IEnumerable<(string Heading, string Body)> Choose(
            List<(string Heading, string Body)> pool, OverviewOptions options)
        {
            if (options.Full || pool.Count <= 3) return pool;

            if (options.Random is not { } random) return pool.Take(4);

            var count = Math.Min(pool.Count, random.Next(3, 6));
            var keep = Enumerable.Range(0, pool.Count)
                .OrderBy(_ => random.Next())
                .Take(count)
                .ToHashSet();

            return pool.Where((_, i) => keep.Contains(i));
        }

        private static string? WhereFound(ElementRecord element, IStringProvider strings, List<string> sentences)
        {
            var body = new StringBuilder();

            if (Show(element.Value("abundance_earth_crust")) is { Length: > 0 } crust)
                body.Append("• **").Append(Label(strings, S.abundance_earth_crust)).Append("**: ").Append(crust).Append('\n');

            // Whole words, as the other sections match. Android matches within words, so the Swedish
            // and German "natur" claimed "naturally occurring isotopes" as a sentence about where
            // uranium is found.
            var sentence = sentences.FirstOrDefault(s =>
            {
                var folded = Retrieval.TextMatching.NormalizeForLookup(s);
                return AbundanceWords.Any(w =>
                    Retrieval.TextMatching.ContainsWord(folded, Retrieval.TextMatching.NormalizeForLookup(w)));
            });
            if (sentence is not null) body.Append("• ").Append(sentence.Trim());

            return body.ToString();
        }

        /// <summary>The NFPA ratings, each with the translated label and the level's meaning.</summary>
        private static string? Safety(ElementRecord element, IStringProvider strings)
        {
            if (element.Nfpa is not { } nfpa) return null;

            var body = new StringBuilder();
            void Rating(string labelKey, int? value, string levelPrefix)
            {
                if (value is not { } level || level < 0 || level > 4) return;
                body.Append("• **").Append(Capitalise(strings.Get(labelKey))).Append("**: ")
                    .Append(level.ToString(CultureInfo.InvariantCulture)).Append("/4 — ")
                    .Append(strings.Get(levelPrefix + level.ToString(CultureInfo.InvariantCulture)))
                    .Append('\n');
            }

            Rating(S.ai_field_nfpa_health, nfpa.Health, "ai_nfpa_health_");
            Rating(S.ai_field_nfpa_flammability, nfpa.Flammability, "ai_nfpa_flammable_");
            Rating(S.ai_field_nfpa_instability, nfpa.Instability, "ai_nfpa_instability_");
            return body.ToString();
        }

        /// <summary>Electron configuration, and electron affinity where the user's tier allows it.</summary>
        private static string? Advanced(ElementRecord element, IStringProvider strings, OverviewOptions options)
        {
            var body = new StringBuilder();

            if (Show(element.Value("electron_configuration")) is { Length: > 0 } configuration && configuration != "---")
                body.Append("• **").Append(Label(strings, S.electron_configuration_colon)).Append("**: ").Append(configuration).Append('\n');

            var entitlements = options.Entitlements ?? Entitlements.Full;
            if (entitlements.AllowsField("electron_affinity") &&
                Show(element.Value("electron_affinity")) is { Length: > 0 } affinity && affinity != "---")
                body.Append("• **").Append(Label(strings, S.electron_affinity_colon)).Append("**: ").Append(affinity).Append('\n');

            return body.ToString();
        }

        /// <summary>Chemical formulas named in the description, as Android finds them.</summary>
        private static string? Compounds(List<string> sentences, int take)
        {
            var found = sentences
                .SelectMany(s => Formula.Matches(s).Select(m => m.Value))
                .Where(f => f.Length > 2)
                .Distinct(StringComparer.Ordinal)
                .Take(take)
                .ToList();

            return found.Count == 0 ? null : "• " + string.Join(", ", found);
        }

        private static readonly System.Text.RegularExpressions.Regex Formula =
            JavaRegex.Compile(@"\b[A-Z][a-z]?\d*[A-Z][a-z]?\d*\b");

        /// <summary>
        /// The isotopes worth naming: the stable ones, or for an element with none, the longest-lived.
        /// </summary>
        private static string? Isotopes(ElementRecord element, IStringProvider strings, int take)
        {
            var stable = element.Isotopes.Where(i => i.Stable).Take(take).ToList();
            var lines = stable.Count > 0
                ? stable.Select(i => strings.Get(S.ai_isotope_stable, i.Name))
                : element.Isotopes
                    .Where(i => i.HalfLifeSeconds is not null)
                    .OrderByDescending(i => i.HalfLifeSeconds)
                    .Take(take)
                    .Select(i => strings.Get(S.ai_isotope_halflife_only, i.Name, i.HalfLifeDisplay));

            var list = lines.ToList();
            return list.Count == 0 ? null : string.Join("\n", list);
        }

        // ---- Helpers ---------------------------------------------------------------------------------

        /// <summary>
        /// Sentences side by side: a space after a Latin full stop, none after a full-width one, which
        /// is how Chinese runs its sentences together.
        /// </summary>
        private static string JoinSentences(IReadOnlyList<string> sentences)
        {
            var joined = new StringBuilder();
            foreach (var sentence in sentences)
            {
                if (joined.Length > 0 && joined[^1] is not ('\u3002' or '\uFF01' or '\uFF1F')) joined.Append(' ');
                joined.Append(sentence);
            }
            return joined.ToString();
        }

        private static string? Bullets(IEnumerable<string> sentences, int take)
        {
            var picked = sentences.Take(take).Select(s => "• " + s.Trim()).ToList();
            return picked.Count == 0 ? null : string.Join("\n", picked);
        }

        /// <summary>
        /// A shipped label without its separator. Both colons: the labels are authored with one
        /// attached, and Chinese writes a full-width one, which survived the ASCII strip and printed
        /// "原子序数：:".
        /// </summary>
        private static string Label(IStringProvider strings, string key) =>
            strings.Get(key)
                .Replace(":", string.Empty, StringComparison.Ordinal)
                .Replace("：", string.Empty, StringComparison.Ordinal)
                .Trim();

        private static string Capitalise(string label) =>
            label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..];

        /// <summary>A field value as text, whatever shape it is stored in.</summary>
        private static string? Show(FieldValue value) => value switch
        {
            FieldValue.Text t => t.Raw,
            FieldValue.Enum e => string.IsNullOrWhiteSpace(e.Localized) ? e.Canonical : e.Localized,
            FieldValue.Num n => string.IsNullOrWhiteSpace(n.Quantity.Display)
                ? n.Quantity.Value.ToString(CultureInfo.InvariantCulture)
                : n.Quantity.Display,
            _ => null,
        };
    }
}
