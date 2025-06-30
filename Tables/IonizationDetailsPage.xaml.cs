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


namespace Atomic_PeriodicTable.Tables
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

            if (e.Parameter is Atomic_WinUI.Ionization selectedIonization)
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

            IonizationTitle.Text = $"Ionization Energies for {elementName}";

            if (!string.IsNullOrEmpty(elementName))
            {
                string jsonFilePath = Path.Combine(AppContext.BaseDirectory, "Elements", $"{elementName.ToLower()}.json");
                if (File.Exists(jsonFilePath))
                {
                    string jsonContent = File.ReadAllText(jsonFilePath);
                    var node = JsonNode.Parse(jsonContent)?.AsObject();
                    if (node != null)
                    {
                        IonizationEnergies.Clear();
                        int i = 1;
                        while (node.ContainsKey($"element_ionization_energy{i}"))
                        {
                            var energy = node[$"element_ionization_energy{i}"]?.ToString();
                            IonizationEnergies.Add($"{i}: {energy}");
                            i++;
                        }
                    }
                }
            }
        }
    }
}

