using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Documents;
using Atomic.App.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Atomic.App.Tools
{
    public sealed class DeveloperPage : Page
    {
        private ToggleSwitch _mockToggle;

        public DeveloperPage()
        {
            var scroll = new ScrollViewer { Padding = new Thickness(12) };
            var stack = new StackPanel { Spacing = 12 };

            var title = new TextBlock { Text = "Developer", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
            stack.Children.Add(title);

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 12 };
            var label = new TextBlock { Text = "Use Mock Store (development only)", VerticalAlignment = VerticalAlignment.Center };
            _mockToggle = new ToggleSwitch();
            _mockToggle.Toggled += MockStoreToggle_Toggled;

            row.Children.Add(label);
            row.Children.Add(_mockToggle);

            stack.Children.Add(row);

            var desc = new TextBlock { Text = "Toggle this to simulate Store behaviour when running the app unpackaged. Changes persist in local settings.", TextWrapping = TextWrapping.Wrap };
            stack.Children.Add(desc);

            scroll.Content = stack;
            this.Content = scroll;

            // Initialize state
            _mockToggle.IsOn = SettingsStore.UseMockStore;
        }

        private void MockStoreToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var toggle = (ToggleSwitch)sender;
            SettingsStore.UseMockStore = toggle.IsOn;

            AppNotificationManager.Default.Show(
                new AppNotificationBuilder()
                    .AddText("Developer")
                    .AddText(toggle.IsOn ? "Mock store enabled" : "Mock store disabled")
                    .BuildNotification());
        }
    }
}
