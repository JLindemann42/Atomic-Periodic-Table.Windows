using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using Windows.Storage;
using System.Collections.ObjectModel;
using Atomic_PeriodicTable.Tables;
using System.Linq;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;

namespace Atomic_PeriodicTable.Tables
{
    public class Achievement
    {
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
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue("Lives", out object value) && value is int l)
                    return l;
                return MaxLives;
            }
            set
            {
                if (!IsProPlusUser)
                    ApplicationData.Current.LocalSettings.Values["Lives"] = Math.Max(0, Math.Min(value, MaxLives));
                UpdateLivesUI();
            }
        }

        public DateTime NextRefillTime
        {
            get
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue("NextRefillTime", out object value) && value is string s && DateTime.TryParse(s, out var dt))
                    return dt;
                return DateTime.UtcNow;
            }
            set => ApplicationData.Current.LocalSettings.Values["NextRefillTime"] = value.ToString("o");
        }

        private DispatcherTimer refillTimer;

        private void SetStartFlashcardsButtonsEnabled(bool enabled)
        {
            int userLevel = XpManager.GetCurrentLevel();

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
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("IsProPlusUser", out object proPlusObj) && proPlusObj is bool proPlus)
                _isProPlusUser = proPlus;
            else
                _isProPlusUser = false;

            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("IsProUser", out object proObj) && proObj is bool pro)
                _isProUser = pro;
            else
                _isProUser = false;

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
            else if (!ApplicationData.Current.LocalSettings.Values.ContainsKey("Lives"))
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
            var settings = ApplicationData.Current.LocalSettings.Values;

            int totalGamesPlayed = 0;
            int perfectGames = 0;

            if (settings.TryGetValue("TotalGamesPlayed", out object gamesObj))
            {
                if (gamesObj is int gamesInt)
                    totalGamesPlayed = gamesInt;
                else if (gamesObj is long gamesLong)
                    totalGamesPlayed = (int)gamesLong;
            }
            if (settings.TryGetValue("PerfectGames", out object perfectObj))
            {
                if (perfectObj is int perfectInt)
                    perfectGames = perfectInt;
                else if (perfectObj is long perfectLong)
                    perfectGames = (int)perfectLong;
            }

            var achievementList = new List<Achievement>
            {
                new Achievement
                {
                    Icon = "\uEA86",
                    Header = "Perfect Game",
                    Description = "Get all questions correct in a game.",
                    Progress = perfectGames > 0 ? 1 : 0,
                    Goal = 1
                },
                new Achievement
                {
                    Icon = "\uE7FC",
                    Header = "Quiz Enthusiast",
                    Description = "Play 10 games.",
                    Progress = totalGamesPlayed,
                    Goal = 10
                },
                new Achievement
                {
                    Icon = "\uE7FC",
                    Header = "Quiz Master",
                    Description = "Play 100 games.",
                    Progress = totalGamesPlayed,
                    Goal = 100
                },
                new Achievement
                {
                    Icon = "\uE7FC",
                    Header = "Getting the hang of it",
                    Description = "Play 500 games.",
                    Progress = totalGamesPlayed,
                    Goal = 500
                }
            };

            var sorted = achievementList
                .OrderByDescending(a => a.IsCompleted)
                .ThenByDescending(a => a.ClampedProgress)
                .ToList();

            Achievements.Clear();
            foreach (var a in sorted)
                Achievements.Add(a);
        }

        private void LoadLastQuizXP()
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("LastQuizXP", out object value) && value is int xp)
            {
                LastQuizXP = xp;
            }
            else if (ApplicationData.Current.LocalSettings.Values.TryGetValue("LastQuizXP", out value) && value is long xpLong)
            {
                LastQuizXP = (int)xpLong;
            }
            else
            {
                LastQuizXP = 0;
            }

            if (TotalXpTextBlock != null)
                TotalXpTextBlock.Text = LastQuizXP.ToString();
            if (TotalXpTextBlock_single != null)
                TotalXpTextBlock_single.Text = LastQuizXP.ToString();
        }

        private void BuildCategoryGroups()
        {
            int userLevel = XpManager.GetCurrentLevel();
            bool isProPlus = _isProPlusUser;

            CategoryGroups = new List<FlashCardCategoryGroup>
            {
                new FlashCardCategoryGroup
                {
                    GroupName = "Level 0 - 4",
                    Icon = "\uE72E",
                    Categories = new List<FlashCardCategory>
                    {
                        new FlashCardCategory { Name = "Element Symbols", UnlockLevel = 0, IsPro = false, IsUnlocked = userLevel >= 0, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Element Names", UnlockLevel = 0, IsPro = false, IsUnlocked = userLevel >= 0, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Element Groups", UnlockLevel = 0, IsPro = false, IsUnlocked = userLevel >= 0, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Discovered By", UnlockLevel = 0, IsPro = true, IsUnlocked = isProPlus, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Discovery Year", UnlockLevel = 0, IsPro = true, IsUnlocked = isProPlus, IsProPlusUser = isProPlus }
                    }
                },
                new FlashCardCategoryGroup
                {
                    GroupName = "Level 5 - 9",
                    Icon = "\uE72E",
                    Categories = new List<FlashCardCategory>
                    {
                        new FlashCardCategory { Name = "Appearance", UnlockLevel = 5, IsPro = false, IsUnlocked = userLevel >= 5, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Atomic Number", UnlockLevel = 5, IsPro = false, IsUnlocked = userLevel >= 5, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Electrical Type", UnlockLevel = 5, IsPro = true, IsUnlocked = isProPlus && userLevel >= 5, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Radioactive", UnlockLevel = 5, IsPro = true, IsUnlocked = isProPlus && userLevel >= 5, IsProPlusUser = isProPlus }
                    }
                },
                new FlashCardCategoryGroup
                {
                    GroupName = "Level 10 - 14",
                    Icon = "\uE72E",
                    Categories = new List<FlashCardCategory>
                    {
                        new FlashCardCategory { Name = "Atomic Mass", UnlockLevel = 10, IsPro = false, IsUnlocked = userLevel >= 10, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Density", UnlockLevel = 10, IsPro = false, IsUnlocked = userLevel >= 10, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Electronegativity", UnlockLevel = 10, IsPro = true, IsUnlocked = isProPlus && userLevel >= 10, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Block", UnlockLevel = 10, IsPro = true, IsUnlocked = isProPlus && userLevel >= 10, IsProPlusUser = isProPlus }
                    }
                },
                new FlashCardCategoryGroup
                {
                    GroupName = "Level 15 - 19",
                    Icon = "\uE72E",
                    Categories = new List<FlashCardCategory>
                    {
                        new FlashCardCategory { Name = "Magnetic Type", UnlockLevel = 15, IsPro = false, IsUnlocked = userLevel >= 15, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Phase (STP)", UnlockLevel = 15, IsPro = false, IsUnlocked = userLevel >= 15, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Crystal Structure", UnlockLevel = 15, IsPro = true, IsUnlocked = isProPlus && userLevel >= 15, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Superconducting Point", UnlockLevel = 15, IsPro = true, IsUnlocked = isProPlus && userLevel >= 15, IsProPlusUser = isProPlus }
                    }
                },
                new FlashCardCategoryGroup
                {
                    GroupName = "Level 20 - 24",
                    Icon = "\uE72E",
                    Categories = new List<FlashCardCategory>
                    {
                        new FlashCardCategory { Name = "Neutron Cross Sectional", UnlockLevel = 20, IsPro = false, IsUnlocked = userLevel >= 20, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Specific Heat Capacity", UnlockLevel = 20, IsPro = false, IsUnlocked = userLevel >= 20, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Mohs Hardness", UnlockLevel = 20, IsPro = true, IsUnlocked = isProPlus && userLevel >= 20, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Vickers Hardness", UnlockLevel = 20, IsPro = true, IsUnlocked = isProPlus && userLevel >= 20, IsProPlusUser = isProPlus },
                        new FlashCardCategory { Name = "Brinell Hardness", UnlockLevel = 20, IsPro = true, IsUnlocked = isProPlus && userLevel >= 20, IsProPlusUser = isProPlus }
                    }
                }
            };

            foreach (var group in CategoryGroups)
            {
                group.Icon = group.IsUnlocked ? "\uE785" : "\uE72E";
            }

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
            int userXp = XpManager.GetXp();
            int userLevel = XpManager.GetCurrentLevel();
            var (progressCurrent, progressTotal) = XpManager.GetXpProgressInLevel();

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
            int totalGames = 0;
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (settings.TryGetValue("TotalGamesPlayed", out object value) && value is int games)
                totalGames = games;
            else if (settings.TryGetValue("TotalGamesPlayed", out value) && value is long gamesLong)
                totalGames = (int)gamesLong;

            if (TotalGamesPlayedTextBlock != null)
                TotalGamesPlayedTextBlock.Text = totalGames.ToString();
            if (TotalGamesPlayedTextBlock_single != null)
                TotalGamesPlayedTextBlock_single.Text = totalGames.ToString();
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (Lives <= 0 && !IsProPlusUser)
                return;

            if (sender is Button btn && btn.Tag is string categoryName)
            {
                string normalized = categoryName.ToLower().Replace(" ", "_");
                Frame.Navigate(typeof(Atomic_PeriodicTable.Tools.LearningGamesPage), normalized);
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
