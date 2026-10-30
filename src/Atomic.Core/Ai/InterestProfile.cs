using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// Which elements this user keeps asking about, so the assistant can greet them by name.
    ///
    /// <para>Counts only. No question text and no answer is kept - Android's class comment makes
    /// the same promise ("Does NOT store user-provided facts") and this keeps it structurally, by
    /// having nowhere to put one.</para>
    ///
    /// <para><b>Deliberately smaller than Android's <c>AILearningManager</c>.</b> That class also
    /// carries a property-interest map and a "technical score" that leans the reply style, but
    /// <c>trackPropertyInterest</c> and <c>incrementSession</c> are never called anywhere in the
    /// Android source. The score therefore sits at its 0.5 initial value forever,
    /// <c>isTechnicalPreferred()</c> is always false, and <c>ai_personalized_greeting_interest</c>
    /// is unreachable. Only the element half runs, so only the element half is here.</para>
    /// </summary>
    public sealed class InterestProfile
    {
        private readonly Dictionary<string, int> _elements = new(StringComparer.Ordinal);

        /// <summary>How many times each element key has been asked about.</summary>
        public IReadOnlyDictionary<string, int> Elements => _elements;

        /// <summary>Count one question about an element.</summary>
        public void Track(string? elementKey)
        {
            if (string.IsNullOrWhiteSpace(elementKey)) return;

            var key = elementKey.Trim().ToLowerInvariant();
            _elements[key] = _elements.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        /// <summary>
        /// The most-asked-about element, or null if none yet.
        ///
        /// <para>Ties break on the key, so the greeting does not change between launches for a user
        /// who has asked about two elements equally often. Android's <c>maxByOrNull</c> returns
        /// whichever the map yields first.</para>
        /// </summary>
        public string? Favourite =>
            _elements.Count == 0
                ? null
                : _elements
                    .OrderByDescending(e => e.Value)
                    .ThenBy(e => e.Key, StringComparer.Ordinal)
                    .First().Key;

        /// <summary>Restore a saved profile.</summary>
        public void Load(IReadOnlyDictionary<string, int>? counts)
        {
            _elements.Clear();
            if (counts is null) return;

            foreach (var (key, count) in counts)
                if (!string.IsNullOrWhiteSpace(key) && count > 0)
                    _elements[key.ToLowerInvariant()] = count;
        }

        /// <summary>Forget everything. Used when the user clears their chat history.</summary>
        public void Clear() => _elements.Clear();
    }
}