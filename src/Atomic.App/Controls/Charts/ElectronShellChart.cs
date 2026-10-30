using System;
using System.Collections.Generic;
using Atomic.Core.Ai.Cards;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// The Bohr diagram: a labelled nucleus with one ring per shell and a dot per electron.
    ///
    /// <para>Ports <c>ElectronShellView.kt</c>. The shell string is parsed by
    /// <see cref="ElectronShells"/> in the core, where it is tested against all 118 elements.</para>
    /// </summary>
    public sealed partial class ElectronShellChart : ChartBase
    {
        /// <summary>How much of the half-extent the outermost ring uses.</summary>
        private const double OuterExtent = 0.85;

        /// <summary>Nucleus radius as a fraction of the smaller side.</summary>
        private const double NucleusExtent = 0.08;

        /// <summary>Electron dot radius as a fraction of the smaller side.</summary>
        private const double ElectronExtent = 0.015;

        /// <summary>Ring opacity. Android draws the orbit at alpha 80 of 255.</summary>
        private const double RingOpacity = 80.0 / 255.0;

        private IReadOnlyList<int> _shells = [];
        private string _symbol = string.Empty;

        public void SetShells(string? authored, string symbol)
        {
            _shells = ElectronShells.Parse(authored);
            _symbol = symbol;
            Redraw();
        }

        protected override void Draw()
        {
            if (_shells.Count == 0) return;

            var centreX = PlotWidth / 2;
            var centreY = PlotHeight / 2;
            var smaller = Math.Min(PlotWidth, PlotHeight);

            var nucleus = smaller * NucleusExtent;
            var outer = smaller / 2 * OuterExtent;
            var step = (outer - nucleus) / _shells.Count;
            var dot = smaller * ElectronExtent;

            DrawNucleus(centreX, centreY, nucleus);

            for (var index = 0; index < _shells.Count; index++)
            {
                var radius = nucleus + (index + 1) * step;
                DrawRing(centreX, centreY, radius);

                // An empty shell still gets its ring - see ElectronShells for why a gap is drawn
                // rather than closed up. Palladium is the one element this is visible on.
                for (var i = 0; i < _shells[index]; i++)
                {
                    // From the top, going clockwise, so two-electron shells sit vertically rather
                    // than at an arbitrary angle.
                    var angle = -Math.PI / 2 + 2 * Math.PI * i / _shells[index];
                    DrawElectron(
                        centreX + radius * Math.Cos(angle),
                        centreY + radius * Math.Sin(angle),
                        dot);
                }
            }
        }

        private void DrawNucleus(double centreX, double centreY, double radius)
        {
            Place(
                new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Fill = Palette.PrimaryBrush,
                },
                centreX - radius,
                centreY - radius);

            if (_symbol.Length == 0) return;

            var label = new TextBlock
            {
                Text = _symbol,
                FontSize = radius * 0.9,
                FontWeight = FontWeights.Bold,
                // On the nucleus fill, not on the card, so it follows the accent rather than the
                // surface.
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            };
            label.Measure(new Windows.Foundation.Size(
                double.PositiveInfinity, double.PositiveInfinity));

            Place(
                label,
                centreX - label.DesiredSize.Width / 2,
                centreY - label.DesiredSize.Height / 2);
        }

        private void DrawRing(double centreX, double centreY, double radius) =>
            Place(
                new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Stroke = Palette.OutlineBrush,
                    StrokeThickness = 1.5,
                    Opacity = RingOpacity,
                },
                centreX - radius,
                centreY - radius);

        private void DrawElectron(double x, double y, double radius) =>
            Place(
                new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Fill = Palette.PrimaryBrush,
                },
                x - radius,
                y - radius);
    }
}
