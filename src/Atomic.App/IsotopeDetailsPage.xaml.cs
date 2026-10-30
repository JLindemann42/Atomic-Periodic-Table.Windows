using Atomic.App.Tables;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using Atomic.App.Services;

namespace Atomic.App
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

            if (e.Parameter is Atomic.App.Isotope isotope)
            {
                elementName = isotope.Name;
            }
            if (e.Parameter is IsotopeInfo isoInfo)
            {
                elementName = isoInfo.ElementFullName;
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
                if (ElementRepository.Raw(elementName) is { } row)
                {
                    IsotopeDetails.Clear();
                    int i = 1;
                    while (row.TryGetProperty($"iso_{i}", out _))
                    {
                        IsotopeDetails.Add(new IsotopeDetail
                        {
                            Name = row.Field($"iso_{i}"),
                            Mass = row.Field($"iso_mass_{i}"),
                            DecayType = row.Field($"decay_type_{i}"),
                            HalfLife = row.Field($"iso_half_{i}"),
                            Protons = row.Field($"iso_Z_{i}"),
                            Neutrons = row.Field($"iso_N_{i}"),
                            Nucleons = row.Field($"iso_A_{i}")
                        });
                        i++;
                    }
                }
            }

            this.DataContext = this;
        }
    }
}
