using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Avalonia.Headless;
using Avalonia.Threading;
using PSPMan.Core.Devices;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public sealed class DevicesViewModelTests
{
    [Fact]
    public void EmptyInventoryAndStartupFailureHaveDifferentEnglishFeedback()
    {
        using var empty = new DevicesViewModel(postToUi: action => action());
        empty.Update(Snapshot(1));
        Assert.True(empty.IsEmpty);
        Assert.Equal("No devices detected.", empty.StatusText);
        using var unavailable = new DevicesViewModel(postToUi: action => action(), startupError: new("UnsupportedPlatform", "Device services are unavailable."));
        Assert.True(unavailable.HasErrors);
        Assert.Equal("Device services are unavailable.", unavailable.StatusText);
        Assert.Contains("UnsupportedPlatform", Assert.Single(unavailable.ProviderErrors).ErrorText);
    }

    [Theory]
    [InlineData(DeviceState.Detecting, "Detecting", true)]
    [InlineData(DeviceState.Unmounted, "Not mounted", false)]
    [InlineData(DeviceState.Mounting, "Mounting", true)]
    [InlineData(DeviceState.Ready, "Ready", false)]
    [InlineData(DeviceState.Removing, "Removing", true)]
    [InlineData(DeviceState.Unavailable, "Unavailable", false)]
    [InlineData(DeviceState.Error, "Error", false)]
    public void ProjectsEnglishStatesAndPendingFlags(DeviceState state, string text, bool pending)
    {
        var row = new DeviceItemViewModel(Device(state: state));
        Assert.Equal(text, row.StateText);
        Assert.Equal(pending, row.IsPending);
        Assert.Equal("device", row.Identifier);
    }

    [Fact]
    public void SourcesAndPspDestinationsRemainSeparateEvenWithConflictingSourceFlags()
    {
        using var model = new DevicesViewModel(postToUi: action => action());
        var psp = Device("psp") with { Medium = DeviceMedium.Psp, Capabilities = DeviceCapabilities.PspDestination | DeviceCapabilities.RemovableSource };
        model.Update(Snapshot(1, Device(), psp));
        Assert.Equal("device", Assert.Single(model.Sources).Key.DeviceId);
        Assert.Equal("psp", Assert.Single(model.PspDestinations).Key.DeviceId);
        Assert.Equal("PSP destination", model.PspDestinations[0].RoleText);
        Assert.Equal("Game source", model.Sources[0].RoleText);
    }

    [Fact]
    public void AmbiguousReaderClassificationIsInformativeAndDoesNotChangeCapabilities()
    {
        var snapshot = Device() with { Medium = DeviceMedium.Unknown, DisplayName = "" };
        var row = new DeviceItemViewModel(snapshot);
        Assert.Equal("Memory Card", row.MediumText);
        Assert.Equal("Unnamed device", row.Name);
        Assert.True(row.IsSource);
        Assert.False(row.IsPspDestination);
        Assert.Equal(DeviceMedium.Unknown, snapshot.Medium);
        Assert.Equal(DeviceCapabilities.RemovableSource, snapshot.Capabilities);
    }

    [Fact]
    public void DuplicateOrMissingLabelsRemainDistinctByVolumeKeyAndObservedRoot()
    {
        var device = Device();
        device = device with { Volumes = [Volume(device.Key, "first", "Games", "E:\\"),
            Volume(device.Key, "second", "Games", "/media/second"), Volume(device.Key, "third", null, "/media/third")] };
        var row = new DeviceItemViewModel(device);
        Assert.Equal(3, row.Volumes.Length);
        Assert.NotEqual(row.Volumes[0].Key, row.Volumes[1].Key);
        Assert.Equal("E:\\", row.Volumes[0].Root);
        Assert.Equal("/media/second", row.Volumes[1].Root);
        Assert.Equal("Unlabelled volume", row.Volumes[2].Label);
    }

    [Theory]
    [InlineData(DeviceState.Unavailable)]
    [InlineData(DeviceState.Removing)]
    [InlineData(DeviceState.Unmounted)]
    [InlineData(DeviceState.Detecting)]
    public void UnusableDeviceNeverProjectsAnOldRoot(DeviceState state)
    {
        var row = new DeviceItemViewModel(Device(state: state));
        Assert.Null(Assert.Single(row.Volumes).Root);
        Assert.False(row.Volumes[0].IsAvailable);
    }

    [Fact]
    public void AReadyObservationWithoutAUsableRootIsNotAdvertisedAsAvailable()
    {
        var device = Device();
        var row = new DeviceItemViewModel(device with { Volumes = [device.Volumes[0] with { Root = " " }] });
        Assert.Equal("Unavailable", row.StateText);
        Assert.Equal("Unavailable", row.Volumes[0].StateText);
        Assert.False(row.Volumes[0].IsAvailable);
    }

    [Fact]
    public void PartialVolumeFailureRetainsOnlyTheConfirmedUsableSiblingRootAndNativeErrors()
    {
        var device = Device() with { State = DeviceState.Error, Error = new("RemovalRejected", "CONFIGRET 23; veto 6") };
        device = device with { Volumes = [Volume(device.Key, "good", "Games", "/media/good"),
            Volume(device.Key, "bad", "Games", "/media/stale") with { State = DeviceState.Error, Error = new("VolumeBusy", "Another application is using this volume.") }] };
        var row = new DeviceItemViewModel(device);
        Assert.Equal("/media/good", row.Volumes[0].Root);
        Assert.Null(row.Volumes[1].Root);
        Assert.Contains("VolumeBusy", row.Volumes[1].ErrorText);
        Assert.Contains("CONFIGRET 23; veto 6", row.ErrorText);
        Assert.True(row.HasErrors);
    }

    [Fact]
    public async Task WorkerUpdatesAreQueuedAndNotifyOnlyWhenTheUiContextDrains()
    {
        var queue = new UiQueue();
        using var model = new DevicesViewModel(postToUi: queue.Post);
        queue.Drain();
        var notifications = new List<int>();
        model.PropertyChanged += (_, _) => notifications.Add(Environment.CurrentManagedThreadId);
        await Task.Run(() => model.Update(Snapshot(1, Device())));
        Assert.Empty(model.Devices);
        Assert.Empty(notifications);
        var uiThread = Environment.CurrentManagedThreadId;
        queue.Drain();
        Assert.Single(model.Devices);
        Assert.NotEmpty(notifications);
        Assert.All(notifications, thread => Assert.Equal(uiThread, thread));
    }

    [Fact]
    public void QueuedOldSnapshotsCannotOverwriteNewerStateOrRestoreDisconnectedRoots()
    {
        var queue = new UiQueue();
        using var model = new DevicesViewModel(postToUi: queue.Post);
        model.Update(Snapshot(2, Device()));
        model.Update(Snapshot(3));
        model.Update(Snapshot(1, Device()));
        queue.Drain(reverse: true);
        Assert.Empty(model.Devices);
        Assert.Equal("No devices detected.", model.StatusText);
    }

    [Fact]
    public void ProviderFailureIsVisibleAlongsideOtherProvidersDevices()
    {
        using var model = new DevicesViewModel(postToUi: action => action());
        model.Update(Snapshot(1, Device()) with { ProviderErrors = ImmutableDictionary<string, DeviceOperationError>.Empty.Add("other", new("ServiceUnavailable", "Service failed.")) });
        Assert.Single(model.Devices);
        Assert.Equal("other", Assert.Single(model.ProviderErrors).ProviderId);
        Assert.Contains("ServiceUnavailable", model.ProviderErrors[0].ErrorText);
        Assert.True(model.HasErrors);
    }

    [Fact]
    public async Task SubscribesToSessionWithoutStartingItAndDisposesOnlyItsSubscription()
    {
        var queue = new UiQueue(); var provider = new Provider();
        await using var session = new DeviceSessionService([provider]);
        var model = new DevicesViewModel(session, queue.Post);
        Assert.Equal(0, provider.Reads);
        queue.Drain(); Assert.True(model.IsDetecting);
        await session.StartAsync();
        queue.Drain(); Assert.Single(model.Devices);
        model.Dispose(); model.Dispose();
        Assert.Equal(0, provider.Disposals);
        var queued = queue.Count;
        await session.SetAssociationsAsync(new DeviceSessionState());
        Assert.Equal(queued, queue.Count);
        model.Update(Snapshot(10)); queue.Drain();
        Assert.Single(model.Devices);
    }

    [Fact]
    public void DisposalDiscardsAlreadyQueuedCallbacks()
    {
        var queue = new UiQueue(); var model = new DevicesViewModel(postToUi: queue.Post);
        model.Update(Snapshot(1, Device())); model.Dispose(); queue.Drain();
        Assert.Empty(model.Devices);
    }

    private static DeviceSessionSnapshot Snapshot(long revision, params DeviceSnapshot[] devices) => new(new(revision, devices.ToImmutableArray()),
        new DeviceSessionState(), ImmutableDictionary<string, long>.Empty, ImmutableDictionary<string, DeviceOperationError>.Empty);
    private static DeviceSnapshot Device(string id = "device", DeviceState state = DeviceState.Ready)
    {
        var key = new DeviceKey("test", id);
        return new(key, "Reader", DeviceMedium.Usb, DeviceCapabilities.RemovableSource,
            new(null, DeviceIdentityConfidence.SessionOnly), "generation", state, [Volume(key, "volume", "Games", "/media/games")]);
    }
    private static VolumeSnapshot Volume(DeviceKey key, string id, string? label, string root) => new(new(key, id), label, root,
        new(null, DeviceIdentityConfidence.SessionOnly), DeviceState.Ready);
    private sealed class UiQueue
    {
        private readonly List<Action> _actions = [];
        public int Count { get { lock (_actions) return _actions.Count; } }
        public void Post(Action action) { lock (_actions) _actions.Add(action); }
        public void Drain(bool reverse = false)
        {
            Action[] actions; lock (_actions) { actions = _actions.ToArray(); _actions.Clear(); }
            if (reverse) Array.Reverse(actions);
            foreach (var action in actions) action();
        }
    }
    private sealed class Provider : IDeviceProvider
    {
        public int Reads, Disposals;
        public string ProviderId => "test";
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default)
        { Reads++; return new(new DeviceInventorySnapshot(1, [Device()])); }
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        { yield return await GetSnapshotAsync(token); await Task.Delay(Timeout.Infinite, token); }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}

[Collection("Headless UI")]
public sealed class DevicesViewModelDispatcherTests
{
    [Fact]
    public async Task DefaultPosterAppliesWorkerObservationsOnTheRealAvaloniaDispatcher()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await session.Dispatch(() =>
        {
            using var model = new DevicesViewModel();
            Dispatcher.UIThread.RunJobs();
            var notifications = 0;
            model.PropertyChanged += (_, _) => { Assert.True(Dispatcher.UIThread.CheckAccess()); notifications++; };
            var snapshot = new DeviceSessionSnapshot(new(1, []), new(), ImmutableDictionary<string, long>.Empty,
                ImmutableDictionary<string, DeviceOperationError>.Empty.Add("test", new("Unavailable", "Service unavailable.")));
            Task.Run(() => model.Update(snapshot)).GetAwaiter().GetResult();
            Assert.False(model.HasErrors);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.HasErrors);
            Assert.True(notifications > 0);
        }, CancellationToken.None);
    }
}
