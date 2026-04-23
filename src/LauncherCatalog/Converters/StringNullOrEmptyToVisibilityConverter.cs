using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LauncherCatalog.Converters;

public sealed class StringNullOrEmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isEmpty = string.IsNullOrWhiteSpace(value as string);
        if (Invert)
        {
            isEmpty = !isEmpty;
        }

        return isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
