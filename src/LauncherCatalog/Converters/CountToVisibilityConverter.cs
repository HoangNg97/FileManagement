using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LauncherCatalog.Converters;

/// <summary>
/// Maps an integer count to <see cref="Visibility"/>. By default returns <see cref="Visibility.Visible"/>
/// when the count is zero (for empty-state placeholders); set <see cref="Invert"/> to <c>true</c> to
/// show content only when the count is greater than zero.
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            long l => (int)l,
            _ => 0
        };

        var isEmpty = count <= 0;
        var showWhenEmpty = !Invert;

        return isEmpty == showWhenEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
