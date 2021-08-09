using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

namespace DetailsInfo.Converters
{

    public class ArchiveConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Path.GetFileName(value.ToString());
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}
