using System.Collections.Immutable;
using PSPMan.Infraestructure.Devices.Linux;

namespace PSPMan.Infraestructure.Tests;

public sealed class LinuxVolumeUnmountCoordinatorTests
{
    [Fact]
    public async Task SkipsVolumesThatAreAlreadyUnmounted()
    {
        var device = Device([Volume("/volume-1") with { MountPoints = [] }]);
        var client = new FakeUnmountClient();
        var coordinator = new LinuxVolumeUnmountCoordinator(client, _ =>
            ValueTask.FromResult(new LinuxDeviceInventory([device])));

        var result = await coordinator.UnmountAsync(device);

        Assert.True(result.Succeeded);
        Assert.Empty(client.UnmountedPaths);
    }

    [Fact]
    public async Task UnmountsEachMountedVolumeAndRefreshesAfterEveryOperation()
    {
        var device = Device([Volume("/volume-1"), Volume("/volume-2")]);
        var refreshed = Device([
            Volume("/volume-1") with { MountPoints = [] },
            Volume("/volume-2") with { MountPoints = [] }]);
        var client = new FakeUnmountClient();
        var refreshCalls = 0;
        var coordinator = new LinuxVolumeUnmountCoordinator(client, _ =>
        {
            refreshCalls++;
            return ValueTask.FromResult(new LinuxDeviceInventory([refreshed]));
        });

        var result = await coordinator.UnmountAsync(device);

        Assert.True(result.Succeeded);
        Assert.Equal(["/volume-1", "/volume-2"], client.UnmountedPaths);
        Assert.Equal(3, refreshCalls);
        Assert.All(result.Inventory.Devices.Single().Volumes, volume =>
            Assert.Empty(volume.MountPoints));
    }

    [Fact]
    public async Task StopsOnBusyVolumeAndReturnsRefreshedPartialState()
    {
        var device = Device([Volume("/volume-busy"), Volume("/volume-never-reached")]);
        var client = new FakeUnmountClient(new UDisksClientException(
            "Busy", "The volume is busy."));
        var coordinator = new LinuxVolumeUnmountCoordinator(client, _ =>
            ValueTask.FromResult(new LinuxDeviceInventory([device])));

        var result = await coordinator.UnmountAsync(device);

        Assert.False(result.Succeeded);
        Assert.Equal("Busy", result.Error!.Code);
        Assert.Equal(["/volume-busy"], client.UnmountedPaths);
        Assert.Single(result.Inventory.Devices.Single().Volumes,
            volume => volume.ObjectPath == "/volume-busy" && volume.MountPoints.Length == 1);
    }

    [Fact]
    public async Task DoesNotStartUnmountingAfterCancellation()
    {
        var device = Device([Volume("/volume-1")]);
        var client = new FakeUnmountClient();
        var coordinator = new LinuxVolumeUnmountCoordinator(client, _ =>
            ValueTask.FromResult(new LinuxDeviceInventory([device])));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.UnmountAsync(device, cancellation.Token).AsTask());

        Assert.Empty(client.UnmountedPaths);
    }

    private static LinuxPhysicalDevice Device(ImmutableArray<LinuxVolume> volumes) => new()
    {
        ObjectPath = "/drive",
        Kind = LinuxDeviceKind.Usb,
        DisplayName = "USB reader",
        IsSourceEligible = true,
        IsEjectable = true,
        Volumes = volumes
    };

    private static LinuxVolume Volume(string path) => new()
    {
        ObjectPath = path,
        DevicePath = path.Replace("/volume", "/dev/sd", StringComparison.Ordinal),
        Label = "Games",
        MountPoints = ["/media/games"]
    };

    private sealed class FakeUnmountClient(UDisksClientException? exception = null) : IUDisksClient
    {
        public List<string> UnmountedPaths { get; } = [];

        public ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new UDisksObjectManagerSnapshot([]));

        public ValueTask UnmountAsync(string filesystemObjectPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnmountedPaths.Add(filesystemObjectPath);
            if (exception is not null)
                throw exception;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
