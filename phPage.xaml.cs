using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atomic_WinUI
{
    public sealed partial class phPage : Page
    {
        public phPage()
        {
            this.InitializeComponent();
        }
        private void BromothymolInfoButtonClick(object sender, RoutedEventArgs e)
        {
            BromothymolInfoTip.IsOpen = true;
        }
        private void MethylOrangeInfoButtonClick(object sender, RoutedEventArgs e)
        {
            MethylOrangeInfoTip.IsOpen = true;
        }
        private void CongoRedInfoButtonClick(object sender, RoutedEventArgs e)
        {
            CongoRedInfoTip.IsOpen = true;
        }
        private void UniversalInfoButtonClick(object sender, RoutedEventArgs e)
        {
            UniversalInfoTip.IsOpen = true;
        }
    }
}