using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Compose;
using Atomic.Core.Localization;
using Microsoft.UI.Xaml;

namespace Atomic.App.Services
{
    /// <summary>Who said it.</summary>
    public enum ChatRole
    {
        User,
        Assistant,

        /// <summary>
        /// The placeholder shown while the engine runs. Never persisted - a thinking bubble
        /// restored from history would be a conversation permanently waiting for an answer.
        /// </summary>
        Thinking,
    }

    /// <summary>
    /// One stored turn.
    ///
    /// <para><b><see cref="Actions"/> and <see cref="Cards"/> are strings, deliberately.</b> They
    /// hold codec output - <see cref="ChatActionCodec"/> and <see cref="ChatCardCodec"/> - rather
    /// than typed objects, which keeps a stored message a flat record and lets a message written by
    /// an older build decode to nothing instead of needing a migration. Android stores them the
    /// same way, so a synced thread reads on both platforms (P9).</para>
    /// </summary>
    public sealed class ChatMessage
    {
        /// <summary>
        /// Parameterless plus init setters, deliberately.
        /// </summary>
        /// <remarks>
        /// An earlier version used a <c>[JsonConstructor]</c> whose <c>timestamp</c> parameter was
        /// <c>DateTimeOffset?</c> while the property was <c>DateTimeOffset</c>. The source generator
        /// rejects that mismatch at run time, not at compile time, and the throw landed inside an
        /// <c>async void</c> handler - so restoring a saved thread took the whole process down with
        /// a stowed WinRT exception and no managed stack. Init setters have no parameter list to
        /// drift out of step with the properties.
        /// </remarks>
        public ChatMessage()
        {
        }

        public ChatMessage(ChatRole role, string text, string? actions = null, string? cards = null)
        {
            Role = role;
            Text = text;
            Actions = actions;
            Cards = cards;
        }

        /// <summary>
        /// The message's id. Android gives every message one, and a conversation synced between the
        /// two keeps them, which is what lets a Windows rewrite of the conversation carry each
        /// message's fields from the phone across. A thread saved by an earlier build has none and is
        /// given fresh ones on load, which is harmless: nothing has been shared under the old ones.
        /// </summary>
        public string Id { get; init; } = Guid.NewGuid().ToString();

        public ChatRole Role { get; init; }

        public string Text { get; init; } = string.Empty;

        /// <summary>Encoded <see cref="ChatAction"/>s, or null when the answer carries none.</summary>
        public string? Actions { get; init; }

        /// <summary>An encoded <see cref="ChatCard"/>, or null.</summary>
        public string? Cards { get; init; }

        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

        // ---- Decoded views, for binding ---------------------------------------------------------
        //
        // Computed on read rather than stored: the stored form is the string, and a second cached
        // copy is how the two drift apart. Decoding a handful of records per bubble is nothing next
        // to laying one out.

        [JsonIgnore]
        public IReadOnlyList<ChatAction> DecodedActions => ChatActionCodec.Decode(Actions);

        [JsonIgnore]
        public ChatCard? DecodedCard => ChatCardCodec.Decode(Cards);

        /// <summary>
        /// The card kind without a full decode, for the template selector.
        ///
        /// <para><see cref="ChatCardCodec.KindOf"/> exists precisely so choosing a template does not
        /// pay for parsing the card's arguments - selection runs on every container recycle as the
        /// list scrolls.</para>
        /// </summary>
        [JsonIgnore]
        public ChatCardKind? CardKind => ChatCardCodec.KindOf(Cards);

        [JsonIgnore]
        public bool IsUser => Role == ChatRole.User;

        /// <summary>The share button's name and tooltip (roadmap 2.10).</summary>
        public string ShareLabel =>
            ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current)).Get(Atomic.Core.Localization.S.ai_share_image);

        // ---- Card slot ---------------------------------------------------------------------------
        //
        // The engine emits cards regardless of entitlement - ChatCardPolicy only governs network
        // cards - so whether a card is *shown* is a UI decision, exactly as on Android where
        // ProCardGate is a view treatment over a card that stayed bound.

        [JsonIgnore]
        public Visibility HasCard =>
            CardKind is null ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Visual answers are a PRO feature; the card is bound either way.</summary>
        [JsonIgnore]
        public bool IsCardLocked => CardKind is not null && !SettingsStore.HasAnyPro;

        [JsonIgnore]
        public Visibility IsCardLockedVisibility =>
            IsCardLocked ? Visibility.Visible : Visibility.Collapsed;

        [JsonIgnore]
        public string CardLockedLabel => Strings().Get(S.ai_card_pro_locked);

        private static IStringProvider Strings() =>
            ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));


        public static ChatMessage FromUser(string text) => new(ChatRole.User, text);

        public static ChatMessage Thinking() => new(ChatRole.Thinking, string.Empty);
    }
}
