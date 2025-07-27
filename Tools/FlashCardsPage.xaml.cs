using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using Windows.Storage;

namespace Atomic_PeriodicTable.Tables
{
    public class FlashCardCategory
    {
        public string Name { get; set; }
        public int UnlockLevel { get; set; }
        public bool IsPro { get; set; }
        public bool IsUnlocked { get; set; }

        // Add this property for direct XAML binding without a converter
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
        public int LastQuizXP { get; set; }

        // --- PRO status (replace with your real logic) ---
        private bool IsProUser => false; // Set to true to unlock PRO categories

        public List<FlashCardCategoryGroup> CategoryGroups { get; set; } = new();

        public FlashCardsPage()
        {
            this.InitializeComponent();
            LoadLastQuizXP();
            BuildCategoryGroups();
            UpdateStatsUI();

            this.SizeChanged += FlashCardsPage_SizeChanged;
            SetCardsGridLayout(this.ActualWidth);
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

            // Example: display in a TextBlock named TotalXpTextBlock
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

            // Set icon based on unlock state
            foreach (var group in CategoryGroups)
            {
                group.Icon = group.IsUnlocked ? "\uE785" : "\uE72E";
            }

            // Refresh binding if needed
            CategoryGroupsControl.ItemsSource = null;
            CategoryGroupsControl.ItemsSource = CategoryGroups;
        }

        private void UpdateStatsUI()
        {
            int userXp = XpManager.GetXp();
            int userLevel = XpManager.GetCurrentLevel();
            var (progressCurrent, progressTotal) = XpManager.GetXpProgressInLevel();

            LevelTextBlock.Text = $"{userLevel}";
            TotalXpTextBlock.Text = $"{userXp}";
            LevelProgressTextBlock.Text = $"{progressCurrent} / {progressTotal}";
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
                // Stack vertically
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
                CategoriesCard.SetValue(Grid.RowProperty, 0);
                CategoriesCard.SetValue(Grid.ColumnProperty, 2);

                // Restore columns
                CardsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                CardsGrid.ColumnDefinitions[1].Width = new GridLength(24);
                CardsGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }
    }
}
