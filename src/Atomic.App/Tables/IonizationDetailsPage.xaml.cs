using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using Atomic.App.Services;
using Atomic.Core.Localization;


namespace Atomic.App.Tables
{

    public sealed partial class IonizationDetailsPage : Page
    {
        public ObservableCollection<string> IonizationEnergies { get; } = new();

        public IonizationDetailsPage()
        {
            this.InitializeComponent();
            IonizationListView.ItemsSource = IonizationEnergies;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            string elementName = null;

            if (e.Parameter is Ionization selectedIonization)
            {
                elementName = selectedIonization.Name;
            }
            else if (e.Parameter is Element element)
            {
                elementName = !string.IsNullOrEmpty(element.OriginalName) ? element.OriginalName : element.Name;
            }
            else if (e.Parameter is string name)
            {
                elementName = name;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Unsupported navigation parameter type");
                return; // or handle gracefully
            }

            IonizationTitle.Text = Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(S.ionization_for, elementName);

            if (!string.IsNullOrEmpty(elementName))
            {
                if (ElementRepository.Raw(elementName) is { } row)
                {
                    {
                        IonizationEnergies.Clear();
                        int i = 1;
                        while (row.TryGetProperty($"element_ionization_energy{i}", out _))
                        {
                            var energy = row.Field($"element_ionization_energy{i}");
                            IonizationEnergies.Add($"{i}: {energy}");
                            i++;
                        }
                    }
                }
            }
        }
    }
}

