namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>The selected physical storage node; neither its shared parent nor a drive letter.</summary>
public sealed record WindowsDeviceRemovalTarget(string InstanceId, string InterfacePath, WindowsStorageNumber StorageNumber);

public sealed record WindowsDeviceRemovalFailure(WindowsDeviceError Error)
{
    public uint? ConfigurationStatus { get; init; }
    public uint? VetoType { get; init; }
    public string? VetoName { get; init; }
    public string? VolumeGuidPath { get; init; }
    public bool RequiresAuthorization => ConfigurationStatus == 0x33 || VetoType == 12
        || (ConfigurationStatus is null && Error.Code != "VolumeBusy" && Error.NativeCode is 5 or 1314);

    internal static WindowsDeviceRemovalFailure FromError(WindowsDeviceError error) => new(error)
    {
        ConfigurationStatus = error.Code == "ConfigurationManagerFailure" && error.NativeCode is { } status
            ? unchecked((uint)status) : null
    };
}

public sealed record WindowsDeviceRemovalResult(bool Succeeded, WindowsStorageSnapshot Snapshot,
    WindowsDeviceRemovalFailure? Failure = null);

/// <summary>Ordinary volume preparation and one native whole-device safe-removal request.</summary>
public interface IWindowsDeviceRemovalApi
{
    ValueTask<WindowsDeviceRemovalFailure?> RequestRemovalAsync(WindowsDeviceRemovalTarget target,
        CancellationToken cancellationToken = default);
}

/// <summary>Reports safe removal only after native acceptance and independent inventory confirmation.</summary>
public sealed class WindowsDeviceRemovalService(IWindowsDeviceApi inventory, IWindowsDeviceRemovalApi removal)
{
    private readonly IWindowsDeviceApi _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    private readonly IWindowsDeviceRemovalApi _removal = removal ?? throw new ArgumentNullException(nameof(removal));
    private readonly SemaphoreSlim _operation = new(1, 1);

    public async ValueTask<WindowsDeviceRemovalResult> RemoveAsync(WindowsDeviceRemovalTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await _inventory.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var failure = WindowsDeviceRemovalApi.ValidateTarget(snapshot, target);
            if (failure is not null) return new(false, snapshot, failure);
            failure = await _removal.RequestRemovalAsync(target, cancellationToken).ConfigureAwait(false);
            snapshot = await _inventory.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // An error remains an error even if the device disappears concurrently.
            if (failure is not null) return new(false, snapshot, failure);
            if (!snapshot.Errors.IsEmpty)
                return new(false, snapshot, WindowsDeviceRemovalFailure.FromError(snapshot.Errors[0]));
            var remains = snapshot.Devices.Any(device =>
                string.Equals(device.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase))
                || snapshot.Volumes.Any(volume => volume.StorageNumber == target.StorageNumber);
            return remains ? new(false, snapshot, WindowsDeviceRemovalApi.Failure("RemovalIncomplete")) : new(true, snapshot);
        }
        finally { _operation.Release(); }
    }
}
