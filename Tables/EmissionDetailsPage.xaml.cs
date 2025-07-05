using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;


namespace Atomic_PeriodicTable.Tables
{

    public sealed partial class EmissionDetailsPage : Page
    {

        public EmissionDetailsPage()
        {
            this.InitializeComponent();

        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Atomic_WinUI.Emission selectedEmission)
            {
                EmissionTitle.Text = $"Emission Spectrum of {selectedEmission.Name}:";
                EmissionImage.Source = new BitmapImage(new Uri(selectedEmission.EmissionImageUrl));
            }

        }


    }
}

