using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ClaudeLauncher.App.Converters;

/// <summary>Visible when the bound count is zero; pass ConverterParameter="Invert" to flip that.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var isEmpty = count == 0;
        var show = invert ? !isEmpty : isEmpty;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Parses a "#RRGGBB" hex string into a SolidColorBrush; falls back to gray for invalid/missing input
/// (session profiles are user-editable JSON, so a malformed accent color must not crash the app).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Fallback = new(Color.FromRgb(0x9B, 0x9B, 0x9B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex)!;
                return new SolidColorBrush(color);
            }
            catch (FormatException)
            {
                // fall through to fallback brush
            }
        }

        return Fallback;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound value is a non-null, non-empty string.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
