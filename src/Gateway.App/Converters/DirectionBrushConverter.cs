using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Gateway.App.Converters;

/// <summary>Maps a frame direction string to an accent brush for the traffic pill.</summary>
public sealed class DirectionBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InboundBrush = new((Color)ColorConverter.ConvertFromString("#2563EB"));
    private static readonly SolidColorBrush OutboundBrush = new((Color)ColorConverter.ConvertFromString("#10B981"));
    private static readonly SolidColorBrush NeutralBrush = new((Color)ColorConverter.ConvertFromString("#9CA3AF"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "Inbound" => InboundBrush,
            "Outbound" => OutboundBrush,
            _ => NeutralBrush
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
