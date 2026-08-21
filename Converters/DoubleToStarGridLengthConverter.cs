using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Quota.Converters;

public sealed class DoubleToStarGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var weight = value is double number ? number : 0d;
        return weight <= 0 ? new GridLength(0) : new GridLength(weight, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
