using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

public class DelayColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var delay = value.ToString().ToInt();

        // Semantic colors: red only for failure/timeout; green for good; amber for slow.
        return delay switch
        {
            <= 0 => new SolidColorBrush(Color.Parse("#FF3B30")),
            <= 120 => new SolidColorBrush(Color.Parse("#1B7F34")),
            <= 500 => new SolidColorBrush(Color.Parse("#9A6700")),
            _ => new SolidColorBrush(Color.Parse("#C9342D"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
