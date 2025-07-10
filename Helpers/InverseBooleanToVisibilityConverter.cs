using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Atomic_WinUI
{
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isLast = value is bool b && b;
            return isLast ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
