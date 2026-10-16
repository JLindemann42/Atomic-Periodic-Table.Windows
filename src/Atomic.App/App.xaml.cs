using System;
using Microsoft.UI.Xaml;
using Atomic.App.Services;
using Atomic.Core.Links;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Atomic.App
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Before the window, so the first pane build already resolves in the right language.
            AppLanguage.ApplySaved();

            // Before anything can schedule one. Registration is a no-op unpackaged, which is a
            // normal developer configuration rather than an error.
            NotificationScheduler.Register();
            // Lives regenerate while the app is closed, so the pending toast is re-queued against
            // the count as it stands now rather than as it stood when the app was last used.
            NotificationScheduler.ScheduleLivesRefill();

            // Before the first sync: games played and perfect games predate per-achievement
            // storage, so an existing user's flashcard achievements would otherwise read zero and
            // push that zero to a phone that knows better.
            AchievementStore.BackfillFromLegacyCounters();

            // Signing in is the only thing the user does; from then on progress syncs on its own.
            // Safe when signed out - the tick checks, so a sign-in later in the session is picked up.
            CloudSync.StartAutoSync();

            MainWindow = new MainWindow();
            ApplyAppTheme(SettingsStore.AppTheme);
            MainWindow.Activate();

            Follow(LaunchLink());
        }

        /// <summary>
        /// The <c>atomic:</c> link this launch was activated with, if any.
        /// </summary>
        /// <remarks>
        /// Read from the AppInstance activation rather than the <c>LaunchActivatedEventArgs</c>
        /// passed in: for a protocol activation WinUI still reports <c>Launch</c> there, and the
        /// URI only appears on the richer args.
        /// </remarks>
        private static AppLink LaunchLink()
        {
            try
            {
                var activation = Microsoft.Windows.AppLifecycle.AppInstance
                    .GetCurrent().GetActivatedEventArgs();

                if (activation?.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol &&
                    activation.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol)
                {
                    return AppLinks.Parse(protocol.Uri);
                }
            }
            catch (Exception)
            {
                // Unpackaged, or an activation shape this build does not know. Opening normally is
                // always a safe answer.
            }

            return AppLinks.Home;
        }

        /// <summary>
        /// Go where a link points.
        /// </summary>
        /// <remarks>
        /// A link naming no route is an <c>atomic://ask</c> - the assistant, not a page - so the
        /// panel opens instead of a navigation happening.
        /// </remarks>
        private void Follow(AppLink link)
        {
            if (MainWindow is not MainWindow window) return;

            if (link.Query is { Length: > 0 } question)
            {
                window.AskAtomic(question);
                return;
            }

            if (link.RouteTag is { Length: > 0 } tag && tag != AppLinks.HomeRoute)
            {
                window.Navigation.Navigate(tag, ElementParameter(link));
            }
        }

        /// <summary>The element pages take the Element object, so a key has to be resolved first.</summary>
        private static object? ElementParameter(AppLink link) =>
            link.ElementKey is { Length: > 0 } key ? ElementRepository.Get(key) : null;

        public void ApplyAppTheme(string? theme)
        {
            if (MainWindow?.Content is FrameworkElement root)
            {
                switch (theme)
                {
                    case "Light":
                        root.RequestedTheme = ElementTheme.Light;
                        break;
                    case "Dark":
                        root.RequestedTheme = ElementTheme.Dark;
                        break;
                    default:
                        root.RequestedTheme = ElementTheme.Default;
                        break;
                }
            }
        }
    }
}
