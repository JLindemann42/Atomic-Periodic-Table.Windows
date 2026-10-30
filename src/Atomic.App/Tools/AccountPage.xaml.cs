using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using Atomic.App.Services;
using Atomic.Core.Licensing;
using Atomic.Core.Localization;
using Atomic.Core.Progression;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Atomic.App.Tools
{
    /// <summary>
    /// The account screen: identity, sync, usage statistics and achievements.
    ///
    /// <para>Laid out after the Android user activity - avatar and title, PRO badge, sign-in,
    /// a statistics box and the achievements list.</para>
    ///
    /// <para>The statistics box now matches Android - elements opened, calculations, searches -
    /// because the achievements needed those counters anyway, and it adds level, XP, games and
    /// streak, which Windows keeps and Android does not show. All of it syncs.</para>
    ///
    /// <para>Shows the Firebase uid on purpose. It is the only value that proves a Windows sign-in
    /// landed in the same project as the phone - the precondition the whole feature rests on, and
    /// the one that fails silently rather than loudly - and nothing else in the app can reveal
    /// it.</para>
    /// </summary>
    public sealed partial class AccountPage : Page
    {
        public ObservableCollection<Achievement> Achievements { get; } = new();

        public AccountPage()
        {
            InitializeComponent();
            ApplyStrings();
        }

        /// <remarks>
        /// <para><c>async void</c> because it overrides a framework handler: an exception from any
        /// await below has nowhere to go and takes the process with it. This signs in, reads the
        /// credential vault and talks to Firestore, so failure is ordinary - a laptop in flight
        /// mode is enough - and the page must still open.</para>
        /// </remarks>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Subscribed only while shown, so the static event does not keep a page alive after it
            // has been navigated away from. Removed first in case the page is shown twice.
            SettingsStore.LicenceChanged -= OnLicenceChanged;
            SettingsStore.LicenceChanged += OnLicenceChanged;

            try
            {
                LoadAchievements();
                LoadStatistics();
                await RefreshAsync();
                await LoadAvatarAsync();

            // Opening this page is the one moment the user is definitely looking at their sync
            // state, so it is worth being current rather than up to five minutes old.
            if (await CloudSync.AutoSyncAsync(force: true) is not null)
            {
                LoadAchievements();
                LoadStatistics();
                await RefreshAsync();
            }
            }
            catch (Exception ex)
            {
                // The page is already rendered; what failed is the refresh behind it.
                System.Diagnostics.Debug.WriteLine($"AccountPage.OnNavigatedTo: {ex}");
            }
        }

        private async Task LoadAvatarAsync()
        {
            var avatar = await UserProfile.AvatarAsync(88);

            AvatarImage.Source = avatar;
            AvatarImage.Visibility = avatar is null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ApplyStrings()
        {
            // Reuses existing translated keys; the account screen adds no new vocabulary beyond
            // what the PRO, settings and flashcard surfaces already carry.
            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

            // Both platforms, when both are known: the point of naming them is being able to see
            // "Windows PRO+" and "Android PRO" at once, which is the question a shared account
            // makes it possible to ask and impossible to answer from a bare tier.
            var windows = EntitlementRules.Summary(SettingsStore.HasUltimate, SettingsStore.Entitlement.Tier);

            // Ultimate already says "both platforms"; a second badge for the phone would repeat it.
            var android = SettingsStore.HasUltimate
                ? null
                : EntitlementRules.BadgeFor(
                    EntitlementRules.AndroidPlatform,
                    EntitlementRules.FromWire(SettingsStore.AndroidTier));

            ProBadgeText.Text = windows ?? string.Empty;
            AndroidBadgeText.Text = android ?? string.Empty;
            AndroidBadge.Visibility = android is null ? Visibility.Collapsed : Visibility.Visible;
            SignInButton.Content = strings.Get(S.login_for_sync_and_more);
            SignOutButton.Content = strings.Get(S.sign_out);
            UidLabel.Text = "Firebase user id";

            ProBadge.Visibility = windows is null ? Visibility.Collapsed : Visibility.Visible;

            ApplyLicences(strings);
        }

        /// <summary>
        /// The licences card: each platform's tier, Ultimate, and the offer when it is not owned.
        /// </summary>
        /// <remarks>
        /// <para>An Ultimate owner sees "PRO+ Ultimate" on both platforms - that is what they bought,
        /// and the PRO+ it grants on each is a consequence, not a second purchase.</para>
        ///
        /// <para>The phone's tier is what it last wrote to the account document. Until a sync has
        /// read one, it is shown as not known rather than as free - a phone that has not synced yet
        /// is not a phone that owns nothing.</para>
        ///
        /// <para>The offer is the upgrade when PRO+ is already owned on either platform, and the full
        /// product otherwise, matching the two products the PRO page sells.</para>
        /// </remarks>
        private void ApplyLicences(IStringProvider strings)
        {
            var ultimate = SettingsStore.HasUltimate;
            var windows = SettingsStore.Entitlement.Tier;
            var androidWire = SettingsStore.AndroidTier;
            var android = EntitlementRules.FromWire(androidWire);

            WindowsTierText.Text = ultimate
                ? EntitlementRules.UltimateProductName
                : EntitlementRules.TierName(windows) ?? strings.Get(S.free);

            AndroidTierText.Text = ultimate
                ? EntitlementRules.UltimateProductName
                : string.IsNullOrEmpty(androidWire)
                    ? strings.Get(S.account_tier_unknown)
                    : EntitlementRules.TierName(android) ?? strings.Get(S.free);

            UltimateStatusText.Text = ultimate ? strings.Get(S.ultimate_owned) : string.Empty;
            UltimateLabel.Visibility = ultimate ? Visibility.Visible : Visibility.Collapsed;
            UltimateStatusText.Visibility = UltimateLabel.Visibility;

            UltimateOffer.Visibility = ultimate ? Visibility.Collapsed : Visibility.Visible;
            UltimateOfferText.Text = strings.Get(S.ultimate_description);
            UltimateOfferButton.Content = strings.Get(
                windows == EntitlementTier.ProPlus || android == EntitlementTier.ProPlus
                    ? S.ultimate_upgrade_title
                    : S.ultimate_buy);
        }

        private void UltimateOffer_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(Atomic.App.ProPage));

        /// <summary>
        /// A licence changed while this page was open - typically Ultimate or the phone's tier
        /// arriving with a sync. Raised from the sync's thread, hence the dispatch.
        /// </summary>
        private void OnLicenceChanged(object? sender, EventArgs e) =>
            DispatcherQueue.TryEnqueue(ApplyStrings);

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            SettingsStore.LicenceChanged -= OnLicenceChanged;
        }

        private void LoadAchievements()
        {
            Achievements.Clear();
            foreach (var a in AchievementCatalog.Build())
                Achievements.Add(a);
        }

        /// <summary>
        /// The usage counters, as tiles.
        /// </summary>
        /// <remarks>
        /// <para>The first three are Android's <c>stats_Box</c> exactly - elements opened,
        /// calculations, searches - which this app could not show until it started counting them
        /// for the achievements. They sync, so the numbers match the phone.</para>
        ///
        /// <para>Level, XP, games and streak follow because Windows has them and the box would
        /// otherwise be thinner than the progression the user can see elsewhere in the app. Every
        /// label is an existing Android key, so the whole box is translated in all twelve.</para>
        /// </remarks>
        private void LoadStatistics()
        {
            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

            StatsPanel.Children.Clear();
            AddStat(strings.Get(S.elements_opened), AchievementStore.Statistic(UsageStatistics.ElementsOpened));
            AddStat(strings.Get(S.calculations_stat), AchievementStore.Statistic(UsageStatistics.Calculations));
            AddStat(strings.Get(S.searches), AchievementStore.Statistic(UsageStatistics.Searches));
            AddStat(strings.Get(S.flashcards_level), ProgressStore.Level);
            AddStat(strings.Get(S.flashcards_total_xp), ProgressStore.Xp);
            AddStat(strings.Get(S.flashcards_total_games), SettingsStore.TotalGamesPlayed);
            // streak_label is authored with its separator attached - "Streak:", and a full-width
            // colon in Chinese - because Android puts it in front of a value on one line.
            AddStat(strings.Get(S.streak_label).TrimEnd(':', '：', ' '), ProgressStore.Streak);
        }

        private void AddStat(string label, int value)
        {
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = value.ToString(CultureInfo.CurrentCulture),
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current
                    .Resources["TextFillColorSecondaryBrush"],
            });

            StatsPanel.Children.Add(new Border
            {
                MinWidth = 140,
                Padding = new Thickness(16, 12, 16, 12),
                CornerRadius = new CornerRadius(12),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current
                    .Resources["CardBackgroundFillColorSecondaryBrush"],
                BorderThickness = new Thickness(1),
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current
                    .Resources["CardStrokeColorDefaultBrush"],
                Child = stack,
            });
        }

        private async Task RefreshAsync()
        {
            var configured = FirebaseSecrets.IsConfigured;
            var signedIn = CloudSync.IsSignedIn;

            SignInButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
            SignOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
            SignInButton.IsEnabled = configured;

            UidPanel.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
            UidText.Text = CloudSync.SignedInUid ?? string.Empty;

            LastSyncText.Text = CloudSync.LastSyncedAt is { } at
                ? "Last synced " + at.ToString("g", CultureInfo.CurrentCulture)
                : string.Empty;

            if (!configured)
            {
                StatusText.Text = "Cloud sync is not configured on this build.";
                Explanation.Text = FirebaseSecrets.Unavailable ?? string.Empty;
                return;
            }

            var name = UserProfile.DisplayName;

            StatusText.Text = signedIn
                ? (name.Length > 0 ? "Signed in as " + name + ". " : "Signed in. ")
                  + "Progress, achievements, streak, element notes, chat history and licences sync "
                  + "with your phone automatically - there is nothing to switch on."
                : "Sign in with Google to sync progress, achievements, streak and element notes "
                  + "with the Android app.";

            Explanation.Text =
                "Sign-in opens your browser. The user id shown above must match the one on your "
                + "phone - if it differs, the two devices are syncing to separate documents.";

            await Task.CompletedTask;
        }

        private async void SignIn_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(CloudSync.SignInAndSyncAsync);
            await LoadAvatarAsync();
        }

        private async void SignOut_Click(object sender, RoutedEventArgs e)
        {
            CloudSync.SignOut();
            await RefreshAsync();
            await LoadAvatarAsync();
        }

        /// <summary>
        /// Run an operation with the buttons disabled and any failure surfaced.
        /// </summary>
        /// <remarks>
        /// A sync that fails silently is worse than one that fails loudly - the user would believe
        /// their progress is safe on another device. The error text is Google's own, which names
        /// the real cause.
        /// </remarks>
        private async Task RunAsync(Func<Task<SyncOutcome>> operation)
        {
            Busy.IsActive = true;
            SignInButton.IsEnabled = false;
            Problem.IsOpen = false;

            try
            {
                var outcome = await operation();

                if (!outcome.Success)
                {
                    Problem.Message = outcome.Error ?? "Sync failed.";
                    Problem.IsOpen = true;
                }
            }
            finally
            {
                Busy.IsActive = false;
                // A sync can move XP, streak and achievements, so the boxes above are stale now.
                LoadAchievements();
                LoadStatistics();
                await RefreshAsync();
            }
        }
    }
}
