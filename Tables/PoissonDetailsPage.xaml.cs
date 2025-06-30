using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Atomic_WinUI;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Windows.Storage;


namespace Atomic_PeriodicTable.Tables
{

    public sealed partial class PoissonDetailsPage : Page
    {

        public PoissonDetailsPage()
        {
            this.InitializeComponent();
            //Check PRO or Not:
            if ((ApplicationData.Current.LocalSettings.Values["IsProUser"] as bool?) == true)
            {
                MainContent.Visibility = Visibility.Visible;
                NoProContent.Visibility = Visibility.Collapsed;
            }
            else
            {
                MainContent.Visibility = Visibility.Collapsed;
                NoProContent.Visibility = Visibility.Visible;
            }
        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProPage));
        }


        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            string poissonName = null;
            double poissonStart = 0.0;
            double poissonEnd = 0.0;


            if (e.Parameter is Atomic_WinUI.Poisson selectedPoisson)
            {
                poissonName = selectedPoisson.Name;
                poissonStart = selectedPoisson.StartNumber;
                poissonEnd = selectedPoisson.EndNumber;
            }

            //Wait until layout is measured before setting progress
            ProgressContainer.SizeChanged += (s, e) =>
            {
                SetProgress(poissonStart, poissonEnd); // Setting the start value and end value (error -> 0.0)
            };

            //Setup text depending on selected ratio:
            PoissonTitle.Text = $"Poisson's Ratio for {poissonName}:";
            PoissonRangeInfo.Text = $"Value Range: {poissonStart} - {poissonEnd}";

        }

        private void SetProgress(double startPercent, double endPercent)
        {
            double maxRange = 0.5; // Setting 0.5 as the full width
            double totalWidth = ProgressContainer.ActualWidth;

            double scaleFactor = totalWidth / maxRange; // Scale to fit 0.5 as the max
            double startX = scaleFactor * startPercent;
            double width = scaleFactor * (endPercent - startPercent);

            ProgressBar.Margin = new Thickness(startX, 0, 0, 0);
            ProgressBar.Width = width;
        }

    }
}

