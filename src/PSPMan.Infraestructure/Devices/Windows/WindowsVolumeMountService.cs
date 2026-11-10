namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Current target observations, not proof of persistent media identity.</summary>
public sealed record WindowsVolumeMountTarget(string InstanceId, string InterfacePath, string VolumeGuidPath,
    WindowsStorageNumber StorageNumber, uint? FileSystemSerialNumber);

public sealed record WindowsDriveLetterObservation(uint OccupiedMask, WindowsDeviceError? Error = null);

/// <summary>Only drive-letter assignment; no policy changes, disk activation, removal or file operations.</summary>
public interface IWindowsVolumeMountApi
{
    ValueTask<WindowsDriveLetterObservation> GetOccupiedDriveLettersAsync(CancellationToken cancellationToken = default);
    ValueTask<WindowsDeviceError?> AssignRootAsync(WindowsVolumeMountTarget target, string root,
        CancellationToken cancellationToken = default);
}

public sealed record WindowsVolumeMountResult(WindowsStorageSnapshot Snapshot, WindowsVolumeReference? Volume,
    WindowsDeviceError? Error)
{
    public bool Succeeded => Error is null && Volume?.IsMounted == true;
    public bool RequiresAuthorization => Error?.NativeCode is 5 or 1314;
}

/// <summary>Assigns one eligible volume a free root and reports independently observed state.</summary>
public sealed class WindowsVolumeMountService(IWindowsDeviceApi inventory, IWindowsVolumeMountApi mount)
{
    private readonly IWindowsDeviceApi _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    private readonly IWindowsVolumeMountApi _mount = mount ?? throw new ArgumentNullException(nameof(mount));
    private readonly SemaphoreSlim _assignment = new(1, 1);

    public async ValueTask<WindowsVolumeMountResult> MountAsync(WindowsVolumeMountTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        await _assignment.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await _inventory.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
            uint attempted = 0;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var error = ValidateTarget(snapshot, target, out var volume);
                if (error is not null || volume!.IsMounted)
                    return new(snapshot, volume, error);

                var letters = await _mount.GetOccupiedDriveLettersAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (letters.Error is not null) return new(snapshot, volume, letters.Error);
                var root = SelectRoot(letters.OccupiedMask | attempted);
                if (root is null) return new(snapshot, volume, Failure("NoDriveLetterAvailable"));

                // Letter discovery is not a reservation. Recheck the target, then let the OS reject a race.
                snapshot = await _inventory.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                error = ValidateTarget(snapshot, target, out volume);
                if (error is not null || volume!.IsMounted) return new(snapshot, volume, error);
                attempted |= 1u << (root[0] - 'A');
                error = await _mount.AssignRootAsync(target, root, cancellationToken).ConfigureAwait(false);
                // Even a successful native reply cannot fabricate a usable root; preserve actual partial state.
                snapshot = await _inventory.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var observationError = ValidateTarget(snapshot, target, out volume);
                if (error is null)
                    return new(snapshot, volume, observationError ?? (volume!.IsMounted ? null : Failure("MountNotObserved")));
                if (observationError is not null || volume!.IsMounted || !IsConflict(error) || attempt == 2)
                    return new(snapshot, volume, error);
            }
            throw new InvalidOperationException("The bounded assignment loop must return a result.");
        }
        finally { _assignment.Release(); }
    }

    internal static WindowsDeviceError? ValidateTarget(WindowsStorageSnapshot snapshot, WindowsVolumeMountTarget target,
        out WindowsVolumeReference? volume)
    {
        volume = null;
        if (string.IsNullOrWhiteSpace(target.InstanceId) || string.IsNullOrWhiteSpace(target.InterfacePath)
            || !WindowsDeviceApi.IsVolumeGuidPath(target.VolumeGuidPath) || target.FileSystemSerialNumber is null)
            return Failure("InvalidMountTarget");
        var devices = snapshot.Devices.Where(device =>
            string.Equals(device.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(device.InterfacePath, target.InterfacePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (devices.Length != 1) return Failure("TargetUnavailable");
        var device = devices[0];
        if (device.Error is not null) return device.Error;
        if (!(device.IsOptical || device.IsRemovable || device.Bus is WindowsStorageBus.Usb or WindowsStorageBus.Sd or WindowsStorageBus.Mmc))
            return Failure("IneligibleDevice");
        if (device.StorageNumber != target.StorageNumber
            || snapshot.Devices.Count(candidate => candidate.StorageNumber == target.StorageNumber) != 1)
            return Failure("AmbiguousStorageParent");
        var volumes = snapshot.Volumes.Where(candidate =>
            string.Equals(candidate.VolumeGuidPath, target.VolumeGuidPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (volumes.Length != 1) return Failure("TargetUnavailable");
        var current = volumes[0];
        if (current.StorageNumber != target.StorageNumber || current.FileSystemSerialNumber != target.FileSystemSerialNumber)
            return Failure("TargetChanged");
        if (current.Error is not null) return current.Error;
        if (current.State != WindowsVolumeState.Available) return Failure("VolumeUnavailable");
        volume = current;
        return null;
    }

    private static string? SelectRoot(uint occupied)
    {
        // Prefer conventional data-drive letters; reserve A/B for legacy readers and C for the system.
        for (var letter = 'D'; letter <= 'Z'; letter++)
            if ((occupied & (1u << (letter - 'A'))) == 0) return $"{letter}:\\";
        return null;
    }

    private static bool IsConflict(WindowsDeviceError error) => error.Code == "DriveLetterConflict"
        || error.NativeCode is 85 or 183 or 145;
    private static WindowsDeviceError Failure(string code) => new(code, "MountVolume");
}
