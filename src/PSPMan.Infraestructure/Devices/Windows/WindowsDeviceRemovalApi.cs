using System.Collections.Immutable;

namespace PSPMan.Infraestructure.Devices.Windows;

internal interface IWindowsRemovalVolume : IDisposable
{
    WindowsStorageNumber? ReadStorageNumber(out WindowsDeviceError? error);
    WindowsDeviceError? Lock();
}

internal interface IWindowsRemovalNativeApi
{
    IWindowsRemovalVolume? OpenVolume(WindowsVolumeReference volume, out WindowsDeviceError? error);
    WindowsDeviceRemovalFailure? LocateDevice(string instanceId, out uint node);
    WindowsDeviceRemovalFailure? RequestEject(uint node);
}

/// <summary>Prepares only the selected disk's volumes and requests PnP safe removal without force.</summary>
public sealed class WindowsDeviceRemovalApi : IWindowsDeviceRemovalApi
{
    private readonly IWindowsDeviceApi _inventory;
    private readonly IWindowsRemovalNativeApi _native;
    private readonly bool _requiresWindows;

    public WindowsDeviceRemovalApi() : this(new WindowsDeviceApi(), new WindowsRemovalNativeApi()) => _requiresWindows = true;

    internal WindowsDeviceRemovalApi(IWindowsDeviceApi inventory, IWindowsRemovalNativeApi native)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public ValueTask<WindowsDeviceRemovalFailure?> RequestRemovalAsync(WindowsDeviceRemovalTarget target,
        CancellationToken cancellationToken = default)
    {
        if (_requiresWindows && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows safe removal requires Windows.");
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        return new(Task.Run(async () =>
        {
            var snapshot = await _inventory.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var failure = ValidateTarget(snapshot, target);
            if (failure is not null) return failure;
            var volumes = snapshot.Volumes.Where(volume => volume.StorageNumber == target.StorageNumber).ToImmutableArray();
            failure = PrepareVolumes(volumes, target.StorageNumber, cancellationToken);
            if (failure is not null) return failure;

            // All our volume handles must be closed before inventory or PnP query-remove.
            snapshot = await _inventory.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            failure = ValidateTarget(snapshot, target);
            if (failure is not null) return failure;
            var current = snapshot.Volumes.Where(volume => volume.StorageNumber == target.StorageNumber).ToImmutableArray();
            if (!SameMedia(volumes, current)) return Failure("TargetChanged");
            failure = _native.LocateDevice(target.InstanceId, out var node);
            if (failure is not null) return failure;
            cancellationToken.ThrowIfCancellationRequested();
            // No parent traversal: ejecting a shared USB hub could remove unrelated physical disks.
            return _native.RequestEject(node);
        }, cancellationToken));
    }

    private WindowsDeviceRemovalFailure? PrepareVolumes(ImmutableArray<WindowsVolumeReference> volumes,
        WindowsStorageNumber storageNumber, CancellationToken token)
    {
        List<IWindowsRemovalVolume> handles = [];
        try
        {
            foreach (var volume in volumes)
            {
                token.ThrowIfCancellationRequested();
                if (volume.State == WindowsVolumeState.NoMedia) continue;
                var handle = _native.OpenVolume(volume, out var error);
                if (handle is null) return VolumeFailure(error ?? new("NativeFailure", "OpenVolume"), volume);
                handles.Add(handle);
                var number = handle.ReadStorageNumber(out error);
                if (error is not null) return VolumeFailure(error, volume);
                if (number != storageNumber) return Failure("TargetChanged") with { VolumeGuidPath = volume.VolumeGuidPath };
                token.ThrowIfCancellationRequested();
                error = handle.Lock();
                if (error is not null) return VolumeFailure(error, volume);
            }
            token.ThrowIfCancellationRequested();
            return null;
        }
        finally
        {
            // Closing releases ordinary locks. Never use FSCTL_DISMOUNT_VOLUME or retain self-vetoing handles.
            foreach (var handle in handles.AsEnumerable().Reverse()) handle.Dispose();
        }
    }

    internal static WindowsDeviceRemovalFailure? ValidateTarget(WindowsStorageSnapshot snapshot, WindowsDeviceRemovalTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.InstanceId) || string.IsNullOrWhiteSpace(target.InterfacePath))
            return Failure("InvalidRemovalTarget");
        if (!snapshot.Errors.IsEmpty) return WindowsDeviceRemovalFailure.FromError(snapshot.Errors[0]);
        var devices = snapshot.Devices.Where(device =>
            string.Equals(device.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(device.InterfacePath, target.InterfacePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (devices.Length != 1) return Failure("TargetUnavailable");
        var device = devices[0];
        if (device.Error is not null) return WindowsDeviceRemovalFailure.FromError(device.Error);
        if (device.IsOptical) return Failure("OpticalEjectRequired");
        if (!(device.IsRemovable || device.Bus is WindowsStorageBus.Usb or WindowsStorageBus.Sd or WindowsStorageBus.Mmc))
            return Failure("IneligibleDevice");
        if (device.StorageNumber != target.StorageNumber
            || snapshot.Devices.Count(candidate => candidate.StorageNumber == target.StorageNumber) != 1)
            return Failure("AmbiguousStorageParent");
        foreach (var volume in snapshot.Volumes.Where(volume => volume.StorageNumber == target.StorageNumber))
        {
            if (volume.VolumeGuidPath is null || !WindowsDeviceApi.IsVolumeGuidPath(volume.VolumeGuidPath)
                || snapshot.Volumes.Count(candidate => string.Equals(candidate.VolumeGuidPath, volume.VolumeGuidPath,
                    StringComparison.OrdinalIgnoreCase)) != 1)
                return Failure("InvalidRemovalVolume");
            if (volume.State == WindowsVolumeState.NoMedia && volume.MountPoints.IsEmpty) continue;
            if (volume.Error is not null) return VolumeFailure(volume.Error, volume);
            if (volume.State != WindowsVolumeState.Available) return Failure("VolumeUnavailable");
        }
        return null;
    }

    private static bool SameMedia(ImmutableArray<WindowsVolumeReference> before, ImmutableArray<WindowsVolumeReference> after) =>
        before.OrderBy(volume => volume.VolumeGuidPath, StringComparer.OrdinalIgnoreCase)
            .Select(volume => (volume.VolumeGuidPath!.ToUpperInvariant(), volume.FileSystemSerialNumber, volume.State))
            .SequenceEqual(after.OrderBy(volume => volume.VolumeGuidPath, StringComparer.OrdinalIgnoreCase)
                .Select(volume => (volume.VolumeGuidPath!.ToUpperInvariant(), volume.FileSystemSerialNumber, volume.State)));

    private static WindowsDeviceRemovalFailure VolumeFailure(WindowsDeviceError error, WindowsVolumeReference volume) =>
        WindowsDeviceRemovalFailure.FromError(error) with { VolumeGuidPath = volume.VolumeGuidPath };

    internal static WindowsDeviceRemovalFailure Failure(string code) => new(new(code, "RemoveDevice"));
}
