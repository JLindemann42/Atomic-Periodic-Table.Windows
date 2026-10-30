using System;
using Atomic.App.Services;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// The emission spectrum image.
    ///
    /// <para>The one card not drawn from the app's own numbers - it is a picture on the author's
    /// server. Not a <see cref="ChartBase"/> for that reason: there is nothing to lay out, and the
    /// failure mode is a network one rather than a geometric one.</para>
    ///
    /// <para><c>ChatCardPolicy.AllowNetworkCards</c> withholds it upstream when offline, so this
    /// control is only ever created when a fetch was intended. It still has to handle the fetch
    /// failing: an image that silently does not arrive leaves an empty box with no explanation,
    /// which is precisely what the policy comment warns about.</para>
    /// </summary>
    public sealed partial class EmissionSpectrumCard : UserControl
    {
        private readonly Image _image;
        private readonly TextBlock _failure;

        public EmissionSpectrumCard()
        {
            IsTabStop = false;

            _image = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            _failure = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };

            Content = new StackPanel
            {
                Spacing = 4,
                Children = { _image, _failure },
            };
        }

        /// <param name="symbol">The element symbol as authored, e.g. <c>Fe</c>.</param>
        public void SetSymbol(string symbol)
        {
            var source = new BitmapImage(new Uri(EmissionSpectrumUrl.Of(symbol)));

            source.ImageFailed += (_, _) =>
            {
                _image.Visibility = Visibility.Collapsed;
                _failure.Visibility = Visibility.Visible;
                // Reuses the engine's own "no data right now" line rather than inventing a network
                // error string - it is translated in all twelve languages and says the true thing.
                _failure.Text = ResourceStringProvider
                    .For(AppLanguage.BaseCode(AppLanguage.Current))
                    .Get(S.ai_no_data_right_now);
            };

            _image.Visibility = Visibility.Visible;
            _failure.Visibility = Visibility.Collapsed;
            _image.Source = source;
        }
    }
}
