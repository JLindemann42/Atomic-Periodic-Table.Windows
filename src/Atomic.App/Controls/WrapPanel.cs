using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Atomic.App.Controls
{
    /// <summary>
    /// Lays children out left to right, wrapping to a new row when the line is full.
    ///
    /// <para>WinUI 3 has no wrap panel of its own - the one people reach for lives in the
    /// Community Toolkit. This is ~60 lines and gets used in three places (equation examples,
    /// alloy filter chips, and the chat suggestion chips later), so it pays for itself rather than
    /// pulling in a package for one control.</para>
    /// </summary>
    public sealed class WrapPanel : Panel
    {
        public static readonly DependencyProperty HorizontalSpacingProperty =
            DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
                new PropertyMetadata(8.0, OnLayoutPropertyChanged));

        public static readonly DependencyProperty VerticalSpacingProperty =
            DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
                new PropertyMetadata(8.0, OnLayoutPropertyChanged));

        public double HorizontalSpacing
        {
            get => (double)GetValue(HorizontalSpacingProperty);
            set => SetValue(HorizontalSpacingProperty, value);
        }

        public double VerticalSpacing
        {
            get => (double)GetValue(VerticalSpacingProperty);
            set => SetValue(VerticalSpacingProperty, value);
        }

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((WrapPanel)d).InvalidateMeasure();

        protected override Size MeasureOverride(Size availableSize)
        {
            // An unbounded width would put everything on one line, which is never what a wrap panel
            // is for; fall back to infinity only so the measure pass does not divide by NaN.
            var lineWidth = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
            var childConstraint = new Size(lineWidth, double.PositiveInfinity);

            double x = 0, rowHeight = 0, totalWidth = 0, totalHeight = 0;

            foreach (var child in Children)
            {
                child.Measure(childConstraint);

                // A child never takes more than a line. Text that cannot wrap reports its natural
                // width however little room it was offered, and a chip that runs off the edge is
                // clipped, not wrapped.
                var size = new Size(Math.Min(child.DesiredSize.Width, lineWidth), child.DesiredSize.Height);

                if (x > 0 && x + size.Width > lineWidth)
                {
                    totalWidth = Math.Max(totalWidth, x - HorizontalSpacing);
                    totalHeight += rowHeight + VerticalSpacing;
                    x = 0;
                    rowHeight = 0;
                }

                x += size.Width + HorizontalSpacing;
                rowHeight = Math.Max(rowHeight, size.Height);
            }

            totalWidth = Math.Max(totalWidth, x - HorizontalSpacing);
            totalHeight += rowHeight;

            return new Size(
                double.IsInfinity(availableSize.Width) ? Math.Max(0, totalWidth) : Math.Min(Math.Max(0, totalWidth), availableSize.Width),
                Math.Max(0, totalHeight));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double x = 0, y = 0, rowHeight = 0;

            foreach (var child in Children)
            {
                var size = new Size(Math.Min(child.DesiredSize.Width, finalSize.Width), child.DesiredSize.Height);

                if (x > 0 && x + size.Width > finalSize.Width)
                {
                    x = 0;
                    y += rowHeight + VerticalSpacing;
                    rowHeight = 0;
                }

                child.Arrange(new Rect(x, y, size.Width, size.Height));
                x += size.Width + HorizontalSpacing;
                rowHeight = Math.Max(rowHeight, size.Height);
            }

            return finalSize;
        }
    }
}
