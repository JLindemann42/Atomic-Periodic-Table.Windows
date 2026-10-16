using System;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Cards;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// Successive ionization energies, with the shell closures marked.
    ///
    /// <para>Ports <c>IonizationSeriesView.kt</c>. The dashed rules are the teaching point: sodium's
    /// second ionization costs nearly ten times its first because the first emptied its outer shell,
    /// and that jump is what the chart is for. Which orders count as closures is decided by the
    /// reducer, not here.</para>
    /// </summary>
    public sealed partial class IonizationSeriesChart : ChartBase
    {
        private const double MarginLeft = 40;
        private const double MarginRight = 8;
        private const double MarginTop = 8;
        private const double MarginBottom = 20;

        private IonizationSeries? _series;

        public void SetSeries(IonizationSeries series)
        {
            _series = series;
            Redraw();
        }

        protected override void Draw()
        {
            if (_series is not { } series || series.Steps.Count == 0) return;

            var plotWidth = PlotWidth - MarginLeft - MarginRight;
            var plotHeight = PlotHeight - MarginTop - MarginBottom;
            if (plotWidth <= 0 || plotHeight <= 0) return;

            // The scale comes from the data, not from a preference: a series spanning four decades
            // is unreadable linearly, and one spanning half a decade is unreadable logarithmically.
            var log = series.SuggestedScale == ChartScale.Log10;

            double Value(double ev) => log ? Math.Log10(Math.Max(ev, 1e-6)) : ev;

            var values = series.Steps.Select(s => Value(s.ElectronVolts)).ToList();
            var min = log ? Math.Floor(values.Min()) : 0;
            var max = Math.Ceiling(values.Max());
            if (max - min < 1e-9) max = min + 1;

            double X(int index) => series.Steps.Count == 1
                ? MarginLeft + plotWidth / 2
                : MarginLeft + (double)index / (series.Steps.Count - 1) * plotWidth;

            double Y(double value) => MarginTop + (1 - (value - min) / (max - min)) * plotHeight;

            DrawAxes(min, max, log, plotWidth, plotHeight);
            DrawClosures(series, X, plotHeight);

            var line = new Polyline
            {
                Stroke = Palette.PrimaryBrush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
            };

            for (var index = 0; index < series.Steps.Count; index++)
            {
                var point = new Point(X(index), Y(values[index]));
                line.Points.Add(point);
            }
            Surface.Children.Add(line);

            for (var index = 0; index < series.Steps.Count; index++)
            {
                var step = series.Steps[index];
                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Palette.PrimaryBrush,
                };
                Place(dot, X(index) - 3, Y(values[index]) - 3);
                Tip(dot, $"{step.Order}: {step.Display}");
            }
        }

        private void DrawAxes(double min, double max, bool log, double plotWidth, double plotHeight)
        {
            Rule(MarginLeft, MarginTop + plotHeight, MarginLeft + plotWidth, MarginTop + plotHeight, Palette.Outline);
            Rule(MarginLeft, MarginTop, MarginLeft, MarginTop + plotHeight, Palette.Outline);

            // Four gridlines is enough to read a value off without turning the card into a table.
            const int Divisions = 4;
            for (var i = 0; i <= Divisions; i++)
            {
                var value = min + (max - min) * i / Divisions;
                var y = MarginTop + (1 - (double)i / Divisions) * plotHeight;

                if (i > 0)
                {
                    var grid = Rule(MarginLeft, y, MarginLeft + plotWidth, y, Palette.Outline, 0.5);
                    grid.Opacity = 0.35;
                }

                var shown = log ? Math.Pow(10, value) : value;
                var label = Label(Format(shown), Palette.OnSurface, 9);
                Place(label, MarginLeft - label.DesiredSize.Width - 4, y - label.DesiredSize.Height / 2);
            }
        }

        private void DrawClosures(IonizationSeries series, Func<int, double> x, double plotHeight)
        {
            foreach (var order in series.ShellClosureAfter)
            {
                // An explicit search rather than FirstOrDefault, whose "not found" is index 0 -
                // indistinguishable from a genuine closure after the first step.
                var index = -1;
                for (var i = 0; i < series.Steps.Count; i++)
                {
                    if (series.Steps[i].Order != order) continue;
                    index = i;
                    break;
                }
                if (index < 0) continue;

                // Between this step and the next, because the closure is the gap rather than a
                // point: the shell emptied at `order`, and the cost jumps at `order + 1`.
                var at = index + 1 < series.Steps.Count ? (x(index) + x(index + 1)) / 2 : x(index);
                var rule = Rule(at, MarginTop, at, MarginTop + plotHeight, Palette.Error, 1);
                rule.StrokeDashArray = [3, 3];
                rule.Opacity = 0.7;
            }
        }

        private static string Format(double electronVolts) => electronVolts switch
        {
            >= 1000 => (electronVolts / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k",
            >= 10 => electronVolts.ToString("0", CultureInfo.InvariantCulture),
            _ => electronVolts.ToString("0.#", CultureInfo.InvariantCulture),
        };
    }
}
