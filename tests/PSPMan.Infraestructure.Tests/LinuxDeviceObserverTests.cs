using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PSPMan.Infraestructure.Devices.Linux;

namespace PSPMan.Infraestructure.Tests;

public sealed class LinuxDeviceObserverTests
{
    [Fact]
    public async Task SubscribesBeforeInitialInventoryAndPublishesInsertionAndRootChanges()
    {
        var source = new FakeChangeSource();
        var initial = Snapshot("/media/old");
        var changed = Snapshot("/media/new");
        var calls = 0;
        ValueTask<LinuxDeviceInventory> ReadAsync(CancellationToken token)
        {
            Assert.True(source.Subscribed);
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(calls++ == 0 ? initial : changed);
        }
        await using var observer = new LinuxDeviceObserver(ReadAsync, source);
        using var cancellation = new CancellationTokenSource();
        await using var changes = observer.ObserveAsync(cancellation.Token).GetAsyncEnumerator();

        Assert.True(await changes.MoveNextAsync());
        Assert.True(source.SubscribedBeforeInventory);
        Assert.Equal("/media/old", Root(changes.Current));

        await source.PublishAsync(new UDisksChange(UDisksChangeKind.InterfacesAdded, "/device/new"));
        Assert.True(await changes.MoveNextAsync());
        Assert.Equal("/media/new", Root(changes.Current));
        cancellation.Cancel();
    }

    [Fact]
    public async Task SuppressesDuplicateEventsAndDisposesSubscriptionsOnCancellation()
    {
        var source = new FakeChangeSource();
        var snapshot = Snapshot("/media/games");
        ValueTask<LinuxDeviceInventory> ReadAsync(CancellationToken token) =>
            ValueTask.FromResult(snapshot);
        await using var observer = new LinuxDeviceObserver(ReadAsync, source);
        using var cancellation = new CancellationTokenSource();
        await using var changes = observer.ObserveAsync(cancellation.Token).GetAsyncEnumerator();

        Assert.True(await changes.MoveNextAsync());
        await source.PublishAsync(new UDisksChange(UDisksChangeKind.PropertiesChanged, "/device"));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await changes.MoveNextAsync());

        Assert.True(source.Disposed);
    }

    private static string Root(LinuxDeviceInventory inventory) =>
        Assert.Single(Assert.Single(inventory.Devices).Volumes).MountPoints.Single();

    private static LinuxDeviceInventory Snapshot(string root) => new(
    [new LinuxPhysicalDevice
    {
        ObjectPath = "/device",
        Kind = LinuxDeviceKind.Usb,
        DisplayName = "USB reader",
        IsSourceEligible = true,
        IsEjectable = true,
        Volumes = [new LinuxVolume
        {
            ObjectPath = "/volume",
            DevicePath = "/dev/sdb1",
            Label = "Games",
            MountPoints = [root]
        }]
    }]);

    private sealed class FakeChangeSource : IUDisksChangeSource
    {
        private readonly Channel<UDisksChange> _changes = Channel.CreateUnbounded<UDisksChange>();

        public bool Subscribed { get; private set; }
        public bool SubscribedBeforeInventory => Subscribed;
        public bool Disposed { get; private set; }

        public ValueTask<IAsyncEnumerable<UDisksChange>> SubscribeChangesAsync(
            CancellationToken cancellationToken = default)
        {
            Subscribed = true;
            return ValueTask.FromResult<IAsyncEnumerable<UDisksChange>>(
                ReadChangesAsync(cancellationToken));
        }

        private async IAsyncEnumerable<UDisksChange> ReadChangesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var change in _changes.Reader.ReadAllAsync(cancellationToken))
                yield return change;
        }

        public ValueTask PublishAsync(UDisksChange change) => _changes.Writer.WriteAsync(change);
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _changes.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
