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
using Microsoft.UI.Xaml.Media.Imaging;


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

