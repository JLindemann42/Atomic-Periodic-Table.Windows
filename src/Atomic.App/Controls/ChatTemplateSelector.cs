using Atomic.App.Services;
using Atomic.Core.Ai.Cards;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atomic.App.Controls
{
    /// <summary>
    /// Picks the bubble template for one message.
    ///
    /// <para><b>Three templates, not eleven.</b> The plan called for one per card kind, selected on
    /// <see cref="ChatCardCodec.KindOf"/>. Each would have had to repeat the entire assistant bubble
    /// - text, gate, citation chips - around a single differing child, and eight copies of a bubble
    /// is eight places for them to drift apart. The kind switch moved into
    /// <see cref="ChatCardView"/>, on the one element that actually differs; <c>KindOf</c> still
    /// does the cheap decode it exists for, just there rather than here.</para>
    ///
    /// <para>What is left is the only distinction a <i>container</i> needs to make: who is
    /// speaking.</para>
    /// </summary>
    public sealed partial class ChatTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? User { get; set; }

        public DataTemplate? Assistant { get; set; }

        public DataTemplate? Thinking { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item) =>
            SelectTemplateCore(item, null);

        protected override DataTemplate? SelectTemplateCore(object item, DependencyObject? container)
        {
            if (item is not ChatMessage message) return Assistant;

            return message.Role switch
            {
                ChatRole.User => User ?? Assistant,
                ChatRole.Thinking => Thinking ?? Assistant,
                _ => Assistant,
            };
        }
    }
}
