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
    /// <para>WinUI has no side drawer, so this is a <c>Border</c> pinned to the right edge of a grid
    /// that spans the window, in one of two modes. <b>Docked</b>, as Edge's Copilot is: the window
    /// gives it a column of its own and the page narrows to fit, still fully visible. <b>Overlay</b>,
    /// when the window is too small to share: it floats over the page, which the user closes the
    /// panel to see again. See <see cref="IsDocked"/>.</para>
    /// </summary>
    public sealed partial class AiChatPanel : UserControl
    {
        /// <summary>
        /// How wide the drawer opens. Narrow on purpose: it sits beside the page rather than over
        /// it, and a periodic table or a chart should stay readable while the answer is on screen.
        /// </summary>
        private const double DefaultSheetWidth = 380;

        /// <summary>
        /// The narrowest it can be dragged. Below this a bubble is a few words wide and a suggestion
        /// chip wraps to four lines.
        /// </summary>
        private const double MinSheetWidth = 320;

        /// <summary>The widest it can be dragged, as a share of the window, and in absolute terms.</summary>
        private const double MaxWidthFraction = 0.6;

        private const double MaxSheetWidth = 640;

        /// <summary>
        /// The least the page is left with while the panel is docked. Below this a periodic table
        /// stops being one: the navigation pane has already gone compact at 860px, and 680 is what
        /// the table, or a chart page, needs beside a 48px rail to stay legible.
        /// </summary>
        private const double MinDockedContentWidth = 680;

        /// <summary>
        /// Under this the panel takes the whole window. At 560px a 320px drawer leaves a strip of
        /// page too narrow to read or aim at, and an almost-covered page is worse than a covered one.
        /// </summary>
        private const double CompactWindowWidth = 560;

        private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(150);

        private double _sheetWidth;
        private double _windowWidth;
        private bool _isOpen;

        public AiChatPanel()
        {
            InitializeComponent();

            Session = new ChatSession();
            Session.PropertyChanged += Session_PropertyChanged;
            Session.LimitChanged += (_, _) => ApplyLimitBadge();
            Session.Messages.CollectionChanged += Messages_Changed;
            Session.HistoryChanged += (_, _) =>
            {
                if (HistoryPane.Visibility == Visibility.Visible) RefreshHistory();
            };

            ApplyStrings();
            Loaded += OnLoaded;
        }

        /// <summary>The conversation. One per window, created here and never replaced.</summary>
        public ChatSession Session { get; }

        public bool IsOpen => _isOpen;

        /// <summary>
        /// Raised when the room the panel needs beside the page changes: opened, closed, resized,
        /// or the window crossed the line between docked and overlay. The window answers by giving
        /// the panel's column that width, or none.
        /// </summary>
        public event EventHandler? LayoutChanged;

        /// <summary>
        /// Whether the window is wide enough to share with the page.
        ///
        /// <para>Decided by the window alone, not by the panel's current width, so dragging the
        /// panel wider cannot flip it into overlay half way through the drag. Docked, the drag is
        /// held back instead, to leave the page <see cref="MinDockedContentWidth"/>.</para>
        /// </summary>
        public bool IsDocked => WindowWidth() >= MinDockedContentWidth + MinSheetWidth;

        /// <summary>
        /// The column width the window should give the panel: its own width while open and docked,
        /// and nothing otherwise - an overlay takes no room from the page.
        /// </summary>
        public double DockedWidth => _isOpen && IsDocked && !double.IsNaN(Sheet.Width) ? Sheet.Width : 0;

        /// <summary>
        /// The window was resized. Given the width rather than read from <c>XamlRoot</c>, because
        /// the window's own event is the one that is current when it fires.
        /// </summary>
        public void NotifyWindowSize(double width)
        {
            _windowWidth = width;

            if (_isOpen) SizeSheet();
            ApplyMode();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }

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
            Set(HistoryButton, strings.Get(S.chat_history));
            HistoryTitle.Text = strings.Get(S.chat_history);

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
            ApplyMode();
            Animate(open: true);
            UpdateEmptyState();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
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

            // Reopened, the panel shows the conversation, not a history list left open last time.
            HistoryPane.Visibility = Visibility.Collapsed;

            Animate(open: false);
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            OpenChanged?.Invoke(this, false);
        }

        public void Toggle()
        {
            if (_isOpen) Close(); else Open();
        }

        /// <summary>
        /// Slide the drawer in from the right.
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
            slide.InsertKeyFrame(1f, open ? 0f : (float)Math.Max(Sheet.ActualWidth, MinSheetWidth));
            slide.Duration = SlideDuration;

            if (!open)
            {
                // Collapse only once the slide has finished, or the drawer vanishes instead of
                // leaving. A batch is the only way to learn that from a composition animation, and
                // it has to be open before the animation starts to include it.
                var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                visual.StartAnimation("Translation.X", slide);
                batch.Completed += (_, _) =>
                {
                    if (!_isOpen) Visibility = Visibility.Collapsed;
                };
                batch.End();
            }
            else
            {
                visual.StartAnimation("Translation.X", slide);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Start off-screen so the first Open() slides in rather than appearing.
            ElementCompositionPreview.SetIsTranslationEnabled(Sheet, true);
            var visual = ElementCompositionPreview.GetElementVisual(Sheet);
            if (!_isOpen) visual.Properties.InsertVector3("Translation", new Vector3(2000, 0, 0));
        }

        // ---- Sizing -----------------------------------------------------------------------------

        private void SizeSheet()
        {
            var available = WindowWidth();
            if (_sheetWidth <= 0) _sheetWidth = DefaultSheetWidth;

            Sheet.Width = Clamp(_sheetWidth, available);
            PositionGrip();
        }

        /// <summary>
        /// The width the panel may have, for a window of this width.
        ///
        /// <para>Docked, the ceiling is whatever leaves the page its minimum, so the panel and the
        /// page can never be asked for more room than the window has. Overlay, it is a share of the
        /// window, as before. Under <see cref="CompactWindowWidth"/> it is all of it.</para>
        /// </summary>
        private static double Clamp(double width, double available)
        {
            if (available < CompactWindowWidth) return available;

            var docked = available >= MinDockedContentWidth + MinSheetWidth;
            var max = docked
                ? Math.Min(MaxSheetWidth, available - MinDockedContentWidth)
                : Math.Min(MaxSheetWidth, available * MaxWidthFraction);

            return Math.Clamp(width, MinSheetWidth, Math.Max(MinSheetWidth, max));
        }

        /// <summary>
        /// Choose the surface: the page's own when docked, acrylic when floating over it.
        ///
        /// <para>Docked, the panel is part of the window's layout, a second card beside the first,
        /// and should be indistinguishable from it - the same fill, divider and corner. Acrylic
        /// there would blur nothing but the window's Mica and stand out as a flat patch. Floating,
        /// the page is behind the panel, and the blur is what keeps the answer legible over it.</para>
        /// </summary>
        private void ApplyMode()
        {
            var docked = IsDocked;
            DockedLayer.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
            AcrylicLayer.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// The window's width. Not <c>ActualWidth</c>: docked in a column of its own, this control
        /// is exactly as wide as the drawer, so measuring against it would clamp the drawer to
        /// itself.
        /// </summary>
        private double WindowWidth() =>
            _windowWidth > 0 ? _windowWidth
            : XamlRoot is { } root && root.Size.Width > 0 ? root.Size.Width
            : 1200;

        /// <summary>
        /// The page follows the panel's width as it is dragged. Only a change in width: the height
        /// changes with the window and says nothing about how much room the page has.
        /// </summary>
        private void Sheet_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PositionGrip();

            if (_isOpen && IsDocked && e.NewSize.Width != e.PreviousSize.Width)
                LayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The empty strip between the page card and a docked panel, in device-independent pixels.
        /// MainWindow gives the panel's column this much more than the panel needs.
        /// </summary>
        public const double DockedGap = 6;

        /// <summary>The handle is the gap plus a couple of pixels either side, to be easier to hit.</summary>
        private const double GripWidth = 10;

        private bool _dragging;
        private double _dragStartX;
        private double _dragStartWidth;

        /// <summary>
        /// Centre the handle on the gap: its right edge sits just inside the sheet's left edge, by
        /// half of what the handle has over the gap's own width. Margin rather than a layout
        /// column, because the sheet is slid in by a composition translation that layout cannot
        /// see - and the handle is deliberately not part of that slide.
        /// </summary>
        private void PositionGrip()
        {
            var width = double.IsNaN(Sheet.Width) ? Sheet.ActualWidth : Sheet.Width;
            var overhang = (GripWidth - DockedGap) / 2;

            Grip.Margin = new Thickness(0, 0, Math.Max(0, width - overhang), 0);
        }

        // Dragging uses pointer capture and window coordinates, not manipulation events. The
        // handle moves as the panel widens, and manipulation deltas are measured against the
        // element: with the element sliding under the pointer each delta is partly its own
        // movement, which makes the drag lag or jitter. The window's coordinates do not move.

        private void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!Grip.CapturePointer(e.Pointer)) return;

            _dragging = true;
            _dragStartX = e.GetCurrentPoint(null).Position.X;
            _dragStartWidth = Sheet.ActualWidth;
            GripLine.Opacity = 1;
            e.Handled = true;
        }

        private void Grip_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging) return;

            // Dragging left makes the panel wider, which is why the movement is subtracted.
            var x = e.GetCurrentPoint(null).Position.X;
            Sheet.Width = Clamp(_dragStartWidth - (x - _dragStartX), WindowWidth());

            // The drag is the user's preference. Remembering it from the width the panel happens
            // to have would make a full-width compact panel the new default once the window grew.
            _sheetWidth = Sheet.Width;
            PositionGrip();
            e.Handled = true;
        }

        private void Grip_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging) return;

            // Still over the handle after letting go? Keep the line, so it does not vanish under a
            // pointer that has not moved - Entered will not fire again.
            var p = e.GetCurrentPoint(Grip).Position;
            var over = p.X >= 0 && p.X <= Grip.ActualWidth && p.Y >= 0 && p.Y <= Grip.ActualHeight;

            _dragging = false;
            Grip.ReleasePointerCaptures();
            if (!over) EndHover();
        }

        private void Grip_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging) return;

            _dragging = false;
            EndHover();
        }

        private void Grip_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
                Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
            GripLine.Opacity = 1;
        }

        private void Grip_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            // Leaving while dragging is normal - the panel is moving under the pointer. The end of
            // the drag puts things back.
            if (!_dragging) EndHover();
        }

        private void EndHover()
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
                Microsoft.UI.Input.InputSystemCursorShape.Arrow);
            GripLine.Opacity = 0;
        }

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

                // The sidebar first: Escape backs out one layer at a time, as everywhere else.
                if (HistoryPane.Visibility == Visibility.Visible) HideHistory();
                else Close();
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

        // ---- History ----------------------------------------------------------------------------

        private void History_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryPane.Visibility == Visibility.Visible) HideHistory();
            else ShowHistory();
        }

        private void ShowHistory()
        {
            HistoryPane.Visibility = Visibility.Visible;
            RefreshHistory();

            // What is saved here shows at once; the phone's conversations follow when the sync lands.
            _ = RefreshAfterSyncAsync();
        }

        private void HideHistory() => HistoryPane.Visibility = Visibility.Collapsed;

        private async System.Threading.Tasks.Task RefreshAfterSyncAsync()
        {
            try
            {
                await Session.SyncHistoryAsync();
            }
            catch (Exception)
            {
                // A sync that cannot run leaves the list as it is; it must not take the panel down.
            }

            if (HistoryPane.Visibility == Visibility.Visible) RefreshHistory();
        }

        /// <summary>
        /// Fill the sidebar: the conversations, or why there are none.
        /// </summary>
        /// <remarks>
        /// Gated as Android gates it - signed in, then PRO or PRO+ - and with Android's own messages
        /// for each, which are already translated into every language the app ships.
        /// </remarks>
        private void RefreshHistory()
        {
            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

            string? message = null;
            if (!CloudSync.IsSignedIn) message = strings.Get(S.ai_history_sign_in);
            else if (!SettingsStore.HasAnyPro) message = strings.Get(S.ai_history_pro_only);

            var items = message is null ? Session.History.ToList() : [];
            if (message is null && items.Count == 0) message = strings.Get(S.chat_history_empty);

            HistoryList.ItemsSource = items;
            HistoryList.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            HistoryMessage.Text = message ?? string.Empty;
            HistoryMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;

            HistorySyncing.Visibility = Session.IsSyncingHistory ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not StoredChatSession conversation) return;

            Session.OpenConversation(conversation);
            HideHistory();
            UpdateEmptyState();
            ScrollToEnd();
            Input.Focus(FocusState.Programmatic);
        }

        private void HistoryScrim_Tapped(object sender, TappedRoutedEventArgs e) => HideHistory();

        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            HideHistory();
            Session.Reset();
            UpdateEmptyState();
            Input.Focus(FocusState.Programmatic);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ---- Binding glue -----------------------------------------------------------------------

        private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatSession.Suggestions)) UpdateSuggestions();

            if (e.PropertyName == nameof(ChatSession.IsBusy)) UpdateSendEnabled();

            if (e.PropertyName == nameof(ChatSession.IsSyncingHistory))
                HistorySyncing.Visibility = Session.IsSyncingHistory ? Visibility.Visible : Visibility.Collapsed;
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

            UpdateSuggestions();
        }

        /// <summary>
        /// The example questions: the latest answer's follow-ups if it offered any, otherwise the
        /// starters while the thread is empty.
        /// </summary>
        /// <remarks>
        /// Driven from here as well as from the session's property change, because the starters
        /// depend on the thread being empty rather than on anything the session raises: opening
        /// the panel, starting a new chat and a restore all change which set is right without
        /// touching <see cref="ChatSession.Suggestions"/>. While a question is in flight the
        /// thread is no longer empty and the follow-ups are not in yet, so the strip is hidden
        /// rather than offering starters for a question already asked.
        /// </remarks>
        private void UpdateSuggestions()
        {
            var items = Session.Suggestions.Count > 0
                ? Session.Suggestions
                : Session.Messages.Count == 0 ? Session.Starters : Session.Suggestions;

            SuggestionList.ItemsSource = items;
            SuggestionStrip.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // A new set starts from the left; keeping the old offset would open on the middle of it.
            SuggestionScroller.ChangeView(0, null, null, disableAnimation: true);

            // After layout, when the new chips have widths to measure.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateChipFade);
        }

        private void Suggestions_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateChipFade();

        private void Suggestions_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateChipFade();

        /// <summary>How far in from the right edge a chip starts to fade.</summary>
        private const double ChipFadeZone = 56;

        /// <summary>
        /// Fade the chips that reach the strip's right edge, so it reads as continuing rather than
        /// ending.
        ///
        /// <para>Per chip, not per pixel. The drawer's background is acrylic, a blur of the page,
        /// so there is no colour a gradient could fade to; lowering the opacity of the chips
        /// themselves works over anything. The cost is that a chip fades as a unit rather than
        /// smoothly along its length.</para>
        ///
        /// <para>Only while there is more to scroll to. At the end of the strip the last chip is
        /// simply the last chip, and fading it would suggest something after it.</para>
        /// </summary>
        private void UpdateChipFade()
        {
            if (SuggestionList.ItemsPanelRoot is not Panel panel) return;

            var viewport = SuggestionScroller.ViewportWidth;
            var moreToTheRight = SuggestionScroller.ScrollableWidth - SuggestionScroller.HorizontalOffset > 1;
            var fadeStart = viewport - ChipFadeZone;

            foreach (var child in panel.Children)
            {
                if (child is not FrameworkElement chip) continue;

                if (!moreToTheRight || viewport <= 0 || chip.ActualWidth <= 0)
                {
                    chip.Opacity = 1;
                    continue;
                }

                var right = chip.TransformToVisual(SuggestionScroller)
                    .TransformPoint(new Point(chip.ActualWidth, 0)).X;

                chip.Opacity = right <= fadeStart
                    ? 1
                    : Math.Clamp(1 - (right - fadeStart) / (chip.ActualWidth + ChipFadeZone), 0.15, 1);
            }
        }

        /// <summary>
        /// A mouse wheel scrolls the strip sideways. Left alone it does nothing here - the strip
        /// only scrolls horizontally, and a wheel reports vertical movement - so on a desktop with
        /// no touchpad the chips past the edge would be unreachable without dragging the bar.
        /// </summary>
        private void Suggestions_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(SuggestionScroller).Properties.MouseWheelDelta;
            if (delta == 0) return;

            SuggestionScroller.ChangeView(SuggestionScroller.HorizontalOffset - delta, null, null, disableAnimation: false);
            e.Handled = true;
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
