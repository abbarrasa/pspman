using System;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Desktop;

/// <summary>Dispatch before any Avalonia/catalog initialization. No helper failure may enter the UI branch.</summary>
internal static class ProgramDispatch
{
    internal static int Run(string[] args, Func<string[], int> startDesktop, Func<string[], int?>? tryHelper = null)
    {
        var helperStatus = (tryHelper ?? WindowsDeviceOperationMode.RunIfRequested)(args);
        return helperStatus ?? startDesktop(args);
    }
}
