using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml;
using System.Text.RegularExpressions;
using System.Linq;
using System;
using Microsoft.UI.Text;
using Atomic_PeriodicTable;
using System.IO;
using System.Text.Json;

// Type aliases to avoid Dictionary name conflicts
using StringDoubleMap = System.Collections.Generic.Dictionary<string, double>;
using StringStringMap = System.Collections.Generic.Dictionary<string, string>;
using StringElementMap = System.Collections.Generic.Dictionary<string, (double count, double weight, string name)>;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using System.Collections.ObjectModel;
using Windows.Storage;
using System.Threading.Tasks;

namespace Atomic_WinUI
{
    public sealed partial class CalculatorPage : Page
    {
        private readonly StringDoubleMap elementWeights = new(StringComparer.OrdinalIgnoreCase);
        private readonly StringStringMap elementNames = new(StringComparer.OrdinalIgnoreCase);
        public ObservableCollection<FavoriteCompound> Favorites { get; } = new();
        private const string FavoritesFileName = "favorites.json";


        public CalculatorPage()
        {
            this.InitializeComponent();
            LoadElementData();
            CompoundInput.TextChanged += CompoundInput_TextChanged;
            this.SizeChanged += CalculatorPage_SizeChanged;

            CompoundInput.Visibility = Visibility.Visible;
            CompoundFormatted.Visibility = Visibility.Collapsed;

            FavoritesList.ContainerContentChanging += FavoritesList_ContainerContentChanging;

            SetCardsGridLayout(this.ActualWidth);

            LoadFavoritesAsync();
        }

        private void CalculatorPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SetCardsGridLayout(e.NewSize.Width);
        }

        private void FavoritesList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer == null || args.Item == null)
                return;

            var root = args.ItemContainer.ContentTemplateRoot as FrameworkElement;
            if (root == null)
                return;

            var formulaTextBlock = root.FindName("FormulaTextBlock") as TextBlock;
            if (formulaTextBlock == null)
                return;

            formulaTextBlock.Inlines.Clear();

            if (args.Item is FavoriteCompound fav && !string.IsNullOrEmpty(fav.Formula))
            {
                FormatFormulaWithSubscripts(fav.Formula, formulaTextBlock.Inlines);
            }
        }

        private async void LoadFavoritesAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(FavoritesFileName) as StorageFile;
                if (file != null)
                {
                    using var stream = await file.OpenStreamForReadAsync();
                    var loaded = await JsonSerializer.DeserializeAsync<List<FavoriteCompound>>(stream);
                    if (loaded != null)
                    {
                        Favorites.Clear();
                        foreach (var fav in loaded)
                            Favorites.Add(fav);
                    }
                }
            }
            catch { /* Handle or log as needed */ }
        }

        private async Task SaveFavoritesAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FavoritesFileName, CreationCollisionOption.ReplaceExisting);
                using var stream = await file.OpenStreamForWriteAsync();
                await JsonSerializer.SerializeAsync(stream, Favorites.ToList());
            }
            catch { /* Handle or log as needed */ }
        }

        private async void AddFavorite_Click(object sender, RoutedEventArgs e)
        {
            var formula = CompoundInput.Text?.Trim();
            if (string.IsNullOrEmpty(formula)) return;

            double molarMass = 0;
            var includedElements = new StringElementMap(StringComparer.OrdinalIgnoreCase);
            foreach (var (f, multiplier) in ParseCompounds(formula))
            {
                var elements = ParseFormulaWithGroups(f, multiplier);
                foreach (var (symbol, count) in elements)
                {
                    if (!includedElements.ContainsKey(symbol))
                        includedElements[symbol] = (0, elementWeights.GetValueOrDefault(symbol, 0), elementNames.GetValueOrDefault(symbol, symbol));
                    includedElements[symbol] = (includedElements[symbol].count + count, includedElements[symbol].weight, includedElements[symbol].name);
                }
            }
            foreach (var (symbol, (count, weight, _)) in includedElements)
                molarMass += count * weight;

            if (!Favorites.Any(f => f.Formula == formula && Math.Abs(f.MolarMass - molarMass) < 0.00001))
            {
                Favorites.Add(new FavoriteCompound { Formula = formula, MolarMass = molarMass });
                await SaveFavoritesAsync();
            }
        }

        private async void FavoriteRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is FavoriteCompound fav)
            {
                Favorites.Remove(fav);
                await SaveFavoritesAsync();
            }
        }

        private void FavoriteCopy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is FavoriteCompound fav)
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText($"{fav.MolarMass:0.#####} (u)");
                Clipboard.SetContent(dataPackage);
            }
        }

        private void SetCardsGridLayout(double width)
        {
            if (width < 900)
            {
                // Stack vertically
                if (CardsGrid.RowDefinitions.Count == 0)
                {
                    CardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    CardsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
                    CardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
                CalculatorCard.SetValue(Grid.RowProperty, 0);
                CalculatorCard.SetValue(Grid.ColumnProperty, 0);
                FavoritesCard.SetValue(Grid.RowProperty, 2);
                FavoritesCard.SetValue(Grid.ColumnProperty, 0);

                // Hide columns except the first
                for (int i = 0; i < CardsGrid.ColumnDefinitions.Count; i++)
                    CardsGrid.ColumnDefinitions[i].Width = (i == 0) ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            }
            else
            {
                // Side by side
                if (CardsGrid.RowDefinitions.Count > 0)
                {
                    CardsGrid.RowDefinitions.Clear();
                }
                CalculatorCard.SetValue(Grid.RowProperty, 0);
                CalculatorCard.SetValue(Grid.ColumnProperty, 0);
                FavoritesCard.SetValue(Grid.RowProperty, 0);
                FavoritesCard.SetValue(Grid.ColumnProperty, 2);

                // Restore columns
                CardsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                CardsGrid.ColumnDefinitions[1].Width = new GridLength(24);
                CardsGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }


        // When user taps the formatted text, switch to edit mode
        private void CompoundFormatted_Tapped(object sender, TappedRoutedEventArgs e)
        {
            CompoundFormatted.Visibility = Visibility.Collapsed;
            CompoundInput.Visibility = Visibility.Visible;
            CompoundInput.Focus(FocusState.Programmatic);
            CompoundInput.Select(CompoundInput.Text?.Length ?? 0, 0);
        }

        // When user leaves the TextBox, switch back to formatted view
        private void CompoundInput_LostFocus(object sender, RoutedEventArgs e)
        {
            CompoundInput.Visibility = Visibility.Collapsed;
            CompoundFormatted.Visibility = Visibility.Visible;
        }

        // Optional: When user presses Enter, exit edit mode
        private void CompoundInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CompoundFormatted.Visibility = Visibility.Visible;
                CompoundInput.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadElementData()
        {
            elementWeights.Clear();
            elementNames.Clear();

            string elementsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Elements");
            if (!Directory.Exists(elementsDir))
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Elements directory not found: {elementsDir}");
                return;
            }

            foreach (var file in Directory.GetFiles(elementsDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // If your JSON is an array, get the first object
                    var elementObj = root.ValueKind == JsonValueKind.Array ? root[0] : root;

                    string symbol = elementObj.TryGetProperty("short", out var shortProp) ? shortProp.GetString() : null;
                    string name = elementObj.TryGetProperty("element", out var nameProp) ? nameProp.GetString() : symbol;
                    string massRaw = elementObj.TryGetProperty("element_atomicmass", out var massProp) ? massProp.GetString() : null;

                    // Remove " (u)" and any non-numeric characters
                    string massClean = massRaw?.Split(' ').FirstOrDefault();
                    double mass = 0;
                    double.TryParse(massClean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mass);

                    System.Diagnostics.Debug.WriteLine($"[DEBUG] {symbol}: \"{massRaw}\" -> {mass}");

                    if (!string.IsNullOrWhiteSpace(symbol))
                    {
                        elementWeights[symbol] = mass;
                        elementNames[symbol] = name;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Error reading {file}: {ex.Message}");
                }
            }
        }

        private void CompoundInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Format input visually
            CompoundFormatted.Inlines.Clear();
            FormatFormulaWithSubscripts(CompoundInput.Text, CompoundFormatted.Inlines);

            string input = CompoundInput.Text?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                MolarMassText.Text = "0.0 (g/mol)";
                ElementsPanel.Children.Clear();
                return;
            }

            // Parse and calculate
            var includedElements = new StringElementMap(StringComparer.OrdinalIgnoreCase);
            double totalMass = 0;

            foreach (var (formula, multiplier) in ParseCompounds(input))
            {
                var elements = ParseFormulaWithGroups(formula, multiplier);
                foreach (var (symbol, count) in elements)
                {
                    if (!includedElements.ContainsKey(symbol))
                        includedElements[symbol] = (0, elementWeights.GetValueOrDefault(symbol, 0), elementNames.GetValueOrDefault(symbol, symbol));
                    includedElements[symbol] = (includedElements[symbol].count + count, includedElements[symbol].weight, includedElements[symbol].name);
                }
            }

            // Calculate total mass
            foreach (var (symbol, (count, weight, _)) in includedElements)
                totalMass += count * weight;

            // Update UI
            MolarMassText.Text = $"{totalMass:0.#####} (g/mol)";
            ElementsPanel.Children.Clear();

            foreach (var (symbol, (count, weight, name)) in includedElements.OrderByDescending(x => x.Value.count * x.Value.weight))
            {
                double mass = count * weight;
                double percent = totalMass > 0 ? (mass / totalMass) * 100 : 0;

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(16),
                    Margin = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(12)
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var ellipse = new Ellipse
                {
                    Width = 36,
                    Height = 36,
                    Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var symbolText = new TextBlock
                {
                    Text = symbol,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    FontWeight = FontWeights.Bold,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var ellipseGrid = new Grid();
                ellipseGrid.Children.Add(ellipse);
                ellipseGrid.Children.Add(symbolText);

                var stack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
                stack.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 16 });
                stack.Children.Add(new TextBlock { Text = $"{weight:0.###}", FontSize = 14 });

                var percentText = new TextBlock
                {
                    Text = $"{percent:0.##}%",
                    FontWeight = FontWeights.Bold,
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center
                };

                grid.Children.Add(ellipseGrid);
                grid.Children.Add(stack);
                grid.Children.Add(percentText);
                Grid.SetColumn(ellipseGrid, 0);
                Grid.SetColumn(stack, 1);
                Grid.SetColumn(percentText, 2);

                border.Child = grid;
                ElementsPanel.Children.Add(border);
            }
        }

        // Handles + and leading numbers (e.g., 2H2O + NaCl)
        private List<(string Formula, int Multiplier)> ParseCompounds(string input)
        {
            var result = new List<(string, int)>();
            foreach (var part in input.Split('+'))
            {
                var trimmed = part.Trim();
                var match = Regex.Match(trimmed, @"^(\d+)?(.*)$");
                int multiplier = 1;
                string formula = trimmed;
                if (match.Success)
                {
                    if (!string.IsNullOrEmpty(match.Groups[1].Value))
                        multiplier = int.Parse(match.Groups[1].Value);
                    formula = match.Groups[2].Value;
                }
                if (!string.IsNullOrWhiteSpace(formula))
                    result.Add((formula, multiplier));
            }
            return result;
        }

        // Recursively parses a chemical formula with groups and returns a dictionary of element counts
        private StringDoubleMap ParseFormulaWithGroups(string formula, int multiplier)
        {
            var result = new StringDoubleMap(StringComparer.OrdinalIgnoreCase);

            void AddElement(string symbol, double count)
            {
                if (!result.ContainsKey(symbol))
                    result[symbol] = 0;
                result[symbol] += count;
            }

            int i = 0;
            while (i < formula.Length)
            {
                if (formula[i] == '(' || formula[i] == '[')
                {
                    char open = formula[i];
                    char close = open == '(' ? ')' : ']';
                    int start = ++i, depth = 1;
                    while (i < formula.Length && depth > 0)
                    {
                        if (formula[i] == open) depth++;
                        else if (formula[i] == close) depth--;
                        i++;
                    }
                    int end = i - 1;
                    string group = formula.Substring(start, end - start);
                    int groupMultiplier = 1;
                    int j = i;
                    while (j < formula.Length && char.IsDigit(formula[j])) j++;
                    if (j > i)
                        groupMultiplier = int.Parse(formula.Substring(i, j - i));
                    i = j;
                    var groupElements = ParseFormulaWithGroups(group, multiplier * groupMultiplier);
                    foreach (var kvp in groupElements)
                        AddElement(kvp.Key, kvp.Value);
                }
                else if (char.IsUpper(formula[i]))
                {
                    int start = i;
                    i++;
                    while (i < formula.Length && char.IsLower(formula[i])) i++;
                    string symbol = formula.Substring(start, i - start);
                    int count = 1;
                    int j = i;
                    while (j < formula.Length && char.IsDigit(formula[j])) j++;
                    if (j > i)
                        count = int.Parse(formula.Substring(i, j - i));
                    i = j;
                    AddElement(symbol, count * multiplier);
                }
                else
                {
                    i++;
                }
            }
            return result;
        }

        private void FormatFormulaWithSubscripts(string formula, InlineCollection inlines)
        {
            int i = 0;
            // Handle leading number (multiplier)
            if (i < formula.Length && char.IsDigit(formula[i]))
            {
                int start = i;
                while (i < formula.Length && char.IsDigit(formula[i])) i++;
                inlines.Add(new Run { Text = formula.Substring(start, i - start) });
            }

            while (i < formula.Length)
            {
                if (char.IsLetter(formula[i]))
                {
                    int start = i;
                    i++;
                    while (i < formula.Length && char.IsLower(formula[i])) i++;
                    inlines.Add(new Run { Text = formula.Substring(start, i - start) });
                }
                else if (char.IsDigit(formula[i]))
                {
                    // Only subscript if previous char is a letter or ')' or ']'
                    if (i > 0 && (char.IsLetter(formula[i - 1]) || formula[i - 1] == ')' || formula[i - 1] == ']'))
                    {
                        int start = i;
                        while (i < formula.Length && char.IsDigit(formula[i])) i++;
                        string digits = formula.Substring(start, i - start);
                        string subscript = string.Concat(digits.Select(SubscriptChar));
                        inlines.Add(new Run { Text = subscript });
                    }
                    else
                    {
                        // Not a subscript (shouldn't happen except for leading number, already handled)
                        int start = i;
                        while (i < formula.Length && char.IsDigit(formula[i])) i++;
                        inlines.Add(new Run { Text = formula.Substring(start, i - start) });
                    }
                }
                else
                {
                    inlines.Add(new Run { Text = formula[i].ToString() });
                    i++;
                }
            }
        }

        // Helper to convert digit to subscript unicode
        private static char SubscriptChar(char c)
        {
            return c switch
            {
                '0' => '₀',
                '1' => '₁',
                '2' => '₂',
                '3' => '₃',
                '4' => '₄',
                '5' => '₅',
                '6' => '₆',
                '7' => '₇',
                '8' => '₈',
                '9' => '₉',
                _ => c
            };
        }
    }
    public class FavoriteCompound
    {
        public string Formula { get; set; }
        public double MolarMass { get; set; }
        public string MolarMassDisplay => $"{MolarMass:0.#####} (g/mol)";

    }
}
