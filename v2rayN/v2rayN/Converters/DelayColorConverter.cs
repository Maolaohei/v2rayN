using System.Windows.Media;

namespace v2rayN.Converters;

public class DelayColorConverter : IValueConverter
{
    // Design contract (v2 mock): signal green / warn amber / danger red only.
    private static readonly SolidColorBrush Signal = Freeze(Color.FromRgb(0x0F, 0x9F, 0x6E));
    private static readonly SolidColorBrush Warn = Freeze(Color.FromRgb(0xC4, 0x7B, 0x12));
    private static readonly SolidColorBrush Danger = Freeze(Color.FromRgb(0xD9, 0x2D, 0x20));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        if (b.CanFreeze)
        {
            b.Freeze();
        }
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var delay = value.ToString().ToInt();

        return delay switch
        {
            <= 0 => Danger,
            <= 120 => Signal,
            <= 500 => Warn,
            _ => Danger
        };
    }

    public object? ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return null;
    }
}
