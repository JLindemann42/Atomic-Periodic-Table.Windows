using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Atomic.App.Services;
using Atomic.Core.Localization;


namespace Atomic.App.Tables
{

    public sealed partial class PoissonDetailsPage : Page
    {

        public PoissonDetailsPage()
        {
            this.InitializeComponent();
            //Check PRO or Not:
            if (SettingsStore.HasAnyPro)
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


            if (e.Parameter is Poisson selectedPoisson)
            {
                poissonName = selectedPoisson.Name;
                poissonStart = selectedPoisson.StartNumber;
                poissonEnd = selectedPoisson.EndNumber;
            }

            //Wait until layout is measured before setting progress
            ProgressContainer.SizeChanged += (s, e) =>
            {
                SetProgress1(poissonStart, poissonEnd); // Setting the start value and end value (error -> 0.0)
            };

            //Setup text depending on selected ratio:
            PoissonTitle.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.poisson_ratio_for, poissonName);
            PoissonRangeInfo.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.value_range, poissonStart, poissonEnd);

        }

        private void SetProgress1(double startPercent, double endPercent)
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

