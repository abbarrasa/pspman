using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public sealed class DeviceSessionServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StartsObservationBeforeReadingInitialInventoryAndDeduplicatesPhysicalDevices()
    {
        var provider = new ControlledProvider("test");
        var device = Device("test");
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device, device]));
        await using var service = new DeviceSessionService([provider]);

        await service.StartAsync().WaitAsync(Timeout);

        Assert.True(provider.SubscribedBeforeInitialRead);
        var observed = Assert.Single(service.Snapshot.Inventory.Devices);
        Assert.Equal(device with { Volumes = observed.Volumes }, observed);
        Assert.Equal<VolumeSnapshot>(device.Volumes, observed.Volumes);
        Assert.Equal(1, service.Snapshot.ProviderRevisions["test"]);
    }

    [Fact]
    public async Task InsertionDuringPendingInitialInventoryIsPublishedAndCannotBeOverwrittenByOlderInventory()
    {
        var provider = new ControlledProvider("test");
        await using var service = new DeviceSessionService([provider]);
        var startup = service.StartAsync();
        await provider.ReadStarted.Task.WaitAsync(Timeout);
        var device = Device("test");

        provider.Emit(new DeviceInventorySnapshot(2, [device]));
        await Until(service, snapshot => snapshot.ProviderRevisions.GetValueOrDefault("test") == 2);
        Assert.False(startup.IsCompleted);
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, []));
        await startup.WaitAsync(Timeout);

        var observed = Assert.Single(service.Snapshot.Inventory.Devices);
        Assert.Equal(device with { Volumes = observed.Volumes }, observed);
        Assert.Equal<VolumeSnapshot>(device.Volumes, observed.Volumes);
        Assert.Equal(2, service.Snapshot.ProviderRevisions["test"]);
    }

    [Fact]
    public async Task OldAndDuplicateRevisionsDoNotReplaceCurrentRootsOrPublishExtraSnapshots()
    {
        var provider = new ControlledProvider("test");
        var original = Device("test");
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [original]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);
        var updated = original with { Volumes = [original.Volumes[0] with { Root = "/new-root" }] };
        provider.Emit(new DeviceInventorySnapshot(3, [updated]));
        provider.Emit(new DeviceInventorySnapshot(2, [original]));
        provider.Emit(new DeviceInventorySnapshot(3, [original]));
        provider.Emit(new DeviceInventorySnapshot(4, [updated]));

        await Until(service, snapshot => snapshot.ProviderRevisions["test"] == 4);

        Assert.Equal("/new-root", service.Snapshot.Inventory.Devices[0].Volumes[0].Root);
        Assert.Equal(3, service.Snapshot.Inventory.Revision);
    }

    [Fact]
    public async Task DisconnectClearsStaleRootsAndMarksChosenAssociationsUnavailable()
    {
        var provider = new ControlledProvider("test");
        var device = Device("test");
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);
        await service.SetAssociationsAsync(DeviceAssociationRules.ChooseVolume(service.Snapshot.Associations,
            SourceGroup.Removable, device.Volumes[0].Key));
        var before = service.Snapshot;

        provider.Emit(new DeviceInventorySnapshot(2, [device with { State = DeviceState.Unavailable }]));
        await Until(service, snapshot => snapshot.ProviderRevisions["test"] == 2);

        Assert.Null(service.Snapshot.Inventory.Devices[0].Volumes[0].Root);
        Assert.Equal(SourceResolutionStatus.Unavailable, DeviceAssociationRules.ResolveSourceVolume(GameMedium.Usb,
            service.Snapshot.Inventory, service.Snapshot.Associations).Status);
        Assert.Equal("/original-root", before.Inventory.Devices[0].Volumes[0].Root);
    }

    [Fact]
    public async Task RemovalAndReliableReconnectionReconcileTheChosenVolumeWithItsNewRoot()
    {
        var provider = new ControlledProvider("test");
        var device = Device("test");
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);
        await service.SetAssociationsAsync(DeviceAssociationRules.ChooseVolume(service.Snapshot.Associations,
            SourceGroup.Removable, device.Volumes[0].Key));
        provider.Emit(new DeviceInventorySnapshot(2, []));
        await Until(service, snapshot => snapshot.ProviderRevisions["test"] == 2);
        var key = new DeviceKey("test", "reconnected");
        var returned = device with { Key = key, Volumes = [device.Volumes[0] with
            { Key = new VolumeKey(key, "new-volume"), Root = "/reconnected-root" }] };

        provider.Emit(new DeviceInventorySnapshot(3, [returned]));
        await Until(service, snapshot => snapshot.ProviderRevisions["test"] == 3);

        var result = DeviceAssociationRules.ResolveSourceVolume(GameMedium.Usb,
            service.Snapshot.Inventory, service.Snapshot.Associations);
        Assert.Equal(SourceResolutionStatus.Selected, result.Status);
        Assert.Equal("/reconnected-root", result.SelectedVolume?.Root);
    }

    [Fact]
    public async Task ProvidersHaveIndependentRevisionSpacesAndInventories()
    {
        var first = new ControlledProvider("first");
        var second = new ControlledProvider("second");
        first.Initial.SetResult(new DeviceInventorySnapshot(1, [Device("first")]));
        second.Initial.SetResult(new DeviceInventorySnapshot(50, [Device("second")]));
        await using var service = new DeviceSessionService([first, second]);
        await service.StartAsync().WaitAsync(Timeout);

        first.Emit(new DeviceInventorySnapshot(2, []));
        await Until(service, snapshot => snapshot.ProviderRevisions["first"] == 2);

        Assert.Equal("second", Assert.Single(service.Snapshot.Inventory.Devices).Key.ProviderId);
        Assert.Equal(50, service.Snapshot.ProviderRevisions["second"]);
    }

    [Fact]
    public async Task InitialReadFailureIsPublishedWithoutInventingReadyDevices()
    {
        var provider = new ControlledProvider("test");
        provider.Initial.SetException(new IOException("Inventory unavailable."));
        await using var service = new DeviceSessionService([provider]);

        await service.StartAsync().WaitAsync(Timeout);

        Assert.Empty(service.Snapshot.Inventory.Devices);
        Assert.Equal("Inventory unavailable.", service.Snapshot.ProviderErrors["test"].Message);
    }

    [Fact]
    public async Task DisposingTheServiceStopsObservationAndDisposesItsProviders()
    {
        var provider = new ControlledProvider("test");
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, []));
        var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);

        await service.DisposeAsync().AsTask().WaitAsync(Timeout);

        Assert.True(provider.SubscriptionStopped.Task.IsCompletedSuccessfully);
        Assert.True(provider.Disposed);
    }

    [Fact]
    public async Task FailedObservationInvalidatesRootsAndCannotBeRevivedByALateInitialRead()
    {
        var provider = new ControlledProvider("test");
        await using var service = new DeviceSessionService([provider]);
        var startup = service.StartAsync();
        await provider.ReadStarted.Task.WaitAsync(Timeout);
        provider.Emit(new DeviceInventorySnapshot(1, [Device("test")]));
        await Until(service, snapshot => snapshot.Inventory.Devices.Length == 1);

        provider.FailObservation(new IOException("Observation lost."));
        await Until(service, snapshot => snapshot.ProviderErrors.ContainsKey("test"));
        provider.Initial.SetResult(new DeviceInventorySnapshot(2, [Device("test")]));
        await startup.WaitAsync(Timeout);

        Assert.Empty(service.Snapshot.Inventory.Devices);
        Assert.Equal("Observation lost.", service.Snapshot.ProviderErrors["test"].Message);
    }

    private static async Task Until(DeviceSessionService service, Func<DeviceSessionSnapshot, bool> predicate)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, DeviceSessionSnapshot snapshot)
        {
            if (predicate(snapshot)) completion.TrySetResult();
        }
        service.SnapshotChanged += Handler;
        try
        {
            if (predicate(service.Snapshot)) completion.TrySetResult();
            await completion.Task.WaitAsync(Timeout);
        }
        finally { service.SnapshotChanged -= Handler; }
    }

    private static DeviceSnapshot Device(string provider)
    {
        var key = new DeviceKey(provider, "same-local-id");
        return new DeviceSnapshot(key, "Source", DeviceMedium.Usb, DeviceCapabilities.RemovableSource,
            new DeviceIdentity("synthetic-device", DeviceIdentityConfidence.Persistent), "media-1", DeviceState.Ready,
            [new VolumeSnapshot(new VolumeKey(key, "volume"), "Games", "/original-root",
                new DeviceIdentity("synthetic-volume", DeviceIdentityConfidence.Persistent), DeviceState.Ready)]);
    }

    private sealed class ControlledProvider(string id) : IDeviceProvider
    {
        private readonly Channel<DeviceInventorySnapshot> _changes = Channel.CreateUnbounded<DeviceInventorySnapshot>();
        private bool _subscribed;
        public string ProviderId => id;
        public TaskCompletionSource<DeviceInventorySnapshot> Initial { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SubscriptionStopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool SubscribedBeforeInitialRead { get; private set; }
        public bool Disposed { get; private set; }

        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            SubscribedBeforeInitialRead = _subscribed;
            ReadStarted.TrySetResult();
            return new ValueTask<DeviceInventorySnapshot>(Initial.Task.WaitAsync(cancellationToken));
        }

        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _subscribed = true;
            try
            {
                await foreach (var item in _changes.Reader.ReadAllAsync(cancellationToken)) yield return item;
            }
            finally { SubscriptionStopped.TrySetResult(); }
        }

        public void Emit(DeviceInventorySnapshot snapshot) => Assert.True(_changes.Writer.TryWrite(snapshot));
        public void FailObservation(Exception error) => Assert.True(_changes.Writer.TryComplete(error));
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("T10 must not mount devices.");
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("T10 must not remove devices.");
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _changes.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
