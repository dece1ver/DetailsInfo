using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DetailsInfo.Converters
{
    public class PriorityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return int.Parse(value?.ToString() ?? string.Empty) == 1000 ? "" : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}