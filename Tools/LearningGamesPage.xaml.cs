using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using System.IO;

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

            var elements = await LoadElementsAsync();
            if (elements == null || elements.Count == 0)
            {
                // Show error or fallback UI
                ContentDialog dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = "No elements found. Please check your data files.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }
            questions = GenerateQuestions(category, totalQuestions, elements); questions = GenerateQuestions(category, totalQuestions, elements);
            currentQuestionIndex = 0;
            quizCompleted = false;
            gameResults.Clear();
            lives = 5;
            xp = 0;
            ShowQuestion();
        }

        private async Task<List<Element>> LoadElementsAsync()
        {
            var elements = new List<Element>();
            try
            {
                string basePath = AppContext.BaseDirectory;
                string elementsPath = Path.Combine(basePath, "Elements");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Base path: {basePath}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Elements path: {elementsPath}");

                if (!Directory.Exists(elementsPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ERROR] Elements directory not found: {elementsPath}");
                    return elements;
                }

                if (Atomic_WinUI.ElementData.Elements == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ERROR] ElementData.Elements is null.");
                    return elements;
                }

                System.Diagnostics.Debug.WriteLine($"[DEBUG] ElementData.Elements count: {Atomic_WinUI.ElementData.Elements.Count}");

                foreach (var elementData in Atomic_WinUI.ElementData.Elements)
                {
                    string fileName = $"{elementData.OriginalName.ToLower()}.json";
                    string filePath = Path.Combine(elementsPath, fileName);

                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Looking for file: {filePath}");

                    if (!File.Exists(filePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[WARN] Element file not found: {filePath}");
                        continue;
                    }

                    string jsonContent = await File.ReadAllTextAsync(filePath);
                    try
                    {
                        // Try to deserialize as a single Element object
                        var element = JsonSerializer.Deserialize<Element>(jsonContent);
                        if (element != null && !string.IsNullOrWhiteSpace(element.Name) && !string.IsNullOrWhiteSpace(element.Symbol))
                        {
                            elements.Add(element);
                            System.Diagnostics.Debug.WriteLine($"[DEBUG] Loaded element: {element.Name} ({element.Symbol}) from {fileName}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[WARN] Deserialized element is null or missing Name/Symbol in {fileName}");
                        }
                    }
                    catch (Exception ex1)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WARN] Direct deserialization failed for {fileName}: {ex1.Message}");
                        // If not a single object, try to parse as array or object with array property
                        try
                        {
                            using var stream = File.OpenRead(filePath);
                            var json = await JsonDocument.ParseAsync(stream);
                            var root = json.RootElement;

                            if (root.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in root.EnumerateArray())
                                {
                                    var name = item.TryGetProperty("element", out var n) ? n.GetString() : null;
                                    var symbol = item.TryGetProperty("short", out var s) ? s.GetString() : null;
                                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(symbol))
                                    {
                                        elements.Add(new Element { Name = name, Symbol = symbol });
                                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Loaded element from array: {name} ({symbol}) in {fileName}");
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[WARN] Array item missing 'element' or 'short' in {fileName}");
                                    }
                                }
                            }
                            else if (root.ValueKind == JsonValueKind.Object)
                            {
                                bool foundArray = false;
                                foreach (var property in root.EnumerateObject())
                                {
                                    if (property.Value.ValueKind == JsonValueKind.Array)
                                    {
                                        foundArray = true;
                                        foreach (var item in property.Value.EnumerateArray())
                                        {
                                            var name = item.TryGetProperty("element", out var n) ? n.GetString() : null;
                                            var symbol = item.TryGetProperty("short", out var s) ? s.GetString() : null;
                                            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(symbol))
                                            {
                                                elements.Add(new Element { Name = name, Symbol = symbol });
                                                System.Diagnostics.Debug.WriteLine($"[DEBUG] Loaded element from object array: {name} ({symbol}) in {fileName}");
                                            }
                                            else
                                            {
                                                System.Diagnostics.Debug.WriteLine($"[WARN] Object array item missing 'element' or 'short' in {fileName}");
                                            }
                                        }
                                    }
                                }
                                if (!foundArray)
                                {
                                    // Try alternate property names directly in the object
                                    var name = root.TryGetProperty("element", out var n) ? n.GetString() : null;
                                    var symbol = root.TryGetProperty("short", out var s) ? s.GetString() : null;
                                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(symbol))
                                    {
                                        elements.Add(new Element { Name = name, Symbol = symbol });
                                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Loaded element from object: {name} ({symbol}) in {fileName}");
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[WARN] Object missing 'element' or 'short' in {fileName}");
                                    }
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[WARN] Unrecognized JSON structure in {fileName}");
                            }
                        }
                        catch (Exception ex2)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to parse {fileName}: {ex2.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Loading elements: {ex}");
            }
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Total elements loaded: {elements.Count}");
            return elements;
        }


        private List<Question> GenerateQuestions(string category, int count, List<Element> elements)
        {
            var questions = new List<Question>();
            var random = new Random();
            var usedElements = new HashSet<string>();

            // Helper for normalization (similar to Android's normalizeLabel)
            string NormalizeLabel(string label)
            {
                if (string.IsNullOrWhiteSpace(label)) return "";
                return label.Trim()
                    .Replace("-", " ")
                    .Replace("  ", " ")
                    .Replace("metals", "Metal", StringComparison.OrdinalIgnoreCase)
                    .Replace("metal", "Metal", StringComparison.OrdinalIgnoreCase)
                    .Replace("---", "")
                    .Trim();
            }

            // Helper to get wrong answers for a field
            List<string> WrongAnswersFor(Func<Element, string> fieldSelector, string correct)
            {
                return elements
                    .Where(e => NormalizeLabel(fieldSelector(e)) != NormalizeLabel(correct) && !string.IsNullOrWhiteSpace(fieldSelector(e)))
                    .Select(e => NormalizeLabel(fieldSelector(e)))
                    .Distinct()
                    .OrderBy(_ => random.Next())
                    .Take(3)
                    .ToList();
            }

            for (int i = 0; i < count; i++)
            {
                // Pick a random unused element
                var available = elements.Where(e => !usedElements.Contains(e.Name)).ToList();
                var element = available.Count > 0 ? available[random.Next(available.Count)] : elements[random.Next(elements.Count)];
                usedElements.Add(element.Name);

                string questionText, correct;
                List<string> alternatives;

                if (category == "element_symbols")
                {
                    questionText = $"What is the symbol for {NormalizeLabel(element.Name)}?";
                    correct = NormalizeLabel(element.Symbol);
                    var wrongs = WrongAnswersFor(e => e.Symbol, correct);
                    alternatives = wrongs.Append(correct).Distinct().OrderBy(_ => random.Next()).ToList();
                }
                else if (category == "element_names")
                {
                    questionText = $"What is the name for {NormalizeLabel(element.Symbol)}?";
                    correct = NormalizeLabel(element.Name);
                    var wrongs = WrongAnswersFor(e => e.Name, correct);
                    alternatives = wrongs.Append(correct).Distinct().OrderBy(_ => random.Next()).ToList();
                }
                else
                {
                    questionText = $"What is the symbol for {NormalizeLabel(element.Name)}?";
                    correct = NormalizeLabel(element.Symbol);
                    var wrongs = WrongAnswersFor(e => e.Symbol, correct);
                    alternatives = wrongs.Append(correct).Distinct().OrderBy(_ => random.Next()).ToList();
                }

                // Filter out blanks and duplicates
                alternatives = alternatives.Where(a => !string.IsNullOrWhiteSpace(a) && a != "---").Distinct().ToList();

                // If less than 4 alternatives, fill with random wrongs
                var allWrongs = elements
              .Select(e => category == "element_symbols" ? NormalizeLabel(e.Symbol) : NormalizeLabel(e.Name))
              .Where(a => !alternatives.Contains(a) && !string.IsNullOrWhiteSpace(a) && a != "---")
              .Distinct()
              .OrderBy(_ => random.Next())
              .ToList();

                int fillIndex = 0;
                while (alternatives.Count < 4 && fillIndex < allWrongs.Count)
                {
                    alternatives.Add(allWrongs[fillIndex]);
                    fillIndex++;
                }

                // If still less than 4, pad with empty strings (to avoid out-of-range)
                while (alternatives.Count < 4)
                {
                    alternatives.Add("");
                }

                questions.Add(new Question
                {
                    Text = questionText,
                    CorrectAnswer = correct,
                    Alternatives = alternatives,
                    BaseXp = 8 // You can use your getBaseXp logic here if needed
                });
            }

            return questions;
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
