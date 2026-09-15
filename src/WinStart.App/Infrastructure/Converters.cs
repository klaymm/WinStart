using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WinStart.Core.Abstractions;

namespace WinStart.App.Infrastructure;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        value is Visibility.Visible;
}

public sealed class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        value is Visibility.Collapsed;
}

public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is not true;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => value is not true;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is null || (value is string s && string.IsNullOrWhiteSpace(s))
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class NotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object? p, CultureInfo c) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object? value, Type[] t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        try
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch { }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class LocKeyConverter : IValueConverter
{
    public static ILocalizationService? Localization { get; set; }

    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is string key ? Localization?[key] ?? key : "";

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}
