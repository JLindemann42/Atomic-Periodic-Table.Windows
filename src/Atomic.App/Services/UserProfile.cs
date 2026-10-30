using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Atomic.App.Services
{
    /// <summary>
    /// The signed-in user's picture and name.
    ///
    /// <para>Google returns both from <c>signInWithIdp</c> because the flow already asks for the
    /// <c>profile</c> scope. They are persisted rather than re-fetched: the pane wants an avatar
    /// the instant the window opens, and a session restore is a network round trip that may not
    /// have finished - or may fail entirely offline - by then.</para>
    ///
    /// <para>The picture is cached as bytes in the local folder. Google's photo URLs are stable per
    /// account but not permanent, and re-downloading on every launch would put a network dependency
    /// in front of the navigation pane rendering.</para>
    /// </summary>
    public static class UserProfile
    {
        private const string CacheFileName = "account_avatar";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>Raised when the picture or name changes, so the pane and page can follow.</summary>
        public static event EventHandler? Changed;

        public static string PhotoUrl
        {
            get => SettingsStore.Get(SettingsStore.Keys.AccountPhotoUrl, string.Empty);
            private set => SettingsStore.Set(SettingsStore.Keys.AccountPhotoUrl, value);
        }

        public static string DisplayName
        {
            get => SettingsStore.Get(SettingsStore.Keys.AccountDisplayName, string.Empty);
            private set => SettingsStore.Set(SettingsStore.Keys.AccountDisplayName, value);
        }

        /// <summary>
        /// Record what Google said about the user.
        /// </summary>
        /// <remarks>
        /// A changed URL drops the cached bytes. Keeping them would pin the avatar to whatever the
        /// account looked like at first sign-in, which is exactly the case a user notices.
        /// </remarks>
        public static void Remember(string? photoUrl, string? displayName)
        {
            var url = photoUrl ?? string.Empty;

            if (!string.Equals(url, PhotoUrl, StringComparison.Ordinal))
            {
                PhotoUrl = url;
                DropCache();
            }

            DisplayName = displayName ?? string.Empty;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Clear()
        {
            PhotoUrl = string.Empty;
            DisplayName = string.Empty;
            DropCache();
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// The picture, scaled to <paramref name="size"/> and masked to a circle, or null.
        /// </summary>
        /// <remarks>
        /// <para>Masked here rather than clipped in XAML because the navigation pane needs an
        /// <c>IconElement</c>, and neither <c>ImageIcon</c> nor <c>BitmapIcon</c> can be given a
        /// shape. Producing a genuinely round bitmap is the only way the pane and the page can show
        /// the same avatar.</para>
        ///
        /// <para>Returns null rather than throwing on any failure. An avatar is decoration; a
        /// missing one falls back to the person glyph, and no part of the app should break because
        /// a photo did not load.</para>
        /// </remarks>
        public static async Task<ImageSource?> AvatarAsync(int size)
        {
            if (string.IsNullOrEmpty(PhotoUrl)) return null;

            try
            {
                var bytes = await BytesAsync().ConfigureAwait(true);
                if (bytes is null) return null;

                return await CircleAsync(bytes, size).ConfigureAwait(true);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException
                                           or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }

        // ---- Bytes -------------------------------------------------------------------------------

        private static async Task<byte[]?> BytesAsync()
        {
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, CacheFileName);

            if (File.Exists(path))
            {
                var cached = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
                if (cached.Length > 0) return cached;
            }

            var downloaded = await Http.GetByteArrayAsync(new Uri(PhotoUrl)).ConfigureAwait(true);
            if (downloaded.Length == 0) return null;

            await File.WriteAllBytesAsync(path, downloaded).ConfigureAwait(true);
            return downloaded;
        }

        private static void DropCache()
        {
            try
            {
                var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, CacheFileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A stale cached avatar is not worth surfacing, and the next Remember tries again.
            }
        }

        // ---- Masking -----------------------------------------------------------------------------

        /// <summary>
        /// Decode to <paramref name="size"/> square and zero every pixel outside the inscribed circle.
        /// </summary>
        /// <remarks>
        /// <para><see cref="WriteableBitmap"/> reads BGRA8 with <b>premultiplied</b> alpha, so a
        /// transparent pixel is four zero bytes - setting only the alpha byte would leave the colour
        /// channels to bleed as a square halo.</para>
        ///
        /// <para>The edge is antialiased over one pixel. Without it a 24-pixel pane avatar reads as a
        /// visibly polygonal blob rather than a circle.</para>
        /// </remarks>
        private static async Task<ImageSource?> CircleAsync(byte[] bytes, int size)
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);

            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)size,
                ScaledHeight = (uint)size,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var data = pixels.DetachPixelData();

            // A transform is a request, not a guarantee - an exotic source can come back a
            // different size, and writing that into a size-by-size buffer would throw or tear.
            if (data.Length != size * size * 4) return null;

            var centre = (size - 1) / 2.0;
            var radius = size / 2.0;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - centre;
                    var dy = y - centre;
                    var distance = Math.Sqrt((dx * dx) + (dy * dy));

                    // Fully inside: leave the pixel alone.
                    if (distance <= radius - 1) continue;

                    var offset = ((y * size) + x) * 4;

                    if (distance >= radius)
                    {
                        data[offset] = 0;
                        data[offset + 1] = 0;
                        data[offset + 2] = 0;
                        data[offset + 3] = 0;
                        continue;
                    }

                    var coverage = radius - distance;
                    for (var channel = 0; channel < 4; channel++)
                        data[offset + channel] = (byte)(data[offset + channel] * coverage);
                }
            }

            var bitmap = new WriteableBitmap(size, size);
            using (var target = bitmap.PixelBuffer.AsStream())
            {
                await target.WriteAsync(data.AsMemory(0, data.Length));
            }

            bitmap.Invalidate();
            return bitmap;
        }
    }
}
