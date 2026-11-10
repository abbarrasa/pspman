using PSPMan.Infraestructure.Devices.Linux;

namespace PSPMan.Infraestructure.Tests;

public sealed class LinuxDeviceProviderTests
{
    [Fact]
    public async Task MapsRemovableDriveAndMountedVolumesWithoutChangingRoots()
    {
        const string drivePath = "/org/freedesktop/UDisks2/drives/usb_reader";
        var client = new FakeUDisksClient(new UDisksObjectManagerSnapshot(
        [
            new UDisksManagedObject(drivePath,
            [
                new UDisksInterfaceSnapshot("org.freedesktop.UDisks2.Drive",
                    new Dictionary<string, UDisksPropertyValue>
                    {
                        ["Removable"] = UDisksPropertyValue.Boolean(true),
                        ["Ejectable"] = UDisksPropertyValue.Boolean(true),
                        ["CanPowerOff"] = UDisksPropertyValue.Boolean(true),
                        ["ConnectionBus"] = UDisksPropertyValue.String("usb"),
                        ["Vendor"] = UDisksPropertyValue.String("Reader"),
                        ["Model"] = UDisksPropertyValue.String("Multi Slot")
                    })
            ]),
            new UDisksManagedObject("/org/freedesktop/UDisks2/block_devices/sdb1",
            [
                new UDisksInterfaceSnapshot("org.freedesktop.UDisks2.Block",
                    new Dictionary<string, UDisksPropertyValue>
                    {
                        ["Drive"] = UDisksPropertyValue.ObjectPath(drivePath),
                        ["Device"] = UDisksPropertyValue.Bytes("/dev/sdb1"),
                        ["IdLabel"] = UDisksPropertyValue.String("Games")
                    }),
                new UDisksInterfaceSnapshot("org.freedesktop.UDisks2.Filesystem",
                    new Dictionary<string, UDisksPropertyValue>
                    {
                        ["MountPoints"] = UDisksPropertyValue.StringArray("/media/games")
                    })
            ])
        ]));
        await using var provider = new LinuxDeviceProvider(client);

        var inventory = await provider.GetInventoryAsync();

        var device = Assert.Single(inventory.Devices);
        Assert.Equal(LinuxDeviceKind.Usb, device.Kind);
        Assert.True(device.IsSourceEligible);
        Assert.True(device.IsPowerOffSupported);
        var volume = Assert.Single(device.Volumes);
        Assert.Equal("Games", volume.Label);
        Assert.Equal("/dev/sdb1", volume.DevicePath);
        Assert.Equal("/media/games", Assert.Single(volume.MountPoints));
    }

    [Fact]
    public async Task KeepsEmptyOpticalReaderAndExcludesInternalStorage()
    {
        var client = new FakeUDisksClient(new UDisksObjectManagerSnapshot(
        [
            new UDisksManagedObject("/org/freedesktop/UDisks2/drives/optical",
            [
                new UDisksInterfaceSnapshot("org.freedesktop.UDisks2.Drive",
                    new Dictionary<string, UDisksPropertyValue>
                    {
                        ["Removable"] = UDisksPropertyValue.Boolean(true),
                        ["Ejectable"] = UDisksPropertyValue.Boolean(true),
                        ["MediaCompatibility"] = UDisksPropertyValue.StringArray("optical_mmc")
                    })
            ]),
            new UDisksManagedObject("/org/freedesktop/UDisks2/drives/internal",
            [
                new UDisksInterfaceSnapshot("org.freedesktop.UDisks2.Drive",
                    new Dictionary<string, UDisksPropertyValue>
                    {
                        ["Removable"] = UDisksPropertyValue.Boolean(false)
                    })
            ])
        ]));
        await using var provider = new LinuxDeviceProvider(client);

        var inventory = await provider.GetInventoryAsync();

        var device = Assert.Single(inventory.Devices);
        Assert.Equal(LinuxDeviceKind.Optical, device.Kind);
        Assert.Empty(device.Volumes);
    }

    [Fact]
    public async Task ClassifiesRemovableDriveWithUnknownBusAsMemoryCardWithoutChangingEligibility()
    {
        var client = new FakeUDisksClient(new UDisksObjectManagerSnapshot(
        [
            new UDisksManagedObject("/org/freedesktop/UDisks2/drives/unknown",
            [
                new UDisksInterfaceSnapshot("org.freedesktop.UDisks2.Drive",
                    new Dictionary<string, UDisksPropertyValue>
                    {
                        ["Removable"] = UDisksPropertyValue.Boolean(true),
                        ["ConnectionBus"] = UDisksPropertyValue.String("unknown")
                    })
            ])
        ]));
        await using var provider = new LinuxDeviceProvider(client);

        var inventory = await provider.GetInventoryAsync();

        var device = Assert.Single(inventory.Devices);
        Assert.Equal(LinuxDeviceKind.MemoryCard, device.Kind);
        Assert.True(device.IsSourceEligible);
    }

    private sealed class FakeUDisksClient(UDisksObjectManagerSnapshot snapshot) : IUDisksClient
    {
        public ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
