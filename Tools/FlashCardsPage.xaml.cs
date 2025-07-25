using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic_PeriodicTable.Tables
{
    public sealed partial class FlashCardsPage : Page
    {
       

        // --- User stats (should be loaded/saved from persistent storage in a real app) ---
        private int _level = 17;
        private int _xp = 3171;
        private int _quizzesCompleted = 35;
        private int _progressCurrent = 246;
        private int _progressTotal = 375;

        // --- Difficulty ---
        private string _selectedDifficulty = "EASY";

        // --- Category model ---
        public class FlashCardCategory
        {
            public string Name { get; set; }
            public string Group { get; set; }
            public int UnlockLevel { get; set; }
            public bool IsPro { get; set; }
            public bool IsUnlocked { get; set; }
        }

        // --- All categories, grouped by level range ---
        private List<FlashCardCategory> _categories = new()
        {
            // Level 0-4
            new FlashCardCategory { Name = "Element Symbols", Group = "Level 0–4", UnlockLevel = 0, IsPro = false },
            new FlashCardCategory { Name = "Element Names", Group = "Level 0–4", UnlockLevel = 0, IsPro = false },
            new FlashCardCategory { Name = "Element Groups", Group = "Level 0–4", UnlockLevel = 0, IsPro = false },
            new FlashCardCategory { Name = "Discovered By", Group = "Level 0–4", UnlockLevel = 0, IsPro = true },
            new FlashCardCategory { Name = "Discovery Year", Group = "Level 0–4", UnlockLevel = 0, IsPro = true },

            // Level 5-9
            new FlashCardCategory { Name = "Appearance", Group = "Level 5–9", UnlockLevel = 5, IsPro = false },
            new FlashCardCategory { Name = "Atomic Number", Group = "Level 5–9", UnlockLevel = 5, IsPro = false },
            new FlashCardCategory { Name = "Electrical Type", Group = "Level 5–9", UnlockLevel = 5, IsPro = true },
            new FlashCardCategory { Name = "Radioactive", Group = "Level 5–9", UnlockLevel = 5, IsPro = true },

            // Level 10-14
            new FlashCardCategory { Name = "Atomic Mass", Group = "Level 10–14", UnlockLevel = 10, IsPro = false },
            new FlashCardCategory { Name = "Density", Group = "Level 10–14", UnlockLevel = 10, IsPro = false },
            new FlashCardCategory { Name = "Electronegativity", Group = "Level 10–14", UnlockLevel = 10, IsPro = false },
            new FlashCardCategory { Name = "Block", Group = "Level 10–14", UnlockLevel = 10, IsPro = true },

            // Level 15-19
            new FlashCardCategory { Name = "Magnetic Type", Group = "Level 15–19", UnlockLevel = 15, IsPro = false },
            new FlashCardCategory { Name = "Phase (STP)", Group = "Level 15–19", UnlockLevel = 15, IsPro = false },
            new FlashCardCategory { Name = "Crystal Structure", Group = "Level 15–19", UnlockLevel = 15, IsPro = true },
            new FlashCardCategory { Name = "Superconducting Point", Group = "Level 15–19", UnlockLevel = 15, IsPro = true },
        };

        // --- PRO status (replace with your real logic) ---
        private bool IsProUser => false; // Set to true to unlock PRO categories

        public FlashCardsPage()
        {
            this.InitializeComponent();
            UpdateStatsUI();
            UpdateCategoryUnlocks();

            this.SizeChanged += FlashCardsPage_SizeChanged;
            SetCardsGridLayout(this.ActualWidth);

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

        private void UpdateStatsUI()
        {
            // Implement stats UI update logic if needed
        }

        private void UpdateCategoryUnlocks()
        {
            foreach (var cat in _categories)
            {
                cat.IsUnlocked = _level >= cat.UnlockLevel && (!cat.IsPro || IsProUser);
            }
        }

 
   
    }
}
