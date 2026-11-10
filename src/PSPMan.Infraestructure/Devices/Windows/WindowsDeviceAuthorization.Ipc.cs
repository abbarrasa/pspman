namespace PSPMan.Infraestructure.Devices.Windows;

public sealed partial class WindowsDeviceAuthorization
{
    /// <summary>Prepare before a UAC launch. This preparation neither launches a process nor accesses a device.</summary>
    internal static WindowsOperationIpcAttempt PrepareOperationIpc(WindowsOperationRequest request) =>
        WindowsOperationIpcAttempt.Create(request);
}

/// <summary>Owns one unpredictable endpoint and its initiating process lease, never credentials.</summary>
internal sealed class WindowsOperationIpcAttempt : IWindowsOperationAttempt
{
    private readonly IWindowsOperationProcess initiator;
    private readonly WindowsOperationIpc channel;
    private readonly WindowsOperationRequest request;
    private int disposed;
    public string PipeName { get; }
    public WindowsOperationProcessIdentity Initiator => initiator.Identity;

    private WindowsOperationIpcAttempt(string name, WindowsOperationRequest request,
        IWindowsOperationProcess initiator, IWindowsOperationPipe pipe)
    {
        PipeName = name;
        this.request = request;
        this.initiator = initiator;
        channel = new(pipe, initiator, request.RequestId, true);
    }

    internal static WindowsOperationIpcAttempt Create(WindowsOperationRequest request)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows operation IPC requires Windows.");
        return Create(request, CaptureInitiator, WindowsOperationNativePipe.CreateServer);
    }

    private static IWindowsOperationProcess CaptureInitiator()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return WindowsOperationNativeProcess.Capture((uint)Environment.ProcessId);
    }

    // Inject only trusted local factories; neither process evidence nor pipe policy comes from the wire.
    internal static WindowsOperationIpcAttempt Create(WindowsOperationRequest request,
        Func<IWindowsOperationProcess> captureInitiator, Func<string, IWindowsOperationPipe> createPipe)
    {
        WindowsDeviceAuthorization.EncodeRequest(request); // Validate before allocating OS resources.
        var initiator = captureInitiator();
        try
        {
            if (!initiator.IsAlive || initiator.Identity.Elevated)
                throw new UnauthorizedAccessException("An operation must originate from a live unelevated process.");
            var name = WindowsOperationIpc.NewPipeName();
            return new(name, request, initiator, createPipe(name));
        }
        catch { initiator.Dispose(); throw; }
    }

    public async Task<WindowsOperationReply> ExchangeAsync(IWindowsOperationProcess expectedHost,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try { return await channel.ExchangeAsync(request, expectedHost, timeout, cancellationToken).ConfigureAwait(false); }
        finally { Dispose(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { channel.Dispose(); }
        finally { initiator.Dispose(); }
    }
}
