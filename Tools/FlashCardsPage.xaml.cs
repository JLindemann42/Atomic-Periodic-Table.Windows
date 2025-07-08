using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Atomic_PeriodicTable.Tables; // For ProPage access

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Atomic_PeriodicTable.Tables
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class FlashCardsPage : Page
    {
        private class ElementData
        {
            public string Name { get; set; }
            public string Symbol { get; set; }
            public int Number { get; set; }
            public double AtomicMass { get; set; }
            public string ElectricalType { get; set; }
        }

        private List<ElementData> _alternatives = new();
        private ElementData _currentElement;
        private string _currentQuestionType;
        private object _correctAnswer;

        // Quiz state
        private int _currentQuestionIndex = 0;
        private int _totalQuestions = 0;
        private List<string> _questionTypes = new();
        private List<(string type, ElementData correct, List<ElementData> alternatives, int? numberForReverse)> _quizQuestions = new();
        private Random _rnd = new();

        // User stats
        private int _correctCount = 0;
        private int _lives = 5;
        private int _maxLives = 5;
        private DateTimeOffset _lastReset = DateTimeOffset.MinValue;
        private const string LivesKey = "FlashcardLives";
        private const string LastResetKey = "FlashcardLastReset";

        // Pro status
        private bool IsProUser => ProPage.IsProUser;
        private bool IsProPlus => ProPage.IsProPlus;

        // Unlimited lives tracking
        private bool _unlimitedLives = false;

        // Add backing properties for element button data binding
        public string QuizElementNumber { get; set; } = "1";
        public string QuizElementSymbol { get; set; } = "H";
        public string QuizElementName { get; set; } = "Hydrogen";

        public string Alt1Number { get; set; } = "";
        public string Alt1Symbol { get; set; } = "";
        public string Alt1Name { get; set; } = "";

        public string Alt2Number { get; set; } = "";
        public string Alt2Symbol { get; set; } = "";
        public string Alt2Name { get; set; } = "";

        public string Alt3Number { get; set; } = "";
        public string Alt3Symbol { get; set; } = "";
        public string Alt3Name { get; set; } = "";

        public string Alt4Number { get; set; } = "";
        public string Alt4Symbol { get; set; } = "";
        public string Alt4Name { get; set; } = "";

        public FlashCardsPage()
        {
            this.InitializeComponent();
            this.DataContext = this;
            SetMaxLivesByPro();
            LoadLives();
            UpdateStatsUI();
            // Show difficulty selection, hide quiz panel
            DifficultyPanel.Visibility = Visibility.Visible;
            QuizPanel.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed; // Hide back button initially
        }

        private void SetMaxLivesByPro()
        {
            if (IsProPlus)
            {
                _maxLives = int.MaxValue;
            }
            else if (IsProUser)
            {
                _maxLives = 20;
            }
            else
            {
                _maxLives = 5;
            }
        }

        private void LoadLives()
        {
            SetMaxLivesByPro();

            if (IsProPlus)
            {
                _lives = _maxLives;
                _lastReset = DateTimeOffset.Now;
                SaveLives();
                return;
            }

            var settings = ApplicationData.Current.LocalSettings.Values;
            if (settings.ContainsKey(LivesKey) && settings.ContainsKey(LastResetKey))
            {
                _lives = (int)settings[LivesKey];
                _lastReset = DateTimeOffset.Parse((string)settings[LastResetKey]);
                // Reset lives if 24h passed
                if ((DateTimeOffset.Now - _lastReset).TotalHours >= 24)
                {
                    _lives = _maxLives;
                    _lastReset = DateTimeOffset.Now;
                    SaveLives();
                }
            }
            else
            {
                _lives = _maxLives;
                _lastReset = DateTimeOffset.Now;
                SaveLives();
            }
        }

        private void SaveLives()
        {
            if (IsProPlus)
                return; // No need to save for unlimited

            var settings = ApplicationData.Current.LocalSettings.Values;
            settings[LivesKey] = _lives;
            settings[LastResetKey] = _lastReset.ToString("o");
        }

        private void UpdateStatsUI()
        {
            CorrectText.Text = $"Correct: {_correctCount}";
            if (_unlimitedLives)
                LivesText.Text = $"Lives: ∞";
            else
                LivesText.Text = $"Lives: {_lives}";
        }

        private void DifficultyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_unlimitedLives && _lives <= 0)
            {
                DifficultyPanel.Visibility = Visibility.Collapsed;
                QuizPanel.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                QuestionText.Text = $"You have no lives left. Lives reset every 24 hours.";
                AltButton1.Visibility = Visibility.Collapsed;
                AltButton2.Visibility = Visibility.Collapsed;
                AltButton3.Visibility = Visibility.Collapsed;
                AltButton4.Visibility = Visibility.Collapsed;
                ProgressText.Text = "";
                NextButton.Visibility = Visibility.Collapsed;
                return;
            }

            var btn = sender as Button;
            var difficulty = btn.Tag as string;
            switch (difficulty)
            {
                case "Easy":
                    _totalQuestions = 10;
                    _questionTypes = new List<string> { "number", "reverse_number" };
                    break;
                case "Medium":
                    _totalQuestions = 20;
                    _questionTypes = new List<string> { "number", "reverse_number", "atomic_mass" };
                    break;
                case "Hard":
                    _totalQuestions = 30;
                    _questionTypes = new List<string> { "number", "reverse_number", "atomic_mass", "electrical_type" };
                    break;
            }
            DifficultyPanel.Visibility = Visibility.Collapsed;
            QuizPanel.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Visible; // Show back button during quiz
            _currentQuestionIndex = 0;
            _quizQuestions.Clear();
            _correctCount = 0;
            StartQuiz();
            PrepareQuizQuestions();
            ShowCurrentQuestion();
        }

        private void StartQuiz()
        {
            SetInitialLives();
        }

        private void SetInitialLives()
        {
            if (IsProPlus)
            {
                _unlimitedLives = true;
                _lives = int.MaxValue;
                LivesText.Text = "Lives: ∞";
            }
            else if (IsProUser)
            {
                _unlimitedLives = false;
                _lives = 20;
                LivesText.Text = $"Lives: {_lives}";
            }
            else
            {
                _unlimitedLives = false;
                _lives = 5; // Default for non-pro users
                LivesText.Text = $"Lives: {_lives}";
            }
        }

        private void PrepareQuizQuestions()
        {
            // Get all element files
            var folder = Windows.ApplicationModel.Package.Current.InstalledLocation;
            var elementsFolderTask = folder.GetFolderAsync("Elements").AsTask();
            elementsFolderTask.Wait();
            var elementsFolder = elementsFolderTask.Result;
            var filesTask = elementsFolder.GetFilesAsync().AsTask();
            filesTask.Wait();
            var files = filesTask.Result.Where(f => f.FileType == ".json").ToList();

            // Preload all elements for quiz generation
            var allElements = new List<ElementData>();
            foreach (var file in files)
            {
                var textTask = FileIO.ReadTextAsync(file).AsTask();
                textTask.Wait();
                var json = textTask.Result;
                try
                {
                    var element = ParseElementJson(json);
                    allElements.Add(element);
                }
                catch { }
            }

            // Generate questions
            for (int i = 0; i < _totalQuestions; i++)
            {
                // Pick question type
                var qType = _questionTypes[_rnd.Next(_questionTypes.Count)];
                // Pick 4 distinct elements
                var alternatives = allElements.OrderBy(_ => _rnd.Next()).Take(4).ToList();
                int correctIndex = _rnd.Next(4);
                var correct = alternatives[correctIndex];

                int? numberForReverse = null;
                if (qType == "reverse_number")
                {
                    // For reverse, pick a number from one of the alternatives
                    correct = alternatives[correctIndex];
                    numberForReverse = correct.Number;
                }

                _quizQuestions.Add((qType, correct, alternatives, numberForReverse));
            }
        }

        private void ShowCurrentQuestion()
        {
            FeedbackText.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;

            if (_currentQuestionIndex >= _totalQuestions)
            {
                // Quiz finished
                QuestionText.Text = $"Quiz Complete!\nYou finished all {_totalQuestions} questions.\nCorrect: {_correctCount} / {_totalQuestions}";
                AltButton1.Visibility = Visibility.Collapsed;
                AltButton2.Visibility = Visibility.Collapsed;
                AltButton3.Visibility = Visibility.Collapsed;
                AltButton4.Visibility = Visibility.Collapsed;
                QuizElementDisplay.Visibility = Visibility.Collapsed;
                ProgressText.Text = "";

                // Lose a life if majority wrong (unless unlimited lives)
                if (!_unlimitedLives && _correctCount < (_totalQuestions / 2) + (_totalQuestions % 2))
                {
                    _lives = Math.Max(0, _lives - 1);
                    SaveLives();
                    UpdateStatsUI();
                    QuestionText.Text += $"\nYou lost a life!";
                }
                else if (!_unlimitedLives)
                {
                    QuestionText.Text += $"\nYou kept your life!";
                }
                else
                {
                    QuestionText.Text += $"\nUnlimited lives!";
                }
                BackButton.Visibility = Visibility.Visible;
                return;
            }

            AltButton1.Visibility = Visibility.Visible;
            AltButton2.Visibility = Visibility.Visible;
            AltButton3.Visibility = Visibility.Visible;
            AltButton4.Visibility = Visibility.Visible;
            QuizElementDisplay.Visibility = Visibility.Visible;

            var (qType, correct, alternatives, numberForReverse) = _quizQuestions[_currentQuestionIndex];
            _alternatives = alternatives;
            _currentElement = correct;
            _currentQuestionType = qType == "reverse_number" ? "reverse_number" : qType;

            ProgressText.Text = $"Question {_currentQuestionIndex + 1} / {_totalQuestions}";
            UpdateStatsUI();

            BackButton.Visibility = Visibility.Visible;

            // Set the element display above the question, masking relevant part
            switch (_currentQuestionType)
            {
                case "number":
                    QuizElementNumber = "?";
                    QuizElementSymbol = _currentElement.Symbol;
                    QuizElementName = _currentElement.Name;
                    Bindings.Update();
                    QuestionText.Text = $"What element number does {_currentElement.Name} have?";
                    _correctAnswer = _currentElement.Number;
                    SetAlternativesElementButtons(e => e.Number.ToString(), _currentElement.Number.ToString(), mask: "number");
                    break;
                case "reverse_number":
                    QuizElementNumber = numberForReverse?.ToString() ?? "?";
                    QuizElementSymbol = "?";
                    QuizElementName = "?";
                    Bindings.Update();
                    QuestionText.Text = $"Which element has number {numberForReverse}?";
                    _correctAnswer = numberForReverse;
                    SetAlternativesElementButtons(e => e.Name, _currentElement.Name, mask: "name");
                    break;
                case "atomic_mass":
                    QuizElementNumber = _currentElement.Number.ToString();
                    QuizElementSymbol = _currentElement.Symbol;
                    QuizElementName = _currentElement.Name;
                    Bindings.Update();
                    QuestionText.Text = $"What atomic mass does {_currentElement.Name} have?";
                    _correctAnswer = _currentElement.AtomicMass;
                    SetAlternativesElementButtons(e => e.AtomicMass.ToString("0.###"), _currentElement.AtomicMass.ToString("0.###"), mask: "none");
                    break;
                case "electrical_type":
                    QuizElementNumber = _currentElement.Number.ToString();
                    QuizElementSymbol = _currentElement.Symbol;
                    QuizElementName = _currentElement.Name;
                    Bindings.Update();
                    QuestionText.Text = $"What kind of electrical type is {_currentElement.Name}?";
                    _correctAnswer = _currentElement.ElectricalType;
                    SetAlternativesElementButtons(e => e.ElectricalType, _currentElement.ElectricalType, mask: "none");
                    break;
            }
        }

        private void SetAlternativesElementButtons(Func<ElementData, string> selector, string correct, string mask)
        {
            var altList = _alternatives.OrderBy(_ => _rnd.Next()).ToList();

            // For each button, set the element info, masking as needed
            Alt1Number = altList[0].Number.ToString();
            Alt1Symbol = altList[0].Symbol;
            Alt1Name = altList[0].Name;

            Alt2Number = altList[1].Number.ToString();
            Alt2Symbol = altList[1].Symbol;
            Alt2Name = altList[1].Name;

            Alt3Number = altList[2].Number.ToString();
            Alt3Symbol = altList[2].Symbol;
            Alt3Name = altList[2].Name;

            Alt4Number = altList[3].Number.ToString();
            Alt4Symbol = altList[3].Symbol;
            Alt4Name = altList[3].Name;

            Bindings.Update();

            AltButton1.IsEnabled = true;
            AltButton2.IsEnabled = true;
            AltButton3.IsEnabled = true;
            AltButton4.IsEnabled = true;
        }

        private void AltButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var btn = sender as Button;
            var selected = btn.Tag;

            bool isCorrect;
            if (_currentQuestionType == "atomic_mass")
            {
                isCorrect = Math.Abs(Convert.ToDouble(selected) - Convert.ToDouble(_correctAnswer)) < 0.01;
            }
            else
            {
                isCorrect = Equals(selected?.ToString(), _correctAnswer?.ToString());
            }

            if (isCorrect)
                _correctCount++;
            else
                OnWrongAnswer();

            FeedbackText.Text = isCorrect ? "Correct!" : $"Wrong! Correct answer: {FormatAnswer(_correctAnswer)}";
            FeedbackText.Foreground = isCorrect ? new SolidColorBrush(Microsoft.UI.Colors.Green) : new SolidColorBrush(Microsoft.UI.Colors.Red);
            FeedbackText.Visibility = Visibility.Visible;

            AltButton1.IsEnabled = false;
            AltButton2.IsEnabled = false;
            AltButton3.IsEnabled = false;
            AltButton4.IsEnabled = false;

            UpdateStatsUI();
            NextButton.Visibility = Visibility.Visible;
        }

        private void OnWrongAnswer()
        {
            if (!_unlimitedLives)
            {
                _lives--;
                LivesText.Text = $"Lives: {_lives}";
                if (_lives <= 0)
                {
                    // Handle game over logic
                    QuestionText.Text = "Game Over! You have no lives left.";
                    AltButton1.Visibility = Visibility.Collapsed;
                    AltButton2.Visibility = Visibility.Collapsed;
                    AltButton3.Visibility = Visibility.Collapsed;
                    AltButton4.Visibility = Visibility.Collapsed;
                    ProgressText.Text = "";
                    NextButton.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                LivesText.Text = "Lives: ∞";
            }
        }

        private string FormatAnswer(object answer)
        {
            if (_currentQuestionType == "atomic_mass")
                return $"{Convert.ToDouble(answer):0.###}";
            if (_currentQuestionType == "reverse_number")
            {
                // Show the correct element name for reverse_number
                var correct = _alternatives.FirstOrDefault(e => e.Number.Equals(answer));
                return correct?.Name ?? answer?.ToString();
            }
            return answer?.ToString();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            _currentQuestionIndex++;
            ShowCurrentQuestion();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Return to difficulty selection, reset quiz state
            QuizPanel.Visibility = Visibility.Collapsed;
            DifficultyPanel.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Collapsed;
            // Optionally reset stats
            _currentQuestionIndex = 0;
            _correctCount = 0;
            UpdateStatsUI();
        }

        // Helper to parse a single element JSON file
        private ElementData ParseElementJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Robustly extract the numeric part of atomic mass, e.g. "1.00784 (u)" => 1.00784
            string atomicMassRaw = root.TryGetProperty("element_atomicmass", out var massProp) ? massProp.GetString() ?? "" : "";
            string atomicMassClean = "";
            if (!string.IsNullOrWhiteSpace(atomicMassRaw))
            {
                // Find the first part that looks like a number (handles "1.00784 (u)", "227 (u)", etc)
                var parts = atomicMassRaw.Split(' ', '(', ')');
                foreach (var part in parts)
                {
                    if (double.TryParse(part, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var _))
                    {
                        atomicMassClean = part;
                        break;
                    }
                }
            }

            double atomicMassValue = 0.0;
            double.TryParse(atomicMassClean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out atomicMassValue);

            return new ElementData
            {
                Name = root.TryGetProperty("element", out var nameProp) ? nameProp.GetString() : "",
                Symbol = root.TryGetProperty("short", out var symbolProp) ? symbolProp.GetString() : "",
                Number = root.TryGetProperty("element_atomic_number", out var numberProp) && int.TryParse(numberProp.GetString(), out var n) ? n : 0,
                AtomicMass = atomicMassValue,
                ElectricalType = root.TryGetProperty("electrical_type", out var et) ? et.GetString() : "Unknown"
            };
        }
    }
}
