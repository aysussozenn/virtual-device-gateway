using System.Globalization;
using System.Windows.Data;

namespace Gateway.App.Converters;

/// <summary>Inverts a bool (used to disable inputs while the gateway is running).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
