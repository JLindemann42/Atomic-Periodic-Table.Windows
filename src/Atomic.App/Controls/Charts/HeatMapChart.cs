using System;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Data;
using Microsoft.UI.Xaml.Shapes;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// One field over the whole table, as a mini periodic table shaded by value (roadmap 2.3).
    ///
    /// <para>Ports <c>HeatMapView.kt</c>. Every placement and every shade arrives from
    /// <see cref="HeatMapReducer"/>: where an element sits, and its value as a share of the field's
    /// span on whichever scale the reducer chose. An element with no value is an outline, so a gap in
    /// the data never reads as the lowest value.</para>
    /// </summary>
    public sealed partial class HeatMapChart : ChartBase
    {
        private const int Columns = 18;
        private const int Rows = 9;
        private const double Gap = 0.5;
        private const double LegendHeight = 18;
        private const double MinOpacity = 0.16;

        private HeatMap? _map;

        public void SetMap(HeatMap map)
        {
            _map = map;
            Redraw();
        }

        protected override void Draw()
        {
            if (_map is not { } map || PlotWidth <= 0 || PlotHeight <= 0) return;

            // Eighteen columns; seven periods, a half-row gap, and the two f-block rows.
            var cell = Math.Min(PlotWidth / Columns, (PlotHeight - LegendHeight) / (Rows + Gap));
            var inset = cell * 0.08;
            var originX = (PlotWidth - cell * Columns) / 2;

            double Top(int row) => (row - 1 + (row > 7 ? Gap : 0)) * cell;

            foreach (var c in map.Cells)
            {
                var square = new Rectangle { Width = cell - 2 * inset, Height = cell - 2 * inset };
                if (c.Fraction is { } fraction)
                {
                    square.Fill = Palette.PrimaryBrush;
                    square.Opacity = MinOpacity + (1 - MinOpacity) * fraction;
                }
                else
                {
                    square.Stroke = Palette.Brush(Palette.Outline);
                    square.StrokeThickness = 0.75;
                }
                Place(square, originX + (c.Column - 1) * cell + inset, Top(c.Row) + inset);
                Tip(square, c.Value is { } v ? $"{c.Symbol}: {UnitConverter.FormatValue(v)}" : c.Symbol);
            }

            // The legend: the lowest shade, the highest, and the values they stand for.
            var legendTop = PlotHeight - LegendHeight + 4;
            var swatch = Math.Min(cell * 0.9, 12);
            var low = new Rectangle { Width = swatch, Height = swatch, Fill = Palette.PrimaryBrush, Opacity = MinOpacity };
            Place(low, originX, legendTop);
            var lowText = Label(UnitConverter.FormatValue(map.Min), Palette.OnSurface, 9);
            Place(lowText, originX + swatch + 4, legendTop - 1);

            var highText = Label(UnitConverter.FormatValue(map.Max), Palette.OnSurface, 9);
            var highX = originX + cell * Columns - highText.DesiredSize.Width;
            Place(highText, highX, legendTop - 1);
            var high = new Rectangle { Width = swatch, Height = swatch, Fill = Palette.PrimaryBrush };
            Place(high, highX - swatch - 4, legendTop);
        }
    }
}
