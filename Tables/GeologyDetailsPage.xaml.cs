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

    public sealed partial class GeologyDetailsPage : Page
    {

        public GeologyDetailsPage()
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


            if (e.Parameter is Atomic_WinUI.Geology selectedGeology)
            {
                GeologyTitle.Text = selectedGeology.Name;

                //Description
                Group.Text = $"- Group: {selectedGeology.Group}";
                Color.Text = $"- Color: {selectedGeology.Color}";
                Streak.Text = $"- Streak: {selectedGeology.Streak}";
                CristalStructure.Text = $"- Crystal Structure: {selectedGeology.Cristal}";
                Hardness.Text = $"- Hardness: {selectedGeology.Hardness}";
                Density.Text = $"- Density: {selectedGeology.Density}";
                Magnetism.Text = $"- Magnetism: {selectedGeology.Magnetism}";
                Hydrochloride.Text = $"- Hydrochloride: {selectedGeology.Hydrochloride}";


            }




        }


    }
}

