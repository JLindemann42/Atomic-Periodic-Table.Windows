using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Ai.Retrieval;

namespace Atomic.Core.Ai.Nlu
{
    /// <summary>
    /// Words built from other words, written as the words they are made of (roadmap 1.5). Mirrors
    /// Android's <c>Morphology.kt</c>.
    ///
    /// <para>Swedish, German and Afrikaans write a property and what is asked about it as one word:
    /// "smältpunktsskillnaden" is the melting-point difference, "Durchschnittsdichte" the average
    /// density. Afrikaans makes a superlative with -ste and Filipino with pinaka-, so "hardste" and
    /// "pinakamatigas" are the superlative and its property in one word too. Every matcher
    /// downstream looks for whole words, so each of these lost the property, the superlative, or
    /// both.</para>
    ///
    /// <para>The planner runs <see cref="Expand"/> on the query first. Three rules, each taking a
    /// word apart only into parts it knows, and leaving alone any word already known with its
    /// property:</para>
    /// <list type="bullet">
    /// <item><b>Compounds.</b> A word that is no known word on its own is split in two when the
    /// first part is a field's name or a statistic, the rest is a field's name, a statistic or a
    /// <see cref="Lexicon.COMPOUND_HEADS"/> word with a short ending, and at most a linking <c>s</c>
    /// stands between them.</item>
    /// <item><b>-ste.</b> "ligste" is written as the superlative
    /// <see cref="Lexicon.SUPERLATIVE_STEMS"/> knows its stem by, "lightest"; a stem that is a
    /// field's name becomes "most" and the stem.</item>
    /// <item><b>pinaka-.</b> The same for the prefix, after the hyphen and the linker are dropped:
    /// "pinakamababang" is the listed "pinakamababa".</item>
    /// </list>
    ///
    /// <para>A rewritten word is written folded, which is how every matcher reads it anyway. Words
    /// with digits are never touched, so the formulas and quantities the executor re-reads stay as
    /// written.</para>
    /// </summary>
    public sealed class Morphology
    {
        private const string Pinaka = "pinaka";
        private const string Ste = "ste";

        /// <summary>The canonical "most", which <see cref="Lexicon.MOST"/> knows in every language.</summary>
        private const string Most = "most";

        /// <summary>Shortest word that may be a compound, and shortest part of one.</summary>
        private const int MinCompound = 8;
        private const int MinPart = 4;

        /// <summary>Longest grammatical ending on the last part: "skillnad" + "en".</summary>
        private const int MaxEnding = 3;

        /// <summary>What may stand between the parts: nothing, or the Germanic linking s.</summary>
        private static readonly string[] Links = ["", "s"];

        private static readonly Regex Token = new(@"\S+", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Statistics = Lexicon.AVERAGE.Concat(Lexicon.MEDIAN).Concat(Lexicon.SUM)
            .Where(w => !w.Contains(' ', StringComparison.Ordinal) && w.Length >= MinPart)
            .ToHashSet(StringComparer.Ordinal);

        private static readonly string[] Closers = Statistics.Concat(Lexicon.COMPOUND_HEADS)
            .OrderByDescending(w => w.Length).ToArray();

        private static readonly HashSet<string> Markers = Lexicon.MOST.Concat(Lexicon.LEAST).ToHashSet(StringComparer.Ordinal);

        private readonly FieldResolver _fields;

        public Morphology(FieldResolver fields) => _fields = fields;

        /// <summary><paramref name="query"/> with every compound and affixed superlative written as its parts.</summary>
        public string Expand(string query) => Token.Replace(query, m => Rewrite(m.Value) ?? m.Value);

        private string? Rewrite(string token)
        {
            var start = IndexOfLetter(token, fromEnd: false);
            var end = IndexOfLetter(token, fromEnd: true);
            if (start < 0) return null;
            var core = token[start..(end + 1)];
            if (core.Any(c => !char.IsLetter(c) && c != '-')) return null;
            var folded = TextMatching.NormalizeForLookup(core);
            var rewritten = PinakaRule(folded) ?? SuperlativeSuffix(folded) ?? Decompound(folded);
            return rewritten is null ? null : token[..start] + rewritten + token[(end + 1)..];
        }

        private static int IndexOfLetter(string s, bool fromEnd)
        {
            if (fromEnd)
            {
                for (var i = s.Length - 1; i >= 0; i--) if (char.IsLetter(s[i])) return i;
            }
            else
            {
                for (var i = 0; i < s.Length; i++) if (char.IsLetter(s[i])) return i;
            }
            return -1;
        }

        private string? PinakaRule(string word)
        {
            if (!word.StartsWith(Pinaka, StringComparison.Ordinal) || _fields.IsFieldWord(word)) return null;
            var body = word[Pinaka.Length..];
            if (body.StartsWith('-')) body = body[1..];
            if (body.Length == 0) return null;
            // The linker joins an adjective to the noun after it: -ng after a vowel ("mababang"), -g
            // after an n ("magaang").
            var stems = new List<string> { body };
            if (body.EndsWith("ng", StringComparison.Ordinal))
            {
                stems.Add(body[..^2]);
                stems.Add(body[..^1]);
            }
            foreach (var stem in stems)
            {
                var whole = Pinaka + stem;
                if (whole != word && IsListed(whole)) return whole;
                if (Lexicon.SUPERLATIVE_STEMS.TryGetValue(stem, out var superlative)) return superlative;
                if (_fields.IsFieldWord(stem)) return $"{Most} {stem}";
            }
            return null;
        }

        private string? SuperlativeSuffix(string word)
        {
            if (!word.EndsWith(Ste, StringComparison.Ordinal) || word.Length <= Ste.Length + 2 || _fields.IsFieldWord(word))
                return null;
            var stem = word[..^Ste.Length];
            // A table stem wins even over a listed form: "ligste" is listed as least, but with no
            // property. A field's name as the stem only when the whole is not listed, so German
            // "dichteste" stays as it is.
            if (Lexicon.SUPERLATIVE_STEMS.TryGetValue(stem, out var superlative)) return superlative;
            return stem.Length >= MinPart && !Markers.Contains(word) && _fields.IsFieldWord(stem) ? $"{Most} {stem}" : null;
        }

        private string? Decompound(string word)
        {
            if (word.Length < MinCompound || !word.All(char.IsLetter) || IsWord(word)) return null;
            // The longest first part first, so "smältpunkts|skillnaden" is not tried as "smält|...".
            for (var split = word.Length - MinPart; split >= MinPart; split--)
            {
                var first = word[..split];
                if (!IsModifier(first)) continue;
                foreach (var link in Links)
                {
                    if (string.CompareOrdinal(word, split, link, 0, link.Length) != 0) continue;
                    var rest = word[(split + link.Length)..];
                    if (rest.Length >= MinPart && IsWord(rest)) return $"{first} {rest}";
                }
            }
            return null;
        }

        /// <summary>A word that may open a compound: a field's name or a statistic, exactly.</summary>
        private bool IsModifier(string part) => _fields.IsFieldWord(part) || Statistics.Contains(part);

        /// <summary>A word that may close one: any known word, with a short ending.</summary>
        private bool IsWord(string part) =>
            _fields.KnowsWord(part) ||
            Closers.Any(c => part.StartsWith(c, StringComparison.Ordinal) && part.Length - c.Length <= MaxEnding);

        private bool IsListed(string word) => Markers.Contains(word) || _fields.IsFieldWord(word);
    }
}
