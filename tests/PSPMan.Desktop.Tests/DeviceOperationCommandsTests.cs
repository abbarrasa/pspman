using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public sealed class DeviceOperationCommandsTests
{
    [Fact]
    public async Task RemovalStaysPendingWithoutBlockingAnotherDevice()
    {
        await using var test = await Fixture.Create();
        var row = test.Row;
        Assert.True(row.CanRemove);
        await row.RemoveCommand.ExecuteAsync(null);
        await test.Wait(() => test.Row.State == DeviceState.Removing);
        Assert.False(test.Row.CanRemove);
        Assert.False(test.Row.RemoveCommand.CanExecute(null));
        Assert.True(test.Model.Devices.Single(item => item.Key.DeviceId == "other").CanRemove);
        var game = new GameRecord(Guid.NewGuid(), "Game", GameMedium.Usb, null, "/game.iso", GameFormat.Iso, 1, null);
        var catalog = new MainViewModel(new GameCatalogService(new Repository(game)), null);
        catalog.SelectedGame = game;
        Assert.Equal(game, catalog.SelectedGame);
        Assert.True(catalog.IsEditorAvailable);
        Assert.False(row.CanRemove);
        Assert.False(row.RemoveCommand.CanExecute(null));
        test.Provider.Completion.TrySetResult(new(DeviceOperationStatus.Failed, new("Busy", "Native veto")));
        await test.Wait(() => test.Row.ErrorText?.Contains("Native veto") == true);
        Assert.True(test.Row.CanRemove);
        Assert.True(test.Row.RemoveCommand.CanExecute(null));
        Assert.Equal(1, test.Provider.Removals);
    }

    [Theory]
    [InlineData(DeviceOperationStatus.AuthorizationDenied)]
    [InlineData(DeviceOperationStatus.Cancelled)]
    [InlineData(DeviceOperationStatus.Failed)]
    public async Task FailedMountOffersOnlyExplicitRetry(DeviceOperationStatus failure)
    {
        await using var test = await Fixture.Create(unmounted: true, failure: failure);
        await test.Wait(() => test.Row.Volumes[0].CanRetryMount);
        var volume = test.Row.Volumes[0];
        Assert.True(volume.RetryMountCommand.CanExecute(null));
        Assert.Contains("Denied", volume.ErrorText);
        Assert.Equal(1, test.Provider.Mounts);
        await volume.RetryMountCommand.ExecuteAsync(null);
        await test.Wait(() => test.Provider.Mounts == 2 && test.Row.Volumes[0].CanRetryMount);
        Assert.False(volume.CanRetryMount);
        Assert.False(volume.RetryMountCommand.CanExecute(null));
        Assert.Equal(2, test.Provider.Mounts);
    }

    [Fact]
    public async Task StaleDisconnectedAndDisposedRowsCannotSubmitOperations()
    {
        await using var test = await Fixture.Create();
        var old = test.Row;
        test.Provider.Inventory = new(2, [test.Provider.Inventory.Devices[0] with { MediaGeneration = "replacement" }]);
        test.Provider.Changes.Writer.TryWrite(test.Provider.Inventory);
        await test.Wait(() => test.Session.Snapshot.ProviderRevisions["test"] == 2);
        await old.RemoveCommand.ExecuteAsync(null);
        Assert.Equal(0, test.Provider.Removals);
        var current = test.Row;
        test.Model.Dispose();
        Assert.False(current.CanRemove);
        Assert.False(current.RemoveCommand.CanExecute(null));
        await current.RemoveCommand.ExecuteAsync(null);
        Assert.Equal(0, test.Provider.Removals);
        await test.Session.SetAssociationsAsync(new());
    }

    [Fact]
    public async Task PartialRemovalShowsActualRemainingRootsAndAllowsRetry()
    {
        await using var test = await Fixture.Create();
        await test.Row.RemoveCommand.ExecuteAsync(null);
        var device = test.Provider.Inventory.Devices[0];
        test.Provider.Inventory = new(2, [device with { Volumes = [device.Volumes[0], device.Volumes[0] with
            { Key = new(device.Key, "second"), Root = null, State = DeviceState.Unmounted }] }]);
        test.Provider.Completion.TrySetResult(new(DeviceOperationStatus.Failed, new("Busy", "Still in use")));
        await test.Wait(() => test.Row.ErrorText?.Contains("Still in use") == true);
        Assert.Equal("/media/source", test.Row.Volumes[0].Root);
        Assert.Null(test.Row.Volumes[1].Root);
        Assert.True(test.Row.CanRemove);
        Assert.False(test.Row.Volumes[1].CanRetryMount);
        await test.Row.RemoveCommand.ExecuteAsync(null);
        await test.Wait(() => test.Provider.Removals == 2);
    }

    [Fact]
    public async Task SuccessfulRemovalSuppressesRemountAndDisconnectRejectsOldRows()
    {
        await using var test = await Fixture.Create();
        var old = test.Row;
        await old.RemoveCommand.ExecuteAsync(null);
        var device = test.Provider.Inventory.Devices[0];
        test.Provider.Inventory = new(2, [device with { State = DeviceState.Unmounted,
            Capabilities = device.Capabilities | DeviceCapabilities.Mount,
            Volumes = [device.Volumes[0] with { State = DeviceState.Unmounted, Root = null }] }]);
        test.Provider.Completion.TrySetResult(new(DeviceOperationStatus.Succeeded));
        await test.Wait(() => test.Row.State == DeviceState.Unmounted);
        Assert.Equal(0, test.Provider.Mounts);
        Assert.Null(test.Row.Volumes[0].Root);
        var current = test.Row;
        test.Provider.Inventory = new(3, []);
        test.Provider.Changes.Writer.TryWrite(test.Provider.Inventory);
        await test.Wait(() => test.Model.Devices.IsEmpty);
        Assert.False(current.CanRemove);
        await current.RemoveCommand.ExecuteAsync(null);
        Assert.Equal(1, test.Provider.Removals);
    }

    [Fact]
    public async Task CoreRejectsAQueuedRequestFromAnOldObservation()
    {
        await using var test = await Fixture.Create();
        var observation = test.Session.Snapshot.Inventory;
        await test.Session.SetAssociationsAsync(new());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await test.Session.RemoveDeviceAsync(observation.Devices[0].Key, observation: observation));
        Assert.Equal(0, test.Provider.Removals);
    }

    [Fact]
    public async Task OperationProjectionAndAvailabilityNotificationsUseTheUiPoster()
    {
        await using var test = await Fixture.Create();
        var queue = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var model = new DevicesViewModel(test.Session, queue.Enqueue);
        while (queue.TryDequeue(out var action)) action();
        var row = model.Devices[0];
        var threads = new List<int>();
        row.PropertyChanged += (_, _) => threads.Add(Environment.CurrentManagedThreadId);
        await Task.Run(() => row.RemoveCommand.ExecuteAsync(null));
        Assert.Empty(threads);
        Assert.Equal(DeviceState.Ready, model.Devices[0].State);
        var uiThread = Environment.CurrentManagedThreadId;
        while (queue.TryDequeue(out var action)) action();
        Assert.Equal(DeviceState.Removing, model.Devices[0].State);
        Assert.NotEmpty(threads);
        Assert.All(threads, thread => Assert.Equal(uiThread, thread));
    }

    [Fact]
    public void RowsWithoutAnOperationalSessionHaveDisabledCommands()
    {
        using var model = new DevicesViewModel(postToUi: action => action());
        var key = new DeviceKey("test", "optical");
        var identity = new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly);
        var row = new DeviceItemViewModel(new(key, "DVD", DeviceMedium.Optical,
            DeviceCapabilities.SafeRemoval, identity, null, DeviceState.Unmounted, []));
        Assert.Equal("Eject", row.RemovalText);
        Assert.False(row.CanRemove);
        Assert.False(row.RemoveCommand.CanExecute(null));
    }

    private sealed class Repository(GameRecord game) : IGameRecordRepository
    {
        public IReadOnlyList<GameRecord> List() => [game];
        public GameRecord? GetById(Guid id) => game.Id == id ? game : null;
        public void Create(GameRecord record) => throw new NotSupportedException();
        public void Update(GameRecord record) => throw new NotSupportedException();
        public void Delete(Guid id) => throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Provider Provider { get; } = new();
        public DeviceSessionService Session { get; }
        public DevicesViewModel Model { get; }
        public DeviceItemViewModel Row => Model.Devices.First(item => item.Key.DeviceId == "source");
        private Fixture(bool unmounted, DeviceOperationStatus failure)
        {
            DeviceSnapshot Device(string id)
            {
                var key = new DeviceKey("test", id);
                var identity = new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly);
                return new(key, id, DeviceMedium.Usb, DeviceCapabilities.RemovableSource | DeviceCapabilities.SafeRemoval
                    | (unmounted && id == "source" ? DeviceCapabilities.Mount : DeviceCapabilities.None), identity,
                    "media", unmounted && id == "source" ? DeviceState.Unmounted : DeviceState.Ready,
                    [new(new(key, "volume"), "Games", unmounted && id == "source" ? null : "/media/" + id,
                        identity, unmounted && id == "source" ? DeviceState.Unmounted : DeviceState.Ready)]);
            }
            Provider.Inventory = new(1, [Device("source"), Device("other")]);
            Provider.Failure = failure;
            Session = new([Provider]); Model = new(Session, action => action());
        }
        public static async Task<Fixture> Create(bool unmounted = false, DeviceOperationStatus failure = DeviceOperationStatus.Failed)
        { var test = new Fixture(unmounted, failure); await test.Session.StartAsync(); return test; }
        public async Task Wait(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!predicate()) await Task.Delay(10, timeout.Token);
        }
        public async ValueTask DisposeAsync() { Model.Dispose(); await Session.DisposeAsync(); }
    }

    private sealed class Provider : IDeviceProvider
    {
        public string ProviderId => "test";
        public DeviceInventorySnapshot Inventory = new(0, []);
        public readonly Channel<DeviceInventorySnapshot> Changes = Channel.CreateUnbounded<DeviceInventorySnapshot>();
        public readonly TaskCompletionSource<DeviceOperationResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Removals;
        public int Mounts;
        public DeviceOperationStatus Failure;
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default) => new(Inventory);
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        { yield return Inventory; await foreach (var snapshot in Changes.Reader.ReadAllAsync(token)) yield return snapshot; }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default)
        { Interlocked.Increment(ref Mounts); return new(new DeviceOperationResult(Failure, new("Denied", "Native mount failure", request.Volume))); }
        public async ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default)
        { Interlocked.Increment(ref Removals); return await Completion.Task.WaitAsync(token); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
