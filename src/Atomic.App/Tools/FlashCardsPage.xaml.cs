using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using Windows.Storage;
using System.Collections.ObjectModel;
using Atomic.App.Tables;
using System.Linq;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using Atomic.App.Services;
using System.Globalization;
using Atomic.Core.Localization;
using Atomic.Core.Progression;

namespace Atomic.App.Tools
{
    public class Achievement
    {
        /// <summary>The numeric Android id, and the key the sync merges on.</summary>
        public string Id { get; set; }

        public string Icon { get; set; }
        public string Header { get; set; }
        public string Description { get; set; }
        public int Progress { get; set; }
        public int Goal { get; set; }
        public bool IsCompleted => Progress >= Goal;

        public int ClampedProgress => Math.Min(Progress, Goal);
        public string ProgressText => $"{ClampedProgress} / {Goal}";

        // Use trophy icon for completed, otherwise the original icon
        public string DisplayIcon => IsCompleted ? "\uE73E" : Icon;

        // Opacity: 1 for completed, 0.75 for not completed
        public double DisplayOpacity => IsCompleted ? 1.0 : 0.75;
    }

    public class FlashCardCategory
    {
        /// <summary>
        /// The catalog key, which is what the generators are registered under.
        ///
        /// <para>Carried explicitly because the page used to derive it from the English display
        /// name by lowercasing and replacing spaces - which silently stopped resolving the moment
        /// the name was translated, and could never have matched a key like "periodic_period".</para>
        /// </summary>
        public string Key { get; set; }

        public string Name { get; set; }
        public int UnlockLevel { get; set; }
        public bool IsPro { get; set; }
        public bool IsProPlusUser { get; set; } // Used for badge visibility
        public bool IsUnlocked { get; set; }
        public Visibility ProBadgeVisibility => (IsPro && !IsProPlusUser) ? Visibility.Visible : Visibility.Collapsed;
    }

    public class FlashCardCategoryGroup
    {
        public string GroupName { get; set; }
        public string Icon { get; set; }
        public List<FlashCardCategory> Categories { get; set; }
        public bool IsUnlocked => Categories != null && Categories.Exists(c => c.IsUnlocked);
    }

    public sealed partial class FlashCardsPage : Page
    {
        private bool _isSingleColumnMode;
        public bool IsSingleColumnMode
        {
            get => _isSingleColumnMode;
            set
            {
                if (_isSingleColumnMode != value)
                {
                    _isSingleColumnMode = value;
                    if (MainScrollViewer != null && CardsGrid_SingleColumn != null && CardsGrid != null)
                    {
                        MainScrollViewer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                        CardsGrid.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                    }
                }
            }
        }

        public ObservableCollection<Achievement> Achievements { get; set; } = new();
        public int LastQuizXP { get; set; }
        public List<FlashCardCategoryGroup> CategoryGroups { get; set; } = new();

        // Backing fields for PRO/PRO+ status
        private bool _isProUser = false;
        private bool _isProPlusUser = false;

        // Properties for PRO/PRO+ status
        private bool IsProUser => _isProUser && !_isProPlusUser; // Only true if not ProPlus
        private bool IsProPlusUser => _isProPlusUser; // ProPlus always overrides Pro

        // Lives logic
        public int MaxLives => IsProPlusUser ? int.MaxValue : (IsProUser ? 60 : 30);

        public int Lives
        {
            get
            {
                if (IsProPlusUser) return int.MaxValue;
                if (SettingsStore.Contains(SettingsStore.Keys.Lives))
                    return SettingsStore.Lives;
                return MaxLives;
            }
            set
            {
                if (!IsProPlusUser)
                    SettingsStore.Lives = Math.Max(0, Math.Min(value, MaxLives));
                UpdateLivesUI();
            }
        }

        public DateTime NextRefillTime
        {
            get
            {
                return SettingsStore.NextRefillTime;
            }
            set => SettingsStore.NextRefillTime = value;
        }

        private DispatcherTimer refillTimer;

        private void SetStartFlashcardsButtonsEnabled(bool enabled)
        {
            int userLevel = ProgressStore.Level;

            void UpdateButtons(ItemsControl groupsControl)
            {
                if (groupsControl == null) return;
                foreach (var group in groupsControl.Items)
                {
                    var groupObj = group as FlashCardCategoryGroup;
                    var container = groupsControl.ContainerFromItem(group) as ContentPresenter;
                    if (container != null && groupObj != null)
                    {
                        var buttons = FindVisualChildren<Button>(container);
                        foreach (var btn in buttons)
                        {
                            if (btn.Tag is string categoryName)
                            {
                                var category = groupObj.Categories.FirstOrDefault(c => c.Name == categoryName);
                                if (category != null)
                                {
                                    btn.IsEnabled = enabled && userLevel >= category.UnlockLevel && category.IsUnlocked;
                                }
                            }
                        }
                    }
                }
            }

            UpdateButtons(CategoryGroupsControl);
            UpdateButtons(CategoryGroupsControl_Single);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T t)
                    {
                        yield return t;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        public FlashCardsPage()
        {
            this.InitializeComponent();

            // Set Pro/ProPlus status from local settings
            _isProPlusUser = SettingsStore.IsProPlusUser;

            _isProUser = SettingsStore.IsProUser;

            // ProPlus always overrides Pro for UI and logic
            if (ProPlusBox != null)
                ProPlusBox.Visibility = _isProPlusUser ? Visibility.Collapsed : Visibility.Visible;
            if (ProPlusBox_single != null)
                ProPlusBox_single.Visibility = _isProPlusUser ? Visibility.Collapsed : Visibility.Visible;

            LoadLastQuizXP();
            BuildCategoryGroups();
            UpdateStatsUI();
            LoadTotalGamesPlayed();
            LoadAchievements();
            InitLives();

            this.SizeChanged += FlashCardsPage_SizeChanged;
            this.Loaded += (s, e) => { UpdateLivesUI(); };

            SetCardsGridLayout(this.ActualWidth);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            UpdateLivesUI();
        }

        private void InitLives()
        {
            if (IsProPlusUser)
            {
                Lives = int.MaxValue;
            }
            else if (!SettingsStore.Contains(SettingsStore.Keys.Lives))
            {
                Lives = MaxLives;
            }
            UpdateLivesUI();
            StartRefillTimer();
        }

        private void UpdateLivesUI()
        {
            int displayLives = IsProPlusUser ? 999 : Math.Max(0, Math.Min(Lives, MaxLives));
            double percent = IsProPlusUser ? 1.0 : (double)displayLives / MaxLives;

            // Two-column
            if (LivesCountTextBlock != null)
                LivesCountTextBlock.Text = IsProPlusUser ? "∞" : displayLives.ToString();
            if (LivesProgressBar != null)
                LivesProgressBar.Value = percent * 100;
            if (NextRefillTextBlock != null)
                NextRefillTextBlock.Text = IsProPlusUser ? "Unlimited" :
                    (Lives >= MaxLives ? "Full!" :
                    (NextRefillTime - DateTime.UtcNow > TimeSpan.Zero ?
                        $"Next +5 in {(NextRefillTime - DateTime.UtcNow):mm\\:ss}" : "Refilling..."));

            // Single-column
            if (LivesCountTextBlock_single != null)
                LivesCountTextBlock_single.Text = IsProPlusUser ? "∞" : displayLives.ToString();
            if (LivesProgressBar_single != null)
                LivesProgressBar_single.Value = percent * 100;
            if (NextRefillTextBlock_single != null)
                NextRefillTextBlock_single.Text = IsProPlusUser ? "Unlimited" :
                    (Lives >= MaxLives ? "Full!" :
                    (NextRefillTime - DateTime.UtcNow > TimeSpan.Zero ?
                        $"Next +5 in {(NextRefillTime - DateTime.UtcNow):mm\\:ss}" : "Refilling..."));

            SetStartFlashcardsButtonsEnabled(Lives > 0 || IsProPlusUser);
        }

        private void StartRefillTimer()
        {
            if (refillTimer != null)
            {
                refillTimer.Stop();
                refillTimer = null;
            }
            refillTimer = new DispatcherTimer();
            refillTimer.Interval = TimeSpan.FromSeconds(1);
            refillTimer.Tick += (s, e) =>
            {
                if (IsProPlusUser)
                {
                    refillTimer.Stop();
                    return;
                }
                if (Lives < MaxLives)
                {
                    TimeSpan untilRefill = NextRefillTime - DateTime.UtcNow;
                    if (untilRefill <= TimeSpan.Zero)
                    {
                        Lives = Math.Min(Lives + 5, MaxLives);
                        if (Lives < MaxLives)
                            NextRefillTime = DateTime.UtcNow.AddMinutes(20);
                        else
                            NextRefillTime = DateTime.UtcNow;
                    }
                }
                UpdateLivesUI();
            };
            refillTimer.Start();
        }



        private void LoadAchievements()
        {
            Achievements.Clear();
            foreach (var a in AchievementCatalog.Build())
                Achievements.Add(a);
        }

        private void LoadLastQuizXP()
        {
            LastQuizXP = SettingsStore.LastQuizXp;

            if (TotalXpTextBlock != null)
                TotalXpTextBlock.Text = LastQuizXP.ToString();
            if (TotalXpTextBlock_single != null)
                TotalXpTextBlock_single.Text = LastQuizXP.ToString();
        }

        /// <summary>
        /// Render the level boxes from <see cref="FlashcardCatalog"/>.
        ///
        /// <para>Was ~85 lines of hand-written bands listing 22 categories in English. The catalog
        /// is now the single source for both this screen and the quiz page, so a category cannot
        /// appear here without a generator behind it - <c>CurriculumWiringTest</c> asserts exactly
        /// that - and all 47 arrive with their translated labels.</para>
        /// </summary>
        private void BuildCategoryGroups()
        {
            var userLevel = ProgressStore.Level;
            var isProPlus = _isProPlusUser;
            var strings = ResourceStringProvider.For(AppLanguage.BaseCode(AppLanguage.Current));

            CategoryGroups = FlashcardCatalog.LevelBoxes
                .Select(box => new FlashCardCategoryGroup
                {
                    GroupName = strings.Get(
                        S.flashcard_level_band,
                        box.Range.First.ToString(CultureInfo.InvariantCulture),
                        box.Range.Last.ToString(CultureInfo.InvariantCulture)),
                    Icon = userLevel >= box.Range.First ? "" : "",
                    Categories = box.Categories
                        .Select(category => new FlashCardCategory
                        {
                            Key = category.Key,
                            Name = strings.Get(category.LabelKey),
                            UnlockLevel = box.Range.First,
                            IsPro = category.IsPro,
                            IsProPlusUser = isProPlus,
                            // Two gates, and both must pass: the level unlocks the box, the
                            // subscription unlocks the PRO+ cards inside it.
                            IsUnlocked = userLevel >= box.Range.First && (!category.IsPro || isProPlus),
                        })
                        .ToList(),
                })
                .ToList();

            CategoryGroupsControl.ItemsSource = null;
            CategoryGroupsControl.ItemsSource = CategoryGroups;
            if (CategoryGroupsControl_Single != null)
            {
                CategoryGroupsControl_Single.ItemsSource = null;
                CategoryGroupsControl_Single.ItemsSource = CategoryGroups;
            }
        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProPage));
        }

        private void UpdateStatsUI()
        {
            int userXp = ProgressStore.Xp;
            int userLevel = ProgressStore.Level;
            var (progressCurrent, progressTotal) = ProgressStore.LevelProgress;

            if (LevelTextBlock != null)
                LevelTextBlock.Text = $"{userLevel}";
            if (LevelTextBlock_single != null)
                LevelTextBlock_single.Text = $"{userLevel}";

            if (TotalXpTextBlock != null)
                TotalXpTextBlock.Text = $"{userXp}";
            if (TotalXpTextBlock_single != null)
                TotalXpTextBlock_single.Text = $"{userXp}";

            if (LevelProgressTextBlock != null)
                LevelProgressTextBlock.Text = $"{progressCurrent} / {progressTotal}";
            if (LevelProgressTextBlock_single != null)
                LevelProgressTextBlock_single.Text = $"{progressCurrent} / {progressTotal}";

            if (xp_progress_bar != null)
                xp_progress_bar.Value = progressTotal > 0
                    ? (double)progressCurrent / progressTotal * 100
                    : 0;
            if (xp_progress_bar_single != null)
                xp_progress_bar_single.Value = progressTotal > 0
                    ? (double)progressCurrent / progressTotal * 100
                    : 0;
        }

        private void LoadTotalGamesPlayed()
        {
            int totalGames = SettingsStore.TotalGamesPlayed;

            if (TotalGamesPlayedTextBlock != null)
                TotalGamesPlayedTextBlock.Text = totalGames.ToString();
            if (TotalGamesPlayedTextBlock_single != null)
                TotalGamesPlayedTextBlock_single.Text = totalGames.ToString();
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (Lives <= 0 && !IsProPlusUser)
                return;

            if (sender is Button { Tag: string categoryKey })
            {
                Frame.Navigate(
                    typeof(Atomic.App.Tools.LearningGamesPage),
                    new GameRequest(categoryKey, SettingsStore.QuizDifficulty));
            }
        }

        private void FlashCardsPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SetCardsGridLayout(e.NewSize.Width);
        }

        private void SetCardsGridLayout(double width)
        {
            MainScrollViewer.Visibility = (width < 900) ? Visibility.Visible : Visibility.Collapsed;
            CardsGrid.Visibility = (width < 900) ? Visibility.Collapsed : Visibility.Visible;

            if (width < 900)
            {
                IsSingleColumnMode = true;

                if (CardsGrid.RowDefinitions.Count == 0)
                {
                    CardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    CardsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
                    CardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
                UnitConverterCard.SetValue(Grid.RowProperty, 0);
                UnitConverterCard.SetValue(Grid.ColumnProperty, 0);
                CategoriesCard.SetValue(Grid.RowProperty, 2);
                CategoriesCard.SetValue(Grid.ColumnProperty, 0);

                for (int i = 0; i < CardsGrid.ColumnDefinitions.Count; i++)
                    CardsGrid.ColumnDefinitions[i].Width = (i == 0) ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            }
            else
            {
                IsSingleColumnMode = false;

                if (CardsGrid.RowDefinitions.Count > 0)
                {
                    CardsGrid.RowDefinitions.Clear();
                }
                UnitConverterCard.SetValue(Grid.RowProperty, 0);
                UnitConverterCard.SetValue(Grid.ColumnProperty, 0);
                CategoriesCard.SetValue(Grid.RowProperty, 0);
                CategoriesCard.SetValue(Grid.ColumnProperty, 2);
                CategoriesCard.SetValue(Grid.ColumnProperty, 2);

                CardsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                CardsGrid.ColumnDefinitions[1].Width = new GridLength(24);
                CardsGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }
    }
}
