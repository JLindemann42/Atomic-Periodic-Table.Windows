using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Atomic.App.Controls;
using Atomic.Core.Localization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Atomic.App.Services
{

    internal static class AnswerShareImage
    {
        /// <summary>The width the image is laid out at, in device-independent pixels.</summary>
        private const double Width = 540;

        /// <param name="card">The answer's card on screen, or null for none. The caller passes null for a locked card.</param>
        /// <returns>The PNG, in the temporary folder.</returns>
        public static async Task<StorageFile> RenderAsync(
            Panel host, string? question, string answer, FrameworkElement? card, IStringProvider strings)
        {
            var dark = host.ActualTheme == ElementTheme.Dark;
            var text = new SolidColorBrush(dark ? Colors.White : Color(0x1B, 0x1B, 0x1F));
            var accent = Application.Current.Resources.TryGetValue("SystemAccentColor", out var a) && a is Windows.UI.Color color
                ? new SolidColorBrush(color)
                : text;

            var stack = new StackPanel { Spacing = 12 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var logo = new BitmapImage(new Uri("ms-appx:///Assets/128x.png"));
            var logoLoaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            logo.ImageOpened += (_, _) => logoLoaded.TrySetResult(true);
            logo.ImageFailed += (_, args) => logoLoaded.TrySetException(
                new InvalidOperationException("The share image logo could not be loaded.", args.ErrorMessage is null ? null : new Exception(args.ErrorMessage)));

            header.Children.Add(new Image
            {
                Source = logo,
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
            });
            header.Children.Add(new TextBlock
            {
                Text = strings.Get(S.ask_atomic), FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = text,
                VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(header);

            if (!string.IsNullOrWhiteSpace(question))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = question, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = accent,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
                });
            }

            var body = new TextBlock { FontSize = 15, Foreground = text, TextWrapping = TextWrapping.Wrap };
            MarkdownText.SetSource(body, answer);
            stack.Children.Add(body);

            if (card is { ActualWidth: > 0, ActualHeight: > 0 })
            {
                // The card exactly as the user saw it.
                var snapshot = new RenderTargetBitmap();
                await snapshot.RenderAsync(card);
                stack.Children.Add(new Image { Source = snapshot, Stretch = Stretch.Uniform, Width = Math.Min(card.ActualWidth, Width - 48) });
            }

            stack.Children.Add(new TextBlock
            {
                Text = strings.Get(S.ai_share_footer), FontSize = 12, Foreground = text, Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
            });

            var root = new Border
            {
                Width = Width,
                Padding = new Thickness(24),
                Background = new SolidColorBrush(dark ? Color(0x20, 0x20, 0x20) : Colors.White),
                Child = stack,
            };
            Canvas.SetLeft(root, -20000);
            host.Children.Add(root);
            try
            {
                // BitmapImage loading is asynchronous. It starts once the Image is connected to
                // the visual tree; do not capture the host until it has opened or failed.
                await logoLoaded.Task;
                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(root);
                var pixels = await bitmap.GetPixelsAsync();

                var path = Path.Combine(Path.GetTempPath(), "atomic_answer.png");
                using (var file = File.Create(path))
                using (var stream = file.AsRandomAccessStream())
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                    var dpi = 96 * host.XamlRoot.RasterizationScale;
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, dpi, dpi, pixels.ToArray());
                    await encoder.FlushAsync();
                }
                return await StorageFile.GetFileFromPathAsync(path);
            }
            finally
            {
                host.Children.Remove(root);
            }
        }

        /// <summary>Open the Windows share UI with <paramref name="file"/>.</summary>
        public static void Share(StorageFile file, string title)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            var manager = DataTransferManagerInterop.GetForWindow(hwnd);
            void OnRequested(DataTransferManager sender, DataRequestedEventArgs args)
            {
                sender.DataRequested -= OnRequested;
                args.Request.Data.Properties.Title = title;
                Fill(args.Request.Data, file);
            }
            manager.DataRequested += OnRequested;
            DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
        }

        /// <summary>Put <paramref name="file"/> on the clipboard, as an image and as a file.</summary>
        public static void Copy(StorageFile file)
        {
            var package = new DataPackage();
            Fill(package, file);
            Clipboard.SetContent(package);
            // Keep it there after the app closes.
            Clipboard.Flush();
        }

        private static void Fill(DataPackage package, StorageFile file)
        {
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            package.SetStorageItems([file]);
        }

        private static Windows.UI.Color Color(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(0xFF, r, g, b);
    }
}
