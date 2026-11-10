namespace PSPMan.Infraestructure.Devices.Windows;

public sealed partial class WindowsDeviceOperationHost
{
    private int runStarted;

    /// <summary>
    /// Authenticated one-shot entry. Bootstrap must pin the expected initiating lease using native evidence,
    /// not a payload. Returns an exit status only after the reply is written; T35 owns process dispatch/exit.
    /// Does not launch a process, initialize UI/catalog services, or retry authorization.
    /// </summary>
    internal async Task<int> RunAsync(string pipeName, Guid requestId,
        IWindowsOperationProcess expectedInitiator,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows operation IPC requires Windows.");
        using var local = WindowsOperationNativeProcess.Capture((uint)Environment.ProcessId);
        using var pipe = WindowsOperationNativePipe.CreateClient(pipeName);
        using var channel = new WindowsOperationIpc(pipe, local, requestId, false);
        return await RunAsync(channel, expectedInitiator, timeout, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<int> RunAsync(WindowsOperationIpc channel, IWindowsOperationProcess expectedInitiator,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using (channel)
        {
            if (Interlocked.Exchange(ref runStarted, 1) != 0) throw new InvalidOperationException("An operation host is single-use.");
            var exitStatus = 1;
            await channel.RespondAsync(expectedInitiator, async (request, token) =>
            {
                var reply = await ExecuteAsync(request, token).ConfigureAwait(false);
                exitStatus = reply.Completed ? 0 : 1;
                return reply;
            }, timeout, cancellationToken).ConfigureAwait(false);
            return exitStatus;
        }
    }
}
