using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Ai.Engine
{
    /// <summary>
    /// What the agent remembers across turns.
    ///
    /// <para>The important mechanism here is <b>slot-emptiness inheritance</b>, not a pronoun list.
    /// A follow-up like "and its density?" arrives with a field but no element, so the element is
    /// inherited from <see cref="FocusElement"/>; "what about iron?" arrives with an element but no
    /// field, so the field is inherited from <see cref="LastFieldIds"/>. That single rule handles
    /// follow-ups in all twelve languages with no per-language data at all - "och dess densitet?",
    /// "und seine Dichte?", "它的密度呢", "اور اس کی کثافت؟" all work for free.</para>
    /// </summary>
    public sealed class DialogueState
    {
        /// <summary>The element the conversation is currently about.</summary>
        public string? FocusElement { get; set; }

        /// <summary>Recently discussed elements, newest first, for "the previous one" style references.</summary>
        public LinkedList<string> RecentElements { get; } = new();

        /// <summary>Fields asked about on the previous turn, inherited by a bare element follow-up.</summary>
        public IReadOnlyList<string> LastFieldIds { get; set; } = [];

        /// <summary>The previous plan, cloned when a continuation supplies only one new slot.</summary>
        public QueryPlan? LastPlan { get; set; }

        /// <summary>Elements the previous answer listed, for "and the second one?".</summary>
        public IReadOnlyList<string> LastResultKeys { get; set; } = [];

        /// <summary>The unit the user last asked for, so "and iron?" keeps the same scale.</summary>
        public string? LastTargetUnit { get; set; }

        public string ActiveLanguage { get; set; } = "en";

        /// <summary>
        /// The element a quiz question is waiting on, lower-cased, or null when no question is
        /// outstanding.
        /// </summary>
        /// <remarks>
        /// A quiz spans turns - ask, then judge the reply - so it has to live with the rest of
        /// what the conversation remembers rather than in the handler. It is part of the state a
        /// host persists, so a quiz survives the panel being closed and reopened.
        /// </remarks>
        public string? PendingQuizAnswer { get; set; }

        /// <summary>
        /// Fields already covered for <see cref="FocusElement"/>, oldest first, so "tell me more"
        /// says something new each time.
        /// </summary>
        /// <remarks>
        /// <para>Per element: it is reset when the conversation turns to a different one, because
        /// what has been said about gold says nothing about iron.</para>
        /// <para>Keyed by <b>field id</b>, where Android keys by the localized label. That makes
        /// the set survive a mid-thread language switch, which the agent explicitly supports - ask
        /// in English, follow up in Swedish, and Android starts repeating itself because
        /// "Densitet" is not "Density".</para>
        /// </remarks>
        public IReadOnlyList<string> SharedFieldIds => _shared;

        private readonly List<string> _shared = [];
        private string? _sharedFor;

        /// <summary>Record that <paramref name="fieldIds"/> have now been said about an element.</summary>
        public void NoteShared(string? elementKey, IReadOnlyList<string> fieldIds)
        {
            if (elementKey is null) return;

            if (!string.Equals(elementKey, _sharedFor, StringComparison.OrdinalIgnoreCase))
            {
                _shared.Clear();
                _sharedFor = elementKey;
            }

            foreach (var id in fieldIds)
            {
                if (!_shared.Contains(id, StringComparer.Ordinal)) _shared.Add(id);
            }
        }

        /// <summary>Whether a field has already been covered for this element.</summary>
        public bool HasShared(string elementKey, string fieldId) =>
            string.Equals(elementKey, _sharedFor, StringComparison.OrdinalIgnoreCase)
            && _shared.Contains(fieldId, StringComparer.Ordinal);

        /// <summary>Record what an answer covered so the next turn can refer back to it.</summary>
        public void NoteAnswer(QueryPlan plan, IReadOnlyList<string> resultKeys)
        {
            LastPlan = plan;
            if (plan.FieldIds.Count > 0) LastFieldIds = plan.FieldIds;
            if (plan.TargetUnit is not null) LastTargetUnit = plan.TargetUnit;

            if (resultKeys.Count == 0) return;

            LastResultKeys = resultKeys;
            FocusElement = resultKeys[0];

            // Only for a question about one element. A comparison or a filter list names a field
            // for several at once, and marking it "said about" the first of thirty would make
            // "tell me more" skip the very fields it should still be offering.
            if (plan.ElementKeys.Count == 1) NoteShared(FocusElement, plan.FieldIds);

            // Reversed, so that after the loop the first result sits at the front.
            for (var i = resultKeys.Count - 1; i >= 0; i--)
            {
                RecentElements.Remove(resultKeys[i]);
                RecentElements.AddFirst(resultKeys[i]);
            }
            while (RecentElements.Count > MaxRecent) RecentElements.RemoveLast();
        }

        /// <summary>
        /// A detached copy, for speculative planning.
        ///
        /// <para>Answering a compound question means planning each clause in turn, with the second
        /// inheriting from the first. If any clause fails the whole attempt is abandoned, so that
        /// work has to happen somewhere the real conversation state cannot be corrupted.</para>
        /// </summary>
        public DialogueState CopyOf()
        {
            var copy = new DialogueState
            {
                FocusElement = FocusElement,
                LastFieldIds = LastFieldIds,
                LastPlan = LastPlan,
                LastResultKeys = LastResultKeys,
                LastTargetUnit = LastTargetUnit,
                ActiveLanguage = ActiveLanguage,
            };
            foreach (var key in RecentElements) copy.RecentElements.AddLast(key);
            return copy;
        }

        /// <summary>Adopt another state's contents, once a speculative attempt has succeeded.</summary>
        public void Adopt(DialogueState other)
        {
            FocusElement = other.FocusElement;
            RecentElements.Clear();
            foreach (var key in other.RecentElements) RecentElements.AddLast(key);
            LastFieldIds = other.LastFieldIds;
            LastPlan = other.LastPlan;
            LastResultKeys = other.LastResultKeys;
            LastTargetUnit = other.LastTargetUnit;
        }

        public void Clear()
        {
            FocusElement = null;
            RecentElements.Clear();
            LastFieldIds = [];
            LastPlan = null;
            LastResultKeys = [];
            LastTargetUnit = null;
            PendingQuizAnswer = null;
            _shared.Clear();
            _sharedFor = null;
        }

        private const int MaxRecent = 4;
    }
}
