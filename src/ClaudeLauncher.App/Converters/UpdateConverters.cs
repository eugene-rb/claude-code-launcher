using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ClaudeLauncher.App.ViewModels;

namespace ClaudeLauncher.App.Converters;

/// <summary>Maps <see cref="UpdateState"/> to whether the "update ready" banner should be shown.</summary>
public sealed class UpdateStateToIsReadyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is UpdateState.ReadyToApply ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
