using System.Globalization;
using System.Windows.Data;

namespace LauncherCatalog.Converters;

public sealed class MissingStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var missing = value is true;
        var key = parameter as string;

        return key switch
        {
            "Foreground" => System.Windows.Application.Current.Resources[missing ? "DangerBrush" : "SuccessBrush"] as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Black,
            _ => System.Windows.Application.Current.Resources[missing ? "DangerSoftBrush" : "SuccessSoftBrush"] as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Transparent
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
