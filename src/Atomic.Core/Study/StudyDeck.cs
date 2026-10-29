using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Atomic.Core.Study
{
    /// <summary>
    /// The study coach's cards (roadmap 2.6): one per skill and element, keyed
    /// <c>&lt;skill&gt;:&lt;element&gt;</c>, each with its FSRS schedule. Pure and thread-safe; the app
    /// persists it and syncs it through the progress document. Mirrors Android's <c>StudyDeck</c>.
    /// </summary>
    public sealed class StudyDeck
    {
        /// <summary>The Learning Games categories whose questions are about one given element.</summary>
        public static readonly IReadOnlySet<string> Skills = new HashSet<string>(StringComparer.Ordinal)
        {
            "element_classifications", "periodic_period", "periodic_group", "valence_electrons", "isotope_neutrons",
        };

        public const int ReviewSize = 10;

        private readonly Dictionary<string, CardState> _cards;
        private readonly object _lock = new();

        public StudyDeck(IReadOnlyDictionary<string, CardState>? cards = null)
        {
            _cards = cards is null
                ? new Dictionary<string, CardState>(StringComparer.Ordinal)
                : new Dictionary<string, CardState>(cards, StringComparer.Ordinal);
        }

        /// <summary>Every card, as the sync writes them.</summary>
        public IReadOnlyDictionary<string, CardState> Snapshot()
        {
            lock (_lock) return new Dictionary<string, CardState>(_cards, StringComparer.Ordinal);
        }

        public CardState? Card(string key)
        {
            lock (_lock) return _cards.GetValueOrDefault(key);
        }

        /// <summary>
        /// Review the card for <paramref name="skill"/> about <paramref name="elementKey"/> after an
        /// answer. A skill the coach does not schedule is ignored, so a caller can report every answer.
        /// </summary>
        /// <returns>The card's key, or null when the question does not feed a card.</returns>
        public string? Record(string? skill, string? elementKey, bool correct, long now)
        {
            if (skill is null || string.IsNullOrWhiteSpace(elementKey) || !Skills.Contains(skill)) return null;
            var key = Key(skill, elementKey);
            lock (_lock) _cards[key] = Fsrs.Review(_cards.GetValueOrDefault(key), correct ? Fsrs.Good : Fsrs.Again, now);
            return key;
        }

        /// <summary>
        /// The due cards most overdue first - by how far past due they are, relative to their
        /// interval - at most <paramref name="limit"/>.
        /// </summary>
        public IReadOnlyList<string> Due(long now, int limit = ReviewSize)
        {
            lock (_lock)
            {
                return _cards
                    .Where(e => e.Value.Due <= now)
                    .OrderByDescending(e => (double)(now - e.Value.Due) / Math.Max(e.Value.Due - e.Value.Last, Fsrs.DayMs))
                    .Take(limit)
                    .Select(e => e.Key)
                    .ToList();
            }
        }

        /// <summary>Fold in cards from elsewhere (the cloud), keeping each card's later review.</summary>
        public void MergeFrom(IReadOnlyDictionary<string, CardState> other)
        {
            lock (_lock)
            {
                foreach (var (key, card) in other)
                    _cards[key] = Newer(_cards.GetValueOrDefault(key), card) ?? card;
            }
        }

        public static string Key(string skill, string elementKey) =>
            skill + ":" + elementKey.ToLowerInvariant();

        /// <summary>The skill and element of a key, or null for one this build cannot read.</summary>
        public static (string Skill, string Element)? Parse(string key)
        {
            var at = key.IndexOf(':', StringComparison.Ordinal);
            if (at <= 0 || at == key.Length - 1) return null;
            return (key[..at], key[(at + 1)..]);
        }

        /// <summary>
        /// The card with the later review; on a tie the one with more reps, then
        /// <paramref name="cloud"/>. Shared by <see cref="Sync.ProgressMerge"/>.
        /// </summary>
        public static CardState? Newer(CardState? local, CardState? cloud)
        {
            if (local is null) return cloud;
            if (cloud is null) return local;
            if (local.Last != cloud.Last) return local.Last > cloud.Last ? local : cloud;
            if (local.Reps != cloud.Reps) return local.Reps > cloud.Reps ? local : cloud;
            return cloud;
        }

        /// <summary>One card per line, <c>key\ts\td\tdue\tlast\treps\tlapses</c>, for the app's local file.</summary>
        public static string Encode(IReadOnlyDictionary<string, CardState> cards) =>
            string.Join("\n", cards.Select(e => string.Join("\t",
                e.Key,
                e.Value.Stability.ToString("R", CultureInfo.InvariantCulture),
                e.Value.Difficulty.ToString("R", CultureInfo.InvariantCulture),
                e.Value.Due.ToString(CultureInfo.InvariantCulture),
                e.Value.Last.ToString(CultureInfo.InvariantCulture),
                e.Value.Reps.ToString(CultureInfo.InvariantCulture),
                e.Value.Lapses.ToString(CultureInfo.InvariantCulture))));

        public static IReadOnlyDictionary<string, CardState> Decode(string? text)
        {
            var cards = new Dictionary<string, CardState>(StringComparer.Ordinal);
            foreach (var line in (text ?? "").Split('\n'))
            {
                var c = line.TrimEnd('\r').Split('\t');
                if (c.Length != 7) continue;
                if (!double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ||
                    !double.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ||
                    !long.TryParse(c[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var due) ||
                    !long.TryParse(c[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var last) ||
                    !int.TryParse(c[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var reps) ||
                    !int.TryParse(c[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lapses))
                    continue;
                cards[c[0]] = new CardState(s, d, due, last, reps, lapses);
            }
            return cards;
        }
    }

    /// <summary>
    /// What the chat needs from the study coach (roadmap 2.6): the deck, a way to keep a change,
    /// and the clock. The app wraps its store; a test passes a fresh deck and a fixed time.
    /// </summary>
    public sealed class StudyAccess(StudyDeck deck, Action? save = null, Func<long>? now = null)
    {
        public StudyDeck Deck { get; } = deck;

        public Action Save { get; } = save ?? (() => { });

        public Func<long> Now { get; } = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
