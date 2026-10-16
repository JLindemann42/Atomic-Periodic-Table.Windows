using System.Collections.Generic;
using Atomic.Core.Ai.Cards;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// The colours a chart needs, resolved once per theme.
    ///
    /// <para>Ports <c>ChartPalette.kt</c>. Resolved from the WinUI theme resources rather than
    /// hardcoded, so a chart follows a runtime theme switch - which is exactly why these charts are
    /// XAML shapes rather than Win2D: a <c>CanvasControl</c> wants raw colours and manual
    /// invalidation, and there are up to twenty of these live in chat scrollback at once.</para>
    ///
    /// <para>Rebuilt on <c>ActualThemeChanged</c> rather than cached statically. A palette captured
    /// at app start is the reason a chart stays dark after the user switches to light.</para>
    /// </summary>
    public sealed class ChartPalette
    {
        private ChartPalette(
            Color primary, Color onSurface, Color outline,
            Color tertiary, Color error, Color secondary)
        {
            Primary = primary;
            OnSurface = onSurface;
            Outline = outline;
            Tertiary = tertiary;
            Error = error;
            Secondary = secondary;
        }

        public Color Primary { get; }

        /// <summary>Text and axis labels.</summary>
        public Color OnSurface { get; }

        /// <summary>Gridlines, axes, and anything structural rather than meaningful.</summary>
        public Color Outline { get; }

        public Color Tertiary { get; }

        public Color Error { get; }

        public Color Secondary { get; }

        /// <summary>Resolve against the theme currently applied to <paramref name="element"/>.</summary>
        public static ChartPalette For(FrameworkElement element)
        {
            var dark = element.ActualTheme == ElementTheme.Dark;

            return new ChartPalette(
                primary: Resolve("AccentFillColorDefaultBrush", dark ? Color.FromArgb(255, 118, 146, 255) : Color.FromArgb(255, 63, 81, 181)),
                onSurface: Resolve("TextFillColorPrimaryBrush", dark ? Colors.White : Color.FromArgb(255, 32, 32, 32)),
                outline: Resolve("ControlStrongStrokeColorDefaultBrush", dark ? Color.FromArgb(255, 118, 118, 118) : Color.FromArgb(255, 138, 138, 138)),
                // The three accents below carry meaning - a decay mode, a hazard - so they are
                // literals rather than theme colours. A theme that repainted "alpha decay" would
                // break the legend's agreement with its markers, which is the one thing these
                // colours exist to keep.
                tertiary: dark ? Color.FromArgb(255, 77, 182, 172) : Color.FromArgb(255, 0, 137, 123),
                error: dark ? Color.FromArgb(255, 242, 139, 130) : Color.FromArgb(255, 179, 38, 30),
                secondary: dark ? Color.FromArgb(255, 179, 157, 219) : Color.FromArgb(255, 126, 87, 194));

            static Color Resolve(string key, Color fallback) =>
                Application.Current.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush
                    ? brush.Color
                    : fallback;
        }

        /// <summary>Fission's orange, from Android. Not a theme colour - see the note in <see cref="For"/>.</summary>
        private static readonly Color Fission = Color.FromArgb(255, 239, 108, 0);

        /// <summary>
        /// The colour for a decay group, so a legend and its markers cannot disagree.
        ///
        /// <para>One function, called by both, for that reason.</para>
        /// </summary>
        public Color ForDecay(DecayGroup group) => group switch
        {
            DecayGroup.Stable => Tertiary,
            DecayGroup.Alpha => Error,
            DecayGroup.BetaMinus => Primary,
            DecayGroup.BetaPlusEc => Secondary,
            DecayGroup.Isomeric => Outline,
            DecayGroup.Fission => Fission,
            _ => Outline,
        };

        private readonly Dictionary<Color, SolidColorBrush> _brushes = [];

        /// <summary>
        /// A brush for a colour, reused.
        /// </summary>
        /// <remarks>
        /// A chart creates one shape per data point and several per axis; allocating a brush for
        /// each means dozens per card and hundreds across a scrollback. They are immutable here, so
        /// sharing is safe.
        /// </remarks>
        public SolidColorBrush Brush(Color color)
        {
            if (_brushes.TryGetValue(color, out var brush)) return brush;
            return _brushes[color] = new SolidColorBrush(color);
        }

        public SolidColorBrush PrimaryBrush => Brush(Primary);

        public SolidColorBrush OnSurfaceBrush => Brush(OnSurface);

        public SolidColorBrush OutlineBrush => Brush(Outline);
    }
}
