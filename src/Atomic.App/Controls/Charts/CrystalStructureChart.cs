using System;
using Atomic.App.Helpers;
using Microsoft.UI.Xaml.Shapes;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// The unit cell of a crystal system, as a static isometric wireframe.
    ///
    /// <para>Draws the same lattice data <c>CrystalStructures</c> already holds for the element
    /// page, but as shapes rather than through <c>CrystalStructureRenderer</c>'s software
    /// rasterizer. That renderer is the right tool where it is used - a lattice the user can rotate
    /// - but it writes pixels into a <c>WriteableBitmap</c>, and a chat card lives in a virtualized
    /// list where every visible bubble would allocate one. The same reasoning that kept Win2D out of
    /// these charts applies to it.</para>
    ///
    /// <para>Static rather than rotatable, for the same reason: a card in scrollback is a figure,
    /// not an instrument. The element page keeps the interactive version.</para>
    /// </summary>
    public sealed partial class CrystalStructureChart : ChartBase
    {
        /// <summary>Isometric view angles, in radians. Enough to read depth without foreshortening.</summary>
        private const double Yaw = 0.62;
        private const double Pitch = 0.42;

        private string? _system;

        public void SetSystem(string system)
        {
            _system = system;
            Redraw();
        }

        protected override void Draw()
        {
            if (_system is null) return;
            if (!CrystalStructures.Data.TryGetValue(_system, out var lattice)) return;

            var (vertices, edges, _) = lattice;
            var count = vertices.GetLength(0);
            if (count == 0) return;

            // Project every vertex first, so the scale can be fitted to what is actually drawn
            // rather than to the cell's nominal extent - the systems have very different aspect
            // ratios and a fixed scale would clip the triclinic cell.
            var xs = new double[count];
            var ys = new double[count];

            for (var i = 0; i < count; i++)
            {
                var (rx, ry, _) = CrystalStructureRenderer.Rotate(
                    vertices[i, 0], vertices[i, 1], vertices[i, 2], Yaw, Pitch, 0);
                xs[i] = rx;
                ys[i] = ry;
            }

            var minX = Min(xs);
            var maxX = Max(xs);
            var minY = Min(ys);
            var maxY = Max(ys);

            var spanX = Math.Max(maxX - minX, 1e-6);
            var spanY = Math.Max(maxY - minY, 1e-6);

            const double Pad = 10;
            var scale = Math.Min((PlotWidth - 2 * Pad) / spanX, (PlotHeight - 2 * Pad) / spanY);
            if (scale <= 0) return;

            var offsetX = (PlotWidth - spanX * scale) / 2 - minX * scale;
            var offsetY = (PlotHeight - spanY * scale) / 2 - minY * scale;

            double PX(int i) => xs[i] * scale + offsetX;
            double PY(int i) => ys[i] * scale + offsetY;

            for (var e = 0; e < edges.GetLength(0); e++)
            {
                var a = edges[e, 0];
                var b = edges[e, 1];
                if (a >= count || b >= count) continue;

                var edge = Rule(PX(a), PY(a), PX(b), PY(b), Palette.Outline, 1.2);
                edge.Opacity = 0.8;
            }

            for (var i = 0; i < count; i++)
            {
                Place(
                    new Ellipse
                    {
                        Width = 7,
                        Height = 7,
                        Fill = Palette.PrimaryBrush,
                    },
                    PX(i) - 3.5,
                    PY(i) - 3.5);
            }
        }

        private static double Min(double[] values)
        {
            var min = values[0];
            foreach (var v in values) if (v < min) min = v;
            return min;
        }

        private static double Max(double[] values)
        {
            var max = values[0];
            foreach (var v in values) if (v > max) max = v;
            return max;
        }
    }
}
