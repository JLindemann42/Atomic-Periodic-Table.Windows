using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;


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

