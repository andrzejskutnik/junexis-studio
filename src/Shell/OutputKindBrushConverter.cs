using Avalonia.Data.Converters;
using Avalonia.Media;
using Junexis.Studio.Abstractions;
using System;
using System.Globalization;

namespace Junexis.Studio.Shell;

/// <summary>
/// Converts <see cref="ProcessOutputKind"/> to an <see cref="IBrush"/>:
/// stderr → red, stdout → light grey.
/// </summary>
public sealed class OutputKindBrushConverter : IValueConverter
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static readonly OutputKindBrushConverter Instance = new();

    private static readonly IBrush StdOutBrush = new SolidColorBrush(Color.Parse("#cccccc"));
    private static readonly IBrush StdErrBrush = new SolidColorBrush(Color.Parse("#f48771"));

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ProcessOutputKind kind && kind == ProcessOutputKind.StdErr
            ? StdErrBrush
            : StdOutBrush;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
