using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Atomic.Core.Ai;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Compose;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;

namespace Atomic.App.Services
{
    /// <summary>
    /// One conversation, and the only thing in the app that talks to
    /// <see cref="ChatOrchestrator"/>.
    ///
    /// <para><b>Held once by MainWindow, not per page.</b> Building the stack parses 118 elements
    /// and indexes ~780 documents; keeping one instance alive across navigation is the WinUI
    /// equivalent of <see cref="RetrievalService"/>'s process-wide cache, and it is also what lets
    /// a follow-up like "and its density?" survive the user opening a table mid-conversation - the
    /// dialogue state lives in the orchestrator.</para>
    ///
    /// <para>The engine is synchronous and sub-50 ms once warm, but the <i>first</i> call builds
    /// the index, so every call goes through <see cref="Task.Run"/>. Blocking the UI thread for a
    /// second on the first question is exactly the impression the panel must not give.</para>
    /// </summary>
    public sealed class ChatSession : INotifyPropertyChanged
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private ChatOrchestrator? _orchestrator;
        private bool _isBusy;
        private IReadOnlyList<string> _suggestions = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raised when the day's remaining messages change, so the badge can follow.
        ///
        /// <para>Separate from <see cref="PropertyChanged"/> because the count is not a property of
        /// the session - it is stored per day and shared by every session on the machine.</para>
        /// </summary>
        public event EventHandler? LimitChanged;

        /// <summary>The thread, oldest first. Bound directly by the message list.</summary>
        public ObservableCollection<ChatMessage> Messages { get; } = [];

        /// <summary>True while a question is in flight, so the composer can disable Send.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => Set(ref _isBusy, value);
        }

        /// <summary>
        /// Chips offered under the latest answer.
        ///
        /// <para>Held on the session rather than on the message so only the most recent turn shows
        /// them - suggestions from five answers ago are stale prompts, and rendering a chip row on
        /// every bubble turns the thread into a wall of buttons.</para>
        /// </summary>
        public IReadOnlyList<string> Suggestions
        {
            get => _suggestions;
            private set => Set(ref _suggestions, value);
        }

        /// <summary>
        /// What to offer before anything has been asked: the general questions and the tool
        /// examples, all of them.
        ///
        /// <para>The orchestrator offers three after an answer, which is right under a thread but
        /// thin for a first visit - the user has seen nothing yet and these are the only hint of
        /// what the assistant does. They are shipped and translated in every language, and read
        /// each time rather than cached, because the UI language can change while the app runs.</para>
        /// </summary>
        public IReadOnlyList<string> Starters
        {
            get
            {
                var strings = Strings();
                return [.. strings.Array("ai_suggest_general"), .. strings.Array("ai_suggest_tools")];
            }
        }

        /// <summary>True when the engine could not be built at all - no element data.</summary>
        public bool Unavailable { get; private set; }

        /// <summary>
        /// Ask a question and append both turns.
        /// </summary>
        /// <remarks>
        /// Serialised by <see cref="_gate"/>: two questions in flight would interleave their writes
        /// to the shared <see cref="DialogueState"/>, and the second answer would inherit a focus
        /// element from a turn that had not finished resolving.
        /// </remarks>
        public async Task AskAsync(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return;

            await _gate.WaitAsync().ConfigureAwait(true);
            try
            {
                IsBusy = true;
                Suggestions = [];
                Messages.Add(ChatMessage.FromUser(question.Trim()));

                // The allowance, before any work is done. The question stays in the thread: the
                // user asked it, and hiding it would make the refusal look like it answered
                // nothing in particular.
                if (!AiChatLimiter.CanSend())
                {
                    Messages.Add(new ChatMessage(
                        ChatRole.Assistant, AiChatLimiter.ReachedMessage(Strings())));
                    LimitChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                AiChatLimiter.Increment();
                LimitChanged?.Invoke(this, EventArgs.Empty);

                var thinking = ChatMessage.Thinking();
                Messages.Add(thinking);

                var reply = await Task.Run(() => Ask(question.Trim())).ConfigureAwait(true);

                Messages.Remove(thinking);

                if (reply is null)
                {
                    Unavailable = true;
                    Messages.Add(new ChatMessage(
                        ChatRole.Assistant, Strings().Get(S.ai_no_data_right_now)));
                    return;
                }

                Messages.Add(new ChatMessage(
                    ChatRole.Assistant,
                    reply.Text,
                    ChatActionCodec.Encode(reply.Actions),
                    ChatCardCodec.Encode(reply.Card)));

                Suggestions = reply.Suggestions;
            }
            finally
            {
                IsBusy = false;
                _gate.Release();
            }

            // After the gate, so a slow disk cannot hold up the next question. Fire-and-forget is
            // right here: losing the last turn to a failed write is a far smaller cost than making
            // the user wait on one, and the next turn rewrites the whole thread anyway.
            _ = ChatHistoryStore.SaveAsync(Messages);

            if (_orchestrator is { } chat) _ = InterestProfileStore.SaveAsync(chat.Profile);
        }

        /// <summary>Clear the thread and the dialogue state, keeping the warm index.</summary>
        public void Reset()
        {
            Messages.Clear();
            Suggestions = [];
            _orchestrator?.Reset();
            _ = ChatHistoryStore.ClearAsync();
        }

        /// <summary>
        /// Load the persisted thread into an empty session.
        ///
        /// <para>The messages come back but the <see cref="DialogueState"/> does not, so the first
        /// question after a restart cannot be a bare follow-up. That is deliberate: restoring the
        /// state would let "and its density?" resolve against a focus element chosen days ago, which
        /// reads as the assistant having quietly changed the subject.</para>
        /// </summary>
        public async Task RestoreAsync()
        {
            if (Messages.Count > 0) return;

            foreach (var message in await ChatHistoryStore.LoadAsync().ConfigureAwait(true))
                Messages.Add(message);

            // The greeting for a returning user depends on this, and the panel greets before the
            // first question is asked - so it has to be loaded here rather than lazily.
            if (Orchestrator() is { } orchestrator)
                await InterestProfileStore.LoadAsync(orchestrator.Profile).ConfigureAwait(true);
        }

        /// <summary>
        /// The blocking half, always called on a background thread.
        /// </summary>
        /// <returns>Null when the element data could not be read, so the caller can say so.</returns>
        private ChatReply? Ask(string question)
        {
            var orchestrator = Orchestrator();
            return orchestrator?.Ask(question);
        }

        private ChatOrchestrator? Orchestrator()
        {
            if (_orchestrator is { } cached) return cached;

            var retrieval = RetrievalService.Get(ElementRepository.Source);
            if (retrieval is null) return null;

            _orchestrator = new ChatOrchestrator(
                retrieval,
                // Per-language, because the orchestrator answers in the language of the message
                // rather than the language of the UI - the whole point of detecting it per turn.
                language => ResourceStringProvider.For(language),
                Entitlement,
                language: AppLanguage.BaseCode(AppLanguage.Current));

            return _orchestrator;
        }

        /// <summary>
        /// Read live rather than captured, so a purchase completing mid-session takes effect on the
        /// next question. <see cref="ChatOrchestrator"/> drops its cached engine when this changes.
        /// </summary>
        private static Entitlements Entitlement() =>
            new(IsPro: SettingsStore.HasAnyPro, IsProPlus: SettingsStore.IsProPlusUser);

        private static IStringProvider Strings() =>
            ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
