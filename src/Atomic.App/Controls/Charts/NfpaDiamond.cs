using System;
using Atomic.Core.Ai.Cards;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// The NFPA 704 fire diamond: four quadrants, each a rotated square, in the standard colours.
    ///
    /// <para>Ports <c>NfpaDiamondBinder.kt</c>. The colours are the standard's own - blue health,
    /// red flammability, yellow instability, white special - and are therefore literals rather than
    /// theme colours. A theme-coloured hazard diamond would be a different diamond.</para>
    ///
    /// <para>The labels come from <see cref="NfpaLabeller"/>, which is the single owner of that
    /// mapping. On Android it existed twice, the second copy being hardcoded English literals inside
    /// the element screen of an app that ships twelve languages.</para>
    /// </summary>
    public sealed partial class NfpaDiamond : ChartBase
    {
        private static readonly Color Health = Color.FromArgb(255, 0, 102, 204);
        private static readonly Color Fire = Color.FromArgb(255, 224, 32, 32);
        private static readonly Color Instability = Color.FromArgb(255, 245, 200, 0);
        private static readonly Color Special = Colors.White;
        private static readonly Color Edge = Color.FromArgb(255, 60, 60, 60);

        private NfpaLabels? _labels;

        public void SetLabels(NfpaLabels labels)
        {
            _labels = labels;
            Redraw();
        }

        protected override void Draw()
        {
            if (_labels is not { } labels) return;

            // The diamond is a square turned 45 degrees, so what must fit the control is its
            // diagonal, not its side. A square of side `extent` would overflow by a factor of
            // root two once rotated.
            var extent = Math.Min(PlotWidth, PlotHeight);
            var square = extent / Math.Sqrt(2);
            var side = square / 2;

            // Four quadrants meeting at the centre, the whole group rotated once. Rotating the
            // group rather than each square is what keeps the seams aligned.
            var group = new Canvas
            {
                Width = square,
                Height = square,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = 45 },
            };
            Canvas.SetLeft(group, (PlotWidth - square) / 2);
            Canvas.SetTop(group, (PlotHeight - square) / 2);
            Surface.Children.Add(group);

            // Positions are pre-rotation: left becomes the west (health) point once turned, top
            // becomes north (fire), right becomes east (instability), bottom becomes south.
            Quadrant(group, 0, 0, side, Fire, labels.Fire, labels.FireText);
            Quadrant(group, side, side, side, Instability, labels.Reactivity, labels.ReactivityText);
            Quadrant(group, 0, side, side, Health, labels.Health, labels.HealthText);
            Quadrant(group, side, 0, side, Special, labels.Special, labels.SpecialText);
        }

        private void Quadrant(
            Canvas group, double left, double top, double side,
            Color fill, string value, string description)
        {
            var cell = new Rectangle
            {
                Width = side,
                Height = side,
                Fill = Palette.Brush(fill),
                Stroke = Palette.Brush(Edge),
                StrokeThickness = 1,
            };
            Canvas.SetLeft(cell, left);
            Canvas.SetTop(cell, top);
            group.Children.Add(cell);
            Tip(cell, description);

            var label = new TextBlock
            {
                Text = value,
                FontSize = side * 0.42,
                FontWeight = FontWeights.Bold,
                // Black on every quadrant: the standard's yellow and white cannot carry white text,
                // and a per-quadrant rule would be four chances to get the contrast wrong.
                Foreground = Palette.Brush(Color.FromArgb(255, 20, 20, 20)),
                // Counter-rotated, so the numbers read upright in a diamond that is turned 45.
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = -45 },
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(label, left + (side - label.DesiredSize.Width) / 2);
            Canvas.SetTop(label, top + (side - label.DesiredSize.Height) / 2);
            group.Children.Add(label);
            Tip(label, description);
        }
    }
}
