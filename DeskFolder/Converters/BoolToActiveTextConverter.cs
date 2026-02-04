using Avalonia.Data.Converters;
using System.Globalization;

namespace DeskFolder.Converters;

public class BoolToActiveTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive ? "Active" : "Inactive";
        }
        return "Inactive";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
