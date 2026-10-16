using System;
using Atomic.App.Controls.Charts;
using Atomic.App.Services;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Atomic.App.Controls
{
    /// <summary>
    /// Turns an encoded chat card into the visual it names.
    ///
    /// <para><b>One control rather than eight data templates.</b> The plan called for a template per
    /// card kind on <see cref="ChatTemplateSelector"/>, but each would have had to repeat the whole
    /// assistant bubble - text, citations, gate - around a single differing child, and eight copies
    /// of a bubble is eight places for them to drift. The kind switch lives here instead, on the one
    /// element that actually differs. <see cref="ChatCardCodec.KindOf"/> still does the cheap decode
    /// it exists for; it just does it here rather than in the selector.</para>
    ///
    /// <para>Falls back to a named summary strip whenever the visual cannot be built - a reducer
    /// declining, an unknown crystal system, no element data. An upsell or a card slot over an empty
    /// box is a puzzle; over a named visual it is at least an offer.</para>
    /// </summary>
    public sealed partial class ChatCardView : ContentControl
    {
        public ChatCardView()
        {
            IsTabStop = false;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }

        public static readonly DependencyProperty CardProperty =
            DependencyProperty.Register(
                nameof(Card), typeof(string), typeof(ChatCardView),
                new PropertyMetadata(null, (d, _) => ((ChatCardView)d).Rebuild()));

        /// <summary>The encoded card, straight off the stored message.</summary>
        public string? Card
        {
            get => (string?)GetValue(CardProperty);
            set => SetValue(CardProperty, value);
        }

        private void Rebuild()
        {
            Content = null;

            if (ChatCardCodec.Decode(Card) is not { } card) return;

            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));
            var store = RetrievalService.Get(ElementRepository.Source)?.Store;
            var element = store?.Element(card.ElementKey);

            Content = element is null || store is null
                ? Summary(card, strings)
                : Build(card, element, store, strings) ?? Summary(card, strings);
        }

        private static UIElement? Build(
            ChatCard card, ElementRecord element, KnowledgeStore store, IStringProvider strings)
        {
            switch (card.Kind)
            {
                case ChatCardKind.ElectronShell:
                {
                    var authored = (element.Value("electron_shells") as FieldValue.Text)?.Raw;
                    var shells = ElectronShells.Parse(authored);
                    if (shells.Count == 0) return null;

                    var chart = new ElectronShellChart { Height = 180 };
                    chart.SetShells(authored, element.Symbol);
                    return chart;
                }

                case ChatCardKind.CrystalStructure:
                {
                    var raw = element.Value("crystal_structure") switch
                    {
                        FieldValue.Enum e => e.Localized,
                        FieldValue.Text t => t.Raw,
                        _ => null,
                    };
                    if (CrystalSystemResolver.Resolve(raw) is not { } system) return null;

                    var chart = new CrystalStructureChart { Height = 170 };
                    chart.SetSystem(system);
                    return chart;
                }

                case ChatCardKind.NfpaDiamond:
                {
                    if (element.Nfpa is not { } nfpa) return null;

                    var chart = new NfpaDiamond { Height = 150 };
                    chart.SetLabels(NfpaLabeller.Labels(nfpa, strings));
                    return chart;
                }

                case ChatCardKind.PoissonBand:
                {
                    if (PoissonBandReducer.Of(element, store) is not { } band) return null;

                    var chart = new PoissonBandChart();
                    chart.SetBand(band);
                    return chart;
                }

                case ChatCardKind.IonizationSeries:
                {
                    if (IonizationSeriesReducer.Of(element) is not { } series) return null;

                    var chart = new IonizationSeriesChart { Height = 170 };
                    chart.SetSeries(series);
                    return chart;
                }

                case ChatCardKind.IsotopeDecay:
                {
                    if (IsotopeSeriesReducer.Of(element) is not { } series) return null;

                    var chart = new IsotopeDecayChart { Height = 190 };
                    chart.SetSeries(series);
                    return chart;
                }

                case ChatCardKind.Abundance:
                {
                    if (AbundanceReducer.Of(element, strings) is not { } profile) return null;

                    var chart = new AbundanceBarsChart();
                    chart.SetProfile(profile);
                    return chart;
                }

                case ChatCardKind.EmissionSpectrum:
                {
                    var card2 = new EmissionSpectrumCard();
                    card2.SetSymbol(element.Symbol);
                    return card2;
                }

                default:
                    return null;
            }
        }

        /// <summary>The named strip shown when the visual itself cannot be built.</summary>
        private static UIElement Summary(ChatCard card, IStringProvider strings)
        {
            var title = new TextBlock
            {
                Text = card.Title,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };

            var subtitle = new TextBlock
            {
                Text = ChatCardLabels.For(card.Kind, strings),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };

            return new Border
            {
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Child = new StackPanel { Spacing = 2, Children = { title, subtitle } },
            };
        }
    }
}
