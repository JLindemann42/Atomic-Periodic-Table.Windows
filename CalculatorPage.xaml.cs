using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Text;
using Atomic_PeriodicTable; // Assuming ElementData is in this namespace

namespace Atomic_WinUI
{

    public sealed partial class CalculatorPage : Page
    {
        public CalculatorPage()
        {
            this.InitializeComponent();
            CompoundInput.TextChanged += CompoundInput_TextChanged;
        }

        private async void CompoundInput_TextChanged(object sender, TextChangedEventArgs e)
        {

            // Format input visually
            CompoundFormatted.Inlines.Clear();
            FormatFormulaWithSubscripts(CompoundInput.Text, CompoundFormatted.Inlines);

            // Parse and calculate
            string input = CompoundInput.Text;
            var compounds = ParseCompounds(input);

            double totalMass = 0;
            var elementTotals = new Dictionary<string, double>();
            var elementNames = new Dictionary<string, string>();
            var elementWeights = new Dictionary<string, double>();

            foreach (var compound in compounds)
            {
                var elements = await ParseFormula(compound.Formula, compound.Multiplier, elementNames, elementWeights);
                foreach (var kvp in elements)
                {
                    if (!elementTotals.ContainsKey(kvp.Key))
                        elementTotals[kvp.Key] = 0;
                    elementTotals[kvp.Key] += kvp.Value;
                }
            }

            // Calculate total mass
            foreach (var kvp in elementTotals)
            {
                if (elementWeights.TryGetValue(kvp.Key, out double w))
                    totalMass += kvp.Value * w;
            }

            // Update UI
            MolarMassText.Text = $"{totalMass:0.#####} (g/mol)";
            ElementsPanel.Children.Clear();

            foreach (var kvp in elementTotals.OrderByDescending(k => k.Value * elementWeights.GetValueOrDefault(k.Key, 0)))
            {
                var symbol = kvp.Key;
                var name = elementNames.ContainsKey(symbol) ? elementNames[symbol] : symbol;
                var weight = elementWeights.ContainsKey(symbol) ? elementWeights[symbol] : 0;
                double mass = kvp.Value * weight;
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

        // --- Parsing logic ---

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

        // Recursively parses a chemical formula and returns a dictionary of element counts
        private async System.Threading.Tasks.Task<Dictionary<string, double>> ParseFormula(
            string formula,
            int multiplier,
            Dictionary<string, string> elementNames,
            Dictionary<string, double> elementWeights)
        {
            var result = new Dictionary<string, double>();
            int i = 0;
            while (i < formula.Length)
            {
                if (formula[i] == '(' || formula[i] == '[')
                {
                    // ... group parsing unchanged ...
                    i++; // You should implement group parsing if needed
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

                    if (!elementWeights.ContainsKey(symbol))
                    {
                        var element = ElementData.Elements.FirstOrDefault(e =>
                            string.Equals(e.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                        string atomicMass = null;
                        string elementName = symbol;

                        if (element != null)
                        {
                            elementName = element.OriginalName ?? symbol;
                            //atomicMass = element.GetType().GetProperty("element_atomicmass")?.GetValue(element) as string;
                        }

                        if (string.IsNullOrEmpty(atomicMass))
                        {
                            atomicMass = await GetElementAtomicMassFromJsonAsync(symbol);
                        }

                        elementNames[symbol] = elementName;
                        elementWeights[symbol] = ParseAtomicMass(atomicMass);
                    }

                    if (!result.ContainsKey(symbol)) result[symbol] = 0;
                    result[symbol] += count * multiplier;
                }
                else
                {
                    i++;
                }
            }
            return result;
        }
        // Fix for CS1061: 'Run' does not contain a definition for 'Margin'
        // The 'Run' class does not have a 'Margin' property. To achieve the desired effect, you can use an InlineUIContainer with a TextBlock inside it, as TextBlock supports the Margin property.

        private void FormatFormulaWithSubscripts(string formula, InlineCollection inlines)
        {
            int i = 0;
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
                    int start = i;
                    while (i < formula.Length && char.IsDigit(formula[i])) i++;
                    var textBlock = new TextBlock
                    {
                        Text = formula.Substring(start, i - start),
                        FontSize = 12, // Adjust font size for subscripts
                        Margin = new Thickness(0, 6, 0, 0) // Apply margin for visual adjustment
                    };
                    inlines.Add(new InlineUIContainer { Child = textBlock });
                }
                else
                {
                    inlines.Add(new Run { Text = formula[i].ToString() });
                    i++;
                }
            }
        }

        private double ParseAtomicMass(string massStr)
        {
            if (string.IsNullOrEmpty(massStr)) return 0;
            var match = Regex.Match(massStr, @"\d+([.,]\d+)?");
            if (match.Success)
            {
                if (double.TryParse(match.Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double mass))
                    return mass;
            }
            return 0;
        }
        private async System.Threading.Tasks.Task<string> GetElementAtomicMassFromJsonAsync(string symbol)
        {
            try
            {
                string filePath = System.IO.Path.Combine("Elements", symbol.ToLower() + ".json");
                if (File.Exists(filePath))
                {
                    using var stream = File.OpenRead(filePath);
                    using var doc = await JsonDocument.ParseAsync(stream);
                    //if (doc.RootElement.TryGetProperty("element_atomicmass", out var massProp))
                    //{
                      //  return massProp.GetString();
                    //}
                }
            }
            catch
            {
                // Handle or log error as needed
            }
            return null;
        }
    }
}