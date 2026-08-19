using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClaudeLauncher.App.Converters;

/// <summary>Maps IsRunning (bool) to a status-dot brush: green when running, gray when stopped.</summary>
public sealed class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Running = new(Color.FromRgb(0x2E, 0xA0, 0x4A));
    private static readonly SolidColorBrush Stopped = new(Color.FromRgb(0x9B, 0x9B, 0x9B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Running : Stopped;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps IsRunning (bool) to a Japanese status label.</summary>
public sealed class BoolToStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "実行中" : "停止中";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
