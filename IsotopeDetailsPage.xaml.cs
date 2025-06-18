using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Text.Json.Nodes;
using Atomic_WinUI;
using Microsoft.UI.Xaml;

namespace Atomic_PeriodicTable
{
    public class IsotopeDetail
    {
        public string Name { get; set; }
        public string Mass { get; set; }
        public string HalfLife { get; set; }
        public string DecayType { get; set; }
        public string Protons { get; set; }
        public string Neutrons { get; set; }
        public string Nucleons { get; set; }
    }

    public sealed partial class IsotopeDetailsPage : Page
    {
        public ObservableCollection<IsotopeDetail> IsotopeDetails { get; } = new();

        public IsotopeDetailsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            string elementName = null;

            if (e.Parameter is Atomic_WinUI.Isotope isotope)
            {
                elementName = isotope.Name;
            }
            else if (e.Parameter is Element element)
            {
                elementName = !string.IsNullOrEmpty(element.OriginalName) ? element.OriginalName : element.Name;
            }
            else if (e.Parameter is string name)
            {
                elementName = name;
            }

            if (!string.IsNullOrEmpty(elementName))
            {
                string jsonFilePath = Path.Combine(AppContext.BaseDirectory, "Elements", $"{elementName.ToLower()}.json");
                if (File.Exists(jsonFilePath))
                {
                    string jsonContent = File.ReadAllText(jsonFilePath);
                    var node = JsonNode.Parse(jsonContent)?.AsObject();
                    if (node != null)
                    {
                        IsotopeDetails.Clear();
                        int i = 1;
                        while (node.ContainsKey($"iso_{i}"))
                        {
                            IsotopeDetails.Add(new IsotopeDetail
                            {
                                Name = node[$"iso_{i}"]?.ToString(),
                                Mass = node[$"iso_mass_{i}"]?.ToString(),
                                DecayType = node[$"decay_type_{i}"]?.ToString(),
                                HalfLife = node[$"iso_half_{i}"]?.ToString(),
                                Protons = node[$"iso_Z_{i}"]?.ToString(),
                                Neutrons = node[$"iso_N_{i}"]?.ToString(),
                                Nucleons = node[$"iso_A_{i}"]?.ToString()
                            });
                            i++;
                        }
                    }
                }
            }

            this.DataContext = this;
        }
    }
}