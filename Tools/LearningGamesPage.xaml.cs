using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using Atomic_PeriodicTable.Tables;

namespace Atomic_PeriodicTable.Tools
{
    public class Achievement
    {
        public string Icon { get; set; }
        public string Header { get; set; }
        public string Description { get; set; }
        public int Progress { get; set; }
        public int Goal { get; set; }
        public bool IsCompleted => Progress >= Goal;
        public string ProgressText => $"{Progress} / {Goal}";
    }
    public class GameResultItem
    {
        public string Question { get; set; }
        public string PickedAnswer { get; set; }
        public string CorrectAnswer { get; set; }
        public bool WasCorrect { get; set; }
        public int BaseXp { get; set; }
    }

    public class Question
    {
        public string Text { get; set; }
        public string CorrectAnswer { get; set; }
        public List<string> Alternatives { get; set; }
        public int BaseXp { get; set; }
    }

    public sealed partial class LearningGamesPage : Page
    {
        private List<Question> questions;
        private int currentQuestionIndex = 0;
        private string difficulty = "easy";
        private int totalQuestions = 8;
        private string category;
        private bool isAnswering = true;
        private bool hasLeftGame = false;
        private bool quizCompleted = false;
        private List<GameResultItem> gameResults = new();
        private DispatcherTimer timer;
        private int timeLimitMs = 20000;
        private int timeLeftMs;
        private int lives = 5;
        private int xp = 0;

        // Prevents dialog loop on programmatic navigation
        private bool _confirmedBackNavigation = false;

        public LearningGamesPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string categoryName)
            {
                category = categoryName;
            }
            await SetupGameAsync();
        }

        protected override async void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            if (!quizCompleted && e.NavigationMode == NavigationMode.Back && !_confirmedBackNavigation)
            {
                e.Cancel = true;
                var dialog = new ContentDialog
                {
                    Title = "Leave Game?",
                    Content = "Are you sure you want to leave? You will lose 5 lives.",
                    PrimaryButtonText = "Leave",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    // Deduct 5 lives in local settings
                    var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                    if (settings.TryGetValue("Lives", out object value) && value is int currentLives)
                        settings["Lives"] = Math.Max(0, currentLives - 5);
                    else
                        settings["Lives"] = 0;

                    // End the quiz and stop the timer to prevent further ticks/life loss
                    quizCompleted = true;
                    timer?.Stop();

                    // Set flag and navigate back after this method returns
                    _confirmedBackNavigation = true;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (Frame.CanGoBack)
                            Frame.GoBack();
                    });
                }
                // else: stay on page
            }
            else
            {
                base.OnNavigatingFrom(e);
            }
        }

        private async Task SetupGameAsync()
        {
            totalQuestions = difficulty switch
            {
                "easy" => 8,
                "medium" => 16,
                "hard" => 24,
                _ => 8
            };
            timeLimitMs = difficulty switch
            {
                "easy" => 20000,
                "medium" => 12000,
                "hard" => 7500,
                _ => 20000
            };

            questions = new List<Question>();
            for (int i = 0; i < totalQuestions; i++)
            {
                var q = await GenerateRandomQuestionAsync(category);
                if (q != null)
                    questions.Add(q);
            }

            if (questions.Count == 0)
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = "No questions could be generated. Please check your data files.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            currentQuestionIndex = 0;
            quizCompleted = false;
            gameResults.Clear();
            lives = 5;
            xp = 0;
            ShowQuestion();
        }

        // Combined helper for group, discovered_by, and discovery_year
        private async Task<(string Group, string DiscoveredBy, string DiscoveryYear)> GetElementInfoFromJsonAsync(string elementName)
        {
            string group = null, discoveredBy = null, discoveryYear = null;
            try
            {
                string basePath = AppContext.BaseDirectory;
                string elementsPath = Path.Combine(basePath, "Elements");
                string fileName = $"{elementName.ToLowerInvariant()}.json";
                string filePath = Path.Combine(elementsPath, fileName);

                if (!File.Exists(filePath))
                    return (null, null, null);

                using var stream = File.OpenRead(filePath);
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                // Try to get properties at root
                if (root.TryGetProperty("element_group", out var groupProp))
                    group = groupProp.GetString();
                if (root.TryGetProperty("element_discovered_name", out var discoveredByProp))
                    discoveredBy = discoveredByProp.GetString();
                if (root.TryGetProperty("element_year", out var yearProp))
                    discoveryYear = yearProp.GetString();

                // If not found, try nested objects
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (group == null && property.Value.TryGetProperty("element_group", out var groupProp2))
                            group = groupProp2.GetString();
                        if (discoveredBy == null && property.Value.TryGetProperty("element_discovered_name", out var discoveredByProp2))
                            discoveredBy = discoveredByProp2.GetString();
                        if (discoveryYear == null && property.Value.TryGetProperty("element_year", out var yearProp2))
                            discoveryYear = yearProp2.GetString();
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
            return (group, discoveredBy, discoveryYear);
        }

        private async Task<Question> GenerateRandomQuestionAsync(string category)
        {
            var allElements = Atomic_WinUI.ElementData.Elements;
            if (allElements.Count < 4)
                return null;

            var random = new Random();
            var selectedElements = allElements.OrderBy(_ => random.Next()).Take(4).ToList();
            int correctIndex = random.Next(selectedElements.Count);
            var correctElement = selectedElements[correctIndex];

            string questionText, correct;
            List<string> alternatives;

            // Normalize category for robust comparison
            var cat = (category ?? "").Trim().ToLowerInvariant();

            // Helper to get a property from JSON, skipping "---" and ensuring at least two valid values
            async Task<List<string>> GetJsonPropertyForElements(string propertyName)
            {
                var results = new List<string>();
                var usedElements = new HashSet<Element>(selectedElements);
                foreach (var el in selectedElements)
                {
                    string value = null;
                    try
                    {
                        string basePath = AppContext.BaseDirectory;
                        string elementsPath = Path.Combine(basePath, "Elements");
                        string fileName = $"{el.OriginalName.ToLowerInvariant()}.json";
                        string filePath = Path.Combine(elementsPath, fileName);

                        if (!File.Exists(filePath))
                        {
                            continue;
                        }

                        using var stream = File.OpenRead(filePath);
                        using var doc = await JsonDocument.ParseAsync(stream);
                        var root = doc.RootElement;

                        // Try to get property at root
                        if (root.TryGetProperty(propertyName, out var prop))
                            value = prop.GetString();

                        // If not found, try nested objects
                        if (value == null)
                        {
                            foreach (var property in root.EnumerateObject())
                            {
                                if (property.Value.ValueKind == JsonValueKind.Object)
                                {
                                    if (property.Value.TryGetProperty(propertyName, out var prop2))
                                    {
                                        value = prop2.GetString();
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        value = null;
                    }
                    if (!string.IsNullOrWhiteSpace(value) && value.Trim() != "---")
                        results.Add(value);
                }

                // If less than 2 valid results, try to get more elements until we have at least 2
                int attempts = 0;
                while (results.Count < 2 && attempts < 10)
                {
                    var extraElement = allElements.OrderBy(_ => random.Next()).FirstOrDefault(e => !usedElements.Contains(e));
                    if (extraElement == null) break;
                    usedElements.Add(extraElement);

                    string value = null;
                    try
                    {
                        string basePath = AppContext.BaseDirectory;
                        string elementsPath = Path.Combine(basePath, "Elements");
                        string fileName = $"{extraElement.OriginalName.ToLowerInvariant()}.json";
                        string filePath = Path.Combine(elementsPath, fileName);

                        if (!File.Exists(filePath))
                        {
                            attempts++;
                            continue;
                        }

                        using var stream = File.OpenRead(filePath);
                        using var doc = await JsonDocument.ParseAsync(stream);
                        var root = doc.RootElement;

                        if (root.TryGetProperty(propertyName, out var prop))
                            value = prop.GetString();

                        if (value == null)
                        {
                            foreach (var property in root.EnumerateObject())
                            {
                                if (property.Value.ValueKind == JsonValueKind.Object)
                                {
                                    if (property.Value.TryGetProperty(propertyName, out var prop2))
                                    {
                                        value = prop2.GetString();
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        value = null;
                    }
                    if (!string.IsNullOrWhiteSpace(value) && value.Trim() != "---")
                        results.Add(value);
                    attempts++;
                }

                return results;
            }

            if (cat == "element_symbols" || cat == "symbols" || cat == "symbol")
            {
                alternatives = selectedElements.Select(e => e.Symbol).Where(a => !string.IsNullOrWhiteSpace(a) && a.Trim() != "---").Distinct().ToList();
                if (alternatives.Count < 2) return null;
                questionText = $"What is the symbol for {correctElement.OriginalName}?";
                correct = correctElement.Symbol;
            }
            else if (cat == "element_names" || cat == "names" || cat == "name")
            {
                alternatives = selectedElements.Select(e => e.OriginalName).Where(a => !string.IsNullOrWhiteSpace(a) && a.Trim() != "---").Distinct().ToList();
                if (alternatives.Count < 2) return null;
                questionText = $"What is the name for {correctElement.Symbol}?";
                correct = correctElement.OriginalName;
            }
            else if (cat == "element_groups" || cat == "group" || cat == "groups")
            {
                var groups = await GetJsonPropertyForElements("element_group");
                if (groups.Count < 2) return null;
                questionText = $"What is the group for {correctElement.OriginalName}?";
                correct = groups.FirstOrDefault();
                alternatives = groups;
            }
            else if (cat == "discovered_by" || cat == "discoverer" || cat == "discovered")
            {
                var discoverers = await GetJsonPropertyForElements("element_discovered_name");
                if (discoverers.Count < 2) return null;
                questionText = $"Who discovered {correctElement.OriginalName}?";
                correct = discoverers.FirstOrDefault();
                alternatives = discoverers;
            }
            else if (cat == "discovery_year" || cat == "year" || cat == "discovered_year")
            {
                var years = await GetJsonPropertyForElements("element_year");
                if (years.Count < 2) return null;
                questionText = $"In which year was {correctElement.OriginalName} discovered?";
                correct = years.FirstOrDefault();
                alternatives = years;
            }
            else if (cat == "appearance")
            {
                var appearances = await GetJsonPropertyForElements("element_appearance");
                if (appearances.Count < 2) return null;
                questionText = $"What is the appearance of {correctElement.OriginalName}?";
                correct = appearances.FirstOrDefault();
                alternatives = appearances;
            }
            else if (cat == "atomic_number")
            {
                var atomicNumbers = await GetJsonPropertyForElements("element_atomic_number");
                if (atomicNumbers.Count < 2) return null;
                questionText = $"What is the atomic number of {correctElement.OriginalName}?";
                correct = atomicNumbers.FirstOrDefault();
                alternatives = atomicNumbers;
            }
            else if (cat == "electrical_type")
            {
                var electricalTypes = await GetJsonPropertyForElements("electrical_type");
                if (electricalTypes.Count < 2) return null;
                questionText = $"What is the electrical type of {correctElement.OriginalName}?";
                correct = electricalTypes.FirstOrDefault();
                alternatives = electricalTypes;
            }
            else if (cat == "radioactive")
            {
                alternatives = new List<string> { "Yes", "No" };
                questionText = $"Is {correctElement.OriginalName} radioactive?";
                var radioactiveVals = await GetJsonPropertyForElements("radioactive");
                bool isRadioactive = radioactiveVals.FirstOrDefault()?.Trim().ToLowerInvariant() == "yes";
                correct = isRadioactive ? "Yes" : "No";
            }
            else if (cat == "atomic_mass")
            {
                var atomicMasses = await GetJsonPropertyForElements("element_atomicmass");
                if (atomicMasses.Count < 2) return null;
                questionText = $"What is the atomic mass of {correctElement.OriginalName}?";
                correct = atomicMasses.FirstOrDefault();
                alternatives = atomicMasses;
            }
            else if (cat == "density")
            {
                var densities = await GetJsonPropertyForElements("element_density");
                if (densities.Count < 2) return null;
                questionText = $"What is the density of {correctElement.OriginalName}?";
                correct = densities.FirstOrDefault();
                alternatives = densities;
            }
            else if (cat == "electronegativity")
            {
                var electronegativities = await GetJsonPropertyForElements("element_electronegativty");
                if (electronegativities.Count < 2) return null;
                questionText = $"What is the electronegativity of {correctElement.OriginalName}?";
                correct = electronegativities.FirstOrDefault();
                alternatives = electronegativities;
            }
            else if (cat == "block")
            {
                var blocks = await GetJsonPropertyForElements("element_block");
                if (blocks.Count < 2) return null;
                questionText = $"What block does {correctElement.OriginalName} belong to?";
                correct = blocks.FirstOrDefault();
                alternatives = blocks;
            }
            else if (cat == "magnetic_type")
            {
                var magneticTypes = await GetJsonPropertyForElements("magnetic_type");
                if (magneticTypes.Count < 2) return null;
                questionText = $"What is the magnetic type of {correctElement.OriginalName}?";
                correct = magneticTypes.FirstOrDefault();
                alternatives = magneticTypes;
            }
            else if (cat == "phase_stp" || cat == "phase" || cat == "element_phase" || cat == "phase (STP)" || cat == "phase_(stp)")
            {
                var phases = await GetJsonPropertyForElements("element_phase");
                if (phases.Count < 2) return null;
                questionText = $"What is the phase of {correctElement.OriginalName} at STP?";
                correct = phases.FirstOrDefault();
                alternatives = phases;
            }
            else if (cat == "crystal_structure")
            {
                var crystalStructures = await GetJsonPropertyForElements("crystal_structure");
                if (crystalStructures.Count < 2) return null;
                questionText = $"What is the crystal structure of {correctElement.OriginalName}?";
                correct = crystalStructures.FirstOrDefault();
                alternatives = crystalStructures;
            }
            else if (cat == "superconducting_point")
            {
                var superconductingPoints = await GetJsonPropertyForElements("superconducting_point");
                if (superconductingPoints.Count < 2) return null;
                questionText = $"What is the superconducting point of {correctElement.OriginalName}?";
                correct = superconductingPoints.FirstOrDefault();
                alternatives = superconductingPoints;
            }
            else if (cat == "neutron_cross_sectional")
            {
                var neutronCrossSectionals = await GetJsonPropertyForElements("neutron_cross_sectional");
                if (neutronCrossSectionals.Count < 2) return null;
                questionText = $"What is the neutron cross sectional of {correctElement.OriginalName}?";
                correct = neutronCrossSectionals.FirstOrDefault();
                alternatives = neutronCrossSectionals;
            }
            else if (cat == "specific_heat_capacity")
            {
                var specificHeatCapacities = await GetJsonPropertyForElements("element_specific_heat_capacity");
                if (specificHeatCapacities.Count < 2) return null;
                questionText = $"What is the specific heat capacity of {correctElement.OriginalName}?";
                correct = specificHeatCapacities.FirstOrDefault();
                alternatives = specificHeatCapacities;
            }
            else if (cat == "mohs_hardness")
            {
                var mohsHardnesses = await GetJsonPropertyForElements("mohs_hardness");
                if (mohsHardnesses.Count < 2) return null;
                questionText = $"What is the mohs hardness of {correctElement.OriginalName}?";
                correct = mohsHardnesses.FirstOrDefault();
                alternatives = mohsHardnesses;
            }
            else if (cat == "vickers_hardness")
            {
                var vickersHardnesses = await GetJsonPropertyForElements("vickers_hardness");
                if (vickersHardnesses.Count < 2) return null;
                questionText = $"What is the vickers hardness of {correctElement.OriginalName}?";
                correct = vickersHardnesses.FirstOrDefault();
                alternatives = vickersHardnesses;
            }
            else if (cat == "brinell_hardness")
            {
                var brinellHardnesses = await GetJsonPropertyForElements("brinell_hardness");
                if (brinellHardnesses.Count < 2) return null;
                questionText = $"What is the brinell hardness of {correctElement.OriginalName}?";
                correct = brinellHardnesses.FirstOrDefault();
                alternatives = brinellHardnesses;
            }
            else
            {
                // fallback: element_names
                alternatives = selectedElements.Select(e => e.OriginalName).Where(a => !string.IsNullOrWhiteSpace(a) && a.Trim() != "---").Distinct().ToList();
                if (alternatives.Count < 2) return null;
                questionText = $"What is the name for {correctElement.Symbol}?";
                correct = correctElement.OriginalName;
            }

            // Remove empty/null, deduplicate, shuffle
            alternatives = alternatives.Where(a => !string.IsNullOrWhiteSpace(a) && a.Trim() != "---").Distinct().OrderBy(_ => random.Next()).ToList();

            if (alternatives.Count < 2)
                return null;

            // Pick correct answer from alternatives if possible
            if (!alternatives.Contains(correct))
                correct = alternatives.First();

            return new Question
            {
                Text = questionText,
                CorrectAnswer = correct,
                Alternatives = alternatives,
                BaseXp = GetBaseXp(cat)
            };
        }

        private void ShowQuestion()
        {
            if (currentQuestionIndex >= questions.Count)
            {
                FinishWithResults();
                return;
            }

            isAnswering = true;
            var q = questions[currentQuestionIndex];

            QuestionCounter.Text = $"{currentQuestionIndex + 1}/{questions.Count}";
            QuestionText.Text = q.Text;
            var buttons = new[] { Answer1, Answer2, Answer3, Answer4 };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i < q.Alternatives.Count)
                {
                    buttons[i].Content = q.Alternatives[i];
                    buttons[i].Visibility = Visibility.Visible;
                    buttons[i].IsEnabled = true;
                    buttons[i].Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
                else
                {
                    buttons[i].Visibility = Visibility.Collapsed;
                }
            }

            timeLeftMs = timeLimitMs;
            TimeProgressBar.Maximum = timeLimitMs;
            TimeProgressBar.Value = timeLimitMs;
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(50);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void Timer_Tick(object sender, object e)
        {
            timeLeftMs -= 50;
            TimeProgressBar.Value = timeLeftMs;
            if (timeLeftMs <= 0)
            {
                timer.Stop();
                if (isAnswering)
                {
                    isAnswering = false;
                    CheckAnswer("__TIMEOUT__");
                }
            }
        }

        private void Answer_Click(object sender, RoutedEventArgs e)
        {
            if (!isAnswering || hasLeftGame) return;
            isAnswering = false;
            timer?.Stop();
            var btn = sender as Button;
            var selectedAnswer = btn?.Content?.ToString() ?? "";
            CheckAnswer(selectedAnswer);
        }

        private async void CheckAnswer(string selectedAnswer)
        {
            var q = questions[currentQuestionIndex];
            bool correct = NormalizeLabel(selectedAnswer) == NormalizeLabel(q.CorrectAnswer);

            gameResults.Add(new GameResultItem
            {
                Question = q.Text,
                PickedAnswer = selectedAnswer == "__TIMEOUT__" ? "Timeout" : selectedAnswer,
                CorrectAnswer = q.CorrectAnswer,
                WasCorrect = correct,
                BaseXp = q.BaseXp
            });

            int xpGained = (selectedAnswer == "__TIMEOUT__" || !correct) ? 0 : (int)(q.BaseXp * GetXpMultiplier());
            if (correct)
                xp += xpGained;
            else
            {
                lives -= GetLivesLost();
                // Deduct lives in local settings for global sync
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (settings.TryGetValue("Lives", out object value) && value is int currentLives)
                    settings["Lives"] = Math.Max(0, currentLives - GetLivesLost());
                else
                    settings["Lives"] = Math.Max(0, lives);
            }

            var buttons = new[] { Answer1, Answer2, Answer3, Answer4 };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].IsEnabled = false;
                if (buttons[i].Content?.ToString() == q.CorrectAnswer)
                    buttons[i].Background = new SolidColorBrush(Microsoft.UI.Colors.LightGreen);
                else if (buttons[i].Content?.ToString() == selectedAnswer)
                    buttons[i].Background = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
            }

            // Fade out question card, fade in feedback card
            await FadeOut(QuestionCard);
            if (correct)
            {
                FeedbackTitle.Text = "Correct!";
                FeedbackDetail.Text = $"+{xpGained} XP";
                FeedbackDetail.Foreground = new SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
            }
            else
            {
                FeedbackTitle.Text = "Wrong!";
                FeedbackDetail.Text = $"-{GetLivesLost()} Life{(GetLivesLost() > 1 ? "s" : "")}";
                FeedbackDetail.Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
            }
            await FadeIn(FeedbackCard);

            // After delay, fade out feedback and fade in next question
            await Task.Delay(1200);
            await FadeOut(FeedbackCard);
            currentQuestionIndex++;
            ShowQuestion();
            await FadeIn(QuestionCard);
        }

        private async void FinishWithResults()
        {
            quizCompleted = true;

            // Bonus: Completed all questions
            int bonusXp = 0;
            if (gameResults.Count == questions.Count)
                bonusXp += 25;

            // Bonus: All correct
            if (gameResults.All(r => r.WasCorrect))
            {
                bonusXp += 25;

                var achievement = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (!achievement.ContainsKey("PerfectGames") || Convert.ToInt32(achievement["PerfectGames"]) == 0)
                {
                    achievement["PerfectGames"] = 1;
                }
            }

            int correctCount = gameResults.Count(r => r.WasCorrect);
            int baseXp = gameResults.Sum(r => r.WasCorrect ? (int)(r.BaseXp * GetXpMultiplier()) : 0);
            xp = baseXp + bonusXp;

            // Storing the XP in local settings for FlashCardsPage
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["LastQuizXP"] = xp;

            // Adding XP to global XP/level system in XpManager Util
            XpManager.AddXp(xp);

            // Increment total games played
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (settings.TryGetValue("TotalGamesPlayed", out object value) && value is int games)
                settings["TotalGamesPlayed"] = games + 1;
            else if (settings.TryGetValue("TotalGamesPlayed", out value) && value is long gamesLong)
                settings["TotalGamesPlayed"] = (int)gamesLong + 1;
            else
                settings["TotalGamesPlayed"] = 1;

            // Show result card with animation
            ResultXpSummary.Text = $"Total XP: {xp}";
            ResultXpBreakdown.Text =
                $"Correct answers: {correctCount}/{gameResults.Count}\n" +
                $"Base XP: {baseXp}\n" +
                $"Bonus for completion: {(gameResults.Count == questions.Count ? 25 : 0)}\n" +
                $"Bonus for all correct: {(gameResults.All(r => r.WasCorrect) ? 25 : 0)}\n";

            ResultList.ItemsSource = gameResults;

            // Animate out the question card, then animate in the result card
            await FadeOut(QuestionCard);
            await FadeIn(ResultCard);
        }

        private int GetBaseXp(string category)
        {
            return category switch
            {
                "element_symbols" or "symbols" or "symbol" => 8,
                "element_names" or "names" or "name" => 8,
                "element_groups" => 13,
                "discovered_by" or "discoverer" or "discovered" => 16,
                "discovery_year" or "year" or "discovered_year" => 16,
                "appearance" => 12,
                "atomic_number" => 8,
                "electrical_type" => 16,
                "radioactive" => 16,
                "atomic_mass" => 25,
                "density" => 40,
                "electronegativity" => 30,
                "block" => 15,
                "magnetic_type" => 18,
                "phase_stp" or "phase" or "element_phase" or "phase_(stp)" => 10,
                "crystal_structure" => 40,
                "superconducting_point" => 50,
                "neutron_cross_sectional" => 50,
                "specific_heat_capacity" or "specific heat capacity" => 50,
                "mohs_hardness" => 60,
                "vickers_hardness" => 60,
                "brinell_hardness" => 60,
                _ => 5
            };
        }

        private void ResultCard_Close_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to FlashCardsPage
            if (this.Frame != null)
            {
                this.Frame.Navigate(typeof(FlashCardsPage));
            }
        }

        private double GetXpMultiplier() => difficulty switch
        {
            "medium" => 1.3,
            "hard" => 1.5,
            _ => 1.0
        };

        private bool isAnimating = false;

        private async Task FadeOut(UIElement element)
        {
            var original = (Storyboard)this.Resources["FadeOutStoryboard"];
            var sb = new Storyboard();
            foreach (var anim in original.Children)
            {
                if (anim is DoubleAnimation da)
                {
                    var clone = new DoubleAnimation
                    {
                        To = da.To,
                        Duration = da.Duration
                    };
                    Storyboard.SetTarget(clone, element);
                    Storyboard.SetTargetProperty(clone, Storyboard.GetTargetProperty(da));
                    sb.Children.Add(clone);
                }
            }
            sb.Begin();
            await Task.Delay(250);
            element.Visibility = Visibility.Collapsed;
        }

        private async Task FadeIn(UIElement element)
        {
            element.Visibility = Visibility.Visible;
            var original = (Storyboard)this.Resources["FadeInStoryboard"];
            var sb = new Storyboard();
            foreach (var anim in original.Children)
            {
                if (anim is DoubleAnimation da)
                {
                    var clone = new DoubleAnimation
                    {
                        To = da.To,
                        Duration = da.Duration
                    };
                    Storyboard.SetTarget(clone, element);
                    Storyboard.SetTargetProperty(clone, Storyboard.GetTargetProperty(da));
                    sb.Children.Add(clone);
                }
            }
            sb.Begin();
            await Task.Delay(250);
        }

        private int GetLivesLost() => difficulty == "hard" ? 2 : 1;

        private string NormalizeLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            return label.Trim().Replace("-", " ").Replace("  ", " ").ToLowerInvariant();
        }
    }
}
