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
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Atomic.Core.Sync;

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

        // ---- Conversations -----------------------------------------------------------------------

        private List<StoredChatSession> _history = [];
        private bool _historyLoaded;
        private bool _restored;
        private bool _isSyncingHistory;

        /// <summary>
        /// The conversation on screen, once it has something in it. Null for a new chat until its first
        /// answer, so an empty chat never becomes an entry in the history.
        /// </summary>
        private StoredChatSession? _current;

        /// <summary>The saved conversations, newest first - this device's and, once synced, the phone's.</summary>
        public IReadOnlyList<StoredChatSession> History => _history;

        /// <summary>The conversation on screen, or null for a new chat that has not been answered yet.</summary>
        public StoredChatSession? Current => _current;

        /// <summary>Raised when <see cref="History"/> changes: a save, or a sync that brought something in.</summary>
        public event EventHandler? HistoryChanged;

        /// <summary>True while the history is being synced with the phone.</summary>
        public bool IsSyncingHistory
        {
            get => _isSyncingHistory;
            private set => Set(ref _isSyncingHistory, value);
        }

        /// <summary>
        /// Put a saved conversation on screen.
        /// </summary>
        /// <remarks>
        /// <para>The dialogue state starts fresh, as it does after a restart and for the same reason:
        /// a follow-up resolved against a focus element from another day reads as the assistant
        /// changing the subject. The conversation's language is carried, as Android carries it, so
        /// the next answer continues in the language the conversation was in.</para>
        ///
        /// <para>Taken by reference, not by id: a conversation another client saved without an id
        /// still has to be openable.</para>
        /// </remarks>
        public void OpenConversation(StoredChatSession conversation)
        {
            if (IsBusy) return;

            _restored = true;
            _current = conversation;

            Messages.Clear();
            foreach (var message in conversation.Messages) Messages.Add(message);
            Suggestions = [];

            if (Orchestrator() is { } orchestrator)
            {
                orchestrator.Reset();
                orchestrator.State.ActiveLanguage = conversation.Language;
            }
        }

        /// <summary>
        /// Bring the history up to date with the phone's, and save the result here.
        /// </summary>
        /// <remarks>
        /// A failed or skipped sync leaves the history as it was: see <see cref="ChatHistorySync"/>
        /// on why that must never be read as an empty cloud.
        /// </remarks>
        public async Task SyncHistoryAsync()
        {
            await LoadHistoryAsync().ConfigureAwait(true);
            if (!ChatHistorySync.CanSync) return;

            IsSyncingHistory = true;
            try
            {
                var merged = await ChatHistorySync.SyncAsync(_history).ConfigureAwait(true);
                if (merged is null) return;

                // The on-screen conversation is re-pointed at its merged copy, so the next save
                // updates it rather than adding it a second time.
                var currentId = _current?.Id;
                _history = merged;
                if (!string.IsNullOrEmpty(currentId))
                    _current = _history.FirstOrDefault(s => s.Id == currentId) ?? _current;

                await ChatHistoryStore.SaveAsync(_history).ConfigureAwait(true);
                HistoryChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                IsSyncingHistory = false;
            }
        }

        private async Task LoadHistoryAsync()
        {
            if (_historyLoaded) return;

            _history = await ChatHistoryStore.LoadAsync().ConfigureAwait(true);
            _historyLoaded = true;
        }

        /// <summary>
        /// Save the conversation on screen into the history, then share it.
        /// </summary>
        /// <remarks>
        /// Title and timestamp are rewritten on every save, as Android rewrites them, with Android's
        /// title rule - two rules would leave the title changing with whichever device saved last.
        /// </remarks>
        private async Task SaveConversationAsync()
        {
            var messages = Messages.Where(m => m.Role != ChatRole.Thinking).ToList();
            if (messages.Count == 0) return;

            await LoadHistoryAsync().ConfigureAwait(true);

            _current ??= new StoredChatSession();
            _current.Title = ChatHistoryDocument.Title(messages.FirstOrDefault(m => m.IsUser)?.Text);
            _current.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _current.Language = _orchestrator?.State.ActiveLanguage ?? AppLanguage.BaseCode(AppLanguage.Current);
            _current.Messages = messages;

            _history.Remove(_current);
            _history.Insert(0, _current);
            if (_history.Count > ChatHistoryDocument.MaxSessions)
                _history.RemoveRange(ChatHistoryDocument.MaxSessions, _history.Count - ChatHistoryDocument.MaxSessions);

            await ChatHistoryStore.SaveAsync(_history).ConfigureAwait(true);
            HistoryChanged?.Invoke(this, EventArgs.Empty);

            await SyncHistoryAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Ask a question and append both turns.
        /// </summary>
        /// <remarks>
        /// Serialised by <see cref="_gate"/>: two questions in flight would interleave their writes
        /// to the shared <see cref="DialogueState"/>, and the second answer would inherit a focus
        /// element from a turn that had not finished resolving.
        /// </remarks>
        /// <summary>How long the question has the stage to itself before the thinking dots join it.</summary>
        private static readonly TimeSpan QuestionBeat = TimeSpan.FromMilliseconds(180);

        /// <summary>The shortest time from sending to the answer appearing. See <see cref="AskAsync"/>.</summary>
        private static readonly TimeSpan MinimumThinking = TimeSpan.FromMilliseconds(650);

        public async Task AskAsync(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return;

            await _gate.WaitAsync().ConfigureAwait(true);
            try
            {
                IsBusy = true;
                Suggestions = [];
                Messages.Add(ChatMessage.FromUser(question.Trim()));

                // The question lands on its own before anything else moves. The engine answers in
                // well under 50 ms, so without this the question, the thinking dots and the answer
                // all arrived in the same frame or two and animated in together - the reply seemed
                // to appear before the question had.
                var sent = System.Diagnostics.Stopwatch.StartNew();
                await Task.Delay(QuestionBeat).ConfigureAwait(true);

                // The allowance, before any work is done. The question stays in the thread: the
                // user asked it, and hiding it would make the refusal look like it answered
                // nothing in particular.
                if (!AiChatLimiter.CanSend())
                {
                    await Task.Delay(QuestionBeat).ConfigureAwait(true);
                    Messages.Add(new ChatMessage(
                        ChatRole.Assistant, AiChatLimiter.ReachedMessage(Strings())));
                    LimitChanged?.Invoke(this, EventArgs.Empty);
                    LogTurn(new AiTurnStats(
                        AiTurnStats.Paths.RateLimited, ActiveLanguage(),
                        QueryChars: question.Trim().Length, QueryWords: AiTurnStats.Words(question)));
                    Analytics.Log(AiTurnStats.RateLimitedEvent, ("daily_limit", AiChatLimiter.DailyLimit));
                    return;
                }

                AiChatLimiter.Increment();
                LimitChanged?.Invoke(this, EventArgs.Empty);

                var thinking = ChatMessage.Thinking();
                Messages.Add(thinking);

                var languageBefore = _orchestrator?.State.ActiveLanguage;
                var answering = System.Diagnostics.Stopwatch.StartNew();
                var reply = await Task.Run(() => Ask(question.Trim())).ConfigureAwait(true);
                var latencyMs = answering.ElapsedMilliseconds;

                // The dots stay long enough to be read as the assistant working. The answer was
                // computed while they showed, so this is pacing, not waiting: a slow first question
                // that builds the index is not made any slower by it.
                var left = MinimumThinking - sent.Elapsed;
                if (left > TimeSpan.Zero) await Task.Delay(left).ConfigureAwait(true);

                // In the dots' place, rather than removing them and adding the answer after: the
                // answer rises in where the dots were, instead of the dots vanishing and the list
                // jumping before anything new appears.
                var answer = reply is null
                    ? new ChatMessage(ChatRole.Assistant, Strings().Get(S.ai_no_data_right_now))
                    : new ChatMessage(
                        ChatRole.Assistant,
                        reply.Text,
                        ChatActionCodec.Encode([.. reply.Actions, .. ReportAction(reply, question)]),
                        ChatCardCodec.Encode(reply.Card));

                var at = Messages.IndexOf(thinking);
                if (at >= 0) Messages[at] = answer;
                else Messages.Add(answer);

                if (reply is null)
                {
                    Unavailable = true;
                    LogTurn(new AiTurnStats(
                        AiTurnStats.Paths.ContextLost, ActiveLanguage(),
                        QueryChars: question.Trim().Length, QueryWords: AiTurnStats.Words(question),
                        LatencyMs: latencyMs));
                    return;
                }

                Suggestions = reply.Suggestions;
                LogTurn(AiTurnStats.From(
                    reply, question, ActiveLanguage(),
                    languageSwitched: languageBefore is not null && languageBefore != ActiveLanguage(),
                    elementResolved: _orchestrator?.State.FocusElement is not null,
                    actionCount: answer.DecodedActions.Count,
                    latencyMs));
            }
            finally
            {
                IsBusy = false;
                _gate.Release();
            }

            // After the gate, so a slow disk or a slow network cannot hold up the next question.
            // Fire-and-forget is right here: losing the last turn to a failed write is a far smaller
            // cost than making the user wait on one, and the next turn rewrites the conversation anyway.
            _ = SaveConversationAsync();

            if (_orchestrator is { } chat) _ = InterestProfileStore.SaveAsync(chat.Profile);
        }

        /// <summary>
        /// Start a new conversation, keeping the warm index.
        /// </summary>
        /// <remarks>
        /// The conversation that was on screen stays in the history - it was saved after its last
        /// answer. This used to delete the saved thread, which was right when there was only one and
        /// would now throw away a conversation the user may want back.
        /// </remarks>
        public void Reset()
        {
            _restored = true;
            _current = null;

            Messages.Clear();
            Suggestions = [];
            _orchestrator?.Reset();
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
            // Once per run. After a new chat, reopening the panel must show that new chat, not bring
            // back the last conversation the user just chose to leave.
            if (_restored || Messages.Count > 0) return;
            _restored = true;

            await LoadHistoryAsync().ConfigureAwait(true);

            if (_history.FirstOrDefault() is { } latest)
            {
                _current = latest;
                foreach (var message in latest.Messages) Messages.Add(message);
            }

            // In the background: the phone's conversations should be there by the time the user
            // opens the history, without making the panel wait for the network to open.
            _ = SyncHistoryAsync();

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

        /// <summary>Where the analytics events say they came from; Android names its hosting screen.</summary>
        public const string AnalyticsHost = "chat_panel";

        private string ActiveLanguage() =>
            _orchestrator?.State.ActiveLanguage ?? AppLanguage.BaseCode(AppLanguage.Current);

        /// <summary>The shape of the turn, never the question (D45, roadmap 0.7).</summary>
        private static void LogTurn(AiTurnStats stats) =>
            Analytics.Log(AiTurnStats.ResponseEvent, stats.Parameters(AnalyticsHost));

        /// <summary>
        /// On a decline, a chip that offers to send the question to the developer (roadmap 0.7).
        /// Tapping it only opens a dialog saying what would be sent; nothing leaves the device
        /// unless the user confirms there.
        /// </summary>
        private IEnumerable<ChatAction> ReportAction(ChatReply reply, string question)
        {
            if (reply.Source != ReplySource.Declined || string.IsNullOrWhiteSpace(question)) yield break;

            var language = ActiveLanguage();
            yield return new ChatAction(
                ResourceStringProvider.For(language).Get(S.ai_send_question),
                DeepLinkTarget.ReportQuestion,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["q"] = UnansweredQuestions.Clean(question),
                    ["lang"] = language,
                    ["reason"] = reply.DeclineReason?.WireName() ?? "unknown",
                });
        }

        /// <summary>
        /// Take the send chip off its message once the question has gone, so it cannot go twice.
        /// Saved, so a restored thread does not offer it again either.
        /// </summary>
        public void RemoveReportAction(ChatAction sent)
        {
            for (var i = 0; i < Messages.Count; i++)
            {
                var message = Messages[i];
                var actions = message.DecodedActions;
                if (!actions.Any(a => IsSame(a, sent))) continue;

                Messages[i] = new ChatMessage(
                    message.Role, message.Text,
                    ChatActionCodec.Encode(actions.Where(a => !IsSame(a, sent)).ToList()),
                    message.Cards)
                {
                    Id = message.Id,
                    Timestamp = message.Timestamp,
                };
                _ = SaveConversationAsync();
                return;
            }

            // Records compare dictionaries by reference, so a decoded copy never equals the chip
            // that was tapped; the question is what identifies it.
            static bool IsSame(ChatAction a, ChatAction b) =>
                a.Target == DeepLinkTarget.ReportQuestion && b.Target == DeepLinkTarget.ReportQuestion &&
                a.Args.GetValueOrDefault("q") == b.Args.GetValueOrDefault("q");
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
