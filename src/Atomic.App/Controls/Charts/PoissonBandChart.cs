using System;
using System.Globalization;
using Atomic.Core.Ai.Cards;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Shapes;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// An element's Poisson ratio placed against the materials the app tabulates.
    ///
    /// <para>Ports <c>PoissonBandView.kt</c>. A bare "0.29" tells almost nobody anything; against
    /// rock at 0.1-0.4 and the incompressible limit at 0.5 it becomes a statement about how the
    /// material behaves. That argument is why this card is a band rather than a number, and it is
    /// the reducer - not this class - that decides what to compare against.</para>
    /// </summary>
    public sealed partial class PoissonBandChart : ChartBase
    {
        private const double RowHeight = 18;
        private const double RowGap = 4;
        private const double LabelWidth = 92;
        private const double AxisHeight = 16;

        private PoissonBand? _band;

        public void SetBand(PoissonBand band)
        {
            _band = band;
            Height = AxisHeight + (band.References.Count + 1) * (RowHeight + RowGap) + RowGap;
            Redraw();
        }

        protected override void Draw()
        {
            if (_band is not { } band) return;

            var plotLeft = LabelWidth;
            var plotWidth = PlotWidth - plotLeft - 8;
            if (plotWidth <= 0) return;

            var span = band.AxisMax - band.AxisMin;
            if (span <= 0) return;

            double X(double value) => plotLeft + (value - band.AxisMin) / span * plotWidth;

            var y = 0.0;

            // The element first, in the accent, so the eye lands on the subject rather than on the
            // reference material that happens to sort first.
            Row(band.ElementDisplay, X(band.ElementLow), X(band.ElementHigh), y, Palette.Primary, bold: true);
            y += RowHeight + RowGap;

            foreach (var reference in band.References)
            {
                Row(reference.Label, X(reference.Low), X(reference.High), y, Palette.Outline, bold: false);
                y += RowHeight + RowGap;
            }

            DrawAxis(band, X, plotLeft, plotWidth, y);
        }

        private void Row(string label, double x1, double x2, double y, Windows.UI.Color color, bool bold)
        {
            var text = Label(label, Palette.OnSurface, 11);
            if (bold) text.FontWeight = FontWeights.SemiBold;
            Place(text, 0, y + (RowHeight - text.DesiredSize.Height) / 2);

            // A single-valued ratio has zero width, which would draw nothing at all. Give it a
            // minimum so a precise value is still visible as a mark.
            var width = Math.Max(x2 - x1, 3);

            var bar = new Rectangle
            {
                Width = width,
                Height = RowHeight - 6,
                RadiusX = 3,
                RadiusY = 3,
                Fill = Palette.Brush(color),
            };
            Place(bar, x1, y + 3);
            Tip(bar, label);
        }

        private void DrawAxis(
            PoissonBand band, Func<double, double> x, double plotLeft, double plotWidth, double y)
        {
            Rule(plotLeft, y, plotLeft + plotWidth, y, Palette.Outline);

            // Ticks every 0.1, which is the granularity the reference materials are authored at.
            for (var value = Math.Ceiling(band.AxisMin * 10) / 10; value <= band.AxisMax + 1e-9; value += 0.1)
            {
                var tickX = x(value);
                Rule(tickX, y, tickX, y + 3, Palette.Outline);

                var label = Label(value.ToString("0.0", CultureInfo.InvariantCulture), Palette.OnSurface, 9);
                Place(label, tickX - label.DesiredSize.Width / 2, y + 3);
            }

            // The thermodynamic bound for an isotropic material. Worth drawing because half the
            // point of the card is that the element sits some distance below it.
            if (PoissonBandReducer.IncompressibleLimit > band.AxisMin &&
                PoissonBandReducer.IncompressibleLimit <= band.AxisMax)
            {
                var limitX = x(PoissonBandReducer.IncompressibleLimit);
                var limit = Rule(limitX, 0, limitX, y, Palette.Error);
                limit.StrokeDashArray = [3, 3];
            }
        }
    }
}
