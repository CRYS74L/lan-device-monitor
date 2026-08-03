using System.Globalization;
using System.Windows.Data;

namespace LanDeviceMonitor.Converters;

public sealed class JustifyTextConverter : IValueConverter
{
    private const char ThinSpace = '\u2009';

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrEmpty(text))
        {
            return value ?? string.Empty;
        }

        return string.Join(ThinSpace, text.ToCharArray());
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
