namespace PSPMan.Infraestructure.Devices.Linux;

public sealed record LinuxUnmountResult(
    bool Succeeded,
    LinuxDeviceInventory Inventory,
    UDisksClientException? Error = null);

/// <summary>Unmounts all mounted volumes of one physical device without forcing.</summary>
public sealed class LinuxVolumeUnmountCoordinator
{
    private readonly IUDisksClient _client;
    private readonly Func<CancellationToken, ValueTask<LinuxDeviceInventory>> _refresh;

    public LinuxVolumeUnmountCoordinator(
        IUDisksClient client,
        Func<CancellationToken, ValueTask<LinuxDeviceInventory>> refresh)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
    }

    public async ValueTask<LinuxUnmountResult> UnmountAsync(
        LinuxPhysicalDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        var inventory = await _refresh(cancellationToken).ConfigureAwait(false);

        foreach (var volume in device.Volumes.Where(IsMounted))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _client.UnmountAsync(volume.ObjectPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (UDisksClientException exception)
            {
                inventory = await _refresh(cancellationToken).ConfigureAwait(false);
                return new LinuxUnmountResult(false, inventory, exception);
            }

            inventory = await _refresh(cancellationToken).ConfigureAwait(false);
            var refreshedVolume = FindVolume(inventory, volume.ObjectPath);
            if (refreshedVolume is not null && IsMounted(refreshedVolume))
            {
                var error = new UDisksClientException("UnmountIncomplete",
                    "The volume remained mounted after the unmount request.");
                return new LinuxUnmountResult(false, inventory, error);
            }
        }

        return new LinuxUnmountResult(true, inventory);
    }

    private static bool IsMounted(LinuxVolume volume) =>
        volume.MountPoints.Any(root => !string.IsNullOrWhiteSpace(root));

    private static LinuxVolume? FindVolume(LinuxDeviceInventory inventory, string objectPath) =>
        inventory.Devices.SelectMany(device => device.Volumes)
            .FirstOrDefault(volume => volume.ObjectPath == objectPath);
}
