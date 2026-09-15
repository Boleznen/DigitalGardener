using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DigitalGardener
{
    public class BoolToColorConverter : IValueConverter
    {
        public static readonly BoolToColorConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isProblematic)
                return isProblematic
                    ? System.Windows.Media.Brushes.OrangeRed
                    : System.Windows.Media.Brushes.LightGray;
            return System.Windows.Media.Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}