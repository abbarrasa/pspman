using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;
using CopyFixture = PSPMan.Core.Tests.GameCopyServiceTests.Fixture;

namespace PSPMan.Core.Tests;

public sealed class GameCopySessionCoordinatorTests
{
    [Fact]
    public async Task Captures_current_associations_and_completes_after_release()
    {
        await using var f = await Context.Create();
        var result = await f.Coordinator.StartAsync();
        Assert.Equal(GameCopyState.Completed, result.State);
        Assert.True(f.IO.Closed);
        Assert.False(f.Coordinator.IsBusy);
        Assert.Equal(GameCopyState.Completed, f.Coordinator.Snapshot!.State);
    }

    [Theory]
    [InlineData("game")]
    [InlineData("source")]
    [InlineData("psp")]
    [InlineData("multiple-psp")]
    [InlineData("multiple-volumes")]
    public async Task Missing_or_ambiguous_input_never_opens_archives(string missing)
    {
        await using var f = await Context.Create(explicitChoices: false);
        if (missing == "game") f.Coordinator.SetSelectedGame(null);
        else await f.Change(devices => missing switch {
            "source" => devices.Where(d => d.Key != Context.Source).ToImmutableArray(),
            "psp" => devices.Where(d => d.Key != Context.Psp).ToImmutableArray(),
            "multiple-psp" => devices.Add(Context.Device("psp2", true)),
            _ => devices.Select(d => d.Key == Context.Psp ? d with { Volumes = d.Volumes.Add(d.Volumes[0] with { Key = new(d.Key, "second") }) } : d).ToImmutableArray()
        });
        await Assert.ThrowsAsync<GameCopyException>(() => f.Coordinator.StartAsync());
        Assert.Equal(0, f.IO.SourceOpens);
    }

    [Fact]
    public async Task A_second_copy_is_rejected_while_input_is_pending()
    {
        await using var f = await Context.Create();
        var pending = f.PauseInspection();
        var first = f.Coordinator.StartAsync();
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<GameCopyException>(() => f.Coordinator.StartAsync());
        await f.Coordinator.CancelAsync();
        Assert.Equal(GameCopyState.Cancelled, (await first).State);
        Assert.False(f.Coordinator.IsBusy);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("identity")]
    [InlineData("volume-identity")]
    [InlineData("root")]
    [InlineData("unavailable")]
    [InlineData("disconnect")]
    [InlineData("capability")]
    [InlineData("extra-volume")]
    public async Task Relevant_device_changes_fail_without_redirecting(string change)
    {
        await using var f = await Context.Create();
        var pending = f.PauseInspection();
        var operation = f.Coordinator.StartAsync();
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Change(devices => devices.Where(d => change != "disconnect" || d.Key != Context.Psp).Select(d => d.Key != Context.Psp ? d : change switch {
            "generation" => d with { MediaGeneration = "replacement" },
            "identity" => d with { Identity = new("replacement", DeviceIdentityConfidence.Persistent) },
            "volume-identity" => d with { Volumes = [d.Volumes[0] with { Identity = new("replacement", DeviceIdentityConfidence.Persistent) }] },
            "root" => d with { Volumes = [d.Volumes[0] with { Root = "/different" }] },
            "unavailable" => d with { State = DeviceState.Unavailable },
            "capability" => d with { Capabilities = DeviceCapabilities.SafeRemoval },
            "extra-volume" => d with { Volumes = d.Volumes.Add(d.Volumes[0] with { Key = new(d.Key, "extra") }) },
            _ => d
        }).ToImmutableArray());
        Assert.Equal(GameCopyState.Failed, (await operation.WaitAsync(TimeSpan.FromSeconds(5))).State);
        Assert.Equal(1, f.IO.SourceOpens);
        Assert.True(f.IO.Closed);
        Assert.True(f.Coordinator.Snapshot!.Progress?.Fraction is null or < 1);
    }

    [Fact]
    public async Task Unrelated_revisions_labels_and_new_PSP_do_not_invalidate_captured_devices()
    {
        await using var f = await Context.Create(explicitChoices: false);
        var pending = f.PauseInspection();
        var operation = f.Coordinator.StartAsync();
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Change(devices => devices.Select(d => d with { DisplayName = "New label" }).ToImmutableArray().Add(Context.Device("psp2", true)));
        Assert.True(f.Coordinator.IsBusy);
        Assert.False(operation.IsCompleted);
        await f.Coordinator.CancelAsync();
        Assert.Equal(GameCopyState.Cancelled, (await operation).State);
    }

    [Theory]
    [InlineData("game")]
    [InlineData("source")]
    [InlineData("destination")]
    public async Task Selection_changes_fail_pending_password_decisions_and_release_slot(string selection)
    {
        await using var f = await Context.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.IO.OpenErrorOnce = GameCopyErrorCode.IndeterminateEncryptedError;
        f.IO.BeforePassword = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        var operation = f.Coordinator.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (selection == "game") f.Coordinator.SetSelectedGame(f.Game with { Id = Guid.NewGuid() });
        else if (selection == "source") await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseSource(f.Session.Snapshot.Associations, SourceGroup.Removable, Context.Other));
        else await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseDestination(f.Session.Snapshot.Associations, f.Game.Id, Context.Other));
        Assert.Equal(GameCopyState.Failed, (await operation.WaitAsync(TimeSpan.FromSeconds(5))).State);
        Assert.Null(f.Coordinator.Snapshot!.Prompt);
        Assert.False(f.Coordinator.IsBusy);
    }

    [Fact]
    public async Task Removal_stops_and_releases_copy_before_forwarding_same_device()
    {
        await using var f = await Context.Create();
        var pending = f.PauseInspection();
        f.Provider.BeforeRemoval = request => {
            Assert.True(f.IO.Closed); Assert.False(f.IO.OutputOpen); Assert.Equal(Context.Psp, request.Device);
        };
        var operation = f.Coordinator.StartAsync();
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Coordinator.RemoveDeviceAsync(Context.Psp, f.Session.Snapshot.Inventory);
        Assert.Equal(GameCopyState.Failed, (await operation).State);
        await f.Provider.Removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, f.Provider.Removals);
        await f.Wait(() => f.Session.Snapshot.Inventory.Devices.Single(d => d.Key == Context.Psp).Error?.Code == "Veto");
    }

    [Fact]
    public async Task Unrelated_removal_preserves_the_active_copy()
    {
        await using var f = await Context.Create();
        var pending = f.PauseInspection();
        var operation = f.Coordinator.StartAsync();
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Coordinator.RemoveDeviceAsync(Context.Other, f.Session.Snapshot.Inventory);
        await f.Provider.Removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        await f.Coordinator.CancelAsync();
        Assert.Equal(GameCopyState.Cancelled, (await operation).State);
    }

    [Fact]
    public async Task Stale_removal_never_removes_a_replacement_at_the_same_path()
    {
        await using var f = await Context.Create();
        var observation = f.Session.Snapshot.Inventory;
        await f.Change(devices => devices.Select(d => d.Key == Context.Psp ? d with { MediaGeneration = "replacement" } : d).ToImmutableArray());
        await Assert.ThrowsAsync<GameCopyException>(() => f.Coordinator.RemoveDeviceAsync(Context.Psp, observation).AsTask());
        Assert.Equal(0, f.Provider.Removals);
    }

    [Fact]
    public async Task Disposal_stops_owned_copy_but_does_not_dispose_the_session()
    {
        await using var f = await Context.Create();
        var pending = f.PauseInspection();
        var operation = f.Coordinator.StartAsync();
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Coordinator.DisposeAsync();
        Assert.Equal(GameCopyState.Cancelled, (await operation).State);
        Assert.True(f.IO.Closed);
        await f.Session.SetAssociationsAsync(new());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => f.Coordinator.StartAsync());
    }

    [Theory]
    [InlineData("release")]
    [InlineData("replacement")]
    [InlineData("cancel-removal")]
    [InlineData("unrelated-revision")]
    public async Task Removal_reservation_survives_pending_file_close_and_revalidates_before_native_request(string scenario)
    {
        await using var f = await Context.Create();
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.IO.OnWrite = async token => { writing.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        f.IO.BeforeOutputRelease = async () => { closing.SetResult(); await release.Task; };
        var operation = f.Coordinator.StartAsync();
        await writing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var observation = f.Session.Snapshot.Inventory;
        using var cancellation = new CancellationTokenSource();
        var removal = f.Coordinator.RemoveDeviceAsync(Context.Psp, observation, cancellation.Token).AsTask();
        await closing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.True(f.IO.OutputOpen);
            Assert.Equal(0, f.Provider.Removals);
            await Assert.ThrowsAsync<GameCopyException>(() => f.Coordinator.StartAsync());
            await Assert.ThrowsAsync<GameCopyException>(() => f.Coordinator.RemoveDeviceAsync(Context.Psp, observation).AsTask());
            if (scenario == "replacement")
                await f.Change(devices => devices.Select(d => d.Key == Context.Psp ? d with { MediaGeneration = "replacement" } : d).ToImmutableArray());
            if (scenario == "unrelated-revision")
            {
                await f.Change(devices => devices.Select(d => d.Key == Context.Other ? d with { DisplayName = "renamed" } : d).ToImmutableArray());
                f.Provider.BeforeRemoval = request => Assert.Equal(f.Provider.Inventory.Revision, request.ExpectedRevision);
            }
            if (scenario == "cancel-removal") cancellation.Cancel();
            Assert.False(removal.IsCompleted);
        }
        finally { release.SetResult(); }
        if (scenario == "replacement") await Assert.ThrowsAsync<GameCopyException>(() => removal);
        else if (scenario == "cancel-removal") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => removal);
        else { await removal; await f.Provider.Removed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(GameCopyState.Failed, (await operation).State);
        Assert.False(f.IO.OutputOpen);
        Assert.Equal(scenario is "release" or "unrelated-revision" ? 1 : 0, f.Provider.Removals);
    }

    [Fact]
    public async Task Removal_during_indeterminate_password_prompt_invalidates_a_late_response_without_rewriting_output()
    {
        await using var f = await Context.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.IO.ExtractError = GameCopyErrorCode.IndeterminateEncryptedError;
        f.IO.RetryOnce = true;
        f.IO.BeforePassword = async _ => { entered.SetResult(); await lateResponse.Task; };
        var operation = f.Coordinator.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Assert.ThrowsAsync<GameCopyException>(() => f.Coordinator.StartAsync());
            await f.Coordinator.RemoveDeviceAsync(Context.Psp, f.Session.Snapshot.Inventory);
            Assert.Equal(GameCopyState.Failed, (await operation).State);
            Assert.Equal(new byte[] { 1 }, f.IO.Files["game.iso"].ToArray());
            Assert.Equal(1, f.IO.Mutations);
            Assert.Null(f.Coordinator.Snapshot!.Prompt);
        }
        finally { lateResponse.SetResult(); }
        Assert.Equal(1, f.IO.Mutations);
    }

    [Fact]
    public async Task An_unavailable_explicit_PSP_never_falls_back_to_another_connected_PSP()
    {
        await using var f = await Context.Create();
        await f.Change(devices => devices.Where(d => d.Key != Context.Psp).ToImmutableArray().Add(Context.Device("psp2", true)));
        await Assert.ThrowsAsync<GameCopyException>(() => f.Coordinator.StartAsync());
        Assert.Equal(0, f.IO.SourceOpens);
    }

    [Fact]
    public async Task A_source_disconnection_fails_pending_work_and_releases_ownership()
    {
        await using var f = await Context.Create();
        var entered = f.PauseInspection();
        var operation = f.Coordinator.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Change(devices => devices.Where(d => d.Key != Context.Source).ToImmutableArray());
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(GameCopyState.Failed, result.State);
        Assert.Equal(GameCopyErrorCode.SourceChanged, result.Error!.Code);
        Assert.True(f.IO.Closed);
    }

    [Fact]
    public async Task Provider_observation_failure_stops_copy_without_using_last_known_paths()
    {
        await using var f = await Context.Create();
        var entered = f.PauseInspection();
        var operation = f.Coordinator.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Provider.Changes.Writer.TryComplete(new IOException("Observation failed"));
        Assert.Equal(GameCopyState.Failed, (await operation.WaitAsync(TimeSpan.FromSeconds(5))).State);
        Assert.True(f.IO.Closed);
    }

    [Fact]
    public async Task Terminal_result_is_not_reversed_by_late_selection_or_cancellation()
    {
        await using var f = await Context.Create();
        var result = await f.Coordinator.StartAsync();
        f.Coordinator.SetSelectedGame(null);
        await f.Coordinator.CancelAsync();
        Assert.Equal(GameCopyState.Completed, result.State);
        Assert.Equal(GameCopyState.Completed, f.Coordinator.Snapshot!.State);
    }

    [Theory]
    [InlineData(GameMedium.Dvd)]
    [InlineData(GameMedium.Usb)]
    [InlineData(GameMedium.MemoryCard)]
    public async Task Each_catalogue_medium_uses_its_own_captured_source_group(GameMedium medium)
    {
        await using var f = await Context.Create();
        f.Game = f.Game with { Medium = medium, DvdNumber = medium == GameMedium.Dvd ? 1 : null };
        f.Coordinator.SetSelectedGame(f.Game);
        if (medium == GameMedium.Dvd)
        {
            await f.Change(devices => devices.Select(d => d.Key == Context.Source ? d with {
                Medium = DeviceMedium.Optical, Capabilities = DeviceCapabilities.OpticalSource | DeviceCapabilities.SafeRemoval } : d).ToImmutableArray());
            await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseVolume(f.Session.Snapshot.Associations, SourceGroup.Optical, new(Context.Source, "volume")));
        }
        Assert.Equal(GameCopyState.Completed, (await f.Coordinator.StartAsync()).State);
        Assert.Equal(Context.Source, f.IO.OpenedSource!.Device);
        Assert.Equal(Context.Psp, f.IO.OpenedDestination!.Device);
    }

    [Fact]
    public async Task Windows_shaped_roots_are_captured_without_using_host_path_rules()
    {
        await using var f = await Context.Create();
        await f.Coordinator.DisposeAsync();
        await f.Change(devices => devices.Select(d => d with { Volumes = [d.Volumes[0] with {
            Root = d.Key == Context.Source ? "C:\\source\\" : "D:\\psp\\" }] }).ToImmutableArray());
        f.Coordinator = new(f.Session, new(f.IO, f.IO, f.IO), MountedPathSyntax.Windows);
        f.Coordinator.SetSelectedGame(f.Game);
        Assert.Equal(GameCopyState.Completed, (await f.Coordinator.StartAsync()).State);
        Assert.Equal(MountedPathSyntax.Windows, f.IO.OpenedSource!.Syntax);
        Assert.Equal("D:\\psp\\", f.IO.OpenedDestination!.Root);
    }

    [Fact]
    public async Task An_unrelated_source_group_selection_does_not_stop_the_active_game()
    {
        await using var f = await Context.Create();
        var entered = f.PauseInspection();
        var operation = f.Coordinator.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseSource(f.Session.Snapshot.Associations, SourceGroup.Optical, Context.Other));
        Assert.False(operation.IsCompleted);
        await f.Coordinator.CancelAsync();
        Assert.Equal(GameCopyState.Cancelled, (await operation).State);
    }

    [Fact]
    public async Task Failed_progress_consumer_preserves_bytes_warns_and_releases_the_active_slot()
    {
        await using var f = await Context.Create();
        var result = await f.Coordinator.StartAsync(new ThrowingObserver());
        Assert.Equal(GameCopyState.Failed, result.State);
        Assert.Contains("Incomplete or incorrect files remain", result.Error!.Message);
        Assert.True(f.IO.Closed);
        Assert.False(f.Coordinator.IsBusy);
        Assert.Equal(new byte[] { 1 }, f.IO.Files["game.iso"].ToArray());
        Assert.Equal(GameCopyState.Completed, (await f.Coordinator.StartAsync()).State);
    }

    [Fact]
    public async Task Explicit_failure_waits_for_release_and_preserves_its_reason()
    {
        await using var f = await Context.Create();
        var entered = f.PauseInspection();
        var running = f.Coordinator.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Coordinator.FailAndStopAsync(new(GameCopyErrorCode.SourceChanged, "Selection invalidated"));
        Assert.Contains("Selection invalidated", (await running).Error!.Message);
        Assert.True(f.IO.Closed);
        await f.Coordinator.FailAndStopAsync(new(GameCopyErrorCode.SourceChanged, "Too late"));
        Assert.Contains("Selection invalidated", f.Coordinator.Snapshot!.Error!.Message);
    }

    [Fact]
    public async Task Disposal_from_preparation_prevents_any_source_open_and_drains_without_deadlock()
    {
        await using var f = await Context.Create();
        ValueTask? disposal = null;
        var result = await f.Coordinator.StartAsync(new ActionObserver(_ => disposal ??= f.Coordinator.DisposeAsync()));
        await disposal!.Value;
        Assert.Equal(GameCopyState.Cancelled, result.State);
        Assert.Equal(0, f.IO.SourceOpens);
    }

    private sealed class ActionObserver(Action<GameCopySnapshot> report) : IProgress<GameCopySnapshot>
    { public void Report(GameCopySnapshot snapshot) => report(snapshot); }

    private sealed class ThrowingObserver : IProgress<GameCopySnapshot>
    {
        public void Report(GameCopySnapshot snapshot)
        { if (snapshot.Progress?.Work.WrittenBytes > 0) throw new InvalidOperationException("Presentation failed"); }
    }

    [Fact]
    public async Task Removal_reservation_blocks_start_after_copy_release_before_session_acceptance()
    {
        await using var f = await Context.Create();
        var inspecting = f.PauseInspection();
        var operation = f.Coordinator.StartAsync();
        await inspecting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var processorHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void HoldProcessor(object? sender, DeviceSessionSnapshot snapshot)
        {
            if (snapshot.Inventory.Devices.Any(d => d.Key == Context.Other && d.DisplayName == "hold"))
            {
                processorHeld.TrySetResult();
                // Deliberately hold this test worker to separate copy release from removal acceptance.
                resume.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
        }
        f.Session.SnapshotChanged += HoldProcessor;
        try
        {
            await f.Change(devices => devices.Select(d => d.Key == Context.Other ? d with { DisplayName = "hold" } : d).ToImmutableArray());
            await processorHeld.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var removal = f.Coordinator.RemoveDeviceAsync(Context.Psp, f.Session.Snapshot.Inventory).AsTask();
            Assert.Equal(GameCopyState.Failed, (await operation.WaitAsync(TimeSpan.FromSeconds(5))).State);
            Assert.False(f.Coordinator.IsBusy);
            Assert.False(removal.IsCompleted);
            Assert.Equal(DeviceState.Ready, f.Session.Snapshot.Inventory.Devices.Single(d => d.Key == Context.Psp).State);
            var error = await Assert.ThrowsAsync<GameCopyException>(() => f.Coordinator.StartAsync());
            Assert.Equal(GameCopyErrorCode.CopyInProgress, error.Error.Code);
            resume.TrySetResult();
            await removal;
        }
        finally { resume.TrySetResult(); f.Session.SnapshotChanged -= HoldProcessor; }
    }

    internal sealed class Context : IAsyncDisposable
    {
        internal static readonly DeviceKey Source = new("copy-test", "source"), Psp = new("copy-test", "psp"), Other = new("copy-test", "other");
        internal readonly CopyFixture IO = new();
        internal readonly Provider Provider = new();
        internal DeviceSessionService Session = null!;
        internal GameCopySessionCoordinator Coordinator = null!;
        internal GameRecord Game = CopyFixture.Request().Game;
        internal static DeviceSnapshot Device(string id, bool psp = false)
        {
            var key = new DeviceKey("copy-test", id);
            var identity = new DeviceIdentity(id, DeviceIdentityConfidence.Persistent);
            return new(key, id, psp ? DeviceMedium.Psp : DeviceMedium.Usb,
                DeviceCapabilities.SafeRemoval | (psp ? DeviceCapabilities.PspDestination : id == "source" ? DeviceCapabilities.RemovableSource : DeviceCapabilities.None),
                identity, "g1", DeviceState.Ready, [new(new(key, "volume"), null, "/" + id, identity, DeviceState.Ready)]);
        }
        internal static async Task<Context> Create(bool explicitChoices = true)
        {
            var f = new Context();
            f.Session = new([f.Provider]);
            await f.Session.StartAsync();
            if (explicitChoices)
            {
                var state = DeviceAssociationRules.ChooseVolume(new(), SourceGroup.Removable, new(Source, "volume"));
                await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseDestination(state, f.Game.Id, Psp));
            }
            f.Coordinator = new(f.Session, new(f.IO, f.IO, f.IO), MountedPathSyntax.Linux);
            f.Coordinator.SetSelectedGame(f.Game);
            return f;
        }
        internal TaskCompletionSource PauseInspection()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IO.OnInspect = async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
            return entered;
        }
        internal async Task Change(Func<ImmutableArray<DeviceSnapshot>, ImmutableArray<DeviceSnapshot>> change)
        {
            var previous = Session.Snapshot.Inventory.Revision;
            Provider.Inventory = new(Provider.Inventory.Revision + 1, change(Provider.Inventory.Devices));
            Provider.Changes.Writer.TryWrite(Provider.Inventory);
            await Wait(() => Session.Snapshot.Inventory.Revision > previous);
        }
        internal async Task Wait(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!predicate()) await Task.Delay(5, timeout.Token);
        }
        public async ValueTask DisposeAsync() { await Coordinator.DisposeAsync(); await Session.DisposeAsync(); }
    }

    internal sealed class Provider : IDeviceProvider
    {
        public string ProviderId => "copy-test";
        internal DeviceInventorySnapshot Inventory = new(1, [Context.Device("source"), Context.Device("psp", true), Context.Device("other")]);
        internal Channel<DeviceInventorySnapshot> Changes = Channel.CreateUnbounded<DeviceInventorySnapshot>();
        internal int Removals;
        internal Action<DeviceRemovalRequest>? BeforeRemoval;
        internal TaskCompletionSource Removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default) => ValueTask.FromResult(Inventory);
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        { await foreach (var change in Changes.Reader.ReadAllAsync(token)) yield return change; }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default)
        { BeforeRemoval?.Invoke(request); Interlocked.Increment(ref Removals); Removed.TrySetResult(); return ValueTask.FromResult(new DeviceOperationResult(DeviceOperationStatus.Failed, new("Veto", "Native removal veto"))); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
