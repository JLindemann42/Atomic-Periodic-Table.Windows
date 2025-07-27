using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace Atomic_PeriodicTable.Tools
{
    public class BoolToBrushConverter : IValueConverter
    {
        public Brush TrueBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
        public Brush FalseBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
                return b ? TrueBrush : FalseBrush;
            return FalseBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
