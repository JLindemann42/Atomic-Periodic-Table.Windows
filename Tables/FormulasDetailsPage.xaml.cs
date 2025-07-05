using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;


namespace Atomic_PeriodicTable.Tables
{

    public sealed partial class FormulasDetailsPage : Page
    {

        public FormulasDetailsPage()
        {
            this.InitializeComponent();

        }

        private void OpenProPage_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProPage));
        }


        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);


            if (e.Parameter is Atomic_WinUI.Equation selectedEquation)
            {
                FormulasTitle.Text = $"Info about {selectedEquation.Name}:";
                EquationImage.Source = new BitmapImage(new Uri($"ms-appx:///Equations/{selectedEquation.Image}.png"));
                FormulasDetails.Text = selectedEquation.Description;
            }




        }


    }
}

