using Atomic.App.Services;
using Atomic.Core.Localization;
using Atomic.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Atomic.App
{
    /// <summary>
    /// The solubility table: cations across, anions down, and a legend beside it.
    ///
    /// <para>Rendered from <see cref="SolubilityData"/>, which is checked against the table Android
    /// shows (<c>shared/golden/solubility.tsv</c>), rather than from hand-placed XAML cells. The
    /// sizes and brushes are the ones the hand-written page used.</para>
    /// </summary>
    public sealed partial class SolubilityPage : Page
    {
        private const double CellWidth = 64;
        private const double CellHeight = 48;
        private const double LegendWidth = 220;

        public SolubilityPage()
        {
            InitializeComponent();
            Build();
        }

        private void Build()
        {
            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));
            var cations = SolubilityData.Columns;
            var anions = SolubilityData.Anions;

            Table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
            foreach (var _ in cations) Table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
            Table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LegendWidth) });
            for (var row = 0; row <= anions.Count; row++)
                Table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHeight) });

            var legendColumn = cations.Count + 1;

            // Cations across the top, as one rounded strip.
            for (var c = 0; c < cations.Count; c++)
            {
                var radius = c == 0 ? new CornerRadius(8, 0, 0, 8)
                    : c == cations.Count - 1 ? new CornerRadius(0, 8, 8, 0) : new CornerRadius(0);
                Place(Header(SolubilityData.Label(cations[c].Cation), radius), 0, c + 1);
            }

            // Anions down the side, as one rounded strip.
            for (var a = 0; a < anions.Count; a++)
            {
                var radius = a == 0 ? new CornerRadius(8, 8, 0, 0)
                    : a == anions.Count - 1 ? new CornerRadius(0, 0, 8, 8) : new CornerRadius(0);
                Place(Header(SolubilityData.Label(anions[a]), radius), a + 1, 0);
            }

            for (var c = 0; c < cations.Count; c++)
            {
                for (var a = 0; a < anions.Count; a++)
                    Place(Cell(cations[c].Values[a]), a + 1, c + 1);
            }

            // The legend, one row per code.
            var legendHeader = Header(strings.Get(S.label_details), new CornerRadius(8, 8, 0, 0));
            legendHeader.Margin = new Thickness(12, 0, 0, 0);
            Place(legendHeader, 0, legendColumn);
            string[] codes = ["S", "Ss", "I", "---"];
            string[] words = [S.solubility_soluble, S.solubility_slightly, S.solubility_insoluble, S.solubility_decomposed];
            for (var i = 0; i < codes.Length; i++)
            {
                var (background, foreground) = Brushes(codes[i]);
                Place(new Border
                {
                    Background = background,
                    BorderBrush = Resource("CardStrokeColorDefaultBrush"),
                    CornerRadius = i == codes.Length - 1 ? new CornerRadius(0, 0, 8, 8) : new CornerRadius(0),
                    Margin = new Thickness(12, 0, 0, 0),
                    Child = Text(strings.Get(words[i]), foreground, 16),
                }, i + 1, legendColumn);
            }
        }

        private void Place(FrameworkElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Table.Children.Add(element);
        }

        private static Border Header(string text, CornerRadius radius) => new()
        {
            Background = Resource("SolidBackgroundFillColorBaseAltBrush"),
            CornerRadius = radius,
            Child = Text(text, Resource("TextFillColorPrimaryBrush"), 18),
        };

        private static Border Cell(string code)
        {
            var (background, foreground) = Brushes(code);
            return new Border
            {
                Background = background,
                BorderBrush = Resource("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(1),
                Child = Text(code == "---" ? "-" : code, foreground, 16),
            };
        }

        private static (Brush Background, Brush Foreground) Brushes(string code) => code switch
        {
            "S" => (Resource("SystemFillColorSuccessBrush"), Resource("SystemFillColorSuccessBackgroundBrush")),
            "Ss" => (Resource("SystemFillColorCautionBrush"), Resource("SystemFillColorCautionBackgroundBrush")),
            "I" => (Resource("SystemFillColorCriticalBrush"), Resource("SystemFillColorCriticalBackgroundBrush")),
            _ => (Resource("SystemFillColorAttentionBrush"), Resource("TextFillColorPrimaryBrush")),
        };

        private static TextBlock Text(string text, Brush foreground, double size) => new()
        {
            Text = text,
            Foreground = foreground,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
    }
}
