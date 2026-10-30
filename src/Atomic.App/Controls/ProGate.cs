using System;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Atomic.App.Controls
{
    /// <summary>
    /// Obscures content that is behind the paywall, without unbinding it.
    ///
    /// <para>Ports <c>ProCardGate.kt</c>, including its numbers, so a locked card looks the same on
    /// both platforms. Android's reasoning carries over exactly: the card keeps its space and still
    /// hints at its shape, so the upsell is about something visible rather than an empty box.</para>
    ///
    /// <para><b>The content stays bound.</b> That is the whole design: unlocking is one property
    /// change with no re-bind and no reload, so a purchase completing mid-conversation reveals the
    /// card that is already there.</para>
    ///
    /// <para>Two treatments, and they are alternatives rather than layers - same as Android, where
    /// API 31+ gets the blur alone and older devices get the scrim alone:</para>
    /// <list type="number">
    /// <item>A real Gaussian blur, via a Win2D effect over a composition backdrop brush.</item>
    /// <item>If that fails for any reason, fade the content back and put a scrim over it.</item>
    /// </list>
    /// </summary>
    public sealed partial class ProGate : ContentControl
    {
        /// <summary>Blur radius. From <c>ProCardGate.BLUR_RADIUS</c> - enough that no label survives.</summary>
        private const float BlurRadius = 28f;

        /// <summary>
        /// Scrim opacity for the fallback. From <c>ProCardGate.SCRIM_ALPHA</c> - high enough to hide
        /// values, low enough to keep the shape.
        /// </summary>
        private const double ScrimOpacity = 0.92;

        /// <summary>
        /// How far the content is faded back when there is no blur. From <c>ProCardGate</c>'s
        /// pre-31 branch, where the card reads as a shape rather than as data.
        /// </summary>
        private const double FallbackContentOpacity = 0.35;

        private readonly Border _scrim;
        private SpriteVisual? _blurVisual;
        private bool _blurFailed;

        public ProGate()
        {
            DefaultStyleKey = typeof(ContentControl);
            IsTabStop = false;

            _scrim = new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Opacity = 0,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };

            SizeChanged += (_, e) => Resize(e.NewSize);
            Loaded += (_, _) => Apply();
            Unloaded += (_, _) => Teardown();
        }

        public static readonly DependencyProperty IsLockedProperty =
            DependencyProperty.Register(
                nameof(IsLocked), typeof(bool), typeof(ProGate),
                new PropertyMetadata(false, (d, _) => ((ProGate)d).Apply()));

        /// <summary>Whether the content is currently withheld.</summary>
        public bool IsLocked
        {
            get => (bool)GetValue(IsLockedProperty);
            set => SetValue(IsLockedProperty, value);
        }

        private void Apply()
        {
            if (!IsLoaded) return;

            if (!IsLocked)
            {
                Teardown();
                Opacity = 1;
                return;
            }

            if (!_blurFailed && TryBlur())
            {
                // The blur is the whole treatment, exactly as on API 31+.
                Opacity = 1;
                return;
            }

            // No blur available. Fade the content back so the scrim above it does the obscuring.
            Opacity = FallbackContentOpacity;
            ShowScrim();
        }

        /// <summary>
        /// Blur what is rendered behind an overlay visual - which, sitting directly above the
        /// content, is the content.
        /// </summary>
        /// <returns>False when the effect could not be created, so the caller falls back.</returns>
        private bool TryBlur()
        {
            try
            {
                if (_blurVisual is not null) return true;

                var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;

                var blur = new GaussianBlurEffect
                {
                    Name = "Blur",
                    BlurAmount = BlurRadius,
                    // Clamp, matching Android's Shader.TileMode.CLAMP: without it the edges sample
                    // transparent black and the card gets a dark halo.
                    BorderMode = EffectBorderMode.Hard,
                    Source = new CompositionEffectSourceParameter("Backdrop"),
                };

                using var factory = compositor.CreateEffectFactory(blur);
                var brush = factory.CreateBrush();
                brush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());

                _blurVisual = compositor.CreateSpriteVisual();
                _blurVisual.Brush = brush;
                _blurVisual.Size = new Vector2((float)ActualWidth, (float)ActualHeight);

                ElementCompositionPreview.SetElementChildVisual(this, _blurVisual);
                return true;
            }
            catch (Exception)
            {
                // Win2D effects need a hardware path that is not always there - a remote session, a
                // basic display adapter, or a driver that refuses the effect. Remembered so the
                // failing call is not repeated on every recycle of a virtualized list row.
                _blurFailed = true;
                return false;
            }
        }

        private void ShowScrim()
        {
            if (_scrim.Parent is null && Content is not null)
            {
                // The scrim has to live in the tree above the content. A ContentControl holds one
                // child, so the content is re-parented into a grid with the scrim on top of it.
                var host = new Grid();
                var inner = Content;
                Content = null;
                host.Children.Add((UIElement)inner);
                host.Children.Add(_scrim);
                Content = host;
            }

            _scrim.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
            _scrim.Opacity = ScrimOpacity;
            _scrim.Visibility = Visibility.Visible;
        }

        private void Teardown()
        {
            if (_blurVisual is not null)
            {
                ElementCompositionPreview.SetElementChildVisual(this, null);
                _blurVisual.Dispose();
                _blurVisual = null;
            }

            _scrim.Visibility = Visibility.Collapsed;
            _scrim.Opacity = 0;
        }

        private void Resize(Windows.Foundation.Size size)
        {
            if (_blurVisual is not null)
                _blurVisual.Size = new Vector2((float)size.Width, (float)size.Height);
        }
    }
}
