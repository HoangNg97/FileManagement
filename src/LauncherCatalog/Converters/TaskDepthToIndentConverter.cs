using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LauncherCatalog.Converters;

public sealed class TaskDepthToIndentConverter : IValueConverter
{
    public double IndentSize { get; set; } = 24;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var depth = value switch
        {
            int intDepth => intDepth,
            long longDepth => (int)longDepth,
            _ => 0
        };

        return new Thickness(depth * IndentSize, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
