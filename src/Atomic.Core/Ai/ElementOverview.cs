using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Atomic.Core.Ai.Data;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// What an element <i>is</i>, for a question that names one and asks nothing in particular:
    /// "gold", "tell me about gold", "element 79".
    ///
    /// <para><b>The most basic thing a user can ask, and it was answering nothing.</b> All three
    /// are in <c>must_defer.tsv</c> - the engine is required to decline them, because there is no
    /// field to look up - and on Android they are answered by <c>provideOverview</c>. There was no
    /// Windows equivalent, so they reached the decline: "gold" replied "Hmm, I couldn't find
    /// information about that."</para>
    ///
    /// <para>The basic-facts block is Android's, in its order, from the same shipped strings. The
    /// prose that follows is the element's own description, filtered to the reader's language by
    /// <see cref="ElementNarrative.SentencesFor"/> so a Swedish overview does not quote the English
    /// text.</para>
    /// </summary>
    public static class ElementOverview
    {
        /// <summary>How much of the description to include.</summary>
        private const int MaxSentences = 3;

        /// <summary>The overview, or null when the element has nothing to say.</summary>
        public static string? Of(
            ElementRecord element, string displayName, string language, IStringProvider strings)
        {
            var facts = new StringBuilder();

            void Fact(string labelKey, string? value)
            {
                if (string.IsNullOrWhiteSpace(value) || value == "---") return;
                var label = strings.Get(labelKey).Replace(":", string.Empty, StringComparison.Ordinal).Trim();
                facts.Append("• **").Append(label).Append("**: ").Append(value.Trim()).Append('\n');
            }

            // Read off the typed record rather than the raw JSON: atomic number is a number, phase
            // is an enum and mass is a quantity, so a blanket cast to Text finds none of them -
            // which is exactly why the first attempt produced nothing at all.
            Fact(S.atomic_number_label, element.AtomicNumber.ToString(CultureInfo.InvariantCulture));
            Fact(S.element_symbols, element.Symbol);
            Fact(S.atomic_mass_colon, Show(element.Value("atomic_mass")));
            Fact(S.phase_stp_colon, element.Phase);
            Fact(S.type_label, Show(element.Value("element_type")));

            if (facts.Length == 0) return null;

            var text = new StringBuilder();
            text.Append("### ").Append(strings.Get(S.ai_rich_overview_basic_facts, displayName)).Append("\n\n");
            text.Append(facts.ToString().TrimEnd());

            var prose = ElementNarrative
                .SentencesFor(Show(element.Value("description")), language)
                .Take(MaxSentences)
                .ToList();

            if (prose.Count > 0)
            {
                text.Append("\n\n")
                    .Append(string.Join(". ", prose.Select(s => s.Trim())).Trim())
                    .Append('.');
            }

            return text.ToString();
        }

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
