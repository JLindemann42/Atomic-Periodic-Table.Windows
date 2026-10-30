using Atomic.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;


namespace Atomic.App.Tables
{

    public sealed partial class GeologyDetailsPage : Page
    {

        public GeologyDetailsPage()
        {
            this.InitializeComponent();

        }

        /// <summary>One "- Label: value" row, with the label's own colon already in place.</summary>
        private static string Row(string label, string value) => "- " + label + " " + value;

        private void OpenProPage_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProPage));
        }


        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);


            if (e.Parameter is Geology selectedGeology)
            {
                GeologyTitle.Text = selectedGeology.Name;

                // The Text values in the markup are design-time only - every one of these is
                // overwritten here, which is why the XAML retrofit never saw them.
                var strings = Services.ResourceStringProvider.For(
                    Services.AppLanguage.BaseCode(Services.AppLanguage.Current));

                // The Android keys already carry their own colon.
                //
                // Type was declared in the markup and never assigned, so the row rendered as a
                // bare "- Type:" with nothing after it - visible only once the other eight stopped
                // being English and it was the odd one out.
                Type.Text = Row(strings.Get(S.type_label), selectedGeology.Type);
                Group.Text = Row(strings.Get(S.group_label), selectedGeology.Group);
                Color.Text = Row(strings.Get(S.color_label), selectedGeology.Color);
                CristalStructure.Text = Row(strings.Get(S.cristal_structure_label), selectedGeology.Cristal);
                Hardness.Text = Row(strings.Get(S.hardness_label), selectedGeology.Hardness);
                Density.Text = Row(strings.Get(S.density_colon), selectedGeology.Density);
                Magnetism.Text = Row(strings.Get(S.magnetism_label), selectedGeology.Magnetism);

                // Two stay English. Android has no geology "streak" - streak_label is the daily
                // play streak, and its Swedish reads "Svit:", which on a mineral page would be
                // confidently wrong rather than merely untranslated. Nothing carries
                // "hydrochloride" at all.
                Streak.Text = Row("Streak:", selectedGeology.Streak);
                Hydrochloride.Text = Row("Hydrochloride:", selectedGeology.Hydrochloride);


            }




        }


    }
}

