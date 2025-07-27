using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using Windows.Storage;
using System.Collections.ObjectModel;
using Atomic_PeriodicTable.Tables;
using System.Linq;

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
        public bool IsUnlocked { get; set; }
        public Visibility ProBadgeVisibility => IsPro ? Visibility.Visible : Visibility.Collapsed;
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
        public ObservableCollection<Achievement> Achievements { get; set; } = new();
        public int LastQuizXP { get; set; }
        public List<FlashCardCategoryGroup> CategoryGroups { get; set; } = new();

        // --- PRO status (replace with your real logic) ---
        private bool IsProUser => true; // Set to true to unlock PRO categories
        private bool IsProPlusUser => false; // Set to true for unlimited lives

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
                    ApplicationData.Current.LocalSettings.Values["Lives"] = Math.Min(value, MaxLives);
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

        public FlashCardsPage()
        {
            this.InitializeComponent();
            LoadLastQuizXP();
            BuildCategoryGroups();
            UpdateStatsUI();
            LoadTotalGamesPlayed();
            LoadAchievements();
            InitLives();

            this.SizeChanged += FlashCardsPage_SizeChanged;
            SetCardsGridLayout(this.ActualWidth);
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
            int displayLives = IsProPlusUser ? 999 : Math.Min(Lives, MaxLives);
            double percent = IsProPlusUser ? 1.0 : (double)displayLives / MaxLives;

            if (LivesCountTextBlock != null)
                LivesCountTextBlock.Text = IsProPlusUser ? "∞" : displayLives.ToString();
            if (LivesProgressBar != null)
                LivesProgressBar.Value = percent * 100;

            if (NextRefillTextBlock != null)
            {
                if (IsProPlusUser)
                {
                    NextRefillTextBlock.Text = "Unlimited";
                }
                else
                {
                    TimeSpan untilRefill = NextRefillTime - DateTime.UtcNow;
                    if (Lives >= MaxLives)
                        NextRefillTextBlock.Text = "Full!";
                    else if (untilRefill > TimeSpan.Zero)
                        NextRefillTextBlock.Text = $"Next +5 in {untilRefill:mm\\:ss}";
                    else
                        NextRefillTextBlock.Text = "Refilling...";
                }
            }
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
                            NextRefillTime = DateTime.UtcNow.AddMinutes(30); // 30 min for next refill
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
                }
            };

            // Sort: completed first, then by progress descending
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
        }

        private void BuildCategoryGroups()
        {
            int userLevel = XpManager.GetCurrentLevel();
            bool isPro = IsProUser;

            CategoryGroups = new List<FlashCardCategoryGroup>
            {
                new FlashCardCategoryGroup
                {
                    GroupName = "Level 0 - 4",
                    Icon = "\uE72E",
                    Categories = new List<FlashCardCategory>
                    {
                        new FlashCardCategory { Name = "Element Symbols", UnlockLevel = 0, IsPro = false, IsUnlocked = userLevel >= 0 },
                        new FlashCardCategory { Name = "Element Names", UnlockLevel = 0, IsPro = false, IsUnlocked = userLevel >= 0 },
                        new FlashCardCategory { Name = "Element Groups", UnlockLevel = 0, IsPro = false, IsUnlocked = userLevel >= 0 },
                        new FlashCardCategory { Name = "Discovered By", UnlockLevel = 0, IsPro = true, IsUnlocked = isPro },
                        new FlashCardCategory { Name = "Discovery Year", UnlockLevel = 0, IsPro = true, IsUnlocked = isPro }
                    }
                },
                new FlashCardCategoryGroup
                {
                    GroupName = "Level 5 - 9",
                    Icon = "\uE72E",
                    Categories = new List<FlashCardCategory>
                    {
                        new FlashCardCategory { Name = "Appearance", UnlockLevel = 5, IsPro = false, IsUnlocked = userLevel >= 5 },
                        new FlashCardCategory { Name = "Atomic Number", UnlockLevel = 5, IsPro = false, IsUnlocked = userLevel >= 5 },
                        new FlashCardCategory { Name = "Electrical Type", UnlockLevel = 5, IsPro = true, IsUnlocked = isPro & userLevel >= 5 },
                        new FlashCardCategory { Name = "Radioactive", UnlockLevel = 5, IsPro = true, IsUnlocked = isPro & userLevel >= 5 }
                    }
                },
                new FlashCardCategoryGroup
                {
                    GroupName = "Level 10 - 14",
                    Icon = "\uE72E",
                    Categories = new List<FlashCardCategory>
                    {
                        new FlashCardCategory { Name = "Atomic Mass", UnlockLevel = 10, IsPro = false, IsUnlocked = userLevel >= 10 },
                        new FlashCardCategory { Name = "Density", UnlockLevel = 10, IsPro = false, IsUnlocked = userLevel >= 10 },
                        new FlashCardCategory { Name = "Electronegativity", UnlockLevel = 10, IsPro = true, IsUnlocked = isPro & userLevel >= 10 },
                        new FlashCardCategory { Name = "Block", UnlockLevel = 10, IsPro = true, IsUnlocked = isPro & userLevel >= 10 }
                    }
                },
                new FlashCardCategoryGroup
                {
                    GroupName = "Level 15 - 19",
                    Icon = "\uE72E",
                    Categories = new List<FlashCardCategory>
                    {
                        new FlashCardCategory { Name = "Magnetic Type", UnlockLevel = 15, IsPro = false, IsUnlocked = userLevel >= 15 },
                        new FlashCardCategory { Name = "Phase (STP)", UnlockLevel = 15, IsPro = false, IsUnlocked = userLevel >= 15 },
                        new FlashCardCategory { Name = "Crystal Structure", UnlockLevel = 15, IsPro = true, IsUnlocked = isPro & userLevel >= 15 },
                        new FlashCardCategory { Name = "Superconducting Point", UnlockLevel = 15, IsPro = true, IsUnlocked = isPro & userLevel >= 15 }
                    }
                },
                new FlashCardCategoryGroup
                {
                    GroupName = "Level 20 - 24",
                    Icon = "\uE72E",
                    Categories = new List<FlashCardCategory>
                    {
                        new FlashCardCategory { Name = "Meutron Cross Sectional", UnlockLevel = 20, IsPro = false, IsUnlocked = userLevel >= 20 },
                        new FlashCardCategory { Name = "Specific Heat Capacity", UnlockLevel = 20, IsPro = false, IsUnlocked = userLevel >= 20 },
                        new FlashCardCategory { Name = "Mohs Hardness", UnlockLevel = 20, IsPro = true, IsUnlocked = isPro & userLevel >= 20 },
                        new FlashCardCategory { Name = "Vickers Hardness", UnlockLevel = 20, IsPro = true, IsUnlocked = isPro & userLevel >= 20 },
                        new FlashCardCategory { Name = "Brinell Hardness", UnlockLevel = 20, IsPro = true, IsUnlocked = isPro & userLevel >= 20 }
                    }
                }
            };

            foreach (var group in CategoryGroups)
            {
                group.Icon = group.IsUnlocked ? "\uE785" : "\uE72E";
            }

            CategoryGroupsControl.ItemsSource = null;
            CategoryGroupsControl.ItemsSource = CategoryGroups;
        }

        private void UpdateStatsUI()
        {
            int userXp = XpManager.GetXp();
            int userLevel = XpManager.GetCurrentLevel();
            var (progressCurrent, progressTotal) = XpManager.GetXpProgressInLevel();

            if (LevelTextBlock != null)
                LevelTextBlock.Text = $"{userLevel}";
            if (TotalXpTextBlock != null)
                TotalXpTextBlock.Text = $"{userXp}";
            if (LevelProgressTextBlock != null)
                LevelProgressTextBlock.Text = $"{progressCurrent} / {progressTotal}";
            if (xp_progress_bar != null)
                xp_progress_bar.Value = progressTotal > 0
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
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
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
            if (width < 900)
            {
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
                if (CardsGrid.RowDefinitions.Count > 0)
                {
                    CardsGrid.RowDefinitions.Clear();
                }
                UnitConverterCard.SetValue(Grid.RowProperty, 0);
                UnitConverterCard.SetValue(Grid.ColumnProperty, 0);
                CategoriesCard.SetValue(Grid.RowProperty, 0);
                CategoriesCard.SetValue(Grid.ColumnProperty, 2);

                CardsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                CardsGrid.ColumnDefinitions[1].Width = new GridLength(24);
                CardsGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }
    }
}
