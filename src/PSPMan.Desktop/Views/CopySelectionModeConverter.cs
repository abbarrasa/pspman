using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace PSPMan.Desktop.Views;

/// <summary>Maps the composition boundary to the catalogue control's selection mode.</summary>
public sealed class CopySelectionModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? SelectionMode.Multiple : SelectionMode.Single;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
