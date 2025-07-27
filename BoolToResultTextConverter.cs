using Microsoft.UI.Xaml.Data;
using System;

namespace Atomic_PeriodicTable.Tools
{
    public class BoolToResultTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
                return b ? "Correct" : "Wrong";
            return "Wrong";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
