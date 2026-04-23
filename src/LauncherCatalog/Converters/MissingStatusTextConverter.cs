using System.Globalization;
using System.Windows.Data;

namespace LauncherCatalog.Converters;

public sealed class MissingStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "Missing" : "Ready";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
