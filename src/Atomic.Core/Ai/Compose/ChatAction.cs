using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Localization;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Compose
{
    /// <summary>
    /// A tappable affordance attached to an answer - normally "open this element" or "open the table
    /// this figure came from".
    ///
    /// <para>The chat renderer supports no links at all (only <c>###</c> and <c>**bold**</c>), so a
    /// citation cannot be a hyperlink. Carrying it as structured data and rendering it as a chip is
    /// what makes a grounded answer navigable rather than just attributed.</para>
    /// </summary>
    public sealed record ChatAction(
        string Label,
        DeepLinkTarget Target,
        IReadOnlyDictionary<string, string>? Args = null)
    {
        public IReadOnlyDictionary<string, string> Args { get; init; } =
            Args ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static class ChatActionExtensions
    {
        /// <summary>Turn a citation into the chip a user can tap.</summary>
        public static ChatAction ToAction(this Citation citation, IStringProvider strings)
        {
            var label = Try.OrNull(() => strings.Get(S.ai_open_in_app, citation.Source));
            if (label is null || label.StartsWith("str:", StringComparison.Ordinal))
                label = citation.Source;

            return new ChatAction(label, citation.DeepLink, citation.Args);
        }
    }

    /// <summary>
    /// Serialises actions onto a stored chat message.
    ///
    /// <para>A string field rather than a typed list: it keeps the stored message a flat record, and
    /// lets older stored messages decode to an empty list with no migration.</para>
    ///
    /// <para>The format is a deliberately small record encoding rather than JSON. Records are
    /// separated by U+001E, fields by U+001F and pairs by U+001D - ASCII information separators that
    /// cannot appear in a label or an element key, so no escaping is needed.</para>
    /// </summary>
    public static class ChatActionCodec
    {
        private const char Record = '\u001E';
        private const char Field = '\u001F';
        private const char Pair = '\u001D';

        /// <returns>The encoded actions, or null when there is nothing to attach.</returns>
        public static string? Encode(IReadOnlyList<ChatAction> actions)
        {
            if (actions.Count == 0) return null;

            return string.Join(Record, actions.Select(action =>
            {
                var args = string.Join(Pair, action.Args.Select(e => $"{e.Key}={e.Value}"));
                return string.Join(Field, Sanitise(action.Label), action.Target.ToString(), args);
            }));
        }

        /// <summary>Never throws: malformed or absent data yields an empty list.</summary>
        public static IReadOnlyList<ChatAction> Decode(string? encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) return [];

            var actions = new List<ChatAction>();
            foreach (var record in encoded.Split(Record))
            {
                var parts = record.Split(Field);
                if (parts.Length < 2) continue;
                if (string.IsNullOrWhiteSpace(parts[0])) continue;
                if (!Enum.TryParse<DeepLinkTarget>(parts[1], out var target)) continue;

                var args = new Dictionary<string, string>(StringComparer.Ordinal);
                if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                {
                    foreach (var pair in parts[2].Split(Pair))
                    {
                        var at = pair.IndexOf('=', StringComparison.Ordinal);
                        if (at <= 0) continue;
                        args[pair[..at]] = pair[(at + 1)..];
                    }
                }

                actions.Add(new ChatAction(parts[0], target, args));
            }
            return actions;
        }

        /// <summary>Strip the delimiters so a label can never corrupt the encoding.</summary>
        private static string Sanitise(string value) =>
            new(value.Where(c => c != Record && c != Field && c != Pair).ToArray());
    }
}
