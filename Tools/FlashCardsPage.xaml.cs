using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
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

        public FlashCardsPage()
        {
            this.InitializeComponent();
            GenerateFlashCard();
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

        private void GenerateFlashCard()
        {
            FeedbackText.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;

            // Get all element files
            var folder = Windows.ApplicationModel.Package.Current.InstalledLocation;
            var elementsFolderTask = folder.GetFolderAsync("Elements").AsTask();
            elementsFolderTask.Wait();
            var elementsFolder = elementsFolderTask.Result;
            var filesTask = elementsFolder.GetFilesAsync().AsTask();
            filesTask.Wait();
            var files = filesTask.Result.Where(f => f.FileType == ".json").ToList();

            if (files.Count < 4)
            {
                ElementNameText.Text = "Not enough elements!";
                ElementSymbolText.Text = "";
                QuestionText.Text = "Please ensure the Elements folder contains at least 4 element JSON files.";
                AltButton1.IsEnabled = false;
                AltButton2.IsEnabled = false;
                AltButton3.IsEnabled = false;
                AltButton4.IsEnabled = false;
                NextButton.IsEnabled = false;
                return;
            }

            // Randomly pick 4 distinct files
            var rnd = new Random();
            var selectedFiles = files.OrderBy(_ => rnd.Next()).Take(4).ToList();

            // Randomly pick one as the correct answer
            int correctIndex = rnd.Next(4);
            var correctFile = selectedFiles[correctIndex];

            // Parse all 4 files
            _alternatives.Clear();
            foreach (var file in selectedFiles)
            {
                var textTask = FileIO.ReadTextAsync(file).AsTask();
                textTask.Wait();
                var json = textTask.Result;
                try
                {
                    var element = ParseElementJson(json);
                    _alternatives.Add(element);
                }
                catch { _alternatives.Add(new ElementData { Name = "Invalid", Symbol = "", Number = 0, AtomicMass = 0, ElectricalType = "Unknown" }); }
            }

            _currentElement = _alternatives[correctIndex];

            ElementNameText.Text = _currentElement.Name;
            ElementSymbolText.Text = _currentElement.Symbol;

            // Randomly pick question type
            var questionTypes = new[] { "number", "atomic_mass", "electrical_type" };
            _currentQuestionType = questionTypes[rnd.Next(questionTypes.Length)];

            switch (_currentQuestionType)
            {
                case "number":
                    QuestionText.Text = $"What element number does {_currentElement.Name} have?";
                    _correctAnswer = _currentElement.Number;
                    SetAlternatives(e => e.Number, _currentElement.Number, isDouble: false);
                    break;
                case "atomic_mass":
                    QuestionText.Text = $"What atomic mass does {_currentElement.Name} have?";
                    _correctAnswer = _currentElement.AtomicMass;
                    SetAlternatives(e => e.AtomicMass, _currentElement.AtomicMass, isDouble: true);
                    break;
                case "electrical_type":
                    QuestionText.Text = $"What kind of electrical type is {_currentElement.Name}?";
                    _correctAnswer = _currentElement.ElectricalType;
                    SetAlternatives(e => e.ElectricalType, _currentElement.ElectricalType, isDouble: false);
                    break;
            }
        }

        // Only use the 4 loaded alternatives
        private void SetAlternatives<T>(Func<ElementData, T> selector, T correct, bool isDouble = false)
        {
            var rnd = new Random();
            // Shuffle alternatives
            var altList = _alternatives.OrderBy(_ => rnd.Next()).ToList();

            AltButton1.Content = isDouble ? $"{Convert.ToDouble(selector(altList[0])):0.###}" : selector(altList[0])?.ToString();
            AltButton2.Content = isDouble ? $"{Convert.ToDouble(selector(altList[1])):0.###}" : selector(altList[1])?.ToString();
            AltButton3.Content = isDouble ? $"{Convert.ToDouble(selector(altList[2])):0.###}" : selector(altList[2])?.ToString();
            AltButton4.Content = isDouble ? $"{Convert.ToDouble(selector(altList[3])):0.###}" : selector(altList[3])?.ToString();

            AltButton1.Tag = selector(altList[0]);
            AltButton2.Tag = selector(altList[1]);
            AltButton3.Tag = selector(altList[2]);
            AltButton4.Tag = selector(altList[3]);

            AltButton1.IsEnabled = true;
            AltButton2.IsEnabled = true;
            AltButton3.IsEnabled = true;
            AltButton4.IsEnabled = true;
        }

        private void AltButton_Click(object sender, RoutedEventArgs e)
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
                isCorrect = Equals(selected, _correctAnswer);
            }

            FeedbackText.Text = isCorrect ? "Correct!" : $"Wrong! Correct answer: {FormatAnswer(_correctAnswer)}";
            FeedbackText.Foreground = isCorrect ? new SolidColorBrush(Microsoft.UI.Colors.Green) : new SolidColorBrush(Microsoft.UI.Colors.Red);
            FeedbackText.Visibility = Visibility.Visible;

            AltButton1.IsEnabled = false;
            AltButton2.IsEnabled = false;
            AltButton3.IsEnabled = false;
            AltButton4.IsEnabled = false;

            NextButton.Visibility = Visibility.Visible;
        }

        private string FormatAnswer(object answer)
        {
            if (_currentQuestionType == "atomic_mass")
                return $"{Convert.ToDouble(answer):0.###}";
            return answer?.ToString();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateFlashCard();
        }
    }
}
