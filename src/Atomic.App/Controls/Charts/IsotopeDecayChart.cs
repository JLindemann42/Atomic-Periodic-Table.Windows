using System;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Cards;
using Microsoft.UI.Xaml.Shapes;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// An element's isotopes by mass number against half-life, coloured by decay mode.
    ///
    /// <para>Ports <c>IsotopeDecayView.kt</c>, with one deliberate change.</para>
    ///
    /// <para><b>Stable isotopes are drawn in a pinned band above the plot, not on the axis.</b> A
    /// stable isotope has no finite half-life at all - <c>LogSeconds</c> is null and the reducer
    /// documents that the null must survive to the view. Plotting it at the top of the axis would
    /// assert a number the data does not contain, and would put "stable" and "the longest-lived
    /// unstable isotope" at the same height. The band says the right thing: these are off the
    /// scale, not at the end of it.</para>
    /// </summary>
    public sealed partial class IsotopeDecayChart : ChartBase
    {
        private const double MarginLeft = 44;
        private const double MarginRight = 8;
        private const double MarginTop = 6;
        private const double MarginBottom = 20;

        /// <summary>Height of the pinned band that holds the stable isotopes.</summary>
        private const double StableBandHeight = 16;

        /// <summary>Gap between the band and the plot, so the two never read as one axis.</summary>
        private const double BandGap = 6;

        private IsotopeSeries? _series;

        public void SetSeries(IsotopeSeries series)
        {
            _series = series;
            Redraw();
        }

        protected override void Draw()
        {
            if (_series is not { } series || series.Points.Count == 0) return;

            var plotWidth = PlotWidth - MarginLeft - MarginRight;
            if (plotWidth <= 0) return;

            var hasStable = series.Points.Any(p => p.Stable);
            var bandSpace = hasStable ? StableBandHeight + BandGap : 0;
            var plotTop = MarginTop + bandSpace;
            var plotHeight = PlotHeight - plotTop - MarginBottom;
            if (plotHeight <= 0) return;

            var masses = series.Points.Select(p => p.MassNumber).ToList();
            double minMass = masses.Min();
            double maxMass = masses.Max();
            if (maxMass - minMass < 1) maxMass = minMass + 1;

            var minLog = Math.Floor(series.MinLogSeconds);
            var maxLog = Math.Ceiling(series.MaxLogSeconds);
            if (maxLog - minLog < 1) maxLog = minLog + 1;

            double X(double mass) => MarginLeft + (mass - minMass) / (maxMass - minMass) * plotWidth;
            double Y(double log) => plotTop + (1 - (log - minLog) / (maxLog - minLog)) * plotHeight;

            DrawDecades(minLog, maxLog, plotWidth, plotTop, plotHeight);
            DrawMassAxis(minMass, maxMass, X, plotTop + plotHeight);

            if (hasStable) DrawStableBand(series, X, plotWidth);

            foreach (var point in series.Points)
            {
                if (point.Stable) continue;
                if (point.LogSeconds is not { } log) continue;

                var dot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = Palette.Brush(Palette.ForDecay(point.DecayGroup)),
                };
                Place(dot, X(point.MassNumber) - 3.5, Y(log) - 3.5);
                Tip(dot, $"{point.Name} — {point.HalfLifeDisplay}");
            }
        }

        /// <summary>
        /// The stable isotopes, pinned above the plot.
        /// </summary>
        /// <remarks>
        /// Positioned by mass number only. There is no vertical position to give them, which is the
        /// whole reason for the band.
        /// </remarks>
        private void DrawStableBand(IsotopeSeries series, Func<double, double> x, double plotWidth)
        {
            var y = MarginTop;

            var backing = new Rectangle
            {
                Width = plotWidth,
                Height = StableBandHeight,
                RadiusX = 3,
                RadiusY = 3,
                Fill = Palette.Brush(Palette.ForDecay(DecayGroup.Stable)),
                Opacity = 0.12,
            };
            Place(backing, MarginLeft, y);

            var caption = Label("∞", Palette.OnSurface, 10);
            Place(caption, MarginLeft - caption.DesiredSize.Width - 4, y + (StableBandHeight - caption.DesiredSize.Height) / 2);

            foreach (var point in series.Points.Where(p => p.Stable))
            {
                var dot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = Palette.Brush(Palette.ForDecay(DecayGroup.Stable)),
                };
                Place(dot, x(point.MassNumber) - 3.5, y + (StableBandHeight - 7) / 2);
                Tip(dot, $"{point.Name} — {point.HalfLifeDisplay}");
            }
        }

        private void DrawDecades(
            double minLog, double maxLog, double plotWidth, double plotTop, double plotHeight)
        {
            // Half-lives span roughly fifty decades, so every decade would be a solid block of
            // lines. Step so that about five gridlines are drawn whatever the range.
            var decades = Math.Max(1, (int)Math.Round((maxLog - minLog) / 5));

            for (var log = minLog; log <= maxLog + 1e-9; log += decades)
            {
                var y = plotTop + (1 - (log - minLog) / (maxLog - minLog)) * plotHeight;

                var grid = Rule(MarginLeft, y, MarginLeft + plotWidth, y, Palette.Outline, 0.5);
                grid.Opacity = 0.35;

                // Naming the decade is a decision about meaning, so DecadeLabels owns it - and is
                // tested. "1.0E+17 s" is useless on a card.
                var label = Label(DecadeLabels.TimeLabel((int)log), Palette.OnSurface, 9);
                Place(label, MarginLeft - label.DesiredSize.Width - 4, y - label.DesiredSize.Height / 2);
            }
        }

        private void DrawMassAxis(double minMass, double maxMass, Func<double, double> x, double axisY)
        {
            Rule(MarginLeft, axisY, PlotWidth - MarginRight, axisY, Palette.Outline);

            foreach (var mass in new[] { minMass, maxMass })
            {
                var label = Label(mass.ToString("0", CultureInfo.InvariantCulture), Palette.OnSurface, 9);
                Place(label, Math.Min(x(mass) - label.DesiredSize.Width / 2, PlotWidth - label.DesiredSize.Width), axisY + 2);
            }
        }
    }
}
