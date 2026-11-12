using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public sealed class GameDeviceSelectionTests
{
    [Fact]
    public async Task TwoGamesRetainIndependentDestinationsAndSourcePreferences()
    {
        await using var test = await Fixture.Create(Source("usb"), Source("dvd", optical: true), Psp("first"), Psp("second"));
        await test.Devices.ChooseSourceCommand.ExecuteAsync(Assert.Single(test.Devices.RemovableSource.Candidates));
        await test.Devices.ChooseSourceCommand.ExecuteAsync(Assert.Single(test.Devices.OpticalSource.Candidates));
        test.Main.SelectedGame = test.First;
        Assert.Equal(DestinationResolutionStatus.ChooseDevice, test.Devices.GameDestination.Status);
        await test.Choose("first");
        test.Main.SelectedGame = test.Second;
        Assert.Equal(DestinationResolutionStatus.ChooseDevice, test.Devices.GameDestination.Status);
        await test.Choose("second");
        Assert.Equal(Psp("second").Key, test.Devices.GameDestination.SelectedDevice);
        test.Main.SelectedGame = test.First;
        Assert.Equal(Psp("first").Key, test.Devices.GameDestination.SelectedDevice);
        Assert.Equal("usb", test.Session.Snapshot.Associations.RemovableSource!.Value.DeviceId);
        Assert.Equal("dvd", test.Session.Snapshot.Associations.OpticalSource!.Value.DeviceId);
        Assert.Equal(2, test.Session.Snapshot.Associations.PspDestinations.Count);
        Assert.Equal(0, test.Repository.Writes);
    }

    [Theory]
    [InlineData(0, DestinationResolutionStatus.Unavailable)]
    [InlineData(1, DestinationResolutionStatus.Selected)]
    [InlineData(2, DestinationResolutionStatus.ChooseDevice)]
    public async Task ZeroOneAndMultiplePspsHaveDistinctFeedback(int count, DestinationResolutionStatus status)
    {
        await using var test = await Fixture.Create(Enumerable.Range(0, count).Select(index => Psp(index.ToString())).ToArray());
        Assert.Null(test.Devices.GameDestination.GameId);
        Assert.Empty(test.Devices.GameDestination.Candidates);
        test.Main.SelectedGame = test.First;
        Assert.Equal(status, test.Devices.GameDestination.Status);
        Assert.Equal(count, test.Devices.GameDestination.Candidates.Length);
        Assert.Empty(test.Session.Snapshot.Associations.PspDestinations);
        if (count == 1)
        {
            Assert.Equal("0", test.Devices.GameDestination.SelectedDevice!.Value.DeviceId);
            Assert.Equal("/media/0", Assert.Single(test.Devices.GameDestination.Volumes).Root);
            Assert.Contains("not proof", test.Devices.GameDestination.StatusText);
        }
        test.Main.SelectedGame = null;
        Assert.Null(test.Devices.GameDestination.SelectedDevice);
        Assert.Empty(test.Devices.GameDestination.Volumes);
        Assert.Contains("Select a game", test.Devices.GameDestination.StatusText);
    }

    [Theory]
    [InlineData(MountedPathSyntax.Linux, "/media/usb", "/media/usb/GAMES/Full/first.iso")]
    [InlineData(MountedPathSyntax.Windows, "E:\\", "E:\\GAMES\\Full\\first.iso")]
    public async Task SelectedGamePassesItsFullLocationToTheSourceProjection(MountedPathSyntax syntax, string root, string expected)
    {
        var source = Source("usb");
        source = source with { Volumes = [source.Volumes[0] with { Root = root }] };
        await using var test = await Fixture.Create([source], syntax: syntax);
        test.Main.SelectedGame = test.First;
        Assert.Equal(expected, test.Devices.ConstructedSourcePath);
        Assert.Equal("/GAMES/Full/first.iso", test.First.Location);
        test.Main.SelectedGame = null;
        Assert.Null(test.Devices.ConstructedSourcePath);
        Assert.Equal(0, test.Repository.Writes);
    }

    [Fact]
    public async Task DisconnectedExplicitDestinationDoesNotFallBackAndReliableReconnectUsesNewRoots()
    {
        var first = Psp("first");
        await using var test = await Fixture.Create(first, Psp("second"));
        test.Main.SelectedGame = test.First;
        await test.Choose("first");
        await test.Publish(Psp("second"));
        Assert.Equal(DestinationResolutionStatus.Unavailable, test.Devices.GameDestination.Status);
        Assert.Null(test.Devices.GameDestination.SelectedDevice);
        Assert.Empty(test.Devices.GameDestination.Volumes);
        Assert.Equal(first.Key, test.Devices.GameDestination.PreferredDevice);
        await test.Publish(first with { Key = new("test", "reconnected"), Volumes =
            [first.Volumes[0] with { Key = new(new("test", "reconnected"), "volume"), Root = "/media/new" }] }, Psp("second"));
        Assert.Equal("reconnected", test.Devices.GameDestination.SelectedDevice!.Value.DeviceId);
        Assert.Equal("/media/new", Assert.Single(test.Devices.GameDestination.Volumes).Root);
    }

    [Fact]
    public async Task OldGameAndObservationChoicesCannotBeAppliedByDirectExecution()
    {
        await using var test = await Fixture.Create(Psp("first"), Psp("second"));
        test.Main.SelectedGame = test.First;
        var old = test.Devices.GameDestination.Candidates[0];
        test.Main.SelectedGame = test.Second;
        Assert.False(test.Devices.ChoosePspCommand.CanExecute(old));
        await test.Devices.ChoosePspCommand.ExecuteAsync(old);
        Assert.Empty(test.Session.Snapshot.Associations.PspDestinations);
        test.Main.SelectedGame = test.First;
        Assert.False(test.Devices.ChoosePspCommand.CanExecute(old));
        var current = test.Devices.GameDestination.Candidates[0];
        await test.Publish(Psp("first") with { MediaGeneration = "replacement" }, Psp("second"));
        Assert.False(test.Devices.ChoosePspCommand.CanExecute(current));
        await test.Devices.ChoosePspCommand.ExecuteAsync(current);
        Assert.Empty(test.Session.Snapshot.Associations.PspDestinations);
        Assert.NotNull(test.Devices.DestinationSelectionError);
    }

    [Fact]
    public async Task ChoicesAreSessionOnlyAndChangingOneGameDoesNotChangeAnother()
    {
        await using (var test = await Fixture.Create(Psp("first"), Psp("second")))
        {
            test.Main.SelectedGame = test.First;
            await test.Choose("first");
            test.Main.SelectedGame = test.Second;
            await test.Choose("second");
            test.Main.SelectedGame = test.First;
            await test.Choose("second");
            Assert.All(test.Session.Snapshot.Associations.PspDestinations.Values, key => Assert.Equal("second", key.DeviceId));
        }
        await using var restarted = await Fixture.Create(Psp("first"), Psp("second"));
        restarted.Main.SelectedGame = restarted.First;
        Assert.Equal(DestinationResolutionStatus.ChooseDevice, restarted.Devices.GameDestination.Status);
        Assert.Empty(restarted.Session.Snapshot.Associations.PspDestinations);
    }

    [Fact]
    public async Task QueuedOldGameInputsAndChoicesCannotOverwriteTheLatestGame()
    {
        var actions = new List<Action>();
        void Post(Action action) { lock (actions) actions.Add(action); }
        void Drain(bool reverse = false)
        {
            Action[] queued; lock (actions) { queued = actions.ToArray(); actions.Clear(); }
            if (reverse) Array.Reverse(queued);
            foreach (var action in queued) action();
        }
        await using var test = await Fixture.Create([Psp("first"), Psp("second")], post: Post);
        Drain();
        test.Main.SelectedGame = test.First; Drain();
        var old = test.Devices.GameDestination.Candidates[0];
        var threads = new List<int>();
        test.Devices.PropertyChanged += (_, _) => threads.Add(Environment.CurrentManagedThreadId);
        test.Main.SelectedGame = test.Second;
        Assert.False(test.Devices.ChoosePspCommand.CanExecute(old));
        await test.Devices.ChoosePspCommand.ExecuteAsync(old);
        test.Main.SelectedGame = test.First;
        test.Main.SelectedGame = test.Second;
        Assert.Empty(threads);
        var uiThread = Environment.CurrentManagedThreadId;
        Drain(reverse: true);
        Assert.Equal(test.Second.Id, test.Devices.GameDestination.GameId);
        Assert.Null(test.Devices.DestinationSelectionError);
        Assert.All(threads, thread => Assert.Equal(uiThread, thread));
        Assert.All(test.Devices.GameDestination.Candidates, choice => Assert.Equal(test.Second.Id, choice.GameId));
    }

    [Fact]
    public async Task DisposalDisablesChoicesAndDoesNotOwnTheSession()
    {
        await using var test = await Fixture.Create(Psp("first"));
        test.Main.SelectedGame = test.First;
        var choice = Assert.Single(test.Devices.GameDestination.Candidates);
        test.Devices.Dispose();
        Assert.False(test.Devices.ChoosePspCommand.CanExecute(choice));
        await test.Devices.ChoosePspCommand.ExecuteAsync(choice);
        await test.Session.SetAssociationsAsync(new());
        Assert.Empty(test.Session.Snapshot.Associations.PspDestinations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingSourceAndDestinationChangesPreservePreferencesAndNeverRetargetAnotherGame(bool changeGame)
    {
        await using var test = await Fixture.Create(Source("usb"), Psp("first"), Psp("second"));
        test.Main.SelectedGame = test.First;
        using var release = new ManualResetEventSlim();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, DeviceSessionSnapshot snapshot)
        {
            if (snapshot.Associations.RemovableSource is not null && reached.TrySetResult())
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        }
        test.Session.SnapshotChanged += Changed;
        try
        {
            var source = test.Devices.ChooseSourceCommand.ExecuteAsync(Assert.Single(test.Devices.RemovableSource.Candidates));
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var choice = test.Devices.GameDestination.Candidates.First();
            var destination = test.Devices.ChoosePspCommand.ExecuteAsync(choice);
            Assert.False(destination.IsCompleted);
            if (changeGame) test.Main.SelectedGame = test.Second;
            release.Set();
            await Task.WhenAll(source, destination).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("usb", test.Session.Snapshot.Associations.RemovableSource!.Value.DeviceId);
            if (changeGame)
            {
                Assert.Empty(test.Session.Snapshot.Associations.PspDestinations);
                Assert.Equal(test.Second.Id, test.Devices.GameDestination.GameId);
                Assert.Null(test.Devices.DestinationSelectionError);
            }
            else Assert.Equal(choice.Device, test.Session.Snapshot.Associations.PspDestinations[test.First.Id]);
        }
        finally { release.Set(); test.Session.SnapshotChanged -= Changed; }
    }

    [Fact]
    public async Task OrdinarySourcesAndUnavailableVolumesCannotBeChosenAsPsps()
    {
        var unavailable = Psp("unavailable") with { State = DeviceState.Unavailable };
        await using var test = await Fixture.Create(Source("usb"), unavailable, Psp("first"));
        test.Main.SelectedGame = test.First;
        var choice = Assert.Single(test.Devices.GameDestination.Candidates);
        var wrongRole = choice with { Device = Source("usb").Key };
        Assert.False(test.Devices.ChoosePspCommand.CanExecute(wrongRole));
        await test.Devices.ChoosePspCommand.ExecuteAsync(wrongRole);
        Assert.Empty(test.Session.Snapshot.Associations.PspDestinations);
        await test.Publish(Source("usb"), unavailable);
        Assert.Empty(test.Devices.GameDestination.Candidates);
        Assert.Empty(test.Devices.GameDestination.Volumes);
        Assert.Equal(DestinationResolutionStatus.Unavailable, test.Devices.GameDestination.Status);
    }

    [Fact]
    public async Task AcceptedChoiceCompletingAfterAGameChangeOnlyBelongsToItsOriginalGame()
    {
        await using var test = await Fixture.Create(Psp("first"), Psp("second"));
        test.Main.SelectedGame = test.First;
        using var release = new ManualResetEventSlim();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, DeviceSessionSnapshot snapshot)
        {
            if (snapshot.Associations.PspDestinations.ContainsKey(test.First.Id) && reached.TrySetResult())
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        }
        test.Session.SnapshotChanged += Changed;
        try
        {
            var pending = test.Choose("first");
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            test.Main.SelectedGame = test.Second;
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(test.Second.Id, test.Devices.GameDestination.GameId);
            Assert.Equal(DestinationResolutionStatus.ChooseDevice, test.Devices.GameDestination.Status);
            Assert.False(test.Session.Snapshot.Associations.PspDestinations.ContainsKey(test.Second.Id));
            test.Main.SelectedGame = test.First;
            Assert.Equal("first", test.Devices.GameDestination.SelectedDevice!.Value.DeviceId);
        }
        finally { release.Set(); test.Session.SnapshotChanged -= Changed; }
    }

    [Fact]
    public void OptionalInjectionPreservesOldConstructorsAndCatalogFailureBehavior()
    {
        var game = Game("first"); var repository = new Repository(game);
        var old = new MainViewModel(new GameCatalogService(repository), null);
        old.SelectedGame = game;
        Assert.Null(old.Devices);
        Assert.Equal(game, old.SelectedGame);
        Assert.True(old.CanStartEdit);
        Assert.NotNull(new MainViewModel());
        using var devices = new DevicesViewModel(postToUi: action => action());
        var unavailable = new MainViewModel(null, "Catalog unavailable", devicesViewModel: devices);
        Assert.Same(devices, unavailable.Devices);
        Assert.True(unavailable.HasStartupError);
        Assert.False(unavailable.IsEditorAvailable);
    }

    private static GameRecord Game(string title) => new(Guid.NewGuid(), title, GameMedium.Usb, null,
        "/GAMES/Full/" + title + ".iso", GameFormat.Iso, 1, null);
    private static DeviceSnapshot Psp(string id) => Device(id, DeviceMedium.Psp, DeviceCapabilities.PspDestination);
    private static DeviceSnapshot Source(string id, bool optical = false) => Device(id,
        optical ? DeviceMedium.Optical : DeviceMedium.Usb, optical ? DeviceCapabilities.OpticalSource : DeviceCapabilities.RemovableSource);
    private static DeviceSnapshot Device(string id, DeviceMedium medium, DeviceCapabilities capabilities)
    {
        var key = new DeviceKey("test", id);
        var identity = new DeviceIdentity("identity-" + id, DeviceIdentityConfidence.Persistent);
        return new(key, id, medium, capabilities, identity, "generation", DeviceState.Ready,
            [new(new(key, "volume"), "Storage", "/media/" + id, identity, DeviceState.Ready)]);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly Provider Provider = new();
        public readonly GameRecord First = Game("first"), Second = Game("second");
        public Repository Repository { get; }
        public DeviceSessionService Session { get; }
        public DevicesViewModel Devices { get; }
        public MainViewModel Main { get; }
        private Fixture(DeviceSnapshot[] devices, MountedPathSyntax syntax, Action<Action>? post)
        {
            Provider.Inventory = new(1, devices.ToImmutableArray());
            Session = new([Provider]); Devices = new(Session, post ?? (action => action()));
            Repository = new(First, Second);
            Main = new(new GameCatalogService(Repository), null, devicesViewModel: Devices, mountedPathSyntax: syntax);
        }
        public static Task<Fixture> Create(params DeviceSnapshot[] devices) => Create(devices, MountedPathSyntax.Linux);
        public static async Task<Fixture> Create(DeviceSnapshot[] devices, MountedPathSyntax syntax = MountedPathSyntax.Linux, Action<Action>? post = null)
        { var test = new Fixture(devices, syntax, post); await test.Session.StartAsync(); return test; }
        public Task Choose(string id) => Devices.ChoosePspCommand.ExecuteAsync(Devices.GameDestination.Candidates.Single(choice => choice.Device.DeviceId == id));
        public async Task Publish(params DeviceSnapshot[] devices)
        {
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var revision = Provider.Inventory.Revision + 1;
            void Changed(object? sender, DeviceSessionSnapshot snapshot)
            { if (snapshot.ProviderRevisions.GetValueOrDefault("test") == revision) applied.TrySetResult(); }
            Session.SnapshotChanged += Changed;
            try
            {
                Provider.Inventory = new(revision, devices.ToImmutableArray());
                Provider.Changes.Writer.TryWrite(Provider.Inventory);
                await applied.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { Session.SnapshotChanged -= Changed; }
        }
        public async ValueTask DisposeAsync() { Devices.Dispose(); await Session.DisposeAsync(); }
    }
    private sealed class Repository(params GameRecord[] games) : IGameRecordRepository
    {
        public int Writes;
        public IReadOnlyList<GameRecord> List() => games;
        public GameRecord? GetById(Guid id) => games.FirstOrDefault(game => game.Id == id);
        public void Create(GameRecord record) { ++Writes; throw new InvalidOperationException(); }
        public void Update(GameRecord record) { ++Writes; throw new InvalidOperationException(); }
        public void Delete(Guid id) { ++Writes; throw new InvalidOperationException(); }
    }
    private sealed class Provider : IDeviceProvider
    {
        public DeviceInventorySnapshot Inventory = new(0, []);
        public readonly Channel<DeviceInventorySnapshot> Changes = Channel.CreateUnbounded<DeviceInventorySnapshot>();
        public string ProviderId => "test";
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default) => new(Inventory);
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        { yield return Inventory; await foreach (var snapshot in Changes.Reader.ReadAllAsync(token)) yield return snapshot; }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
