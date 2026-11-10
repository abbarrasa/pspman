namespace PSPMan.Infraestructure.Devices.Linux;

public sealed record LinuxDeviceRemovalResult(
    bool Succeeded,
    LinuxDeviceInventory Inventory,
    UDisksClientException? Error = null);

/// <summary>Coordinates safe whole-device removal after ordinary volume unmounting.</summary>
public sealed class LinuxPhysicalDeviceRemovalCoordinator
{
    private readonly IUDisksClient _client;
    private readonly LinuxVolumeUnmountCoordinator _unmount;
    private readonly Func<CancellationToken, ValueTask<LinuxDeviceInventory>> _refresh;

    public LinuxPhysicalDeviceRemovalCoordinator(
        IUDisksClient client,
        Func<CancellationToken, ValueTask<LinuxDeviceInventory>> refresh)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _unmount = new LinuxVolumeUnmountCoordinator(client, refresh);
    }

    public async ValueTask<LinuxDeviceRemovalResult> RemoveAsync(
        LinuxPhysicalDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanRemove(device))
        {
            var unsupportedInventory = await _refresh(cancellationToken).ConfigureAwait(false);
            return Failure(unsupportedInventory, "RemovalUnsupported",
                "The selected Linux device does not support safe removal.");
        }

        var unmount = await _unmount.UnmountAsync(device, cancellationToken).ConfigureAwait(false);
        if (!unmount.Succeeded)
            return new LinuxDeviceRemovalResult(false, unmount.Inventory, unmount.Error);

        var current = FindDevice(unmount.Inventory, device.ObjectPath);
        if (current is null)
            return new LinuxDeviceRemovalResult(true, unmount.Inventory);
        if (current.Volumes.Any(IsMounted))
            return Failure(unmount.Inventory, "RemovalIncomplete",
                "The physical device still has a mounted volume.");

        try
        {
            if (device.Kind == LinuxDeviceKind.Optical)
                await _client.EjectAsync(device.ObjectPath, cancellationToken).ConfigureAwait(false);
            else
                await _client.PowerOffAsync(device.ObjectPath, cancellationToken).ConfigureAwait(false);
        }
        catch (UDisksClientException exception)
        {
            var failedInventory = await _refresh(cancellationToken).ConfigureAwait(false);
            return new LinuxDeviceRemovalResult(false, failedInventory, exception);
        }

        var afterOperation = await _refresh(cancellationToken).ConfigureAwait(false);
        var remaining = FindDevice(afterOperation, device.ObjectPath);
        if (device.Kind == LinuxDeviceKind.Optical)
        {
            if (remaining is not null && remaining.Volumes.All(volume => !IsMounted(volume)))
                return new LinuxDeviceRemovalResult(true, afterOperation);
        }
        else if (remaining is null)
        {
            return new LinuxDeviceRemovalResult(true, afterOperation);
        }

        return Failure(afterOperation, "RemovalIncomplete",
            "The operating system did not confirm removal of the physical device.");
    }

    private static bool CanRemove(LinuxPhysicalDevice device) =>
        device.Kind == LinuxDeviceKind.Optical ? device.IsEjectable : device.IsPowerOffSupported;

    private static LinuxPhysicalDevice? FindDevice(
        LinuxDeviceInventory inventory, string objectPath) =>
        inventory.Devices.FirstOrDefault(device => device.ObjectPath == objectPath);

    private static bool IsMounted(LinuxVolume volume) =>
        volume.MountPoints.Any(root => !string.IsNullOrWhiteSpace(root));

    private static LinuxDeviceRemovalResult Failure(
        LinuxDeviceInventory inventory, string code, string message) =>
        new(false, inventory, new UDisksClientException(code, message));
}
