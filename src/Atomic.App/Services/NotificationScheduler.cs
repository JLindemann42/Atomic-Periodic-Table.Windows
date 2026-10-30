using System;
using System.Globalization;
using System.Linq;
using Atomic.Core.Localization;
using Atomic.Core.Progression;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Atomic.App.Services
{
    /// <summary>
    /// The two reminders the progression system owes the user, as scheduled toasts.
    ///
    /// <para><b>No background task.</b> Android runs a <c>LivesRefillWorker</c> and an exact alarm
    /// for these; on Windows the same behaviour is a notification queued with the OS, which then
    /// fires whether or not the app is running. That skips the whole
    /// <c>BackgroundTaskBuilder</c>/COM-server apparatus for identical user-visible
    /// behaviour.</para>
    ///
    /// <para>Both are re-scheduled rather than accumulated: every call clears the app's pending
    /// notifications of that kind first, so spending a life twice in a minute leaves one queued
    /// toast, not two.</para>
    /// </summary>
    public static class NotificationScheduler
    {
        /// <summary>Tags, so each kind can be replaced without disturbing the other.</summary>
        private const string LivesTag = "lives-refilled";

        private const string StreakTag = "streak-reminder";

        /// <summary>The hour the streak nudge fires, matching Android's alarm.</summary>
        private const int StreakReminderHour = 20;

        private static bool _registered;

        /// <summary>
        /// Start listening for activations. Safe to call more than once.
        /// </summary>
        /// <remarks>
        /// Registration can fail on an unpackaged run, which is a normal developer configuration
        /// rather than an error - the app simply gets no toasts. It must not be fatal.
        /// </remarks>
        public static void Register()
        {
            if (_registered) return;

            try
            {
                AppNotificationManager.Default.Register();
                _registered = true;
            }
            catch (Exception)
            {
                // Unpackaged, or notifications are disabled by policy. Nothing to do.
            }
        }

        /// <summary>Re-queue the "your lives are back" toast for whenever the next one arrives.</summary>
        public static void ScheduleLivesRefill()
        {
            if (!_registered) return;

            Remove(LivesTag);

            // Null when lives are full or unlimited - there is nothing to promise.
            if (ProgressStore.NextRefillAt() is not { } at) return;
            if (at <= DateTimeOffset.Now) return;

            var strings = Strings();
            Schedule(
                LivesTag,
                strings.Get(S.notif_lives_title),
                strings.Get(S.notif_lives_body),
                at);
        }

        /// <summary>
        /// Queue this evening's streak nudge, or clear it when the streak is too short to protect.
        /// </summary>
        public static void ScheduleStreakReminder()
        {
            if (!_registered) return;

            Remove(StreakTag);
            if (!ProgressStore.WantsStreakReminder) return;

            var now = DateTimeOffset.Now;
            var at = new DateTimeOffset(now.Year, now.Month, now.Day, StreakReminderHour, 0, 0, now.Offset);

            // Past the hour already, so the reminder belongs to tomorrow - today's play is what
            // scheduled it, and a toast in the past never fires.
            if (at <= now) at = at.AddDays(1);

            var strings = Strings();
            Schedule(
                StreakTag,
                strings.Get(S.notif_streak_title),
                strings.Get(
                    S.notif_streak_body,
                    ProgressStore.Streak.ToString(CultureInfo.InvariantCulture)),
                at);
        }

        private static void Schedule(string tag, string title, string body, DateTimeOffset at)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(body)
                    .BuildNotification();

                notification.Tag = tag;
                notification.Expiration = at.AddDays(1);

                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception)
            {
                // A toast that cannot be shown is not worth a crash on a background path.
            }
        }

        private static void Remove(string tag)
        {
            try
            {
                _ = AppNotificationManager.Default.RemoveByTagAsync(tag);
            }
            catch (Exception)
            {
            }
        }

        private static IStringProvider Strings() =>
            ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));
    }
}
