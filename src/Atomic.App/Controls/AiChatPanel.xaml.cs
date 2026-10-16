using System;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using Atomic.App.Navigation;
using Atomic.App.Services;
using Atomic.Core.Ai.Compose;
using Atomic.Core.Localization;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Atomic.App.Controls
{
    /// <summary>
    /// The Ask Atomic sheet.
    ///
    /// <para>Lives in <c>MainWindow</c>'s root grid above the content <c>Frame</c>, so it is
    /// reachable from every page and survives navigation - mirroring Android, where one controller
    /// served both hosts. Holding it here rather than per page is also what keeps the BM25 index
    /// warm; see <see cref="ChatSession"/>.</para>
    ///
    /// <para>WinUI has no bottom sheet, so this is a <c>Border</c> pinned to the bottom of a grid
    /// with a scrim behind it.</para>
    /// </summary>
    public sealed partial class AiChatPanel : UserControl
    {
        /// <summary>Sheet height as a fraction of the window, before the user drags it.</summary>
        private const double DefaultHeightFraction = 0.6;

        /// <summary>The tallest it can be dragged, so the scrim is always visibly behind it.</summary>
        private const double MaxHeightFraction = 0.9;

        private const double MinSheetHeight = 260;
        private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(200);

        private double _sheetHeight;
        private bool _isOpen;

        public AiChatPanel()
        {
            InitializeComponent();

            Session = new ChatSession();
            Session.PropertyChanged += Session_PropertyChanged;
            Session.LimitChanged += (_, _) => ApplyLimitBadge();
            Session.Messages.CollectionChanged += Messages_Changed;

            ApplyStrings();
            Loaded += OnLoaded;
        }

        /// <summary>The conversation. One per window, created here and never replaced.</summary>
        public ChatSession Session { get; }

        public bool IsOpen => _isOpen;

        /// <summary>
        /// Raised whenever the panel opens or closes, however it happened.
        ///
        /// <para>The panel closes itself from four places - the close button, the scrim, Escape, and
        /// <see cref="Close"/> - so a host that hid a launcher on open needs telling. Without this
        /// the launcher stays hidden after an Escape and the feature becomes unreachable.</para>
        /// </summary>
        public event EventHandler<bool>? OpenChanged;

        /// <summary>Where a citation chip navigates to. Supplied by the host.</summary>
        public NavigationService? Navigation { get; set; }

        /// <summary>Re-read every string. Called when the app language changes at runtime.</summary>
        public void ApplyStrings()
        {
            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

            TitleText.Text = strings.Get(S.ask_atomic);
            BetaBadge.Text = strings.Get(S.ai_beta_badge);
            EmptyState.Text = strings.Get(S.ai_greeting);
            Input.PlaceholderText = strings.Get(S.ai_hint);

            // Tooltips and automation names rather than visible labels: the three header buttons and
            // Send are icon-only, matching Android, so these are what a screen reader reads.
            Set(SendButton, strings.Get(S.chat_send));
            Set(CloseButton, strings.Get(S.chat_close));
            Set(NewChatButton, strings.Get(S.chat_new));

            ApplyLimitBadge();

            static void Set(DependencyObject control, string label)
            {
                AutomationProperties.SetName(control, label);
                ToolTipService.SetToolTip(control, label);
            }
        }

        /// <summary>
        /// The day's remaining messages, and when they come back.
        ///
        /// <para>Re-read rather than cached: the count is stored per calendar day and shared by
        /// every window, and the tier behind it can change while the panel is open - buying PRO
        /// on the store page must widen the allowance without a restart.</para>
        /// </summary>
        private void ApplyLimitBadge()
        {
            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

            LimitBadge.Text = AiChatLimiter.Badge(strings);

            var reset = AiChatLimiter.ResetDisplay(strings);
            ToolTipService.SetToolTip(
                LimitBadge,
                reset is null ? null : strings.Get(S.ai_limit_reset_badge, reset));
        }

        // ---- Open and close ---------------------------------------------------------------------

        public void Open()
        {
            if (_isOpen) return;
            _isOpen = true;

            Visibility = Visibility.Visible;
            SizeSheet();
            Animate(open: true);
            UpdateEmptyState();
            OpenChanged?.Invoke(this, true);

            // Focus after the slide starts, not before: moving focus into a collapsed element is
            // what makes the panel appear already-scrolled on some machines.
            Input.Focus(FocusState.Programmatic);

            _ = RestoreAsync();
        }

        /// <summary>
        /// Load the saved thread, and never take the window down doing it.
        /// </summary>
        /// <remarks>
        /// Kept out of <see cref="Open"/> so that method is not <c>async void</c>. It was, and a
        /// deserialization fault in the saved history propagated out of it as a stowed WinRT
        /// exception that killed the process on open - with the panel unreachable from then on,
        /// because the same file was reloaded on every attempt. Opening the assistant must survive
        /// anything the history file contains.
        /// </remarks>
        private async System.Threading.Tasks.Task RestoreAsync()
        {
            try
            {
                await Session.RestoreAsync();
            }
            catch (Exception)
            {
                // A thread that cannot be read is not worth a crash; the user gets an empty panel
                // and can carry on. ChatHistoryStore already handles the expected IO and JSON
                // faults - this is the backstop for the ones it cannot name.
            }

            UpdateEmptyState();
            ScrollToEnd();
        }

        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;

            Animate(open: false);
            OpenChanged?.Invoke(this, false);
        }

        public void Toggle()
        {
            if (_isOpen) Close(); else Open();
        }

        /// <summary>
        /// Slide the sheet and fade the scrim.
        ///
        /// <para>Composition rather than a <c>Storyboard</c>: translating a <c>Border</c> through
        /// <c>RenderTransform</c> animates on the UI thread, and this sheet is animating at exactly
        /// the moment the first question is building the BM25 index off it. A composition animation
        /// runs on the compositor and does not stutter when the UI thread is busy.</para>
        /// </summary>
        private void Animate(bool open)
        {
            var visual = ElementCompositionPreview.GetElementVisual(Sheet);
            var compositor = visual.Compositor;

            ElementCompositionPreview.SetIsTranslationEnabled(Sheet, true);

            var slide = compositor.CreateScalarKeyFrameAnimation();
            slide.InsertKeyFrame(1f, open ? 0f : (float)Math.Max(_sheetHeight, MinSheetHeight));
            slide.Duration = SlideDuration;
            visual.StartAnimation("Translation.Y", slide);

            var scrim = ElementCompositionPreview.GetElementVisual(Scrim);
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, open ? 1f : 0f);
            fade.Duration = SlideDuration;

            if (!open)
            {
                // Collapse only once the slide has finished, or the sheet vanishes instead of
                // leaving. A batch is the only way to learn that from a composition animation.
                var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                batch.Completed += (_, _) =>
                {
                    if (!_isOpen) Visibility = Visibility.Collapsed;
                };
                scrim.StartAnimation("Opacity", fade);
                batch.End();
            }
            else
            {
                scrim.StartAnimation("Opacity", fade);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Start off-screen so the first Open() slides in rather than appearing.
            ElementCompositionPreview.SetIsTranslationEnabled(Sheet, true);
            var visual = ElementCompositionPreview.GetElementVisual(Sheet);
            if (!_isOpen) visual.Properties.InsertVector3("Translation", new Vector3(0, 2000, 0));
        }

        // ---- Sizing -----------------------------------------------------------------------------

        private void SizeSheet()
        {
            var available = ActualHeight > 0 ? ActualHeight : 800;
            if (_sheetHeight <= 0) _sheetHeight = available * DefaultHeightFraction;

            Sheet.Height = Clamp(_sheetHeight, available);
        }

        private double Clamp(double height, double available) =>
            Math.Clamp(height, MinSheetHeight, Math.Max(MinSheetHeight, available * MaxHeightFraction));

        private void Sheet_SizeChanged(object sender, SizeChangedEventArgs e) =>
            _sheetHeight = e.NewSize.Height;

        private void Grip_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            var available = ActualHeight > 0 ? ActualHeight : 800;
            // Dragging up (negative delta) makes the sheet taller, which is why this subtracts.
            Sheet.Height = Clamp(Sheet.ActualHeight - e.Delta.Translation.Y, available);
        }

        private void Grip_PointerEntered(object sender, PointerRoutedEventArgs e) =>
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
                Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth);

        private void Grip_PointerExited(object sender, PointerRoutedEventArgs e) =>
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
                Microsoft.UI.Input.InputSystemCursorShape.Arrow);

        // ---- Input ------------------------------------------------------------------------------

        private void Input_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                _ = SendAsync();
                return;
            }

            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e) => UpdateSendEnabled();

        private void Send_Click(object sender, RoutedEventArgs e) => _ = SendAsync();

        private void Suggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string suggestion })
                _ = SendAsync(suggestion);
        }

        private async System.Threading.Tasks.Task SendAsync(string? text = null)
        {
            var question = text ?? Input.Text;
            if (string.IsNullOrWhiteSpace(question) || Session.IsBusy) return;

            Input.Text = string.Empty;
            UpdateEmptyState();

            await Session.AskAsync(question);

            UpdateEmptyState();
            ScrollToEnd();
            Input.Focus(FocusState.Programmatic);
        }

        private void Action_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ChatAction action })
                Navigation?.Follow(action);
        }

        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            Session.Reset();
            UpdateEmptyState();
            Input.Focus(FocusState.Programmatic);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Scrim_Tapped(object sender, TappedRoutedEventArgs e) => Close();

        // ---- Binding glue -----------------------------------------------------------------------

        private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatSession.Suggestions))
            {
                SuggestionList.ItemsSource = Session.Suggestions;
                SuggestionList.Visibility = Session.Suggestions.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (e.PropertyName == nameof(ChatSession.IsBusy)) UpdateSendEnabled();
        }

        private void Messages_Changed(object? sender, object e)
        {
            UpdateEmptyState();
            ScrollToEnd();
        }

        private void UpdateSendEnabled() =>
            SendButton.IsEnabled = !Session.IsBusy && !string.IsNullOrWhiteSpace(Input.Text);

        private void UpdateEmptyState()
        {
            var empty = Session.Messages.Count == 0;
            EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            MessageList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Bring the newest turn into view.
        /// </summary>
        /// <remarks>
        /// Enqueued rather than called directly. <c>ScrollIntoView</c> needs the item to have been
        /// realized, and on a restore the whole thread is added in one go - so calling it inline
        /// scrolls against a list that has not laid out yet and leaves the user looking at the
        /// oldest message. Running after the current layout pass is what makes reopening the panel
        /// land on the latest answer.
        /// </remarks>
        private void ScrollToEnd()
        {
            if (Session.Messages.Count == 0) return;

            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    if (Session.Messages.Count > 0)
                        MessageList.ScrollIntoView(Session.Messages[^1]);
                });
        }
    }
}
