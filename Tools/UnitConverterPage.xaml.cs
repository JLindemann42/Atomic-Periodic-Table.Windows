using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.ApplicationModel.DataTransfer;

namespace Atomic_PeriodicTable.Tools
{
    public sealed partial class UnitConverterPage : Page
    {
        // Categories and their units/factors
        private readonly Dictionary<string, List<UnitDefinition>> unitCategories = new()
        {
            ["Length"] = new()
            {
                new("Meter", 1.0), new("Kilometer", 1000.0), new("Centimeter", 0.01), new("Millimeter", 0.001),
                new("Inch", 0.0254), new("Foot", 0.3048), new("Yard", 0.9144), new("Mile", 1609.344)
            },
            ["Mass"] = new()
            {
                new("Kilogram", 1.0), new("Gram", 0.001), new("Milligram", 0.000001), new("Pound", 0.45359237), new("Ounce", 0.0283495231)
            },
            ["Volume"] = new()
            {
                new("Liter", 1.0), new("Milliliter", 0.001), new("Cubic meter", 1000.0), new("Gallon", 3.78541), new("Pint", 0.473176)
            },
            ["Area"] = new()
            {
                new("Square meter", 1.0), new("Square kilometer", 1_000_000.0), new("Square centimeter", 0.0001), new("Square mile", 2_589_988.11), new("Acre", 4046.85642)
            },
            ["Velocity"] = new()
            {
                new("Meter/second", 1.0), new("Kilometer/hour", 0.277778), new("Mile/hour", 0.44704), new("Foot/second", 0.3048)
            },
            ["Energy"] = new()
            {
                new("Joule", 1.0), new("Kilojoule", 1000.0), new("Calorie", 4.184), new("Kilocalorie", 4184.0), new("Watt hour", 3600.0)
            },
            ["Frequency"] = new()
            {
                new("Hertz", 1.0), new("Kilohertz", 1000.0), new("Megahertz", 1_000_000.0), new("Gigahertz", 1_000_000_000.0)
            },
            ["Temperature"] = new()
            {
                new("Celsius", 0), new("Fahrenheit", 0), new("Kelvin", 0)
            },
            ["Time"] = new()
            {
                new("Second", 1.0), new("Millisecond", 0.001), new("Minute", 60.0), new("Hour", 3600.0), new("Day", 86400.0)
            },
            ["Force"] = new()
            {
                new("Newton", 1.0), new("Kilonewton", 1000.0), new("Dyne", 0.00001), new("Pound-force", 4.4482216), new("Kilogram-force", 9.80665)
            },
            ["Power"] = new()
            {
                new("Watt", 1.0), new("Kilowatt", 1000.0), new("Megawatt", 1_000_000.0), new("Horsepower", 745.699872)
            },
            ["Voltage"] = new()
            {
                new("Volt", 1.0), new("Millivolt", 0.001), new("Kilovolt", 1000.0)
            },
            ["Resistance"] = new()
            {
                new("Ohm", 1.0), new("Milliohm", 0.001), new("Kiloohm", 1000.0), new("Megaohm", 1_000_000.0)
            },
            ["Pressure"] = new()
            {
                new("Pascal", 1.0), new("Kilopascal", 1000.0), new("Bar", 100_000.0), new("Atmosphere", 101_325.0), new("PSI", 6894.757)
            }
        };

        public ObservableCollection<string> Categories { get; } = new();
        public ObservableCollection<string> Units { get; } = new();
        public ObservableCollection<UnitConversionFavorite> Favorites { get; } = new();
        private const string FavoritesFileName = "unit_favorites.json";

        public UnitConverterPage()
        {
            this.InitializeComponent();

            foreach (var cat in unitCategories.Keys)
                Categories.Add(cat);

            CategoryComboBox.ItemsSource = Categories;
            CategoryComboBox.SelectedIndex = 0; // Default to first category

            this.SizeChanged += UnitConverterPage_SizeChanged;
            SetCardsGridLayout(this.ActualWidth);

            _ = LoadFavoritesAsync();

            // Initial state for favorite button
            UpdateFavoriteButtonState();
        }

        private void UnitConverterPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SetCardsGridLayout(e.NewSize.Width);
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
                UnitConverterCard.SetValue(Grid.RowProperty, 0);
                UnitConverterCard.SetValue(Grid.ColumnProperty, 0);
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
                UnitConverterCard.SetValue(Grid.RowProperty, 0);
                UnitConverterCard.SetValue(Grid.ColumnProperty, 0);
                FavoritesCard.SetValue(Grid.RowProperty, 0);
                FavoritesCard.SetValue(Grid.ColumnProperty, 2);

                // Restore columns
                CardsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                CardsGrid.ColumnDefinitions[1].Width = new GridLength(24);
                CardsGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryComboBox.SelectedItem is string category)
            {
                Units.Clear();
                foreach (var unit in unitCategories[category])
                    Units.Add(unit.Name);

                FromUnitComboBox.ItemsSource = Units;
                ToUnitComboBox.ItemsSource = Units;

                if (Units.Count > 0)
                {
                    FromUnitComboBox.SelectedIndex = 0;
                    ToUnitComboBox.SelectedIndex = 1;
                }
            }
            ConvertUnits();
            UpdateFavoriteButtonState();
        }

        private void FromUnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ConvertUnits();
            UpdateFavoriteButtonState();
        }

        private void ToUnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ConvertUnits();
            UpdateFavoriteButtonState();
        }

        private void InputValueBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ConvertUnits();
            UpdateFavoriteButtonState();
        }

        private void ConvertUnits()
        {
            if (CategoryComboBox.SelectedItem is not string category ||
                FromUnitComboBox.SelectedItem is not string from ||
                ToUnitComboBox.SelectedItem is not string to ||
                !double.TryParse(InputValueBox.Text, out double value))
            {
                OutputValueBox.Text = "";
                FormulaTextBlock.Text = "";
                return;
            }

            double result = 0;
            string formula = "";

            if (category == "Temperature")
            {
                result = ConvertTemperature(from, to, value, out formula);
            }
            else
            {
                var fromDef = unitCategories[category].Find(u => u.Name == from);
                var toDef = unitCategories[category].Find(u => u.Name == to);
                if (fromDef != null && toDef != null)
                {
                    result = value * fromDef.Factor / toDef.Factor;
                    formula = $"Divide {category.ToLower()}-value with: {fromDef.Factor / toDef.Factor:G}";
                }
            }

            OutputValueBox.Text = result.ToString("G");
            FormulaTextBlock.Text = formula;
        }

        private double ConvertTemperature(string from, string to, double value, out string formula)
        {
            double celsius = from switch
            {
                "Celsius" => value,
                "Fahrenheit" => (value - 32) * 5 / 9,
                "Kelvin" => value - 273.15,
                _ => value
            };

            double result = to switch
            {
                "Celsius" => celsius,
                "Fahrenheit" => celsius * 9 / 5 + 32,
                "Kelvin" => celsius + 273.15,
                _ => celsius
            };

            formula = from switch
            {
                "Celsius" when to == "Fahrenheit" => "Multiply with 9/5 and add 32",
                "Celsius" when to == "Kelvin" => "Add 273.15",
                "Fahrenheit" when to == "Celsius" => "Subtract 32, divide with 5/9",
                "Fahrenheit" when to == "Kelvin" => "Subtract 32, multiply with 5/9, add 273.15",
                "Kelvin" when to == "Celsius" => "subtract 273.15",
                "Kelvin" when to == "Fahrenheit" => "subtract 273.15, multiply with 9/5, add 32",
                _ => "no conversion"
            };

            return result;
        }

        private async void AddFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (IsCurrentConversionFavorite())
                return; // Already a favorite, do nothing
            if (CategoryComboBox.SelectedItem is string category &&
                FromUnitComboBox.SelectedItem is string from &&
                ToUnitComboBox.SelectedItem is string to &&
                double.TryParse(InputValueBox.Text, out double value))
            {
                double converted = 0;
                if (category == "Temperature")
                {
                    string _; // formula not needed here
                    converted = ConvertTemperature(from, to, value, out _);
                }
                else
                {
                    var fromDef = unitCategories[category].Find(u => u.Name == from);
                    var toDef = unitCategories[category].Find(u => u.Name == to);
                    if (fromDef != null && toDef != null)
                        converted = value * fromDef.Factor / toDef.Factor;
                }

                var favorite = new UnitConversionFavorite
                {
                    Category = category,
                    FromUnit = from,
                    ToUnit = to,
                    InputValue = value,
                    ConvertedValue = converted // <-- Store the converted value
                };
                Favorites.Add(favorite);
                await SaveFavoritesAsync();
                UpdateFavoriteButtonState();
            }
        }

        private async Task LoadFavoritesAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(FavoritesFileName) as StorageFile;
                if (file != null)
                {
                    using var stream = await file.OpenStreamForReadAsync();
                    var loaded = await JsonSerializer.DeserializeAsync<List<UnitConversionFavorite>>(stream);
                    if (loaded != null)
                    {
                        Favorites.Clear();
                        foreach (var fav in loaded)
                            Favorites.Add(fav);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Loading favorites: {ex}");
            }
            UpdateFavoriteButtonState();
        }

        private async Task SaveFavoritesAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FavoritesFileName, CreationCollisionOption.ReplaceExisting);
                using var stream = await file.OpenStreamForWriteAsync();
                await JsonSerializer.SerializeAsync(stream, Favorites);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Saving favorites: {ex}");
            }
        }

        public class UnitDefinition
        {
            public string Name { get; }
            public double Factor { get; }
            public UnitDefinition(string name, double factor)
            {
                Name = name;
                Factor = factor;
            }
        }

        private async void FavoriteRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: UnitConversionFavorite fav })
            {
                Favorites.Remove(fav);
                await SaveFavoritesAsync();
                UpdateFavoriteButtonState();
            }
        }

        private void FavoriteCopy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: UnitConversionFavorite fav })
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText($"{fav.InputValue} {fav.FromUnit} → {fav.ConvertedValue} {fav.ToUnit} ({fav.Category})");
                Clipboard.SetContent(dataPackage);
            }
        }

        private bool IsCurrentConversionFavorite()
        {
            if (CategoryComboBox.SelectedItem is string category &&
                FromUnitComboBox.SelectedItem is string from &&
                ToUnitComboBox.SelectedItem is string to &&
                double.TryParse(InputValueBox.Text, out double value))
            {
                foreach (var fav in Favorites)
                {
                    if (fav.Category == category &&
                        fav.FromUnit == from &&
                        fav.ToUnit == to &&
                        fav.InputValue == value)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void UpdateFavoriteButtonState()
        {
            if (AddFavoriteButton != null)
                AddFavoriteButton.IsEnabled = !IsCurrentConversionFavorite();
        }
    }

    public class UnitConversionFavorite
    {
        public string Category { get; set; }
        public string FromUnit { get; set; }
        public string ToUnit { get; set; }
        public double InputValue { get; set; }
        public double ConvertedValue { get; set; } // <-- Add this
    }
}
