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

    [Theory]
    [InlineData(DeviceMedium.Optical, DeviceCapabilities.OpticalSource)]
    [InlineData(DeviceMedium.Usb, DeviceCapabilities.RemovableSource)]
    [InlineData(DeviceMedium.MemoryCard, DeviceCapabilities.RemovableSource)]
    [InlineData(DeviceMedium.Psp, DeviceCapabilities.PspDestination)]
    public async Task AutomaticallyRequestsEligibleMountsAndPublishesPendingWithoutARoot(
        DeviceMedium medium, DeviceCapabilities capability)
    {
        var provider = new ControlledProvider("test") { AllowMounts = true };
        var device = UnmountedDevice() with { Medium = medium, Capabilities = capability | DeviceCapabilities.Mount };
        provider.Initial.SetResult(new DeviceInventorySnapshot(7, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);

        var call = await provider.NextMount();

        Assert.Equal(device.Volumes[0].Key, call.Request.Volume);
        Assert.Equal(7, call.Request.ExpectedRevision);
        Assert.Equal("media-1", call.Request.MediaGeneration);
        Assert.Equal(DeviceState.Mounting, service.Snapshot.Inventory.Devices[0].State);
        Assert.Equal(DeviceState.Mounting, service.Snapshot.Inventory.Devices[0].Volumes[0].State);
        Assert.Null(service.Snapshot.Inventory.Devices[0].Volumes[0].Root);
    }

    [Fact]
    public async Task MountsEveryVolumeInSeriesAndDeduplicatesNotificationsWhilePending()
    {
        var provider = new ControlledProvider("test") { AllowMounts = true };
        var device = UnmountedDevice();
        device = device with { Volumes = device.Volumes.Add(device.Volumes[0] with
            { Key = new VolumeKey(device.Key, "second-volume") }) };
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);
        var first = await provider.NextMount();
        provider.Emit(new DeviceInventorySnapshot(2, [device, device]));
        provider.Emit(new DeviceInventorySnapshot(3, [device]));
        await Until(service, snapshot => snapshot.ProviderRevisions["test"] == 3);
        Assert.Equal(1, provider.MountCount);

        first.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
        var second = await provider.NextMount();

        Assert.Equal("second-volume", second.Request.Volume.VolumeId);
        Assert.Equal(3, second.Request.ExpectedRevision);
        Assert.Equal(1, provider.MaximumConcurrentMounts);
        second.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
        await Until(service, snapshot => snapshot.Inventory.Devices[0].State != DeviceState.Mounting);
        Assert.Equal(2, provider.MountCount);
    }

    [Fact]
    public async Task ReusesMountedVolumesAndOnlyRequestsTheUnmountedOne()
    {
        var provider = new ControlledProvider("test") { AllowMounts = true };
        var device = Device("test") with { Capabilities = DeviceCapabilities.RemovableSource | DeviceCapabilities.Mount };
        device = device with { Volumes = device.Volumes.Add(device.Volumes[0] with
            { Key = new VolumeKey(device.Key, "unmounted"), Root = null, State = DeviceState.Unmounted }) };
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);

        var call = await provider.NextMount();

        Assert.Equal("unmounted", call.Request.Volume.VolumeId);
        Assert.Equal("/original-root", service.Snapshot.Inventory.Devices[0].Volumes[0].Root);
        Assert.Equal(1, provider.MountCount);
    }

    [Fact]
    public async Task SuccessDoesNotInventARootOrRemountAnAlreadyAttemptedVolume()
    {
        var provider = new ControlledProvider("test") { AllowMounts = true };
        var device = UnmountedDevice();
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);
        var call = await provider.NextMount();

        call.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
        await Until(service, snapshot => snapshot.Inventory.Devices[0].State == DeviceState.Unmounted);
        Assert.Null(service.Snapshot.Inventory.Devices[0].Volumes[0].Root);
        provider.Emit(new DeviceInventorySnapshot(2, [device]));
        await Until(service, snapshot => snapshot.ProviderRevisions["test"] == 2);
        Assert.Equal(1, provider.MountCount);
        var mounted = device with { State = DeviceState.Ready, Volumes = [device.Volumes[0] with
            { State = DeviceState.Ready, Root = "/observed-root" }] };
        provider.Emit(new DeviceInventorySnapshot(3, [mounted]));
        await Until(service, snapshot => snapshot.ProviderRevisions["test"] == 3);

        Assert.Equal("/observed-root", service.Snapshot.Inventory.Devices[0].Volumes[0].Root);
        Assert.Equal(1, provider.MountCount);
    }

    [Fact]
    public async Task AuthorizationDenialPublishesAnErrorWithoutAutomaticallyRetrying()
    {
        var provider = new ControlledProvider("test") { AllowMounts = true };
        var device = UnmountedDevice();
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);

        var call = await provider.NextMount();
        call.Completion.SetResult(new DeviceOperationResult(
            DeviceOperationStatus.AuthorizationDenied,
            new DeviceOperationError("AuthorizationDenied", "Authorization was denied.", call.Request.Volume)));

        await Until(service, snapshot => snapshot.Inventory.Devices[0].State == DeviceState.Error);
        Assert.Equal("AuthorizationDenied", service.Snapshot.Inventory.Devices[0].Volumes[0].Error?.Code);
        Assert.Equal(1, provider.MountCount);
    }

    [Fact]
    public async Task ExplicitRetryClearsThePreviousErrorAndRequestsTheVolumeAgain()
    {
        var provider = new ControlledProvider("test") { AllowMounts = true };
        var device = UnmountedDevice();
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);

        var first = await provider.NextMount();
        first.Completion.SetResult(new DeviceOperationResult(
            DeviceOperationStatus.Cancelled,
            new DeviceOperationError("AuthorizationCancelled", "Authorization was cancelled.", first.Request.Volume)));
        await Until(service, snapshot => snapshot.Inventory.Devices[0].State == DeviceState.Error);

        await service.RetryMountAsync(first.Request.Volume);
        var retry = await provider.NextMount();
        Assert.Equal(first.Request.Volume, retry.Request.Volume);
        Assert.Equal(DeviceState.Mounting, service.Snapshot.Inventory.Devices[0].State);

        retry.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
        await Until(service, snapshot => snapshot.Inventory.Devices[0].State == DeviceState.Unmounted);
        Assert.Equal(2, provider.MountCount);
    }

    [Fact]
    public async Task MountAuthorizationIsSerializedAcrossDevices()
    {
        var concurrency = new OperationConcurrencyProbe();
        var provider = new ControlledProvider("test", concurrency) { AllowMounts = true };
        var first = UnmountedDevice() with { Key = new DeviceKey("test", "first"),
            Volumes = [new VolumeSnapshot(new VolumeKey(new DeviceKey("test", "first"), "volume"),
                "First", null, new DeviceIdentity("first-volume", DeviceIdentityConfidence.Persistent), DeviceState.Unmounted)] };
        var second = UnmountedDevice() with { Key = new DeviceKey("test", "second"),
            Volumes = [new VolumeSnapshot(new VolumeKey(new DeviceKey("test", "second"), "volume"),
                "Second", null, new DeviceIdentity("second-volume", DeviceIdentityConfidence.Persistent), DeviceState.Unmounted)] };
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [first, second]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);

        var firstCall = await provider.NextMount();
        Assert.Equal(1, concurrency.Maximum);

        firstCall.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
        var secondCall = await provider.NextMount();
        Assert.Equal(1, concurrency.Maximum);
        secondCall.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
    }

    [Fact]
    public async Task SessionAssociationsRemainAvailableWhileAuthorizationIsPending()
    {
        var provider = new ControlledProvider("test") { AllowMounts = true };
        var device = UnmountedDevice();
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);
        var call = await provider.NextMount();

        var associations = DeviceAssociationRules.ChooseVolume(service.Snapshot.Associations,
            SourceGroup.Removable, call.Request.Volume);
        await service.SetAssociationsAsync(associations);

        Assert.Equal(call.Request.Volume, service.Snapshot.Associations.RemovableVolume);
        call.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
    }

    [Fact]
    public async Task SafeRemovalTargetsTheWholeDeviceAndPreventsMountsWhilePending()
    {
        var provider = new ControlledProvider("test") { AllowMounts = true, AllowRemovals = true };
        var device = Device("test") with { Capabilities = DeviceCapabilities.RemovableSource |
            DeviceCapabilities.Mount | DeviceCapabilities.SafeRemoval };
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);

        await service.RemoveDeviceAsync(device.Key);
        var removal = await provider.NextRemoval();
        Assert.Equal(device.Key, removal.Request.Device);
        Assert.Equal(1, removal.Request.ExpectedRevision);
        Assert.Equal("media-1", removal.Request.MediaGeneration);
        Assert.Equal(DeviceState.Removing, service.Snapshot.Inventory.Devices[0].State);
        Assert.Equal(0, provider.MountCount);

        removal.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
        var unmounted = device with { State = DeviceState.Unmounted,
            Volumes = [device.Volumes[0] with { Root = null, State = DeviceState.Unmounted }] };
        provider.Emit(new DeviceInventorySnapshot(2, [unmounted]));
        await Until(service, snapshot => snapshot.ProviderRevisions["test"] == 2
            && snapshot.Inventory.Devices[0].State == DeviceState.Unmounted);
        Assert.Equal(DeviceState.Unmounted, service.Snapshot.Inventory.Devices[0].State);
        Assert.Equal(0, provider.MountCount);
    }

    [Fact]
    public async Task FailedRemovalPreservesNativePartialErrorState()
    {
        var provider = new ControlledProvider("test") { AllowRemovals = true };
        var device = Device("test") with { Capabilities = DeviceCapabilities.RemovableSource |
            DeviceCapabilities.SafeRemoval };
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);

        await service.RemoveDeviceAsync(device.Key);
        var removal = await provider.NextRemoval();
        removal.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Failed,
            new DeviceOperationError("Busy", "The volume is busy.", device.Volumes[0].Key)));
        await Until(service, snapshot => snapshot.Inventory.Devices[0].State == DeviceState.Error);

        var observed = service.Snapshot.Inventory.Devices[0];
        Assert.Equal(DeviceState.Error, observed.State);
        Assert.Equal("Busy", observed.Volumes[0].Error?.Code);
        Assert.Equal("The volume is busy.", observed.Volumes[0].Error?.Message);
    }

    [Fact]
    public async Task SuccessfulRemovalSuppressesRemountUntilTheMediaGenerationChanges()
    {
        var provider = new ControlledProvider("test") { AllowMounts = true, AllowRemovals = true };
        var device = Device("test") with { State = DeviceState.Ready,
            Capabilities = DeviceCapabilities.RemovableSource | DeviceCapabilities.Mount | DeviceCapabilities.SafeRemoval };
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);

        await service.RemoveDeviceAsync(device.Key);
        var removal = await provider.NextRemoval();
        removal.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
        var sameMedia = device with { State = DeviceState.Unmounted,
            Volumes = [device.Volumes[0] with { Root = null, State = DeviceState.Unmounted }] };
        provider.Emit(new DeviceInventorySnapshot(2, [sameMedia]));
        await Until(service, snapshot => snapshot.ProviderRevisions["test"] == 2
            && snapshot.Inventory.Devices[0].State == DeviceState.Unmounted);
        Assert.Equal(0, provider.MountCount);

        var newMedia = sameMedia with { MediaGeneration = "media-2" };
        provider.Emit(new DeviceInventorySnapshot(3, [newMedia]));
        var mount = await provider.NextMount();
        Assert.Equal(1, provider.MountCount);
        mount.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
    }

    [Fact]
    public async Task RemovalIsRejectedWhileTheDeviceHasAnActiveMount()
    {
        var provider = new ControlledProvider("test") { AllowMounts = true, AllowRemovals = true };
        var device = UnmountedDevice() with { Capabilities = DeviceCapabilities.RemovableSource |
            DeviceCapabilities.Mount | DeviceCapabilities.SafeRemoval };
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);
        var mount = await provider.NextMount();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveDeviceAsync(device.Key).AsTask());
        Assert.Equal(0, provider.RemovalCount);
        mount.Completion.SetResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
    }

    [Theory]
    [InlineData(DeviceState.Ready, DeviceState.Ready, false)]
    [InlineData(DeviceState.Unavailable, DeviceState.Unmounted, true)]
    [InlineData(DeviceState.Removing, DeviceState.Unmounted, true)]
    [InlineData(DeviceState.Detecting, DeviceState.Unmounted, true)]
    [InlineData(DeviceState.Ready, DeviceState.Mounting, true)]
    public async Task DoesNotMountIneligibleVolumes(DeviceState deviceState, DeviceState volumeState, bool mountCapability)
    {
        var provider = new ControlledProvider("test") { AllowMounts = true };
        var device = UnmountedDevice() with { State = deviceState,
            Capabilities = DeviceCapabilities.RemovableSource | (mountCapability ? DeviceCapabilities.Mount : DeviceCapabilities.None) };
        device = device with { Volumes = [device.Volumes[0] with { State = volumeState }] };
        provider.Initial.SetResult(new DeviceInventorySnapshot(1, [device]));
        await using var service = new DeviceSessionService([provider]);
        await service.StartAsync().WaitAsync(Timeout);
        await service.SetAssociationsAsync(new DeviceSessionState());

        Assert.Equal(0, provider.MountCount);
    }

    private static DeviceSnapshot UnmountedDevice()
    {
        var device = Device("test");
        return device with { State = DeviceState.Unmounted,
            Capabilities = device.Capabilities | DeviceCapabilities.Mount,
            Volumes = [device.Volumes[0] with { State = DeviceState.Unmounted, Root = null }] };
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

    private sealed class ControlledProvider(string id, OperationConcurrencyProbe? concurrency = null) : IDeviceProvider
    {
        private readonly Channel<DeviceInventorySnapshot> _changes = Channel.CreateUnbounded<DeviceInventorySnapshot>();
        private bool _subscribed;
        private DeviceInventorySnapshot? _latest;
        private readonly Channel<PendingMount> _mounts = Channel.CreateUnbounded<PendingMount>();
        private readonly Channel<PendingRemoval> _removals = Channel.CreateUnbounded<PendingRemoval>();
        private int _mountCount;
        private int _activeMounts;
        private readonly OperationConcurrencyProbe _concurrency = concurrency ?? new();
        public bool AllowMounts { get; init; }
        public bool AllowRemovals { get; init; }
        public int MountCount => Volatile.Read(ref _mountCount);
        public int RemovalCount { get; private set; }
        public int MaximumConcurrentMounts { get; private set; }
        public Task<PendingMount> NextMount() => _mounts.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        public Task<PendingRemoval> NextRemoval() => _removals.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
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
            return new ValueTask<DeviceInventorySnapshot>(_latest is { } latest
                ? Task.FromResult(latest) : Initial.Task.WaitAsync(cancellationToken));
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

        public void Emit(DeviceInventorySnapshot snapshot)
        {
            Volatile.Write(ref _latest, snapshot);
            Assert.True(_changes.Writer.TryWrite(snapshot));
        }
        public void FailObservation(Exception error) => Assert.True(_changes.Writer.TryComplete(error));
        public async ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken cancellationToken = default)
        {
            if (!AllowMounts) throw new InvalidOperationException("Unexpected mount request.");
            using var operation = _concurrency.Enter();
            Interlocked.Increment(ref _mountCount);
            var active = Interlocked.Increment(ref _activeMounts);
            MaximumConcurrentMounts = Math.Max(MaximumConcurrentMounts, active);
            var pending = new PendingMount(request);
            Assert.True(_mounts.Writer.TryWrite(pending));
            try { return await pending.Completion.Task.WaitAsync(cancellationToken); }
            finally { Interlocked.Decrement(ref _activeMounts); }
        }
        public async ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken cancellationToken = default)
        {
            if (!AllowRemovals) throw new InvalidOperationException("Unexpected removal request.");
            RemovalCount++;
            var pending = new PendingRemoval(request);
            Assert.True(_removals.Writer.TryWrite(pending));
            return await pending.Completion.Task.WaitAsync(cancellationToken);
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _changes.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OperationConcurrencyProbe
    {
        private int _active;
        public int Maximum { get; private set; }

        public IDisposable Enter()
        {
            var active = Interlocked.Increment(ref _active);
            Maximum = Math.Max(Maximum, active);
            return new Exit(this);
        }

        private sealed class Exit(OperationConcurrencyProbe owner) : IDisposable
        {
            public void Dispose() => Interlocked.Decrement(ref owner._active);
        }
    }

    private sealed record PendingRemoval(DeviceRemovalRequest Request)
    {
        public TaskCompletionSource<DeviceOperationResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record PendingMount(DeviceMountRequest Request)
    {
        public TaskCompletionSource<DeviceOperationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

}
