using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ClaudeLauncher.App.Converters;

/// <summary>Green when the bound file exists, gray otherwise.</summary>
public sealed class ExistsToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ExistsBrush = new(Color.FromRgb(0x0F, 0x7B, 0x0F));
    private static readonly SolidColorBrush MissingBrush = new(Color.FromRgb(0x9B, 0x9B, 0x9B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ExistsBrush : MissingBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ExistsToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "あり" : "なし";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound string is null/whitespace.</summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
