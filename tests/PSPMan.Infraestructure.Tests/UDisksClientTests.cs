using PSPMan.Infraestructure.Devices.Linux;

namespace PSPMan.Infraestructure.Tests;

public sealed class UDisksClientTests
{
    [Fact]
    public async Task DecodesTypedObjectManagerInventoryWithoutLocalizedParsing()
    {
        var transport = new FakeUDisksTransport(new UDisksObjectManagerSnapshot(
        [
            new UDisksManagedObject("/org/freedesktop/UDisks2/drives/usb", [
                new UDisksInterfaceSnapshot("org.freedesktop.UDisks2.Drive",
                    new Dictionary<string, UDisksPropertyValue>
                    {
                        ["Removable"] = UDisksPropertyValue.Boolean(true),
                        ["Ejectable"] = UDisksPropertyValue.Boolean(true)
                    })
            ]),
            new UDisksManagedObject("/org/freedesktop/UDisks2/block_devices/sdb1", [
                new UDisksInterfaceSnapshot("org.freedesktop.UDisks2.Block",
                    new Dictionary<string, UDisksPropertyValue>
                    {
                        ["Device"] = UDisksPropertyValue.Bytes("/dev/sdb1"),
                        ["IdLabel"] = UDisksPropertyValue.String("Games")
                    }),
                new UDisksInterfaceSnapshot("org.freedesktop.UDisks2.Filesystem",
                    new Dictionary<string, UDisksPropertyValue>
                    {
                        ["MountPoints"] = UDisksPropertyValue.StringArray("/media/games")
                    })
            ])]));
        await using var client = new UDisksClient(transport);

        var inventory = await client.GetManagedObjectsAsync();

        Assert.Equal(2, inventory.Objects.Length);
        var block = Assert.Single(inventory.Objects, item => item.ObjectPath.EndsWith("sdb1"));
        Assert.Equal("/dev/sdb1", block.Interface("org.freedesktop.UDisks2.Block").Property("Device").AsBytes());
        Assert.Equal("/media/games", block.Interface("org.freedesktop.UDisks2.Filesystem")
            .Property("MountPoints").AsStringArray().Single());
    }

    [Fact]
    public async Task ReportsUnavailableServiceWithoutInventingAnEmptyInventory()
    {
        var transport = new FakeUDisksTransport(exception: new UDisksTransportException(
            "ServiceUnavailable", "The UDisks2 service is unavailable."));
        await using var client = new UDisksClient(transport);

        var error = await Assert.ThrowsAsync<UDisksClientException>(() => client.GetManagedObjectsAsync().AsTask());

        Assert.Equal("ServiceUnavailable", error.Code);
        Assert.False(error.Message.Contains("locale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RejectsMalformedObjectPathsAndPropertyValues()
    {
        var transport = new FakeUDisksTransport(new UDisksObjectManagerSnapshot(
        [new UDisksManagedObject("not-an-object-path", [
            new UDisksInterfaceSnapshot("org.freedesktop.UDisks2.Block",
                new Dictionary<string, UDisksPropertyValue>
                {
                    ["Device"] = UDisksPropertyValue.String("wrong-variant")
                })
        ])]));
        await using var client = new UDisksClient(transport);

        var error = await Assert.ThrowsAsync<UDisksClientException>(() => client.GetManagedObjectsAsync().AsTask());

        Assert.Equal("MalformedReply", error.Code);
    }

    [Fact]
    public async Task PropagatesCancellationAndDisposesTransport()
    {
        var transport = new FakeUDisksTransport(new UDisksObjectManagerSnapshot([]));
        await using var client = new UDisksClient(transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetManagedObjectsAsync(cancellation.Token).AsTask());

        await client.DisposeAsync();

        Assert.True(transport.Disposed);
    }

    private sealed class FakeUDisksTransport(
        UDisksObjectManagerSnapshot? snapshot = null,
        UDisksTransportException? exception = null) : IUDisksTransport
    {
        public bool Disposed { get; private set; }

        public ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exception is not null) throw exception;
            return ValueTask.FromResult(snapshot!);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
