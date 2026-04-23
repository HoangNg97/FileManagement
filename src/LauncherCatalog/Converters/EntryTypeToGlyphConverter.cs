using System.Globalization;
using System.Windows.Data;
using LauncherCatalog.Models;

namespace LauncherCatalog.Converters;

public sealed class EntryTypeToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is EntryType.Folder ? "\uE8B7" : "\uE756";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
