using System.Globalization;
using System.Windows.Data;
using LauncherCatalog.Services;

namespace LauncherCatalog.Converters;

public sealed class MissingStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? LocalizationService.Current.Get("MissingStatus.Missing")
            : LocalizationService.Current.Get("MissingStatus.Ready");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
