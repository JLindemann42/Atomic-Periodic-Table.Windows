using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Atomic.App.Controls.Charts
{
    /// <summary>
    /// The shared plumbing for the chat cards' charts: a <see cref="Canvas"/>, a palette, and one
    /// rule about when to redraw.
    ///
    /// <para><b>Why shapes on a canvas and not Win2D.</b> Every one of these charts is vector with
    /// well under a hundred primitives, so there is no throughput problem to solve. They need
    /// theme colours that re-resolve on a theme switch, where a <c>CanvasControl</c> wants raw
    /// colours and manual invalidation. And the deciding fact: they render inside a virtualized
    /// <c>ListView</c> in chat scrollback, where each <c>CanvasControl</c> would allocate its own
    /// device resources - twenty cards in history is twenty swapchains.</para>
    ///
    /// <para>Drawing is deliberately dumb. Every decision that depends on what a number <i>means</i>
    /// lives in the reducers and in <c>DecadeLabels</c>, where a unit test can pin it; these classes
    /// only turn an already-reduced model into shapes. That division is what makes it acceptable
    /// that the drawing itself has no tests.</para>
    /// </summary>
    public abstract partial class ChartBase : UserControl
    {
        protected ChartBase()
        {
            IsTabStop = false;
            Palette = ChartPalette.For(this);
            Surface = new Canvas();
            Content = Surface;

            Loaded += (_, _) =>
            {
                Palette = ChartPalette.For(this);
                Redraw();
            };

            // A palette captured once is why a chart stays dark after a switch to light.
            ActualThemeChanged += (_, _) =>
            {
                Palette = ChartPalette.For(this);
                Redraw();
            };

            SizeChanged += (_, _) => Redraw();
        }

        /// <summary>Theme colours, refreshed whenever the theme changes.</summary>
        protected ChartPalette Palette { get; private set; }

        /// <summary>Where the shapes go.</summary>
        protected Canvas Surface { get; }

        protected double PlotWidth => ActualWidth;

        protected double PlotHeight => ActualHeight;

        /// <summary>Rebuild the shapes. Called on load, on resize, and on a theme change.</summary>
        protected void Redraw()
        {
            if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return;

            Surface.Children.Clear();
            Draw();
        }

        /// <summary>Draw into <see cref="Surface"/>. Called with a non-zero size and a live palette.</summary>
        protected abstract void Draw();

        // ---- Small drawing helpers, shared by every chart ---------------------------------------

        /// <summary>Add a shape at a canvas position.</summary>
        protected T Place<T>(T shape, double left, double top) where T : FrameworkElement
        {
            Canvas.SetLeft(shape, left);
            Canvas.SetTop(shape, top);
            Surface.Children.Add(shape);
            return shape;
        }

        /// <summary>A measured text block, so callers can position it by its real size.</summary>
        protected TextBlock Label(string text, Color color, double size = 10)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = Palette.Brush(color),
            };
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return block;
        }

        protected Line Rule(double x1, double y1, double x2, double y2, Color color, double thickness = 1)
        {
            var line = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = Palette.Brush(color),
                StrokeThickness = thickness,
            };
            Surface.Children.Add(line);
            return line;
        }

        /// <summary>A tooltip on a shape. Free with hit-testing, which is another reason for shapes.</summary>
        protected static void Tip(DependencyObject shape, string text) =>
            ToolTipService.SetToolTip(shape, text);
    }
}
