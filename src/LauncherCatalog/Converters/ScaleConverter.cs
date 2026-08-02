using System.Globalization;
using System.Windows.Data;

namespace LauncherCatalog.Converters;

/// <summary>
/// Multiplies an incoming <see cref="double"/> (typically an <c>ActualHeight</c>) by <see cref="Ratio"/>.
/// Used to cap the auto-expanding "Add message" box at a fraction of the right panel height so it
/// grows with content, then scrolls internally instead of covering the message list.
/// </summary>
public sealed class ScaleConverter : IValueConverter
{
    public double Ratio { get; set; } = 0.5;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var input = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0d
        };

        var ratio = Ratio;
        if (parameter is string raw && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            ratio = parsed;
        }

        var result = input * ratio;
        return result < 0 ? 0d : result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
