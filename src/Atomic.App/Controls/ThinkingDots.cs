using System;
using Atomic.App.Services;
using Atomic.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Atomic.App.Controls
{
    /// <summary>
    /// Three dots pulsing in sequence, shown while the engine runs.
    ///
    /// <para>Built in code rather than XAML because the three animations differ only by a delay, and
    /// three near-identical <c>Storyboard</c> blocks in markup is the kind of thing that gets edited
    /// in two places out of three.</para>
    ///
    /// <para>The storyboard is stopped when the control leaves the tree. A <c>ListView</c> recycles
    /// containers, so an animation left running on a removed thinking bubble keeps a composition
    /// animation alive for the life of the window.</para>
    /// </summary>
    public sealed partial class ThinkingDots : UserControl
    {
        private const int DotCount = 3;
        private const double DotSize = 7;
        private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(1200);

        private readonly Storyboard _storyboard = new();

        public ThinkingDots()
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
            };

            for (var index = 0; index < DotCount; index++)
            {
                var dot = new Ellipse
                {
                    Width = DotSize,
                    Height = DotSize,
                    Opacity = 0.3,
                    Fill = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                };
                row.Children.Add(dot);

                var fade = new DoubleAnimationUsingKeyFrames
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    // Each dot starts a third of a period after the one before it, which is what
                    // makes the row read as a travelling pulse rather than three blinking dots.
                    BeginTime = TimeSpan.FromMilliseconds(Period.TotalMilliseconds / DotCount * index),
                };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame
                {
                    KeyTime = TimeSpan.Zero,
                    Value = 0.3,
                });
                fade.KeyFrames.Add(new LinearDoubleKeyFrame
                {
                    KeyTime = TimeSpan.FromMilliseconds(Period.TotalMilliseconds / 2),
                    Value = 1.0,
                });
                fade.KeyFrames.Add(new LinearDoubleKeyFrame
                {
                    KeyTime = Period,
                    Value = 0.3,
                });

                Storyboard.SetTarget(fade, dot);
                Storyboard.SetTargetProperty(fade, "Opacity");
                _storyboard.Children.Add(fade);
            }

            Content = row;

            // Read aloud in place of the animation, which conveys nothing to a screen reader.
            AutomationProperties.SetName(this,
                ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current))
                    .Get(S.chat_thinking));
            AutomationProperties.SetLiveSetting(
                this, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

            Loaded += (_, _) => _storyboard.Begin();
            Unloaded += (_, _) => _storyboard.Stop();
        }
    }
}
