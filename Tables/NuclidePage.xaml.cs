using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI;

namespace Atomic_PeriodicTable.Tables
{
    public sealed partial class NuclidePage : Page
    {
        private const double IsotopeBlockWidth = 90;
        private const double IsotopeBlockHeight = 90;
        private const double HorizontalSpacing = 4;
        private const double VerticalSpacing = 4;
        private const double LeftPadding = 56;
        private const double TopPadding = 78;

        private bool _isDragging = false;
        private Windows.Foundation.Point _lastPoint;

        private int _totalFiles = 0;
        private int _filesLoaded = 0;

        public NuclidePage()
        {
            this.InitializeComponent();

            //Check PRO or Not:
            if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
            {
                ScrollViewer.Visibility = Visibility.Visible;
                NoProContent.Visibility = Visibility.Collapsed;
            }
            else
            {
                ScrollViewer.Visibility = Visibility.Collapsed;
                NoProContent.Visibility = Visibility.Visible;
            }
            _ = LoadAllElementsAsync();
        }

        //If User isn't PRO User:
        private void OpenProPage_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProPage));
        }

        private void ShowInfoPanel(object sender, RoutedEventArgs e)
        {
            ShowInfoButton.Visibility = Visibility.Collapsed;
            NuclideInfoPanel.Visibility = Visibility.Visible;
            VisualStateManager.GoToState(this, "ExpandedState", true);
        }

        private void HideInfoPanel(object sender, RoutedEventArgs e)
        {
            VisualStateManager.GoToState(this, "CollapsedState", true);
            ShowInfoButton.Visibility = Visibility.Visible;
            NuclideInfoPanel.Visibility = Visibility.Collapsed;
        }


        private readonly Dictionary<string, Color> DecayColors = new()
        {
            ["b-"] = Colors.IndianRed,
            ["b+"] = Colors.LightGreen,
            ["p"] = Colors.LightBlue,
            ["stable"] = Colors.LightGray,
            ["p+"] = Colors.LightPink,
            ["alpha"] = Colors.Gold,
            ["n"] = Colors.Goldenrod,
            ["electron capture"] = Colors.LightSeaGreen,
        };

        private Color GetDecayColor(string decayType)
        {
            string key = decayType.ToLowerInvariant();
            return DecayColors.TryGetValue(key, out var color) ? color : Colors.LightSlateGray;
        }

        private async Task LoadAllElementsAsync()
        {
            NuclideCanvas.Children.Clear();

            try
            {
                StorageFolder installedLocation = Package.Current.InstalledLocation;
                StorageFolder elementsFolder = await installedLocation.GetFolderAsync("Elements");
                var files = await elementsFolder.GetFilesAsync();

                _totalFiles = files.Count;
                _filesLoaded = 0;

                LoadingPanel.Visibility = Visibility.Visible;
                LoadingProgressRing.IsActive = true;

                foreach (var file in files)
                {
                    try
                    {
                        string jsonText = await FileIO.ReadTextAsync(file);
                        using JsonDocument doc = JsonDocument.Parse(jsonText);
                        JsonElement root = doc.RootElement;

                        string shortName = root.GetProperty("short").GetString() ?? "N/A";
                        string fullName = root.TryGetProperty("element", out var nameProp) ? nameProp.GetString() ?? shortName : shortName;

                        if (!root.TryGetProperty("element_protons", out JsonElement protonsElem))
                            continue;

                        if (!int.TryParse(protonsElem.GetString(), out int protonNumber))
                            continue;

                        for (int i = 1; i <= 25; i++)
                        {
                            string isoKey = $"iso_{i}";
                            string decayKey = $"decay_type_{i}";
                            string isoANumberKey = $"iso_A_{i}";
                            string isoNNumberKey = $"iso_N_{i}";

                            if (root.TryGetProperty(isoKey, out JsonElement isoElem) &&
                                root.TryGetProperty(decayKey, out JsonElement decayElem) &&
                                root.TryGetProperty(isoANumberKey, out JsonElement isoANumElem) &&
                                root.TryGetProperty(isoNNumberKey, out JsonElement isoNNumElem))
                            {
                                string isotopeName = isoElem.GetString() ?? "";
                                string decayType = decayElem.GetString() ?? "";
                                string isotopeMassNum = isoANumElem.GetString() ?? "";
                                string neutronNumberStr = isoNNumElem.GetString() ?? "";

                                if (!string.IsNullOrWhiteSpace(isotopeName) && isotopeName != "---" &&
                                    !string.IsNullOrWhiteSpace(decayType) && decayType != "---" &&
                                    !string.IsNullOrWhiteSpace(isotopeMassNum) && isotopeMassNum != "---" &&
                                    !string.IsNullOrWhiteSpace(neutronNumberStr) && neutronNumberStr != "---" &&
                                    int.TryParse(neutronNumberStr, out int neutronNumber))
                                {
                                    AddIsotopeBlock(
                                        elementShort: shortName,
                                        elementFull: fullName,
                                        decayType: decayType,
                                        isotopeMassNumber: isotopeMassNum,
                                        protonNumber: protonNumber,
                                        neutronNumber: neutronNumber,
                                        elementJsonRoot: root);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading file {file.Name}: {ex.Message}");
                    }

                    _filesLoaded++;
                    LoadingText.Text = $"Loading: {_filesLoaded}/{_totalFiles}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error accessing Elements folder: {ex.Message}");
            }

            LoadingProgressRing.IsActive = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }

        private void AddIsotopeBlock(string elementShort, string elementFull, string decayType, string isotopeMassNumber,
                              int protonNumber, int neutronNumber, JsonElement elementJsonRoot)
        {
            var border = new Border
            {
                Width = IsotopeBlockWidth,
                Height = IsotopeBlockHeight,
                Background = new SolidColorBrush(GetDecayColor(decayType)),
                BorderBrush = new SolidColorBrush(Colors.DarkGray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(4),
                Tag = new IsotopeInfo
                {
                    ElementShortName = elementShort,
                    ElementFullName = elementFull,
                    DecayType = decayType,
                    IsotopeMassNumber = isotopeMassNumber,
                    ProtonNumber = protonNumber,
                    NeutronNumber = neutronNumber,
                    ElementJson = elementJsonRoot
                }
            };

            var grid = new Grid();

            var massNumber = new TextBlock
            {
                Text = isotopeMassNumber,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 2, 0),
                Foreground = new SolidColorBrush(Colors.Black)
            };

            var elementSymbol = new TextBlock
            {
                Text = elementShort,
                FontSize = 26,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.Black)
            };

            var charge = new TextBlock
            {
                Text = GetChargeSymbol(decayType), // helper method to format decay types like β⁻
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 0, 2),
                Foreground = new SolidColorBrush(Colors.Black)
            };

            grid.Children.Add(massNumber);
            grid.Children.Add(elementSymbol);
            grid.Children.Add(charge);

            border.Child = grid;

            double x = LeftPadding + HorizontalSpacing + neutronNumber * (IsotopeBlockWidth + HorizontalSpacing);
            double y = VerticalSpacing - TopPadding + protonNumber * (IsotopeBlockHeight + VerticalSpacing);

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);

            border.Tapped += OnIsotopeTapped;

            NuclideCanvas.Children.Add(border);
        }

        private string GetChargeSymbol(string decayType)
        {
            return decayType.ToLowerInvariant() switch
            {
                "b-" => "β⁻",
                "b+" => "β⁺",
                "alpha" => "α",
                "n" => "n",
                "p" => "p",
                "p+" => "p⁺",
                "electron capture" => "EC",
                "stable" => "",
                _ => decayType
            };
        }

        private void OnIsotopeTapped(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is IsotopeInfo isotopeNuc)
            {
                Frame.Navigate(typeof(IsotopeDetailsPage), isotopeNuc);
            }
        }

        private void ScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv == null) return;

            _isDragging = true;
            _lastPoint = e.GetCurrentPoint(sv).Position;
            sv.CapturePointer(e.Pointer);
        }

        private void ScrollViewer_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (!_isDragging || sv == null) return;

            var currentPoint = e.GetCurrentPoint(sv).Position;
            double deltaX = _lastPoint.X - currentPoint.X;
            double deltaY = _lastPoint.Y - currentPoint.Y;

            sv.ChangeView(sv.HorizontalOffset + deltaX, sv.VerticalOffset + deltaY, sv.ZoomFactor);

            _lastPoint = currentPoint;
        }

        private void ScrollViewer_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            var sv = sender as ScrollViewer;
            _isDragging = false;
            sv?.ReleasePointerCapture(e.Pointer);
        }

        private void ScrollViewer_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            var sv = sender as ScrollViewer;
            _isDragging = false;
            sv?.ReleasePointerCapture(e.Pointer);
        }


    }

    public class IsotopeInfo
    {
        public string ElementShortName { get; set; }
        public string ElementFullName { get; set; } // <-- Newly added
        public string DecayType { get; set; }
        public string IsotopeMassNumber { get; set; }
        public int ProtonNumber { get; set; }
        public int NeutronNumber { get; set; }
        public JsonElement ElementJson { get; set; }
    }
}
