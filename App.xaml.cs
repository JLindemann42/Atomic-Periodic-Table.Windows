using Microsoft.UI.Xaml;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Atomic_WinUI
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
            MainWindow = new MainWindow();


            // Apply saved theme
            var savedTheme = ApplicationData.Current.LocalSettings.Values["AppTheme"] as string;
            if (savedTheme is not null)
            {
                var root = MainWindow.Content as FrameworkElement;
                if (root != null)
                {
                    switch (savedTheme)
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

            MainWindow.Activate();
        }

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
