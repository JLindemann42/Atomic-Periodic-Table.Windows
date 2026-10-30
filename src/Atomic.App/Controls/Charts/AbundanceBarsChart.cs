using System;
using Atomic.Core.Ai.Cards;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Shapes;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// An element's abundance across the reservoirs the app records.
    ///
    /// <para>Ports <c>AbundanceBarsView.kt</c>. Three things about this chart are constraints from
    /// the data rather than styling choices, and all three are decided by the reducer:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Bars are grouped by unit.</b> The nine reservoirs are not measured alike - crust and
    /// meteorites in mg/kg, sea water in µg/l, the sun in atom counts against silicon. Drawing them
    /// as one run claims a comparison the data does not support.</item>
    /// <item><b>The axis starts at a drawn, labelled floor.</b> A log axis has no zero, so bars
    /// growing from an unlabelled edge have lengths that mean nothing at all.</item>
    /// <item><b>"Trace" is an outlined stub, not a short bar.</b> It is a statement that the value
    /// is below measurement, not a small measurement.</item>
    /// </list>
    /// </summary>
    public sealed partial class AbundanceBarsChart : ChartBase
    {
        private const double BarHeight = 14;
        private const double BarGap = 4;
        private const double GroupGap = 10;
        private const double UnitHeaderHeight = 15;

        /// <summary>
        /// Share of the width given to the reservoir names.
        ///
        /// <para>Proportional rather than fixed. A fixed column sized for "crust" clips "förekomst i
        /// människokroppen", and every one of these labels is a translated phrase rather than a
        /// word.</para>
        /// </summary>
        private const double LabelFraction = 0.34;

        private const double MinLabelWidth = 70;
        private const double MaxLabelWidth = 170;

        /// <summary>Room kept at the right for the value, so it is never cut off by the card edge.</summary>
        private const double ValueGutter = 62;

        private AbundanceProfile? _profile;

        public void SetProfile(AbundanceProfile profile)
        {
            _profile = profile;

            var rows = 0.0;
            foreach (var group in profile.Groups)
                rows += UnitHeaderHeight + group.Bars.Count * (BarHeight + BarGap) + GroupGap;

            Height = rows + 4;
            Redraw();
        }

        protected override void Draw()
        {
            if (_profile is not { } profile) return;

            var labelWidth = Math.Clamp(PlotWidth * LabelFraction, MinLabelWidth, MaxLabelWidth);
            var plotLeft = labelWidth + 6;
            var plotWidth = PlotWidth - plotLeft - ValueGutter;
            if (plotWidth <= 0) return;

            var span = profile.MaxLog - profile.FloorLog;
            if (span <= 0) return;

            var y = 0.0;

            foreach (var group in profile.Groups)
            {
                var header = Label(group.UnitLabel, Palette.OnSurface, 9);
                header.Opacity = 0.75;
                Place(header, 0, y);
                y += UnitHeaderHeight;

                // The floor, drawn and owned by the group, because each group has its own unit and
                // so its own claim about what "no bar" means.
                Rule(plotLeft, y - 2, plotLeft, y + group.Bars.Count * (BarHeight + BarGap), Palette.Outline);

                foreach (var bar in group.Bars)
                {
                    DrawBar(bar, labelWidth, plotLeft, plotWidth, y, profile.FloorLog, span);
                    y += BarHeight + BarGap;
                }

                y += GroupGap;
            }
        }

        private void DrawBar(
            AbundanceBar bar, double labelWidth, double plotLeft, double plotWidth,
            double y, double floor, double span)
        {
            var label = Label(bar.Label, Palette.OnSurface, 10);
            // Trimmed rather than wrapped: a wrapped label would change the row height and pull the
            // bar out of line with its neighbours. The full text stays reachable as a tooltip.
            label.MaxWidth = labelWidth;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.TextWrapping = TextWrapping.NoWrap;
            Tip(label, bar.Label);
            Place(label, 0, y + (BarHeight - label.DesiredSize.Height) / 2);

            Shape shape;

            if (bar.Trace || bar.LogMgPerKg is not { } log)
            {
                // Outlined stub: "present, below measurement". A short filled bar would read as a
                // small measured value, which is the one thing the data does not say.
                shape = new Rectangle
                {
                    Width = 14,
                    Height = BarHeight,
                    RadiusX = 2,
                    RadiusY = 2,
                    Stroke = Palette.OutlineBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = [2, 2],
                };
            }
            else
            {
                var width = Math.Max((log - floor) / span * plotWidth, 2);
                shape = new Rectangle
                {
                    Width = width,
                    Height = BarHeight,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = Palette.PrimaryBrush,
                };
            }

            Place(shape, plotLeft, y);
            Tip(shape, $"{bar.Label} — {bar.Display}");

            var value = Label(bar.Display, Palette.OnSurface, 9);
            value.Opacity = 0.8;
            value.FontWeight = FontWeights.SemiBold;
            Place(value, plotLeft + shape.Width + 4, y + (BarHeight - value.DesiredSize.Height) / 2);
        }
    }
}
