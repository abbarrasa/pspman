using System.Collections.Immutable;
using PSPMan.Infraestructure.Devices.Linux;

namespace PSPMan.Infraestructure.Tests;

public sealed class LinuxPhysicalDeviceRemovalCoordinatorTests
{
    [Fact]
    public async Task EjectsOpticalMediaAndRetainsTheDriveWhenMediaDisappears()
    {
        var drive = Device("/drive/optical", LinuxDeviceKind.Optical, ejectable: true,
            volumes: [Volume("/volume/disc", "/media/disc")]);
        var afterEject = Device("/drive/optical", LinuxDeviceKind.Optical, ejectable: true);
        var client = new FakeRemovalClient();
        var refreshes = new Queue<LinuxDeviceInventory>([
            Inventory(drive), Inventory(afterEject), Inventory(afterEject)]);
        var coordinator = new LinuxPhysicalDeviceRemovalCoordinator(client, Refresh(refreshes));

        var result = await coordinator.RemoveAsync(drive);

        Assert.True(result.Succeeded);
        Assert.Equal(["/drive/optical"], client.EjectedPaths);
        Assert.Empty(client.PoweredOffPaths);
        Assert.Contains(result.Inventory.Devices, item => item.ObjectPath == afterEject.ObjectPath);
    }

    [Fact]
    public async Task PowersOffOnlyTheSelectedPhysicalDeviceAfterAllVolumesAreUnmounted()
    {
        var selected = Device("/drive/usb-selected", LinuxDeviceKind.Usb,
            powerOffSupported: true, volumes: [Volume("/volume/selected", "/media/selected")]);
        var sibling = Device("/drive/usb-sibling", LinuxDeviceKind.Usb,
            powerOffSupported: true, volumes: [Volume("/volume/sibling")]);
        var afterPowerOff = Inventory(sibling);
        var unmountedSelected = Device("/drive/usb-selected", LinuxDeviceKind.Usb,
            powerOffSupported: true, volumes: [Volume("/volume/selected")]);
        var client = new FakeRemovalClient();
        var refreshes = new Queue<LinuxDeviceInventory>([
            Inventory(selected, sibling), Inventory(unmountedSelected, sibling), afterPowerOff]);
        var coordinator = new LinuxPhysicalDeviceRemovalCoordinator(client, Refresh(refreshes));

        var result = await coordinator.RemoveAsync(selected);

        Assert.True(result.Succeeded);
        Assert.Equal(["/volume/selected"], client.UnmountedPaths);
        Assert.Equal(["/drive/usb-selected"], client.PoweredOffPaths);
        Assert.Contains(result.Inventory.Devices, device => device.ObjectPath == sibling.ObjectPath);
    }

    [Fact]
    public async Task ReportsUnsupportedRemovalWithoutUnmounting()
    {
        var device = Device("/drive/reader", LinuxDeviceKind.MemoryCard);
        var client = new FakeRemovalClient();
        var coordinator = new LinuxPhysicalDeviceRemovalCoordinator(client,
            _ => ValueTask.FromResult(Inventory(device)));

        var result = await coordinator.RemoveAsync(device);

        Assert.False(result.Succeeded);
        Assert.Equal("RemovalUnsupported", result.Error?.Code);
        Assert.Empty(client.UnmountedPaths);
        Assert.Empty(client.EjectedPaths);
        Assert.Empty(client.PoweredOffPaths);
    }

    [Fact]
    public async Task PreservesRejectedNativeRemovalAndDoesNotReportSuccess()
    {
        var device = Device("/drive/usb", LinuxDeviceKind.Usb, powerOffSupported: true);
        var client = new FakeRemovalClient(
            powerOffError: new UDisksClientException("org.freedesktop.UDisks2.Error.Busy",
                "The device is busy."));
        var coordinator = new LinuxPhysicalDeviceRemovalCoordinator(client,
            _ => ValueTask.FromResult(Inventory(device)));

        var result = await coordinator.RemoveAsync(device);

        Assert.False(result.Succeeded);
        Assert.Equal("org.freedesktop.UDisks2.Error.Busy", result.Error?.Code);
        Assert.Single(result.Inventory.Devices);
    }

    [Fact]
    public async Task RejectsFalsePowerOffSuccessWhenTheDriveRemainsPresent()
    {
        var device = Device("/drive/usb", LinuxDeviceKind.Usb, powerOffSupported: true);
        var client = new FakeRemovalClient();
        var coordinator = new LinuxPhysicalDeviceRemovalCoordinator(client,
            _ => ValueTask.FromResult(Inventory(device)));

        var result = await coordinator.RemoveAsync(device);

        Assert.False(result.Succeeded);
        Assert.Equal("RemovalIncomplete", result.Error?.Code);
        Assert.Single(result.Inventory.Devices);
    }

    private static Func<CancellationToken, ValueTask<LinuxDeviceInventory>> Refresh(
        Queue<LinuxDeviceInventory> inventories) => _ =>
        ValueTask.FromResult(inventories.Count > 1 ? inventories.Dequeue() : inventories.Peek());

    private static LinuxDeviceInventory Inventory(params LinuxPhysicalDevice[] devices) =>
        new(devices);

    private static LinuxPhysicalDevice Device(
        string path,
        LinuxDeviceKind kind,
        bool ejectable = false,
        bool powerOffSupported = false,
        ImmutableArray<LinuxVolume> volumes = default) => new()
        {
            ObjectPath = path,
            Kind = kind,
            DisplayName = path,
            IsSourceEligible = true,
            IsEjectable = ejectable,
            IsPowerOffSupported = powerOffSupported,
            Volumes = volumes.IsDefault ? [] : volumes
        };

    private static LinuxVolume Volume(string path, string mountPoint = "") => new()
    {
        ObjectPath = path,
        DevicePath = path.Replace("/volume", "/dev/sd", StringComparison.Ordinal),
        Label = "Games",
        MountPoints = string.IsNullOrEmpty(mountPoint) ? [] : [mountPoint]
    };

    private sealed class FakeRemovalClient(UDisksClientException? powerOffError = null) : IUDisksClient
    {
        public List<string> UnmountedPaths { get; } = [];
        public List<string> EjectedPaths { get; } = [];
        public List<string> PoweredOffPaths { get; } = [];

        public ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new UDisksObjectManagerSnapshot([]));

        public ValueTask UnmountAsync(string filesystemObjectPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnmountedPaths.Add(filesystemObjectPath);
            return ValueTask.CompletedTask;
        }

        public ValueTask EjectAsync(string driveObjectPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EjectedPaths.Add(driveObjectPath);
            return ValueTask.CompletedTask;
        }

        public ValueTask PowerOffAsync(string driveObjectPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PoweredOffPaths.Add(driveObjectPath);
            if (powerOffError is not null)
                throw powerOffError;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
