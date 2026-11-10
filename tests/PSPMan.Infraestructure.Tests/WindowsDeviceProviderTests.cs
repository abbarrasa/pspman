using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsDeviceProviderTests
{
    [Fact]
    public async Task GroupsVolumesUnderTheirPhysicalDiskWithoutIncludingInternalStorage()
    {
        var usb = Disk("usb", WindowsStorageBus.Usb, 1);
        var internalDisk = Disk("internal", WindowsStorageBus.Sata, 0);
        var api = new FakeApi(new WindowsStorageSnapshot(
            [usb, internalDisk, usb], [Volume("volume-a", 1), Volume("volume-b", 1), Volume("internal-volume", 0)], []));
        var inventory = await new WindowsDeviceProvider(api).GetInventoryAsync();

        var device = Assert.Single(inventory.Devices);
        Assert.Equal("usb", device.InstanceId);
        Assert.Equal("parent-usb", device.ParentInstanceId);
        Assert.Equal(WindowsDeviceKind.Usb, device.Kind);
        Assert.True(device.IsSourceEligible);
        Assert.Equal(["volume-a", "volume-b"], device.Volumes.Select(volume => volume.InterfacePath));
        Assert.Empty(inventory.Errors);
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task RetainsEmptyOpticalDrivesAndDoesNotGroupUnrelatedSiblingDisks()
    {
        var optical = Disk("optical", WindowsStorageBus.Sata, 2) with { IsOptical = true, CanEject = true };
        var first = Disk("first", WindowsStorageBus.Usb, 3) with { ParentInstanceId = "shared-hub" };
        var second = Disk("second", WindowsStorageBus.Usb, 4) with { ParentInstanceId = "shared-hub" };
        var api = new FakeApi(new WindowsStorageSnapshot([optical, first, second],
            [Volume("first-volume", 3), Volume("second-volume", 4)], []));

        var inventory = await new WindowsDeviceProvider(api).GetInventoryAsync();

        Assert.Equal(3, inventory.Devices.Length);
        var opticalDevice = inventory.Devices.Single(device => device.InstanceId == "optical");
        Assert.Equal(WindowsDeviceKind.Optical, opticalDevice.Kind);
        Assert.True(opticalDevice.CanEject);
        Assert.Empty(opticalDevice.Volumes);
        Assert.Equal("first-volume", Assert.Single(inventory.Devices.Single(device => device.InstanceId == "first").Volumes).InterfacePath);
        Assert.Equal("second-volume", Assert.Single(inventory.Devices.Single(device => device.InstanceId == "second").Volumes).InterfacePath);
    }

    [Fact]
    public async Task ClassifiesRemovableUnknownBusAsMemoryCardAndRetainsNativeFailure()
    {
        var failure = new WindowsDeviceError("AccessDenied", "StorageQuery", 5);
        var reader = Disk("reader", WindowsStorageBus.Unknown, 5) with
        {
            IsRemovable = true,
            DisplayName = "",
            Error = failure
        };
        var api = new FakeApi(new WindowsStorageSnapshot([reader], [], [failure]));

        var inventory = await new WindowsDeviceProvider(api).GetInventoryAsync();

        var device = Assert.Single(inventory.Devices);
        Assert.Equal(WindowsDeviceKind.MemoryCard, device.Kind);
        Assert.True(device.IsSourceEligible);
        Assert.Equal("Removable device", device.DisplayName);
        Assert.Same(failure, device.Error);
        Assert.Same(failure, Assert.Single(inventory.Errors));
    }

    [Fact]
    public async Task DoesNotAssignAnAmbiguousVolumeToAnArbitraryPhysicalDevice()
    {
        var api = new FakeApi(new WindowsStorageSnapshot(
            [Disk("first", WindowsStorageBus.Usb, 1), Disk("second", WindowsStorageBus.Usb, 1)],
            [Volume("ambiguous", 1)], []));

        var inventory = await new WindowsDeviceProvider(api).GetInventoryAsync();

        Assert.All(inventory.Devices, device => Assert.Empty(device.Volumes));
        Assert.Equal("AmbiguousStorageParent", Assert.Single(inventory.Errors).Code);
    }

    [Theory]
    [InlineData(WindowsStorageBus.Sd)]
    [InlineData(WindowsStorageBus.Mmc)]
    public async Task RecognizesCardBusesWithoutChangingSharedSourceEligibility(WindowsStorageBus bus)
    {
        var api = new FakeApi(new WindowsStorageSnapshot([Disk("card", bus, 1)], [], []));

        var inventory = await new WindowsDeviceProvider(api).GetInventoryAsync();

        var device = Assert.Single(inventory.Devices);
        Assert.Equal(WindowsDeviceKind.MemoryCard, device.Kind);
        Assert.True(device.IsSourceEligible);
    }

    [Fact]
    public async Task CancellationDoesNotCallTheNativeBoundary()
    {
        var api = new FakeApi(new WindowsStorageSnapshot([], [], []));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsDeviceProvider(api).GetInventoryAsync(cancellation.Token).AsTask());

        Assert.Equal(0, api.Calls);
    }

    [NonWindowsFact]
    public async Task RealApiRejectsNonWindowsBeforeLoadingNativeLibraries()
    {
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            new WindowsDeviceApi().EnumerateStorageAsync().AsTask());
    }

    public sealed class NonWindowsFactAttribute : FactAttribute
    {
        public NonWindowsFactAttribute()
        {
            if (OperatingSystem.IsWindows())
                Skip = "Verifies the non-Windows guard; real Windows integration belongs to T36.";
        }
    }

    private static WindowsStorageDevice Disk(string id, WindowsStorageBus bus, uint number) => new(
        id, "parent-" + id, "interface-" + id, id, new WindowsStorageNumber(7, number),
        bus, IsOptical: false, IsRemovable: false, CanEject: false);

    private static WindowsVolumeReference Volume(string path, uint number) => new(path, new WindowsStorageNumber(7, number));

    private sealed class FakeApi(WindowsStorageSnapshot snapshot) : IWindowsDeviceApi
    {
        public int Calls { get; private set; }
        public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(snapshot);
        }
    }
}
