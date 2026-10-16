using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Ai.Cards
{
    /// <summary>The visual an answer can carry alongside its text.</summary>
    public enum ChatCardKind
    {
        ElectronShell, CrystalStructure, EmissionSpectrum, PoissonBand,
        NfpaDiamond, IonizationSeries, IsotopeDecay, Abundance,
    }

    public sealed record ChatCard(
        ChatCardKind Kind,
        string ElementKey,
        string Title,
        IReadOnlyDictionary<string, string>? Args = null)
    {
        public IReadOnlyDictionary<string, string> Args { get; init; } =
            Args ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Serialises a card onto a chat message.
    ///
    /// <para>A string field rather than a typed one, for the same reason
    /// <see cref="Compose.ChatActionCodec"/> is: the stored message format stays a flat record, and
    /// older stored messages decode to nothing rather than needing a migration.</para>
    ///
    /// <para>The delimiters are the ASCII information separators - U+001F for fields, U+001E for
    /// pairs. They cannot appear in a label or an element key, so no escaping is needed, and
    /// <see cref="Sanitise"/> strips them anyway so a value can never corrupt the encoding.</para>
    /// </summary>
    public static class ChatCardCodec
    {
        private const char Field = '\u001F';
        private const char Pair = '\u001E';

        public static string? Encode(ChatCard? card)
        {
            if (card is null) return null;

            var args = string.Join(Pair, card.Args.Select(e => $"{e.Key}={e.Value}"));
            return string.Join(Field, card.Kind.ToString(), Sanitise(card.ElementKey),
                               Sanitise(card.Title), args);
        }

        public static ChatCard? Decode(string? encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) return null;

            var parts = encoded.Split(Field);
            if (parts.Length < 3) return null;
            if (!Enum.TryParse<ChatCardKind>(parts[0], out var kind)) return null;
            if (string.IsNullOrWhiteSpace(parts[1])) return null;

            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
            {
                foreach (var pair in parts[3].Split(Pair))
                {
                    var at = pair.IndexOf('=', StringComparison.Ordinal);
                    if (at <= 0) continue;
                    args[pair[..at]] = pair[(at + 1)..];
                }
            }

            return new ChatCard(kind, parts[1], parts[2], args);
        }

        /// <summary>
        /// The card kind alone, without decoding the rest.
        ///
        /// <para>The template selector is asked for every visible row on every layout pass, so it
        /// should not pay for a full decode just to choose a template.</para>
        /// </summary>
        public static ChatCardKind? KindOf(string? encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) return null;

            var end = encoded.IndexOf(Field, StringComparison.Ordinal);
            var name = end < 0 ? encoded : encoded[..end];
            return Enum.TryParse<ChatCardKind>(name, out var kind) ? kind : null;
        }

        /// <summary>Strip the delimiters so a value can never corrupt the encoding.</summary>
        private static string Sanitise(string value) =>
            new(value.Where(c => c != Field && c != Pair).ToArray());
    }
}
