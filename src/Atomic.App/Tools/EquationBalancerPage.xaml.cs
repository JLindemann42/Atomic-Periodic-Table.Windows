using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Atomic.App.Services;
using Atomic.Core.Ai.Data;
using Atomic.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Atomic.App.Tools
{
    /// <summary>One row of the atom-check proof, already rendered.</summary>
    public sealed class TallyRow
    {
        /// <summary>The element, shown in a circle as the calculator lists its elements.</summary>
        public string Symbol { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;
    }

    /// <summary>A saved equation: what the user typed, and what it balanced to.</summary>
    public sealed class SavedEquation
    {
        public string Input { get; set; } = string.Empty;
        public string Balanced { get; set; } = string.Empty;
    }

    /// <summary>
    /// Balances chemical equations as the user types.
    ///
    /// <para>The solver is <see cref="EquationBalancer"/> in Atomic.Core, shared with the
    /// assistant, so "balance Fe + O2 -&gt; Fe2O3" gives the same answer in chat and here. This
    /// page is presentation only.</para>
    ///
    /// <para>Balancing is free. Only keeping the result is behind PRO, matching Android.</para>
    /// </summary>
    public sealed partial class EquationBalancerPage : Page
    {
        /// <summary>
        /// Re-balance delay after a keystroke.
        ///
        /// <para>Android re-balances on every character through a TextWatcher. Here a short debounce
        /// avoids running Gaussian elimination on every intermediate string while somebody types a
        /// long equation - and, more visibly, stops the result flickering through a parse failure
        /// on its way to being valid.</para>
        /// </summary>
        private static readonly TimeSpan RebalanceDelay = TimeSpan.FromMilliseconds(150);

        private static readonly string[] Examples =
        [
            "Fe + O2 -> Fe2O3",
            "C3H8 + O2 -> CO2 + H2O",
            "KMnO4 + HCl -> KCl + MnCl2 + H2O + Cl2",
        ];

        private readonly IStringProvider _strings = ResourceStringProvider.For(AppLanguage.Current);
        private readonly ObservableCollection<TallyRow> _tally = [];
        private readonly ObservableCollection<SavedEquation> _favorites = [];
        private readonly DispatcherTimer _debounce = new();

        private HashSet<string>? _symbols;
        private string? _lastBalanced;

        public EquationBalancerPage()
        {
            InitializeComponent();

            TallyItems.ItemsSource = _tally;
            FavoritesList.ItemsSource = _favorites;

            _debounce.Interval = RebalanceDelay;
            _debounce.Tick += (_, _) => { _debounce.Stop(); Rebalance(); };

            ApplyStrings();
            BuildExamples();
            ApplyProGate();

            Loaded += async (_, _) => await LoadFavoritesAsync();
        }

        private void ApplyStrings()
        {
            CardTitle.Text = _strings.Get(S.balancer_tool_card_title);
            EquationBox.PlaceholderText = _strings.Get(S.balancer_tool_hint);
            ResultText.Text = _strings.Get(S.balancer_tool_prompt);
            TallyHeader.Text = _strings.Get(S.ai_equation_check_header);
            ExamplesHeader.Text = _strings.Get(S.balancer_tool_examples);
            NoteText.Text = _strings.Get(S.balancer_tool_note);
            FavoritesTitle.Text = _strings.Get(S.favorites_colon);
            FavoritesEmpty.Text = _strings.Get(S.balancer_text);
            ProGateText.Text = _strings.Get(S.requires_pro);
            ProGateButton.Content = _strings.Get(S.get_pro_title);
        }

        /// <summary>
        /// The three examples, as chips.
        ///
        /// <para>Plain buttons in a wrap panel rather than a fixed row: the permanganate example is
        /// long enough to need its own line at most window widths, and truncating an example
        /// equation makes it useless.</para>
        /// </summary>
        private void BuildExamples()
        {
            foreach (var example in Examples)
            {
                var chip = new Button
                {
                    // The page's own font, as the calculator uses, rather than Consolas.
                    Content = new TextBlock { Text = example },
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(12, 6, 12, 6),
                    Tag = example,
                };
                chip.Click += (s, _) =>
                {
                    EquationBox.Text = (string)((Button)s).Tag;
                    EquationBox.SelectionStart = EquationBox.Text.Length;
                };
                ExamplesPanel.Children.Add(chip);
            }
        }

        private void ApplyProGate()
        {
            var hasPro = SettingsStore.HasAnyPro;
            ProGate.Visibility = hasPro ? Visibility.Collapsed : Visibility.Visible;
            FavoritesList.Visibility = hasPro ? Visibility.Visible : Visibility.Collapsed;
            // Balancing stays free; only keeping the result is paid.
            SaveButton.Visibility = hasPro ? Visibility.Visible : Visibility.Collapsed;
        }

        // -- balancing -----------------------------------------------------------------------

        private void EquationBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _debounce.Stop();
            _debounce.Start();
        }

        private void Rebalance()
        {
            _tally.Clear();
            TallyPanel.Visibility = Visibility.Collapsed;
            _lastBalanced = null;

            var raw = EquationBox.Text?.Trim() ?? string.Empty;
            if (raw.Length == 0)
            {
                ResultText.Text = _strings.Get(S.balancer_tool_prompt);
                return;
            }

            switch (EquationBalancer.Parse(raw, IsKnownSymbol))
            {
                // Not an error. This is what half a typed equation looks like, and the reason the
                // parser distinguishes it: showing "I could not read that" to somebody mid-keystroke
                // is worse than saying nothing.
                case EquationBalancer.ParseOutcome.NoArrowOutcome:
                    ResultText.Text = _strings.Get(S.balancer_tool_prompt);
                    return;

                case EquationBalancer.ParseOutcome.NotAnEquationOutcome:
                    ResultText.Text = Plain(_strings.Get(S.ai_equation_parse_failed));
                    return;

                case EquationBalancer.ParseOutcome.TooManySpeciesOutcome:
                    ResultText.Text = Plain(_strings.Get(S.ai_equation_too_large));
                    return;

                case EquationBalancer.ParseOutcome.Sides sides:
                    Show(EquationBalancer.Balance(sides.Reactants, sides.Products));
                    return;
            }
        }

        private void Show(EquationBalancer.Result result)
        {
            if (result is EquationBalancer.Result.Failed failed)
            {
                ResultText.Text = Plain(FailureMessage(failed));
                return;
            }

            var balanced = (EquationBalancer.Result.Balanced)result;
            _lastBalanced = Render(balanced);
            ResultText.Text = _lastBalanced;

            foreach (var t in balanced.Tally)
            {
                _tally.Add(new TallyRow
                {
                    Symbol = t.Symbol,
                    Text = Plain(_strings.Get(S.ai_equation_tally_row, t.Symbol, t.Left, t.Right)),
                });
            }
            TallyPanel.Visibility = _tally.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Each failure gets its own sentence; "it did not work" is not an answer.</summary>
        private string FailureMessage(EquationBalancer.Result.Failed failed) => failed.Reason switch
        {
            // Naming the element is the whole point - without it the user has to find it themselves.
            EquationBalancer.Reason.OneSidedElement when failed.Detail is { } symbol =>
                _strings.Get(S.ai_equation_one_sided_named, symbol),
            EquationBalancer.Reason.OneSidedElement => _strings.Get(S.ai_equation_one_sided),
            EquationBalancer.Reason.NoSolution => _strings.Get(S.ai_equation_no_solution),
            EquationBalancer.Reason.Underdetermined => _strings.Get(S.ai_equation_underdetermined),
            EquationBalancer.Reason.TooLarge => _strings.Get(S.ai_equation_too_large),
            EquationBalancer.Reason.ParseFailed => _strings.Get(S.ai_equation_parse_failed),
            _ => _strings.Get(S.ai_equation_parse_failed),
        };

        /// <summary>
        /// Renders the balanced equation with → and a suppressed coefficient of 1.
        ///
        /// <para>"1Fe" is what a bug looks like, so the 1 is never printed.</para>
        /// </summary>
        private static string Render(EquationBalancer.Result.Balanced balanced)
        {
            static string Side(IReadOnlyList<Term> terms) => string.Join(" + ", terms.Select(t =>
                (t.Coefficient == 1 ? string.Empty : t.Coefficient.ToString(CultureInfo.InvariantCulture))
                + t.Formula));

            return Side(balanced.Reactants) + " → " + Side(balanced.Products);
        }

        /// <summary>
        /// Strips the answer composer's markdown.
        ///
        /// <para>These strings are shared with the assistant, which renders <c>**bold**</c>. A plain
        /// TextBlock would print the asterisks.</para>
        /// </summary>
        private static string Plain(string markdown) =>
            markdown.Replace("**", string.Empty, StringComparison.Ordinal);

        /// <summary>
        /// Symbols come from the element table, so the balancer can tell a formula from prose.
        /// Built once - the table is already parsed and cached by the repository.
        /// </summary>
        private bool IsKnownSymbol(string symbol)
        {
            _symbols ??= BuildSymbols();
            return _symbols.Contains(symbol);
        }

        private static HashSet<string> BuildSymbols()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in ElementRepository.Keys())
            {
                if (ElementRepository.Raw(key)?.Field("short") is { Length: > 0 } symbol)
                    set.Add(symbol);
            }
            return set;
        }

        // -- favourites ----------------------------------------------------------------------

        private async System.Threading.Tasks.Task LoadFavoritesAsync()
        {
            _favorites.Clear();
            foreach (var saved in await FavoritesStore.LoadEquationsAsync())
                _favorites.Add(saved);
            UpdateFavoritesEmptyState();
        }

        private void UpdateFavoritesEmptyState() =>
            FavoritesEmpty.Visibility =
                SettingsStore.HasAnyPro && _favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private async void SaveFavorite_Click(object sender, RoutedEventArgs e)
        {
            var input = EquationBox.Text?.Trim();
            if (string.IsNullOrEmpty(input) || _lastBalanced is null) return;

            // Re-saving replaces the row rather than adding a second one for the same input.
            var existing = _favorites.FirstOrDefault(f => f.Input == input);
            if (existing is not null) _favorites.Remove(existing);

            _favorites.Insert(0, new SavedEquation { Input = input, Balanced = _lastBalanced });
            UpdateFavoritesEmptyState();
            await FavoritesStore.SaveEquationsAsync(_favorites);
        }

        private async void RemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string input) return;

            var existing = _favorites.FirstOrDefault(f => f.Input == input);
            if (existing is null) return;

            _favorites.Remove(existing);
            UpdateFavoritesEmptyState();
            await FavoritesStore.SaveEquationsAsync(_favorites);
        }

        private void CopyFavorite_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string balanced) return;

            var package = new DataPackage();
            package.SetText(balanced);
            Clipboard.SetContent(package);
        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e) =>
            MainWindow.Instance?.Navigation?.Navigate(Navigation.AppRoutes.Pro);
    }
}
