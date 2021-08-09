using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DetailsInfo.Converters
{
    public class WidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string filler = new string(' ', 5);
            return filler + value + filler;

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}
