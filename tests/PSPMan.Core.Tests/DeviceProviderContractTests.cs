using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using PSPMan.Core.Devices;

namespace PSPMan.Core.Tests;

public sealed class DeviceProviderContractTests
{
    private static readonly DeviceKey Device = new("test", "reader");
    private static readonly VolumeKey FirstVolume = new(Device, "volume-1");
    private static readonly VolumeKey SecondVolume = new(Device, "volume-2");

    [Fact]
    public async Task ConsumerCanReadMultipleVolumesIncludingAnUnmountedVolume()
    {
        await using IDeviceProvider provider = new FixtureProvider(CreateInventory());

        var inventory = await provider.GetSnapshotAsync();
        var device = Assert.Single(inventory.Devices);

        Assert.Equal("test", provider.ProviderId);
        Assert.Equal(Device, device.Key);
        Assert.Equal(DeviceCapabilities.RemovableSource | DeviceCapabilities.SafeRemoval, device.Capabilities);
        Assert.Equal(DeviceMedium.MemoryCard, device.Medium);
        Assert.Equal(2, device.Volumes.Length);
        Assert.Equal("/media/games", device.Volumes[0].Root);
        Assert.Null(device.Volumes[1].Root);
        Assert.Null(device.Volumes[1].Label);
        Assert.Equal(DeviceState.Unmounted, device.Volumes[1].State);
    }

    [Fact]
    public void SnapshotKeepsVolumeObservationsIndependentOfMutableInput()
    {
        var original = CreateInventory();
        var volumes = original.Devices[0].Volumes.ToList();
        var snapshot = original.Devices[0] with { Volumes = volumes.ToImmutableArray() };

        volumes.Clear();
        var changed = snapshot with
        {
            Volumes = snapshot.Volumes.SetItem(0, snapshot.Volumes[0] with { Root = "/media/new-root" })
        };

        Assert.Equal(2, snapshot.Volumes.Length);
        Assert.Equal("/media/games", snapshot.Volumes[0].Root);
        Assert.Equal("/media/new-root", changed.Volumes[0].Root);
        Assert.Equal(snapshot.Key, changed.Key);
    }

    [Fact]
    public async Task ConsumerCanObserveARevisionWithoutReusingTheOldRoot()
    {
        var initial = CreateInventory();
        var disconnected = initial with
        {
            Revision = 2,
            Devices = [initial.Devices[0] with
            {
                State = DeviceState.Unavailable,
                Volumes = [initial.Devices[0].Volumes[0] with
                {
                    State = DeviceState.Unavailable,
                    Root = null
                }]
            }]
        };
        await using IDeviceProvider provider = new FixtureProvider(initial, disconnected);
        using var cancellation = new CancellationTokenSource();
        await using var changes = provider.WatchChangesAsync(cancellation.Token).GetAsyncEnumerator();

        Assert.True(await changes.MoveNextAsync());
        Assert.Equal(2, changes.Current.Revision);
        Assert.Null(changes.Current.Devices[0].Volumes[0].Root);
        Assert.Equal(DeviceState.Unavailable, changes.Current.Devices[0].State);
        Assert.Equal("/media/games", initial.Devices[0].Volumes[0].Root);
    }

    [Fact]
    public async Task ConsumerCanAddressAVolumeForMountAndAWholeDeviceForRemoval()
    {
        var fixture = new FixtureProvider(CreateInventory());
        await using IDeviceProvider provider = fixture;
        var mountRequest = new DeviceMountRequest(SecondVolume, 1, "media-1");
        var removalRequest = new DeviceRemovalRequest(Device, 1, "media-1");
        using var cancellation = new CancellationTokenSource();

        var mount = await provider.MountAsync(mountRequest, cancellation.Token);
        var removal = await provider.RemoveAsync(removalRequest, cancellation.Token);

        Assert.Equal(mountRequest, fixture.MountRequest);
        Assert.Equal(removalRequest, fixture.RemovalRequest);
        Assert.Equal(cancellation.Token, fixture.LastOperationToken);
        Assert.Equal(DeviceOperationStatus.Succeeded, mount.Status);
        Assert.Null(mount.Error);
        Assert.Equal(DeviceOperationStatus.Failed, removal.Status);
        Assert.Equal("busy", removal.Error?.Code);
        Assert.Equal(FirstVolume, removal.Error?.Volume);
        Assert.Equal("A volume is busy.", removal.Error?.Message);
    }

    private static DeviceInventorySnapshot CreateInventory() => new(1,
    [
        new DeviceSnapshot(Device, "Card reader", DeviceMedium.MemoryCard,
            DeviceCapabilities.RemovableSource | DeviceCapabilities.SafeRemoval,
            new DeviceIdentity("synthetic-reader-id", DeviceIdentityConfidence.Persistent),
            "media-1", DeviceState.Ready,
            [
                new VolumeSnapshot(FirstVolume, "Games", "/media/games",
                    new DeviceIdentity("synthetic-volume-1", DeviceIdentityConfidence.Persistent),
                    DeviceState.Ready),
                new VolumeSnapshot(SecondVolume, null, null,
                    new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly),
                    DeviceState.Unmounted)
            ])
    ]);

    // This fixture demonstrates contract consumption, not native adapter correctness.
    private sealed class FixtureProvider(DeviceInventorySnapshot initial,
        DeviceInventorySnapshot? change = null) : IDeviceProvider
    {
        public string ProviderId => "test";
        public DeviceMountRequest? MountRequest { get; private set; }
        public DeviceRemovalRequest? RemovalRequest { get; private set; }
        public CancellationToken LastOperationToken { get; private set; }

        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(initial);
        }

        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            if (change is not null)
            {
                yield return change;
            }
        }

        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MountRequest = request;
            LastOperationToken = cancellationToken;
            return ValueTask.FromResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
        }

        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemovalRequest = request;
            LastOperationToken = cancellationToken;
            return ValueTask.FromResult(new DeviceOperationResult(DeviceOperationStatus.Failed,
                new DeviceOperationError("busy", "A volume is busy.", FirstVolume)));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
