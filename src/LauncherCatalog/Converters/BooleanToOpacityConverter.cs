using System.Globalization;
using System.Windows.Data;

namespace LauncherCatalog.Converters;

public sealed class BooleanToOpacityConverter : IValueConverter
{
    public double TrueOpacity { get; set; } = 1.0;
    public double FalseOpacity { get; set; } = 0.35;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? TrueOpacity : FalseOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
