using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SmoothCoder.App.Converters;

/// <summary>Nullable usage percentage -> ProgressBar.Value (null, i.e. not yet calibrated, shows as an
/// empty 0% bar rather than binding failing).</summary>
public sealed class UsagePercentToValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? Math.Clamp(d, 0, 100) : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Nullable usage percentage -> display text: "計測中…" before the window's first calibration,
/// otherwise a rounded percentage.</summary>
public sealed class UsagePercentToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? string.Format(culture, "{0:F0}%", d) : "計測中…";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Nullable usage percentage -> bar color: gray while unmeasured, green/amber/red as
/// consumption climbs toward the calibrated limit. Same hardcoded-SolidColorBrush approach as
/// <see cref="ActivityStateToBrushConverter"/>.</summary>
public sealed class UsagePercentToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Unmeasured = new(Color.FromRgb(0x9B, 0x9B, 0x9B));
    private static readonly SolidColorBrush Low = new(Color.FromRgb(0x10, 0x9C, 0x3B));
    private static readonly SolidColorBrush Mid = new(Color.FromRgb(0xE8, 0x8A, 0x00));
    private static readonly SolidColorBrush High = new(Color.FromRgb(0xD1, 0x34, 0x38));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        double d when d >= 90 => High,
        double d when d >= 70 => Mid,
        double => Low,
        _ => Unmeasured,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool IsMeasured -> "実測" (a live reading from Claude Code's status line) / "推定" (the
/// token-based estimate). Sits next to the percentage text on each usage bar.</summary>
public sealed class UsageIsMeasuredToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "実測" : "推定";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound nullable percentage has a value (i.e. the window has been calibrated
/// at least once); pass ConverterParameter="Invert" to show only while it's still null instead.</summary>
public sealed class UsagePercentHasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var hasValue = value is double;
        var show = invert ? !hasValue : hasValue;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
