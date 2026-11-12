using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using PSPMan.Core.Devices;
using PSPMan.Infraestructure.Devices.Linux;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class PlatformSessionProviderTests
{
    [Fact]
    public void WindowsFailureMappingPreservesNativeVetoAndConfigurationDiagnostics()
    {
        var error = WindowsSessionDeviceProvider.FailureError(new(new("ConfigurationManagerFailure", "SafeRemoval", 23, "Busy"))
        { ConfigurationStatus = 23, VetoType = 6, VetoName = "Busy application", VolumeGuidPath = "volume-guid" });
        Assert.Equal("ConfigurationManagerFailure", error!.Code);
        Assert.Contains("CONFIGRET 23", error.Message);
        Assert.Contains("veto 6: Busy application", error.Message);
        Assert.Contains("volume-guid", error.Message);
    }

    [Fact]
    public async Task LinuxLabelsAndRootsDoNotEstablishIdentityOrChangeMediaGeneration()
    {
        var client = new LinuxClient();
        await using var provider = Linux(client);
        var first = Assert.Single((await provider.GetSnapshotAsync()).Devices);
        client.Root = "/media/new"; client.Label = "PSP";
        var second = Assert.Single((await provider.GetSnapshotAsync()).Devices);
        Assert.Equal(first.MediaGeneration, second.MediaGeneration);
        Assert.Equal(DeviceIdentityConfidence.SessionOnly, second.Identity.Confidence);
        Assert.Equal(DeviceMedium.Usb, second.Medium);
        Assert.Equal("/media/new", Assert.Single(second.Volumes).Root);
        client.Uuid = "new-format";
        Assert.NotEqual(first.MediaGeneration, Assert.Single((await provider.GetSnapshotAsync()).Devices).MediaGeneration);
    }

    [Fact]
    public async Task LinuxFilesystemDisappearanceInvalidatesMediaEvenWhenItsUuidIsUnchanged()
    {
        var client = new LinuxClient();
        await using var provider = Linux(client);
        var first = Assert.Single((await provider.GetSnapshotAsync()).Devices);
        client.Root = null; client.HasFilesystem = false;
        var empty = Assert.Single((await provider.GetSnapshotAsync()).Devices);
        Assert.NotEqual(first.MediaGeneration, empty.MediaGeneration);
        Assert.Null(Assert.Single(empty.Volumes).Root);
        Assert.Equal(DeviceState.Unavailable, Assert.Single(empty.Volumes).State);
    }

    [Fact]
    public async Task LinuxRecognizedPspIsADestinationNotASourceAndCardIdentityIsNotBorrowedFromReader()
    {
        var client = new LinuxClient();
        await using var provider = new LinuxSessionDeviceProvider(client, client, _ => Metadata(0x054c, 0x01c8, "unique"));
        var device = Assert.Single((await provider.GetSnapshotAsync()).Devices);
        Assert.Equal(DeviceMedium.Psp, device.Medium);
        Assert.True(device.Capabilities.HasFlag(DeviceCapabilities.PspDestination));
        Assert.False(device.Capabilities.HasFlag(DeviceCapabilities.RemovableSource));
        Assert.Equal(DeviceIdentityConfidence.Persistent, device.Identity.Confidence);
        Assert.Equal(DeviceIdentityConfidence.SessionOnly, Assert.Single(device.Volumes).Identity.Confidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinuxMountRequiresIndependentObservationAndPreservesNativeDenial(bool deny)
    {
        var client = new LinuxClient { Root = null, Deny = deny };
        await using var provider = Linux(client);
        var snapshot = await provider.GetSnapshotAsync(); var device = Assert.Single(snapshot.Devices);
        var result = await provider.MountAsync(new(Assert.Single(device.Volumes).Key, snapshot.Revision, device.MediaGeneration));
        Assert.Equal(deny ? DeviceOperationStatus.AuthorizationDenied : DeviceOperationStatus.Succeeded, result.Status);
        Assert.Equal(1, client.Mounts);
        if (deny) Assert.Equal("org.freedesktop.UDisks2.Error.NotAuthorized", result.Error!.Code);
        else Assert.Equal("/media/mounted", Assert.Single(Assert.Single((await provider.GetSnapshotAsync()).Devices).Volumes).Root);
    }

    [Fact]
    public async Task LinuxUnobservedMountDoesNotBecomeSuccess()
    {
        var client = new LinuxClient { Root = null, ObserveMount = false };
        await using var provider = Linux(client);
        var snapshot = await provider.GetSnapshotAsync(); var device = Assert.Single(snapshot.Devices);
        var result = await provider.MountAsync(new(Assert.Single(device.Volumes).Key, snapshot.Revision, device.MediaGeneration));
        Assert.Equal("MountNotObserved", result.Error!.Code);
    }

    [Fact]
    public async Task LinuxObservationSubscribesBeforeReadingAndDisposesResources()
    {
        var client = new LinuxClient();
        var provider = Linux(client);
        await using (var stream = provider.WatchChangesAsync().GetAsyncEnumerator())
        {
            Assert.True(await stream.MoveNextAsync());
            Assert.True(client.SubscribedBeforeRead);
        }
        await provider.DisposeAsync(); await provider.DisposeAsync();
        Assert.Equal(1, client.Disposals);
        Assert.Equal(1, client.StreamDisposals);
    }

    [Fact]
    public async Task WindowsProjectionRetainsObservedRootsAndRejectsChangedMediaBeforeNativeOperations()
    {
        var api = new WindowsApi();
        await using var provider = new WindowsSessionDeviceProvider(api, new Notifications());
        var snapshot = await provider.GetSnapshotAsync(); var device = Assert.Single(snapshot.Devices);
        Assert.Equal("E:\\", Assert.Single(device.Volumes).Root);
        Assert.Equal(DeviceMedium.Usb, device.Medium);
        api.Serial++;
        Assert.Equal(DeviceOperationStatus.StaleObservation,
            (await provider.RemoveAsync(new(device.Key, snapshot.Revision, device.MediaGeneration))).Status);
    }

    [Fact]
    public async Task WindowsAlreadyMountedVolumeUsesExistingRootWithoutCallingNativeMounting()
    {
        await using var provider = new WindowsSessionDeviceProvider(new WindowsApi(), new Notifications());
        var snapshot = await provider.GetSnapshotAsync(); var device = Assert.Single(snapshot.Devices);
        Assert.Equal(DeviceOperationStatus.Succeeded,
            (await provider.MountAsync(new(Assert.Single(device.Volumes).Key, snapshot.Revision, device.MediaGeneration))).Status);
    }

    [Fact]
    public async Task WindowsObserverReleasesAllNativeBoundaryRegistrations()
    {
        var notifications = new Notifications();
        var provider = new WindowsSessionDeviceProvider(new WindowsApi(), notifications);
        await using (var stream = provider.WatchChangesAsync().GetAsyncEnumerator())
        { Assert.True(await stream.MoveNextAsync()); Assert.Equal(4, notifications.Acquired); }
        await provider.DisposeAsync();
        Assert.Equal(notifications.Acquired, notifications.Released);
    }

    private static LinuxSessionDeviceProvider Linux(LinuxClient client) => new(client, new BorrowedLinuxChanges(client), _ => Metadata(null, null, null));
    private static LinuxUsbMetadata Metadata(ushort? vendor, ushort? product, string? serial) => new("/dev/sdb1", vendor, product, serial,
        [new(8, 6, 80)], "metadata", serial is null ? LinuxUsbIdentityConfidence.None : LinuxUsbIdentityConfidence.Reliable, LinuxUsbIdentityConfidence.Ambiguous);

    private sealed class BorrowedLinuxChanges(LinuxClient client) : IUDisksChangeSource
    {
        public ValueTask<IAsyncEnumerable<UDisksChange>> SubscribeChangesAsync(CancellationToken token = default) => client.SubscribeChangesAsync(token);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class LinuxClient : IUDisksClient, IUDisksChangeSource
    {
        public string? Root = "/media/original";
        public string Label = "Games", Uuid = "format";
        public bool Deny, Subscribed, SubscribedBeforeRead, ObserveMount = true, HasFilesystem = true;
        public int Mounts, Disposals, StreamDisposals;
        public ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(CancellationToken token = default)
        {
            SubscribedBeforeRead = Subscribed;
            return new(new UDisksObjectManagerSnapshot([
                new("/org/freedesktop/UDisks2/drives/usb", [new("org.freedesktop.UDisks2.Drive", new Dictionary<string, UDisksPropertyValue>
                { ["Removable"] = UDisksPropertyValue.Boolean(true), ["CanPowerOff"] = UDisksPropertyValue.Boolean(true), ["ConnectionBus"] = UDisksPropertyValue.String("usb") })]),
                new("/org/freedesktop/UDisks2/block_devices/sdb1", [new("org.freedesktop.UDisks2.Block", new Dictionary<string, UDisksPropertyValue>
                { ["Drive"] = UDisksPropertyValue.ObjectPath("/org/freedesktop/UDisks2/drives/usb"), ["Device"] = UDisksPropertyValue.Bytes("/dev/sdb1"),
                    ["IdLabel"] = UDisksPropertyValue.String(Label), ["IdUUID"] = UDisksPropertyValue.String(Uuid) }),
                    new(HasFilesystem ? "org.freedesktop.UDisks2.Filesystem" : "org.freedesktop.UDisks2.BlockOther", new Dictionary<string, UDisksPropertyValue>
                    { ["MountPoints"] = UDisksPropertyValue.StringArray(Root is null ? [] : [Root]) })])
            ]));
        }
        public ValueTask<string> MountAsync(string path, CancellationToken token = default)
        {
            Mounts++; if (Deny) throw new UDisksClientException("org.freedesktop.UDisks2.Error.NotAuthorized", "Denied");
            if (ObserveMount) Root = "/media/mounted";
            return new("/media/mounted");
        }
        public ValueTask<IAsyncEnumerable<UDisksChange>> SubscribeChangesAsync(CancellationToken token = default)
        { Subscribed = true; return new(Changes(token)); }
        private async IAsyncEnumerable<UDisksChange> Changes([EnumeratorCancellation] CancellationToken token)
        { try { await Task.Delay(Timeout.Infinite, token); yield break; } finally { StreamDisposals++; } }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class WindowsApi : IWindowsDeviceApi
    {
        public uint Serial = 1;
        public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken token = default) => new(new WindowsStorageSnapshot(
            [new(@"USBSTOR\DISK&VEN_TEST\SERIAL", null, "native-interface", "USB", new(7, 1), WindowsStorageBus.Usb, false, true, true)],
            [new("volume-interface", new(7, 1)) { VolumeGuidPath = @"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\", FileSystemSerialNumber = Serial,
                State = WindowsVolumeState.Available, MountPoints = ["E:\\"] }], []));
    }
    private sealed class Notifications : IWindowsDeviceNotificationApi
    {
        public int Acquired, Released;
        public IDisposable RegisterInterface(Guid classGuid, Action changed) { Acquired++; return new Lease(this); }
        public IDisposable RegisterVolume(string path, Action changed) { Acquired++; return new Lease(this); }
        private sealed class Lease(Notifications owner) : IDisposable
        { private bool _disposed; public void Dispose() { if (!_disposed) { _disposed = true; owner.Released++; } } }
    }
}
