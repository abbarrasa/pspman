using Avalonia.Controls;
using Avalonia.VisualTree;

namespace PSPMan.Desktop.Tests;

internal static class CopyViewTestExtensions
{
    internal static T CopyControl<T>(this Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
}
