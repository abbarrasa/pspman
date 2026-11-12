using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public sealed class DeviceSourceCommandsTests
{
    [Fact]
    public async Task OpticalAndSharedRemovablePreferencesAreIndependent()
    {
        await using var test = await Fixture.Create(Device("usb"), Device("card", DeviceMedium.MemoryCard), Device("dvd", DeviceMedium.Optical));
        Assert.Equal(SourceResolutionStatus.ChooseDevice, test.Model.RemovableSource.Status);
        await test.Model.ChooseSourceCommand.ExecuteAsync(test.Model.RemovableSource.Candidates.Single(choice => choice.Device.DeviceId == "card"));
        await test.Model.ChooseSourceCommand.ExecuteAsync(Assert.Single(test.Model.OpticalSource.Candidates));
        Assert.Equal("card", test.Session.Snapshot.Associations.RemovableSource!.Value.DeviceId);
        Assert.Equal("dvd", test.Session.Snapshot.Associations.OpticalSource!.Value.DeviceId);
        test.Model.SetGameSource(GameMedium.Usb, "/GAMES/Game.rar", MountedPathSyntax.Linux);
        Assert.Equal("/media/card/GAMES/Game.rar", test.Model.ConstructedSourcePath);
        test.Model.SetGameSource(GameMedium.MemoryCard, "/GAMES/Game.rar", MountedPathSyntax.Linux);
        Assert.Equal("/media/card/GAMES/Game.rar", test.Model.ConstructedSourcePath);
    }

    [Theory]
    [InlineData(MountedPathSyntax.Linux, "/media/games", "/media/games/GAMES/Full/Game.rar")]
    [InlineData(MountedPathSyntax.Windows, "E:\\", "E:\\GAMES\\Full\\Game.rar")]
    public async Task UniqueSourceUsesTheCompleteLocationWithoutProbingItsFiles(MountedPathSyntax syntax, string root, string expected)
    {
        var device = Device("source");
        device = device with { Volumes = [device.Volumes[0] with { Root = root }] };
        await using var test = await Fixture.Create(device);
        test.Model.SetGameSource(GameMedium.Usb, "/GAMES/Full/Game.rar", syntax);
        Assert.Equal(expected, test.Model.ConstructedSourcePath);
        Assert.Contains("not proof", test.Model.SourcePathStatusText);
        Assert.Null(test.Session.Snapshot.Associations.RemovableSource);
    }

    [Fact]
    public async Task MultipleVolumesRequireExplicitChoiceEvenWithDuplicateLabels()
    {
        var device = Device("usb");
        device = device with { Volumes = [device.Volumes[0], device.Volumes[0] with { Key = new(device.Key, "second"), Root = "/media/second" }] };
        await using var test = await Fixture.Create(device);
        test.Model.SetGameSource(GameMedium.Usb, "/GAMES/file.iso", MountedPathSyntax.Linux);
        Assert.Equal(SourceResolutionStatus.ChooseVolume, test.Model.RemovableSource.Status);
        Assert.Null(test.Model.ConstructedSourcePath);
        Assert.Equal(2, test.Model.RemovableSource.Volumes.Length);
        await test.Model.ChooseSourceVolumeCommand.ExecuteAsync(test.Model.RemovableSource.Volumes.Single(choice => choice.Volume.VolumeId == "second"));
        Assert.Equal("/media/second/GAMES/file.iso", test.Model.ConstructedSourcePath);
    }

    [Fact]
    public async Task UnavailableExplicitSourceDoesNotFallBackToRemainingCandidate()
    {
        await using var test = await Fixture.Create(Device("one"), Device("two"));
        await test.Model.ChooseSourceCommand.ExecuteAsync(test.Model.RemovableSource.Candidates.First());
        test.Model.SetGameSource(GameMedium.Usb, "/GAMES/file.iso", MountedPathSyntax.Linux);
        await test.Publish(Device("two"));
        Assert.Equal(SourceResolutionStatus.Unavailable, test.Model.RemovableSource.Status);
        Assert.Null(test.Model.ConstructedSourcePath);
        Assert.Equal("one", test.Session.Snapshot.Associations.RemovableSource!.Value.DeviceId);
        await test.Model.ChooseSourceCommand.ExecuteAsync(Assert.Single(test.Model.RemovableSource.Candidates));
        Assert.Equal("/media/two/GAMES/file.iso", test.Model.ConstructedSourcePath);
    }

    [Fact]
    public async Task ChangedRootRefreshesPathAndOldChoiceCannotBeApplied()
    {
        var device = Device("one");
        await using var test = await Fixture.Create(device);
        var old = Assert.Single(test.Model.RemovableSource.Candidates);
        test.Model.SetGameSource(GameMedium.Usb, "/full/file.iso", MountedPathSyntax.Linux);
        await test.Publish(device with { Volumes = [device.Volumes[0] with { Root = "/media/new" }] });
        Assert.Equal("/media/new/full/file.iso", test.Model.ConstructedSourcePath);
        Assert.False(test.Model.ChooseSourceCommand.CanExecute(old));
        await test.Model.ChooseSourceCommand.ExecuteAsync(old);
        Assert.Null(test.Session.Snapshot.Associations.RemovableSource);
        Assert.NotNull(test.Model.SourceSelectionError);
    }

    [Fact]
    public async Task InvalidHostLocationReportsAnErrorWithoutSanitizingIt()
    {
        var device = Device("one");
        await using var test = await Fixture.Create(device with { Volumes = [device.Volumes[0] with { Root = "E:\\" }] });
        test.Model.SetGameSource(GameMedium.Usb, "/GAMES/CON.iso", MountedPathSyntax.Windows);
        Assert.Null(test.Model.ConstructedSourcePath);
        Assert.Contains("cannot be used on Windows", test.Model.SourcePathError);
    }

    [Fact]
    public async Task GameChangesDoNotClearSessionSourcePreferencesAndRestartDoes()
    {
        await using (var test = await Fixture.Create(Device("one"), Device("two")))
        {
            await test.Model.ChooseSourceCommand.ExecuteAsync(test.Model.RemovableSource.Candidates.First());
            test.Model.SetGameSource(GameMedium.Usb, "/first.iso", MountedPathSyntax.Linux);
            test.Model.SetGameSource(GameMedium.Usb, "/second.iso", MountedPathSyntax.Linux);
            Assert.Equal("/media/one/second.iso", test.Model.ConstructedSourcePath);
        }
        await using var restarted = await Fixture.Create(Device("one"), Device("two"));
        Assert.Equal(SourceResolutionStatus.ChooseDevice, restarted.Model.RemovableSource.Status);
    }

    [Fact]
    public async Task MissingExplicitVolumeDoesNotFallBackToAnotherVolumeOnTheSameDevice()
    {
        var device = Device("one");
        var second = device.Volumes[0] with { Key = new(device.Key, "second"), Root = "/media/second", Label = null };
        device = device with { Volumes = [device.Volumes[0], second] };
        await using var test = await Fixture.Create(device);
        await test.Model.ChooseSourceVolumeCommand.ExecuteAsync(test.Model.RemovableSource.Volumes.First());
        test.Model.SetGameSource(GameMedium.Usb, "/file.iso", MountedPathSyntax.Linux);
        await test.Publish(device with { Volumes = [second] });
        Assert.Equal(SourceResolutionStatus.Unavailable, test.Model.RemovableSource.Status);
        Assert.Null(test.Model.ConstructedSourcePath);
        Assert.Equal("Unlabelled volume", Assert.Single(test.Model.RemovableSource.Volumes).Label);
        await test.Model.ChooseSourceVolumeCommand.ExecuteAsync(test.Model.RemovableSource.Volumes[0]);
        Assert.Equal("/media/second/file.iso", test.Model.ConstructedSourcePath);
    }

    [Fact]
    public async Task SourceChoicePreservesAnExistingPerGamePspAssociation()
    {
        var psp = Device("psp") with { Medium = DeviceMedium.Psp, Capabilities = DeviceCapabilities.PspDestination };
        await using var test = await Fixture.Create(Device("one"), psp);
        var game = Guid.NewGuid();
        await test.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseDestination(test.Session.Snapshot.Associations, game, psp.Key));
        await test.Model.ChooseSourceCommand.ExecuteAsync(Assert.Single(test.Model.RemovableSource.Candidates));
        Assert.Equal(psp.Key, test.Session.Snapshot.Associations.PspDestinations[game]);
        Assert.DoesNotContain(test.Model.RemovableSource.Candidates, choice => choice.Device == psp.Key);
    }

    [Fact]
    public async Task ChangedMediaInvalidatesOldChoicesAndDoesNotRecoverAReaderBasedPreference()
    {
        var device = Device("one");
        await using var test = await Fixture.Create(device);
        await test.Model.ChooseSourceCommand.ExecuteAsync(Assert.Single(test.Model.RemovableSource.Candidates));
        var old = Assert.Single(test.Model.RemovableSource.Candidates);
        await test.Publish(device with { MediaGeneration = "replacement" });
        Assert.Equal(SourceResolutionStatus.Unavailable, test.Model.RemovableSource.Status);
        Assert.False(test.Model.ChooseSourceCommand.CanExecute(old));
    }

    [Fact]
    public async Task GameMetadataNotificationsAreMarshalledAndLateInputsCannotRestoreAnOldPath()
    {
        var actions = new List<Action>();
        void Post(Action action) { lock (actions) actions.Add(action); }
        void Drain(bool reverse = false)
        {
            Action[] queued; lock (actions) { queued = actions.ToArray(); actions.Clear(); }
            if (reverse) Array.Reverse(queued);
            foreach (var action in queued) action();
        }
        var provider = new Provider { Inventory = new(1, [Device("one")]) };
        await using var session = new DeviceSessionService([provider]);
        using var model = new DevicesViewModel(session, Post);
        await session.StartAsync(); Drain();
        var notifications = new List<int>();
        model.PropertyChanged += (_, _) => notifications.Add(Environment.CurrentManagedThreadId);
        await Task.Run(() =>
        {
            model.SetGameSource(GameMedium.Usb, "/old.iso", MountedPathSyntax.Linux);
            model.SetGameSource(GameMedium.Usb, "/new.iso", MountedPathSyntax.Linux);
        });
        Assert.Null(model.ConstructedSourcePath);
        Assert.Empty(notifications);
        var uiThread = Environment.CurrentManagedThreadId;
        Drain(reverse: true);
        Assert.Equal("/media/one/new.iso", model.ConstructedSourcePath);
        Assert.All(notifications, thread => Assert.Equal(uiThread, thread));
    }

    [Fact]
    public async Task DisposalDisablesCommandsWithoutDisposingTheSession()
    {
        await using var test = await Fixture.Create(Device("one"));
        var choice = Assert.Single(test.Model.RemovableSource.Candidates);
        test.Model.Dispose();
        Assert.False(test.Model.ChooseSourceCommand.CanExecute(choice));
        await test.Model.ChooseSourceCommand.ExecuteAsync(choice);
        Assert.Null(test.Session.Snapshot.Associations.RemovableSource);
    }

    private static DeviceSnapshot Device(string id, DeviceMedium medium = DeviceMedium.Usb)
    {
        var key = new DeviceKey("test", id); var identity = new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly);
        return new(key, id, medium, medium == DeviceMedium.Optical ? DeviceCapabilities.OpticalSource : DeviceCapabilities.RemovableSource,
            identity, "generation", DeviceState.Ready, [new(new(key, "volume"), "Games", "/media/" + id, identity, DeviceState.Ready)]);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Provider _provider = new();
        public DeviceSessionService Session { get; }
        public DevicesViewModel Model { get; }
        private Fixture(DeviceSnapshot[] devices)
        {
            _provider.Inventory = new(1, devices.ToImmutableArray());
            Session = new([_provider]); Model = new(Session, action => action());
        }
        public static async Task<Fixture> Create(params DeviceSnapshot[] devices)
        { var fixture = new Fixture(devices); await fixture.Session.StartAsync(); return fixture; }
        public async Task Publish(params DeviceSnapshot[] devices)
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var revision = _provider.Inventory.Revision + 1;
            void Changed(object? sender, DeviceSessionSnapshot snapshot)
            { if (snapshot.ProviderRevisions.GetValueOrDefault("test") == revision) ready.TrySetResult(); }
            Session.SnapshotChanged += Changed;
            try
            {
                _provider.Inventory = new(revision, devices.ToImmutableArray());
                _provider.Changes.Writer.TryWrite(_provider.Inventory);
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { Session.SnapshotChanged -= Changed; }
        }
        public async ValueTask DisposeAsync() { Model.Dispose(); await Session.DisposeAsync(); }
    }
    private sealed class Provider : IDeviceProvider
    {
        public DeviceInventorySnapshot Inventory = new(0, []);
        public Channel<DeviceInventorySnapshot> Changes = Channel.CreateUnbounded<DeviceInventorySnapshot>();
        public string ProviderId => "test";
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default) => new(Inventory);
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        { yield return Inventory; await foreach (var snapshot in Changes.Reader.ReadAllAsync(token)) yield return snapshot; }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
