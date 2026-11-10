namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>A drive selection. Empty drives may not expose a current storage number.</summary>
public sealed record WindowsOpticalMediaTarget(string InstanceId, string InterfacePath, WindowsStorageNumber? StorageNumber);

internal sealed record WindowsOpticalMediaObservation(bool IsPresent, uint? ChangeCount = null, WindowsDeviceError? Error = null);

internal interface IWindowsOpticalHandle : IWindowsRemovalVolume
{
    WindowsOpticalMediaObservation CheckMedia();
    WindowsDeviceError? Eject();
}

internal interface IWindowsOpticalNativeApi
{
    IWindowsOpticalHandle? Open(string path, out WindowsDeviceError? error);
}

/// <summary>Ejects optical media without issuing any physical-node removal request.</summary>
public sealed class WindowsOpticalMediaEjector
{
    private readonly IWindowsDeviceApi _inventory;
    private readonly IWindowsOpticalNativeApi _native;
    private readonly bool _requiresWindows;
    private readonly SemaphoreSlim _operation = new(1, 1);

    public WindowsOpticalMediaEjector() : this(new WindowsDeviceApi()) { }
    public WindowsOpticalMediaEjector(IWindowsDeviceApi inventory) : this(inventory, new WindowsOpticalNativeApi()) => _requiresWindows = true;

    internal WindowsOpticalMediaEjector(IWindowsDeviceApi inventory, IWindowsOpticalNativeApi native)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public ValueTask<WindowsDeviceRemovalResult> EjectAsync(WindowsOpticalMediaTarget target,
        CancellationToken cancellationToken = default)
    {
        if (_requiresWindows && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows optical ejection requires Windows.");
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        return new(Task.Run(async () =>
        {
            await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var snapshot = await _inventory.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var failure = ValidateTarget(snapshot, target, out var device);
                if (failure is not null) return new WindowsDeviceRemovalResult(false, snapshot, failure);
                failure = EjectMedia(snapshot, device!, target, cancellationToken);
                // No inventory while any of our filesystem locks or device handles remain open.
                snapshot = await _inventory.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (failure is not null) return new WindowsDeviceRemovalResult(false, snapshot, failure);
                failure = ValidateTarget(snapshot, target, out device);
                if (failure is not null) return new WindowsDeviceRemovalResult(false, snapshot, failure);
                if (Volumes(snapshot, device!).Any(volume => volume.State != WindowsVolumeState.NoMedia || !volume.MountPoints.IsEmpty))
                    return new WindowsDeviceRemovalResult(false, snapshot, Failure("EjectionIncomplete"));

                // Absence of filesystem volumes does not prove absence of audio/unrecognized media.
                using var handle = _native.Open(device!.InterfacePath, out var error);
                if (handle is null) return new WindowsDeviceRemovalResult(false, snapshot, FromError(error));
                var observed = handle.CheckMedia();
                cancellationToken.ThrowIfCancellationRequested();
                if (observed.Error is not null) return new WindowsDeviceRemovalResult(false, snapshot, FromError(observed.Error));
                return observed.IsPresent
                    ? new WindowsDeviceRemovalResult(false, snapshot, Failure("EjectionIncomplete"))
                    : new WindowsDeviceRemovalResult(true, snapshot);
            }
            finally { _operation.Release(); }
        }, cancellationToken));
    }

    private WindowsDeviceRemovalFailure? EjectMedia(WindowsStorageSnapshot snapshot, WindowsStorageDevice device,
        WindowsOpticalMediaTarget target, CancellationToken token)
    {
        var volumes = Volumes(snapshot, device).Where(volume => volume.State == WindowsVolumeState.Available).ToArray();
        var paths = volumes.Length == 0 ? [device.InterfacePath]
            : volumes.Select(volume => volume.VolumeGuidPath!.TrimEnd('\\')).ToArray();
        List<IWindowsOpticalHandle> handles = [];
        try
        {
            WindowsOpticalMediaObservation? initial = null;
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                var handle = _native.Open(path, out var error);
                if (handle is null) return FromError(error);
                handles.Add(handle);
                if (initial is null)
                {
                    initial = handle.CheckMedia();
                    if (initial.Error is not null) return FromError(initial.Error);
                    if (!initial.IsPresent) return null; // Idempotent no-op, independently confirmed after cleanup.
                }
                var number = handle.ReadStorageNumber(out error);
                if (error is not null) return FromError(error);
                if (number is not { DeviceType: 2 } || number != (device.StorageNumber ?? target.StorageNumber ?? number))
                    return Failure("TargetChanged");
                // Also check the drive handle when no filesystem GUID was enumerated: enumeration
                // can race insertion, and an unsupported lock must fail closed rather than bypass safety.
                error = handle.Lock();
                if (error is not null) return FromError(error);
            }
            token.ThrowIfCancellationRequested();
            var beforeEject = handles[0].CheckMedia();
            if (beforeEject.Error is not null) return FromError(beforeEject.Error);
            if (!beforeEject.IsPresent || (initial!.ChangeCount is { } count && beforeEject.ChangeCount is { } current && count != current))
                return Failure("MediaChanged");
            token.ThrowIfCancellationRequested();
            // Unlike PnP removal, optical ejection uses the owning handle while all filesystem locks remain held.
            var ejectError = handles[0].Eject();
            return ejectError is null ? null : FromError(ejectError);
        }
        finally
        {
            foreach (var handle in handles.AsEnumerable().Reverse()) handle.Dispose();
        }
    }

    internal static WindowsDeviceRemovalFailure? ValidateTarget(WindowsStorageSnapshot snapshot, WindowsOpticalMediaTarget target,
        out WindowsStorageDevice? device)
    {
        device = null;
        if (string.IsNullOrWhiteSpace(target.InstanceId) || string.IsNullOrWhiteSpace(target.InterfacePath)
            || target.StorageNumber is { DeviceType: not 2 }) return Failure("InvalidOpticalTarget");
        var inventoryError = snapshot.Errors.FirstOrDefault(error => error.Code != "NoMedia");
        if (inventoryError is not null) return FromError(inventoryError);
        var matches = snapshot.Devices.Where(candidate =>
            string.Equals(candidate.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.InterfacePath, target.InterfacePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) return Failure("TargetUnavailable");
        var current = matches[0];
        if (!current.IsOptical) return Failure("NotOpticalDevice");
        if (current.Error is { Code: not "NoMedia" }) return FromError(current.Error);
        if (current.StorageNumber is { DeviceType: not 2 }
            || (current.StorageNumber is { } actual && target.StorageNumber is { } expected && actual != expected))
            return Failure("TargetChanged");
        if (current.StorageNumber is { } number && snapshot.Devices.Count(candidate => candidate.StorageNumber == number) != 1)
            return Failure("AmbiguousStorageParent");
        foreach (var volume in Volumes(snapshot, current))
        {
            if (volume.State == WindowsVolumeState.NoMedia && volume.MountPoints.IsEmpty
                && (volume.Error is null || volume.Error.Code == "NoMedia")) continue;
            if (volume.Error is not null) return FromError(volume.Error);
            if (volume.State != WindowsVolumeState.Available) return Failure("VolumeUnavailable");
            if (volume.VolumeGuidPath is null || !WindowsDeviceApi.IsVolumeGuidPath(volume.VolumeGuidPath)
                || snapshot.Volumes.Count(candidate => string.Equals(candidate.VolumeGuidPath, volume.VolumeGuidPath,
                    StringComparison.OrdinalIgnoreCase)) != 1) return Failure("InvalidOpticalVolume");
        }
        device = current;
        return null;
    }

    // Only a currently observed pair may associate volumes. A cached request pair may belong to another drive now.
    private static IEnumerable<WindowsVolumeReference> Volumes(WindowsStorageSnapshot snapshot, WindowsStorageDevice device) =>
        device.StorageNumber is { } number
            ? snapshot.Volumes.Where(volume => volume.StorageNumber == number) : [];

    private static WindowsDeviceRemovalFailure FromError(WindowsDeviceError? error) =>
        WindowsDeviceRemovalFailure.FromError(error ?? new("NativeFailure", "EjectOpticalMedia"));
    private static WindowsDeviceRemovalFailure Failure(string code) => new(new(code, "EjectOpticalMedia"));
}
