using System.Runtime.CompilerServices;

namespace PSPMan.Infraestructure.Devices.Linux;

public enum UDisksChangeKind
{
    InterfacesAdded,
    InterfacesRemoved,
    PropertiesChanged
}

public sealed record UDisksChange(UDisksChangeKind Kind, string ObjectPath);

public interface IUDisksChangeSource : IAsyncDisposable
{
    ValueTask<IAsyncEnumerable<UDisksChange>> SubscribeChangesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Publishes refreshed, de-duplicated Linux inventories after UDisks changes.</summary>
public sealed class LinuxDeviceObserver : IAsyncDisposable
{
    private readonly Func<CancellationToken, ValueTask<LinuxDeviceInventory>> _readInventory;
    private readonly IUDisksChangeSource _changeSource;

    public LinuxDeviceObserver(
        Func<CancellationToken, ValueTask<LinuxDeviceInventory>> readInventory,
        IUDisksChangeSource changeSource)
    {
        _readInventory = readInventory ?? throw new ArgumentNullException(nameof(readInventory));
        _changeSource = changeSource ?? throw new ArgumentNullException(nameof(changeSource));
    }

    public LinuxDeviceObserver(LinuxDeviceProvider provider, IUDisksChangeSource changeSource)
        : this(provider.GetInventoryAsync, changeSource)
    {
    }

    public async IAsyncEnumerable<LinuxDeviceInventory> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stream = await _changeSource.SubscribeChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var changes = stream.GetAsyncEnumerator(cancellationToken);
        var current = await _readInventory(cancellationToken).ConfigureAwait(false);
        var fingerprint = Fingerprint(current);
        yield return current;

        try
        {
            while (await changes.MoveNextAsync().ConfigureAwait(false))
            {
                var refreshed = await _readInventory(cancellationToken).ConfigureAwait(false);
                var refreshedFingerprint = Fingerprint(refreshed);
                if (refreshedFingerprint == fingerprint)
                    continue;
                fingerprint = refreshedFingerprint;
                yield return refreshed;
            }
        }
        finally
        {
            await _changeSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => _changeSource.DisposeAsync();

    private static string Fingerprint(LinuxDeviceInventory inventory) => string.Join("|",
        inventory.Devices
            .OrderBy(device => device.ObjectPath, StringComparer.Ordinal)
            .Select(device => string.Join(";", device.ObjectPath, device.Kind,
                device.IsSourceEligible, device.IsEjectable,
                string.Join(",", device.Volumes
                    .OrderBy(volume => volume.ObjectPath, StringComparer.Ordinal)
                    .Select(volume => string.Join(":", volume.ObjectPath, volume.DevicePath,
                        volume.Label, string.Join(",", volume.MountPoints)))))));
}
