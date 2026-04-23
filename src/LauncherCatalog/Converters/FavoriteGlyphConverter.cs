using System.Globalization;
using System.Windows.Data;

namespace LauncherCatalog.Converters;

public sealed class FavoriteGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "\uE735" : "\uE734";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
