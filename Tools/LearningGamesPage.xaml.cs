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

namespace Atomic_PeriodicTable.Tools
{
    public class GameResultItem
    {
        public string Question { get; set; }
        public string PickedAnswer { get; set; }
        public string CorrectAnswer { get; set; }
        public bool WasCorrect { get; set; }
        public int BaseXp { get; set; }
    }
    public class Element
    {
        public string Name { get; set; }
        public string Symbol { get; set; }
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
            int correctIndex = random.Next(4);
            var correctElement = selectedElements[correctIndex];

            string questionText, correct;
            List<string> alternatives;

            // Normalize category for robust comparison
            var cat = (category ?? "").Trim().ToLowerInvariant();

            if (cat == "element_groups" || cat == "group" || cat == "groups")
            {
                var infoTasks = selectedElements.Select(e => GetElementInfoFromJsonAsync(e.OriginalName)).ToArray();
                var infos = await Task.WhenAll(infoTasks);
                var groups = infos.Select(i => i.Group).ToList();
                if (groups.Any(g => string.IsNullOrWhiteSpace(g)))
                    return null;
                questionText = $"What is the group for {correctElement.OriginalName}?";
                correct = groups[correctIndex];
                alternatives = groups;
            }
            else if (cat == "discovered_by" || cat == "discoverer" || cat == "discovered")
            {
                var infoTasks = selectedElements.Select(e => GetElementInfoFromJsonAsync(e.OriginalName)).ToArray();
                var infos = await Task.WhenAll(infoTasks);
                var discoverers = infos.Select(i => i.DiscoveredBy).ToList();
                if (discoverers.Any(d => string.IsNullOrWhiteSpace(d)))
                    return null;
                questionText = $"Who discovered {correctElement.OriginalName}?";
                correct = discoverers[correctIndex];
                alternatives = discoverers;
            }
            else if (cat == "discovery_year" || cat == "year" || cat == "discovered_year")
            {
                var infoTasks = selectedElements.Select(e => GetElementInfoFromJsonAsync(e.OriginalName)).ToArray();
                var infos = await Task.WhenAll(infoTasks);
                var years = infos.Select(i => i.DiscoveryYear).ToList();
                if (years.Any(y => string.IsNullOrWhiteSpace(y)))
                    return null;
                questionText = $"In which year was {correctElement.OriginalName} discovered?";
                correct = years[correctIndex];
                alternatives = years;
            }
            else if (cat == "element_symbols" || cat == "symbols" || cat == "symbol")
            {
                questionText = $"What is the symbol for {correctElement.OriginalName}?";
                correct = correctElement.Symbol;
                alternatives = selectedElements.Select(e => e.Symbol).ToList();
            }
            else if (cat == "element_names" || cat == "names" || cat == "name")
            {
                questionText = $"What is the name for {correctElement.Symbol}?";
                correct = correctElement.OriginalName;
                alternatives = selectedElements.Select(e => e.OriginalName).ToList();
            }
            else
            {
                questionText = $"What is the name for {correctElement.Symbol}?";
                correct = correctElement.OriginalName;
                alternatives = selectedElements.Select(e => e.OriginalName).ToList();
            }

            alternatives = alternatives.OrderBy(_ => random.Next()).ToList();

            return new Question
            {
                Text = questionText,
                CorrectAnswer = correct,
                Alternatives = alternatives,
                BaseXp = 8
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

        private void CheckAnswer(string selectedAnswer)
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
            if (correct) xp += xpGained;
            else lives -= GetLivesLost();

            var buttons = new[] { Answer1, Answer2, Answer3, Answer4 };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].IsEnabled = false;
                if (buttons[i].Content?.ToString() == q.CorrectAnswer)
                    buttons[i].Background = new SolidColorBrush(Microsoft.UI.Colors.LightGreen);
                else if (buttons[i].Content?.ToString() == selectedAnswer)
                    buttons[i].Background = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
            }

            Task.Delay(1200).ContinueWith(_ =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    currentQuestionIndex++;
                    ShowQuestion();
                });
            });
        }

        private void FinishWithResults()
        {
            quizCompleted = true;

            // Store XP in local settings for FlashCardsPage
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["LastQuizXP"] = xp;

            // Add XP to global XP/level system
            XpManager.AddXp(xp);

            var dialog = new ContentDialog
            {
                Title = "Quiz Finished",
                Content = $"XP: {xp}\nLives left: {lives}\nCorrect: {gameResults.Count(r => r.WasCorrect)}/{gameResults.Count}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
        }
        private double GetXpMultiplier() => difficulty switch
        {
            "medium" => 1.3,
            "hard" => 1.5,
            _ => 1.0
        };

        private int GetLivesLost() => difficulty == "hard" ? 2 : 1;

        private string NormalizeLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            return label.Trim().Replace("-", " ").Replace("  ", " ").ToLowerInvariant();
        }
    }
}
