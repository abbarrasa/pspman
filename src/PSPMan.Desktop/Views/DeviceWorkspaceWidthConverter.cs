using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace PSPMan.Desktop.Views;

/// <summary>Chooses XAML-defined column widths without moving layout policy into the view model.</summary>
public sealed class DeviceWorkspaceWidthConverter : IValueConverter
{
    public GridLength WithDevices { get; set; }
    public GridLength WithoutDevices { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? WithDevices : WithoutDevices;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Workspace widths are one-way bindings.");
}
