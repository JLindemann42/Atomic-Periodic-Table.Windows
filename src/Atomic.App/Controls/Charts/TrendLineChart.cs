using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// One field across a period, group or series, as a line in atomic-number order (roadmap 2.3).
    ///
    /// <para>Ports <c>TrendLineView.kt</c>. Like the other charts it decides nothing about the data:
    /// the points, their order and whether the axis is logarithmic all arrive from
    /// <see cref="TrendSeriesReducer"/>. It draws the line, a symbol under each point, and the values
    /// at the ends and the extremes.</para>
    /// </summary>
    public sealed partial class TrendLineChart : ChartBase
    {
        private const double MarginSide = 14;
        private const double MarginTop = 20;
        private const double MarginBottom = 20;

        /// <summary>Beyond this many points a symbol under each one runs together.</summary>
        private const int MaxLabelled = 12;

        private TrendSeries? _series;

        public void SetSeries(TrendSeries series)
        {
            _series = series;
            Redraw();
        }

        protected override void Draw()
        {
            if (_series is not { } series || series.Points.Count == 0) return;

            var plotWidth = PlotWidth - 2 * MarginSide;
            var plotHeight = PlotHeight - MarginTop - MarginBottom;
            if (plotWidth <= 0 || plotHeight <= 0) return;

            var log = series.SuggestedScale == ChartScale.Log10;
            var values = series.Points.Select(p => log ? Math.Log10(Math.Max(p.Value, 1e-30)) : p.Value).ToList();
            var min = values.Min();
            var max = values.Max();
            var span = max - min > 0 ? max - min : 1.0;

            double X(int index) => series.Points.Count == 1
                ? MarginSide + plotWidth / 2
                : MarginSide + (double)index / (series.Points.Count - 1) * plotWidth;

            double Y(double value) => MarginTop + (1 - (value - min) / span) * plotHeight;

            Rule(MarginSide, MarginTop + plotHeight, MarginSide + plotWidth, MarginTop + plotHeight, Palette.Outline);

            var line = new Polyline { Stroke = Palette.PrimaryBrush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            for (var index = 0; index < series.Points.Count; index++) line.Points.Add(new Point(X(index), Y(values[index])));
            Surface.Children.Add(line);

            // A symbol under every point while they fit; past that, every other one, so a long series
            // such as the lanthanides stays legible.
            var stride = series.Points.Count > MaxLabelled ? 2 : 1;
            for (var index = 0; index < series.Points.Count; index++)
            {
                var point = series.Points[index];
                var dot = new Ellipse { Width = 6, Height = 6, Fill = Palette.PrimaryBrush };
                Place(dot, X(index) - 3, Y(values[index]) - 3);
                Tip(dot, $"{point.Symbol}: {UnitConverter.FormatValue(point.Value)}");

                if (index % stride != 0 && index != series.Points.Count - 1) continue;
                var symbol = Label(point.Symbol, Palette.OnSurface, 9);
                Place(symbol, X(index) - symbol.DesiredSize.Width / 2, MarginTop + plotHeight + 3);
            }

            // The values worth reading off: the two ends, and the highest and lowest if they are elsewhere.
            var labelled = new HashSet<int> { 0, series.Points.Count - 1, values.IndexOf(max), values.IndexOf(min) };
            foreach (var index in labelled)
            {
                var text = Label(UnitConverter.FormatValue(series.Points[index].Value), Palette.OnSurface, 9);
                Place(text, X(index) - text.DesiredSize.Width / 2, Y(values[index]) - text.DesiredSize.Height - 3);
            }
        }
    }
}
