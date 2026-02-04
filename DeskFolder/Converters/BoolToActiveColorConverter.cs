using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace DeskFolder.Converters;

public class BoolToActiveColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive ? Color.Parse("#4CAF50") : Color.Parse("#757575");
        }
        return Color.Parse("#757575");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
