using System.Globalization;
using System.Windows.Data;

namespace Quota.Converters;

public sealed class FillWidthMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return 0d;

        var totalWidth = values[0] is double width ? width : 0d;
        var percent = values[1] is double fillPercent ? fillPercent : 0d;

        return Math.Max(0, totalWidth * percent / 100d);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
