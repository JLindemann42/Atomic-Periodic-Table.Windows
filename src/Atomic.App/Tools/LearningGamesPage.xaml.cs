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
using Atomic.App.Tables;

using Atomic.App.Services;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Ai.Data;
using Atomic.Core.Progression;
using Atomic.Core.Quiz;
namespace Atomic.App.Tools
{
    public class GameResultItem
    {
        public string Question { get; set; }
        public string PickedAnswer { get; set; }
        public string CorrectAnswer { get; set; }
        public bool WasCorrect { get; set; }
        public int BaseXp { get; set; }
    }

    /// <summary>What a caller asks for when starting a game.</summary>
    /// <param name="Difficulty">"easy", "medium" or "hard". Was hardcoded before P8.</param>
    public sealed record GameRequest(string Category, string Difficulty);

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
            switch (e.Parameter)
            {
                // The request form carries the difficulty the user picked. The bare string is kept
                // because several call sites still navigate with one; it means "easy", which is
                // what the page always did.
                case GameRequest request:
                    category = request.Category;
                    difficulty = request.Difficulty;
                    break;
                case string categoryName:
                    category = categoryName;
                    break;
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
                    ProgressStore.Spend(5);

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
            totalQuestions = FlashcardCatalog.ExamFor(category) is { } exam
                ? FlashcardCatalog.ExamQuestionCount(exam, difficulty)
                : difficulty switch
                {
                    "easy" => 8,
                    "medium" => 16,
                    "hard" => 24,
                    _ => 8,
                };
            timeLimitMs = difficulty switch
            {
                "easy" => 20000,
                "medium" => 12000,
                "hard" => 7500,
                _ => 20000
            };

            questions = [.. BuildQuestions()];

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

        /// <summary>
        /// Build the whole question set for this game.
        /// </summary>
        /// <remarks>
        /// <para>Replaces a ~325-line if/else that read raw JSON strings, plus a per-question
        /// re-read of the element file - <c>ElementRepository.All()</c> and a JSON re-parse ran once
        /// for <i>every question</i>, so a 24-question hard game deserialized 118 elements 24 times
        /// over. The store is built once here and shared by every question.</para>
        ///
        /// <para>An exam draws across every category it covers rather than from one, which is what
        /// makes it an exam; <see cref="FlashcardCatalog"/> owns that list so the flashcard screen
        /// and this page cannot disagree about what a given exam contains.</para>
        /// </remarks>
        private IReadOnlyList<Question> BuildQuestions()
        {
            var store = RetrievalService.Get(ElementRepository.Source)?.Store;
            if (store is null) return [];

            var language = AppLanguage.BaseCode(AppLanguage.Current);
            var context = new GeneratorContext(
                store,
                KnowledgeStore.Overlay(ElementRepository.Source, language),
                ResourceStringProvider.For(language),
                difficulty);

            var built = new List<Question>(totalQuestions);

            foreach (var key in CategoriesToAsk())
            {
                if (QuestionGenerators.ForCategory(key) is not { } generator) continue;

                // An exam spreads its questions over its categories; a single-category game asks
                // for the lot from one.
                var share = Math.Max(1, totalQuestions / Math.Max(1, CategoriesToAsk().Count));
                foreach (var q in generator.Generate(context, share))
                {
                    built.Add(new Question
                    {
                        Text = q.Question,
                        CorrectAnswer = q.CorrectAnswer,
                        Alternatives = [.. q.Alternatives],
                        BaseXp = q.BaseXp,
                    });
                }
            }

            // Shuffled so an exam does not walk its categories in curriculum order, which would
            // tell the player what is coming.
            return built.OrderBy(_ => Random.Shared.Next()).Take(totalQuestions).ToList();
        }

        /// <summary>The categories this game draws from: one, or every category an exam covers.</summary>
        private IReadOnlyList<string> CategoriesToAsk() =>
            FlashcardCatalog.ExamFor(category) is { } exam
                ? FlashcardCatalog.CategoriesForExam(exam).Select(c => c.Key).ToList()
                : [category];


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
                SettingsStore.Lives = Math.Max(0, SettingsStore.Lives - GetLivesLost());
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

                // Deliberately a flag rather than a count: the only consumer is the "Perfect Game"
                // achievement, whose Goal is 1 and which reads this as `perfectGames > 0`.
                if (SettingsStore.PerfectGames == 0)
                {
                    SettingsStore.PerfectGames = 1;
                }
            }

            int correctCount = gameResults.Count(r => r.WasCorrect);
            int baseXp = gameResults.Sum(r => r.WasCorrect ? (int)(r.BaseXp * GetXpMultiplier()) : 0);
            xp = baseXp + bonusXp;

            // Storing the XP in local settings for FlashCardsPage
            SettingsStore.LastQuizXp = xp;

            // Adding XP to global XP/level system in XpManager Util
            ProgressStore.AddGameXp(xp);

            // Increment total games played
            SettingsStore.TotalGamesPlayed = SettingsStore.TotalGamesPlayed + 1;

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
