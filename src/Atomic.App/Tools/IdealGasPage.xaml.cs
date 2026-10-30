using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Atomic.App.Services;
using Atomic.Core.Sync;
using Atomic.Core.Ai.Data;
using Atomic.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Atomic.App.Tools
{
    /// <summary>A saved ideal-gas calculation.</summary>
    public sealed class SavedIdealGas
    {
        public string Summary { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;

        /// <summary>Epoch milliseconds of the save, which decides it against a removal on another device.</summary>
        public long SavedAt { get; set; }
    }

    /// <summary>
    /// Solves PV = nRT for whichever variable is selected.
    ///
    /// <para>Unit conversion goes through <see cref="UnitConverter"/> rather than a second factor
    /// table. The engine's converter already knows atm, kPa, bar, mmHg, litres and the three
    /// temperature scales, and two tables of the same constants drift.</para>
    /// </summary>
    public sealed partial class IdealGasPage : Page
    {
        /// <summary>
        /// The gas constant in L·atm/(mol·K), matching Android's R_ATM.
        ///
        /// <para>Everything is converted to atm, litres and kelvin before this is applied, so only
        /// the one constant is needed. Android also declares an unused R_JOULE; it is not carried
        /// over.</para>
        /// </summary>
        private const double R = 0.08206;

        private enum Solve { Pressure, Volume, Moles, Temperature }

        private static readonly string[] PressureUnits = ["atm", "Pa", "kPa", "bar", "mmHg"];
        private static readonly string[] VolumeUnits = ["L", "mL", "m³", "cm³"];
        private static readonly string[] TemperatureUnits = ["K", "°C", "°F"];

        /// <summary>
        /// mmHg is not in the engine's unit table - it is a pressure unit the element data never
        /// uses - so its factor lives here. 1 atm = 760 mmHg exactly, by definition.
        /// </summary>
        private const double MmHgPerAtm = 760.0;

        private readonly IStringProvider _strings = ResourceStringProvider.For(AppLanguage.Current);
        private readonly ObservableCollection<SavedIdealGas> _favorites = [];
        private bool _initialising = true;

        public IdealGasPage()
        {
            InitializeComponent();
            FavoritesList.ItemsSource = _favorites;

            ApplyStrings();
            BuildCombos();
            ApplyProGate();

            _initialising = false;
            ApplySolveFor();

            Loaded += async (_, _) =>
            {
                FavoritesStore.Changed += Favorites_Changed;
                await LoadFavoritesAsync();
                _ = FavoritesSync.SyncAsync();
            };
            Unloaded += (_, _) => FavoritesStore.Changed -= Favorites_Changed;
        }

        private void ApplyStrings()
        {
            CardTitle.Text = _strings.Get(S.ideal_gas_card_title);
            SolveForLabel.Text = _strings.Get(S.ideal_gas_calculate_field);
            PressureLabel.Text = _strings.Get(S.ideal_gas_pressure);
            VolumeLabel.Text = _strings.Get(S.ideal_gas_volume);
            MolesLabel.Text = _strings.Get(S.ideal_gas_moles);
            TemperatureLabel.Text = _strings.Get(S.ideal_gas_temperature);
            ResultLabel.Text = _strings.Get(S.ideal_gas_result);
            NoteText.Text = _strings.Get(S.ideal_gas_text);
            FavoritesTitle.Text = _strings.Get(S.favorites_colon);
            FavoritesEmpty.Text = _strings.Get(S.ideal_gas_favorites_text);
            ProGateText.Text = _strings.Get(S.requires_pro);
            ProGateButton.Content = _strings.Get(S.get_pro_title);

            PressureBox.PlaceholderText = _strings.Get(S.ideal_gas_pressure_hint);
            VolumeBox.PlaceholderText = _strings.Get(S.ideal_gas_volume_hint);
            MolesBox.PlaceholderText = _strings.Get(S.ideal_gas_moles_hint);
            TemperatureBox.PlaceholderText = _strings.Get(S.ideal_gas_temperature_hint);
        }

        private void BuildCombos()
        {
            foreach (var (label, tag) in new[]
            {
                (_strings.Get(S.ideal_gas_pressure), Solve.Pressure),
                (_strings.Get(S.ideal_gas_volume), Solve.Volume),
                (_strings.Get(S.ideal_gas_moles), Solve.Moles),
                (_strings.Get(S.ideal_gas_temperature), Solve.Temperature),
            })
            {
                SolveForBox.Items.Add(new ComboBoxItem { Content = label.TrimEnd(':'), Tag = tag });
            }
            SolveForBox.SelectedIndex = 0;

            Fill(PressureUnit, PressureUnits);
            Fill(VolumeUnit, VolumeUnits);
            Fill(TemperatureUnit, TemperatureUnits);

            static void Fill(ComboBox box, string[] units)
            {
                foreach (var u in units) box.Items.Add(new ComboBoxItem { Content = u, Tag = u });
                box.SelectedIndex = 0;
            }
        }

        private void ApplyProGate()
        {
            var hasPro = SettingsStore.HasAnyPro;
            ProGate.Visibility = hasPro ? Visibility.Collapsed : Visibility.Visible;
            FavoritesList.Visibility = hasPro ? Visibility.Visible : Visibility.Collapsed;
            SaveButton.Visibility = hasPro ? Visibility.Visible : Visibility.Collapsed;
        }

        // -- solving -------------------------------------------------------------------------

        private Solve Selected =>
            (SolveForBox.SelectedItem as ComboBoxItem)?.Tag is Solve s ? s : Solve.Pressure;

        private void SolveFor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initialising) return;
            ApplySolveFor();
        }

        /// <summary>
        /// Disables, clears and dims the field being solved for.
        ///
        /// <para>Leaving it editable invites the user to type a value that is about to be
        /// overwritten, and leaving a stale number in it is worse - it reads as an input.</para>
        /// </summary>
        private void ApplySolveFor()
        {
            SetTarget(PressureBox, Selected == Solve.Pressure);
            SetTarget(VolumeBox, Selected == Solve.Volume);
            SetTarget(MolesBox, Selected == Solve.Moles);
            SetTarget(TemperatureBox, Selected == Solve.Temperature);
            Recalculate();

            void SetTarget(TextBox box, bool isTarget)
            {
                box.IsEnabled = !isTarget;
                box.Opacity = isTarget ? 0.5 : 1.0;
                if (isTarget)
                {
                    box.Text = string.Empty;
                    box.PlaceholderText = _strings.Get(S.ideal_calculated);
                }
            }
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_initialising) Recalculate();
        }

        private void Unit_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialising) Recalculate();
        }

        /// <summary>
        /// Count one calculation for a visit that produced a result.
        /// </summary>
        /// <remarks>
        /// <para>Not on every solve. The page has no calculate button - it re-solves on each
        /// keystroke - so counting there would score a dozen calculations for one answer. Android
        /// counts a button press; a visit that ended with a real number is the closest this page
        /// has to that, and it cannot inflate.</para>
        /// </remarks>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            var result = ResultText.Text;
            if (!string.IsNullOrEmpty(result) && !result.StartsWith("---", StringComparison.Ordinal))
                AchievementStore.Record(Atomic.Core.Progression.AchievementTrigger.Calculation);
        }

        private void Recalculate()
        {
            var target = Selected;
            var unit = TargetUnit(target);

            var p = target == Solve.Pressure ? null : PressureAtm();
            var v = target == Solve.Volume ? null : VolumeLitres();
            var n = target == Solve.Moles ? null : Number(MolesBox);
            var t = target == Solve.Temperature ? null : TemperatureKelvin();

            double? answer = target switch
            {
                Solve.Pressure when v > 0 => n * R * t / v,
                Solve.Volume when p > 0 => n * R * t / p,
                Solve.Moles when t > 0 => p * v / (R * t),
                Solve.Temperature when n > 0 => p * v / (R * n),
                _ => null,
            };

            if (answer is not { } value || !double.IsFinite(value) || value < 0)
            {
                // "--- unit" rather than a blank: the field is showing the shape of the answer it
                // will give, which reads as waiting rather than broken.
                ResultText.Text = "--- " + unit;
                return;
            }

            var converted = target switch
            {
                Solve.Pressure => FromAtm(value, unit),
                Solve.Volume => UnitConverter.Convert(value, "L", unit) ?? value,
                Solve.Temperature => UnitConverter.Convert(value, "K", unit) ?? value,
                _ => value,
            };

            ResultText.Text = converted.ToString("0.0000", CultureInfo.InvariantCulture) + " " + unit;
        }

        private string TargetUnit(Solve target) => target switch
        {
            Solve.Pressure => Tag(PressureUnit),
            Solve.Volume => Tag(VolumeUnit),
            Solve.Temperature => Tag(TemperatureUnit),
            _ => "mol",
        };

        private static string Tag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        private double? PressureAtm()
        {
            if (Number(PressureBox) is not { } value) return null;
            var unit = Tag(PressureUnit);
            // mmHg is defined against the atmosphere, not against a pascal, so it is exact here.
            if (unit == "mmHg") return value / MmHgPerAtm;
            if (unit == "atm") return value;
            // Everything else converts through pascals: 1 atm = 101325 Pa exactly.
            var pascals = UnitConverter.Convert(value, unit, "Pa");
            return pascals is { } pa ? pa / 101325.0 : null;
        }

        private static double FromAtm(double atm, string unit) => unit switch
        {
            "atm" => atm,
            "mmHg" => atm * MmHgPerAtm,
            _ => UnitConverter.Convert(atm * 101325.0, "Pa", unit) ?? atm,
        };

        private double? VolumeLitres()
        {
            if (Number(VolumeBox) is not { } value) return null;
            return UnitConverter.Convert(value, Tag(VolumeUnit), "L");
        }

        private double? TemperatureKelvin()
        {
            if (Number(TemperatureBox) is not { } value) return null;
            return UnitConverter.Convert(value, Tag(TemperatureUnit), "K");
        }

        /// <summary>
        /// Reads a field.
        ///
        /// <para>Invariant parsing, then a comma-decimal retry. A Swedish or German user types
        /// "1,5" and means one and a half; refusing it would look like the tool is broken, and
        /// parsing it with the ambient culture would make the same keystrokes mean different
        /// numbers on different machines.</para>
        /// </summary>
        private static double? Number(TextBox box)
        {
            var text = box.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return null;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;

            if (text.Contains(',', StringComparison.Ordinal) &&
                double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;

            return null;
        }

        // -- favourites ----------------------------------------------------------------------

        private async System.Threading.Tasks.Task LoadFavoritesAsync() =>
            ShowFavorites(await FavoritesStore.LoadIdealGasAsync());

        private void ShowFavorites(IEnumerable<SavedIdealGas> saved)
        {
            _favorites.Clear();
            foreach (var item in saved)
                _favorites.Add(item);
            UpdateFavoritesEmptyState();
        }

        /// <summary>A sync brought changes from another device. Raised on any thread.</summary>
        private void Favorites_Changed(object? sender, FavoriteKind kind)
        {
            if (kind == FavoriteKind.IdealGas) DispatcherQueue.TryEnqueue(async () => await LoadFavoritesAsync());
        }

        private void UpdateFavoritesEmptyState() =>
            FavoritesEmpty.Visibility =
                SettingsStore.HasAnyPro && _favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private async void SaveFavorite_Click(object sender, RoutedEventArgs e)
        {
            var result = ResultText.Text;
            if (string.IsNullOrEmpty(result) || result.StartsWith("---", StringComparison.Ordinal)) return;

            var parts = new List<string>();
            Add(PressureBox, Tag(PressureUnit), "P");
            Add(VolumeBox, Tag(VolumeUnit), "V");
            Add(MolesBox, "mol", "n");
            Add(TemperatureBox, Tag(TemperatureUnit), "T");

            var summary = string.Join(", ", parts);
            if (summary.Length == 0) return;

            ShowFavorites(await FavoritesStore.SaveIdealGasAsync(new SavedIdealGas { Summary = summary, Result = result }));

            // "P=1 atm, V=22.4 L, T=273 K", exactly as Android writes it, because the summary is what
            // identifies a saved calculation when it syncs: the same inputs saved on both platforms
            // must come out as the same text to be the same favourite.
            void Add(TextBox box, string unit, string symbol)
            {
                if (box.IsEnabled && !string.IsNullOrWhiteSpace(box.Text))
                    parts.Add($"{symbol}={box.Text.Trim()} {unit}");
            }
        }

        private async void RemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string summary) return;

            ShowFavorites(await FavoritesStore.RemoveIdealGasAsync(summary));
        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e) =>
            MainWindow.Instance?.Navigation?.Navigate(Navigation.AppRoutes.Pro);
    }
}
