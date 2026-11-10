namespace PSPMan.Infraestructure.Devices.Windows;

public sealed partial class WindowsDeviceOperationHost
{
    /// <summary>
    /// Host-side transport only. The expected initiating lease must be pinned from native process evidence
    /// during validated bootstrap, not from a payload. T33 supplies the fixed-operation responder;
    /// T35 supplies early dispatch. This method neither launches a process nor accesses any device.
    /// </summary>
    internal static async Task RespondToOperationAsync(string pipeName, Guid requestId,
        IWindowsOperationProcess expectedInitiator,
        Func<WindowsOperationRequest, CancellationToken, Task<WindowsOperationReply>> respond,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows operation IPC requires Windows.");
        using var local = WindowsOperationNativeProcess.Capture((uint)Environment.ProcessId);
        using var pipe = WindowsOperationNativePipe.CreateClient(pipeName);
        using var channel = new WindowsOperationIpc(pipe, local, requestId, false);
        await channel.RespondAsync(expectedInitiator, respond, timeout, cancellationToken).ConfigureAwait(false);
    }
}
