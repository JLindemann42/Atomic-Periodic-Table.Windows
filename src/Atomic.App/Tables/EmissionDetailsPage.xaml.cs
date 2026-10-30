using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using Atomic.Core.Localization;


namespace Atomic.App.Tables
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

            if (e.Parameter is Emission selectedEmission)
            {
                EmissionTitle.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.emission_spectrum_of, selectedEmission.Name);
                EmissionImage.Source = new BitmapImage(new Uri(selectedEmission.EmissionImageUrl));
            }

        }


    }
}

