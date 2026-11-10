using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PSPMan.Infraestructure.Devices.Windows;

internal interface IWindowsOperationAttempt : IDisposable
{
    string PipeName { get; }
    WindowsOperationProcessIdentity Initiator { get; }
    Task<WindowsOperationReply> ExchangeAsync(IWindowsOperationProcess expectedHost, TimeSpan timeout, CancellationToken cancellationToken);
}

internal interface IWindowsOperationLauncher
{
    Task<IWindowsOperationProcess> LaunchAsync(string pipeName, Guid requestId,
        WindowsOperationProcessIdentity initiator, CancellationToken cancellationToken);
}

internal sealed record WindowsDeviceAuthorizationResult(bool Succeeded, WindowsStorageSnapshot Snapshot,
    WindowsDeviceRemovalFailure? Failure, bool AuthorizationAttempted);

public sealed partial class WindowsDeviceAuthorization
{
    private readonly IWindowsDeviceApi inventory;
    private readonly IWindowsOperationLauncher launcher;
    private readonly Func<WindowsOperationRequest, IWindowsOperationAttempt> prepare;
    private readonly IWindowsOpticalNativeApi optical;
    private readonly TimeSpan operationTimeout;
    private readonly TimeSpan observationTimeout;
    private readonly bool requiresWindows;
    private readonly SemaphoreSlim authorization = new(1, 1);
    private readonly Dictionary<AttemptKey, WindowsDeviceRemovalFailure> attempts = [];

    public WindowsDeviceAuthorization() : this(new WindowsDeviceApi(), new WindowsOperationNativeLauncher(),
        PrepareOperationIpc, new WindowsOpticalNativeApi(), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(5)) => requiresWindows = true;

    internal WindowsDeviceAuthorization(IWindowsDeviceApi inventory, IWindowsOperationLauncher launcher,
        Func<WindowsOperationRequest, IWindowsOperationAttempt> prepare, IWindowsOpticalNativeApi optical,
        TimeSpan operationTimeout, TimeSpan observationTimeout)
    {
        this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        this.prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
        this.optical = optical ?? throw new ArgumentNullException(nameof(optical));
        if (operationTimeout <= TimeSpan.Zero || operationTimeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        if (observationTimeout <= TimeSpan.Zero || observationTimeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(observationTimeout));
        this.operationTimeout = operationTimeout;
        this.observationTimeout = observationTimeout;
    }

    /// <summary>
    /// Call after an ordinary operation, with its failure (null means ordinary success). Only privilege
    /// failures may launch. Future platform composition must clear suppression on observed disconnection.
    /// Request IDs alone never reset suppression; a retry must be an explicit action.
    /// </summary>
    internal async Task<WindowsDeviceAuthorizationResult> AuthorizeAsync(WindowsOperationRequest request,
        WindowsDeviceRemovalFailure? priorFailure, bool explicitRetry = false, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        if (requiresWindows && !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows authorization requires Windows.");
        var key = new AttemptKey(request.Operation, request.DeviceInstanceId.ToUpperInvariant(), request.StorageNumber,
            request.VolumeGuidPath?.ToUpperInvariant(), request.FileSystemSerialNumber);
        var failure = priorFailure;
        var attempted = false;
        var entered = false;
        var claimed = false;
        var launching = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(operationTimeout);
        try
        {
            try
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (priorFailure?.RequiresAuthorization == true)
                {
                    await authorization.WaitAsync(deadline.Token).ConfigureAwait(false);
                    entered = true;
                    WindowsDeviceRemovalFailure? previous;
                    lock (attempts) attempts.TryGetValue(key, out previous);
                    if (previous is not null && !explicitRetry) failure = previous;
                    else
                    {
                        var snapshot = await inventory.EnumerateStorageAsync(deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
                        deadline.Token.ThrowIfCancellationRequested();
                        var validation = WindowsDeviceOperationHost.ValidateTarget(request, snapshot);
                        if (!validation.IsValid) failure = WindowsDeviceRemovalFailure.FromError(validation.Error!);
                        else
                        {
                            lock (attempts) attempts[key] = Failure("AuthorizationRetryRequired");
                            claimed = true;
                            using var attempt = prepare(request); // Endpoint exists before the UAC call.
                            deadline.Token.ThrowIfCancellationRequested();
                            attempted = true;
                            launching = true;
                            using var process = await launcher.LaunchAsync(attempt.PipeName, request.RequestId,
                                attempt.Initiator, deadline.Token).ConfigureAwait(false);
                            launching = false;
                            deadline.Token.ThrowIfCancellationRequested();
                            var reply = await attempt.ExchangeAsync(process, operationTimeout, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                            deadline.Token.ThrowIfCancellationRequested();
                            // Keep fake/native adapters honest as well as the real T32 codec.
                            DecodeReply(EncodeReply(reply), request);
                            failure = reply.Completed ? null : reply.Failure;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            { failure = Failure(cancellationToken.IsCancellationRequested ? "AuthorizationCancelled" : "AuthorizationTimedOut"); }
            catch (Win32Exception error)
            { failure = Failure(launching ? error.NativeErrorCode switch { 1223 => "AuthorizationCancelled", 5 => "AuthorizationDenied", _ => "AuthorizationLaunchFailed" }
                : "AuthorizationCommunicationFailed", error.NativeErrorCode); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException or COMException)
            { failure = Failure(error is UnauthorizedAccessException ? "AuthorizationPeerRejected"
                : launching ? "AuthorizationLaunchFailed" : "AuthorizationCommunicationFailed"); }

            // Use a separate bounded observation window even when the initiating cancellation token is cancelled.
            // Never return old roots as usable when this refresh fails.
            var observed = await RefreshAsync().ConfigureAwait(false);
            if (failure is null && deadline.IsCancellationRequested)
                failure = Failure(cancellationToken.IsCancellationRequested ? "AuthorizationCancelled" : "AuthorizationTimedOut");
            if (failure is null)
            {
                try
                {
                    using var observation = new CancellationTokenSource(observationTimeout);
                    failure = await ConfirmAsync(request, observed, observation.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException or OperationCanceledException)
                { failure = Failure("OperationNotObserved"); }
            }
            if (failure is null && deadline.IsCancellationRequested)
                failure = Failure(cancellationToken.IsCancellationRequested ? "AuthorizationCancelled" : "AuthorizationTimedOut");
            if (claimed)
                lock (attempts)
                    if (attempts.ContainsKey(key)) attempts[key] = failure ?? Failure("AuthorizationRetryRequired");
            return new(failure is null, observed, failure, attempted);
        }
        finally { if (entered) authorization.Release(); }
    }

    /// <summary>Session lifecycle hook for confirmed disconnection/media-generation changes; never for label/root changes.</summary>
    internal void ForgetDevice(string instanceId)
    {
        lock (attempts)
            foreach (var key in attempts.Keys.Where(key => string.Equals(key.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)).ToArray())
                attempts.Remove(key);
    }

    private async Task<WindowsStorageSnapshot> RefreshAsync()
    {
        try
        {
            using var deadline = new CancellationTokenSource(observationTimeout);
            return await inventory.EnumerateStorageAsync(deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException or OperationCanceledException)
        { return new([], [], [new("ObservationUnavailable", "EnumerateStorage")]); }
    }

    private async Task<WindowsDeviceRemovalFailure?> ConfirmAsync(WindowsOperationRequest request,
        WindowsStorageSnapshot snapshot, CancellationToken cancellationToken)
    {
        var inventoryError = snapshot.Errors.FirstOrDefault(error => request.Operation != WindowsElevatedOperation.EjectOpticalMedia || error.Code != "NoMedia");
        if (inventoryError is not null) return WindowsDeviceRemovalFailure.FromError(inventoryError);
        if (request.Operation == WindowsElevatedOperation.RemoveDevice)
            return snapshot.Devices.Any(device => string.Equals(device.InstanceId, request.DeviceInstanceId, StringComparison.OrdinalIgnoreCase))
                || snapshot.Volumes.Any(volume => volume.StorageNumber == request.StorageNumber) ? Failure("RemovalNotObserved") : null;
        var validation = WindowsDeviceOperationHost.ValidateTarget(request, snapshot);
        if (!validation.IsValid) return WindowsDeviceRemovalFailure.FromError(validation.Error!);
        if (request.Operation == WindowsElevatedOperation.MountVolume)
            return snapshot.Volumes.Any(volume => string.Equals(volume.VolumeGuidPath, request.VolumeGuidPath,
                StringComparison.OrdinalIgnoreCase) && volume.IsMounted) ? null : Failure("MountNotObserved");
        var device = snapshot.Devices.Single(device => string.Equals(device.InstanceId, request.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
        if (device.StorageNumber is { } number && snapshot.Volumes.Any(volume => volume.StorageNumber == number
            && (volume.State != WindowsVolumeState.NoMedia || !volume.MountPoints.IsEmpty))) return Failure("EjectionNotObserved");
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var handle = optical.Open(device.InterfacePath, out var error);
            if (handle is null) return WindowsDeviceRemovalFailure.FromError(error ?? new("NativeFailure", "CheckOpticalMedia"));
            var media = handle.CheckMedia(); // Read-only: absence of volumes alone never proves an empty tray.
            cancellationToken.ThrowIfCancellationRequested();
            return media.Error is not null ? WindowsDeviceRemovalFailure.FromError(media.Error)
                : media.IsPresent ? Failure("EjectionNotObserved") : null;
        }, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static WindowsDeviceRemovalFailure Failure(string code, int? nativeCode = null) => new(new(code, "AuthorizeDevice", nativeCode));
    private readonly record struct AttemptKey(WindowsElevatedOperation Operation, string InstanceId,
        WindowsStorageNumber? StorageNumber, string? VolumeGuidPath, uint? Serial);
}
