using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Converters;

/// <summary>Maps ProjectActivityState to a Japanese dashboard label. Unknown maps to empty text; pair
/// with <see cref="ActivityStateToVisibilityConverter"/> to hide the badge entirely in that case.</summary>
public sealed class ActivityStateToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ProjectActivityState.Idle => "待機",
        ProjectActivityState.Responding => "応答中",
        ProjectActivityState.AwaitingApproval => "承認待ち",
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps ProjectActivityState to a status-badge brush.</summary>
public sealed class ActivityStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Idle = new(Color.FromRgb(0x9B, 0x9B, 0x9B));
    private static readonly SolidColorBrush Responding = new(Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush AwaitingApproval = new(Color.FromRgb(0xE8, 0x8A, 0x00));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ProjectActivityState.Idle => Idle,
        ProjectActivityState.Responding => Responding,
        ProjectActivityState.AwaitingApproval => AwaitingApproval,
        _ => Brushes.Transparent,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Hides the activity badge entirely when no signal is available yet (Unknown).</summary>
public sealed class ActivityStateToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ProjectActivityState state && state != ProjectActivityState.Unknown ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
