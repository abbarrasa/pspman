using System.Collections.Concurrent;
using System.Collections.Immutable;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;
using Fixture = PSPMan.Core.Tests.GameCopyServiceTests.Fixture;
using DeviceContext = PSPMan.Core.Tests.GameCopySessionCoordinatorTests.Context;
using Provider = PSPMan.Core.Tests.GameCopySessionCoordinatorTests.Provider;

namespace PSPMan.Core.Tests;

/// <summary>Real coordinator/engine, controlled per-operation I/O; no native-device acceptance.</summary>
public sealed class GameCopyOperationRegistryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("prepare")]
    [InlineData("password")]
    [InlineData("overwrite")]
    [InlineData("resource")]
    [InlineData("stopping")]
    public async Task Additional_submissions_overlap_pending_work_and_cancel_only_their_owner(string stage)
    {
        await using var f = await Harness.Create();
        var a = f.Add("a"); var b = f.Add("b");
        var entered = Signal(); var release = Signal(); var io = f.IO(a);
        switch (stage)
        {
            case "prepare": io.OnInspect = token => Hold(entered, token); break;
            case "password": io.OpenErrorOnce = GameCopyErrorCode.PasswordRequired; io.BeforePassword = token => Hold(entered, token); break;
            case "overwrite": io.Files["a.iso"] = new([9]); io.BeforePrompt = token => Hold(entered, token); break;
            case "resource": io.Resources.Wait = async (waiting, id, token) => { waiting!.Report(new(id, GameCopyWaitReason.DestinationFile, "a.iso")); await Hold(entered, token); }; break;
            default:
                io.OnWrite = token => Hold(Signal(), token);
                io.BeforeOutputRelease = async () => { entered.TrySetResult(); await release.Task; };
                break;
        }
        f.Submit(a);
        Task? cancellation = null;
        if (stage == "stopping") { await f.Wait(() => io.OutputOpen); cancellation = f.Coordinator.CancelAsync(a.OperationId); }
        await entered.Task.WaitAsync(Deadline);
        try
        {
            Assert.Single(f.Submit(b).Accepted);
            Assert.Equal(GameCopyState.Completed, (await f.Completion(b).WaitAsync(Deadline)).State);
            Assert.False(f.Completion(a).IsCompleted);
            Assert.True(f.Coordinator.IsBusy);
        }
        finally { release.TrySetResult(); await f.Coordinator.CancelAsync(a.OperationId); if (cancellation is not null) await cancellation; }
        Assert.Equal(GameCopyState.Cancelled, (await f.Completion(a)).State);
        Assert.Equal(GameCopyState.Completed, f.Snapshot(b).Snapshot.State);
    }

    [Fact]
    public async Task Independent_writes_really_overlap_on_one_engine()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var b = f.Add("b");
        var enteredA = Signal(); var enteredB = Signal(); var release = Signal();
        f.IO(a).OnWrite = async token => { enteredA.TrySetResult(); await release.Task.WaitAsync(token); };
        f.IO(b).OnWrite = async token => { enteredB.TrySetResult(); await release.Task.WaitAsync(token); };
        var accepted = f.Submit(a, b); Assert.Equal(2, accepted.Accepted.Length);
        try { await Task.WhenAll(enteredA.Task, enteredB.Task).WaitAsync(Deadline); Assert.All(accepted.Accepted, r => Assert.False(f.Coordinator.GetCompletion(r.OperationId).IsCompleted)); }
        finally { release.TrySetResult(); await Task.WhenAll(f.Completion(a), f.Completion(b)).WaitAsync(Deadline); }
        Assert.Equal(GameCopyState.Completed, (await f.Completion(a)).State);
        Assert.Equal(GameCopyState.Completed, (await f.Completion(b)).State);
    }

    [Fact]
    public async Task Batch_partially_admits_and_captures_once_without_mutating_preferences()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var bad = f.Add("bad") with { SourceDevice = new("copy-test", "missing") }; var b = f.Add("b");
        var before = f.Session.Snapshot.Associations;
        var admitted = f.Submit(a, bad, b);
        Assert.Equal(new[] { a.OperationId, b.OperationId }, admitted.Accepted.Select(r => r.OperationId));
        Assert.Equal(1, Assert.Single(admitted.Incidents).SubmissionIndex);
        Assert.Same(before, f.Session.Snapshot.Associations);
        await Task.WhenAll(f.Completion(a), f.Completion(b));
        Assert.Equal(0, f.IO(bad).SourceOpens);
        Assert.Equal(2, f.Coordinator.Operations.Length);
    }

    [Fact]
    public async Task A_later_submission_of_the_same_game_to_another_PSP_keeps_both_choices()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var entered = Signal();
        f.IO(a).OnInspect = token => Hold(entered, token);
        f.Submit(a); await entered.Task.WaitAsync(Deadline);
        var b = f.Duplicate(a, Harness.Psp2); f.Submit(b);
        try
        {
            await f.Wait(() => f.Coordinator.Operations.All(s => s.Revision > 1));
            Assert.Equal(a.Game, f.Snapshot(b).Request.Game);
            Assert.Equal(DeviceContext.Psp, f.Snapshot(a).Request.Destination.Device);
            Assert.Equal(Harness.Psp2, f.Snapshot(b).Request.Destination.Device);
            Assert.Equal("/psp/ISO", f.Snapshot(a).Snapshot.Destination);
            Assert.Equal("/psp2/ISO", f.Snapshot(b).Snapshot.Destination);
            await f.Coordinator.CancelAsync(a.OperationId);
            Assert.False(f.Completion(b).IsCompleted);
        }
        finally { await f.Coordinator.CancelAllAsync(); }
    }

    [Fact]
    public async Task Preferences_selection_and_mutable_submission_enumeration_cannot_retarget_admitted_requests()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var entered = Signal();
        f.IO(a).OnInspect = token => Hold(entered, token);
        var batch = new List<GameCopySubmission> { a }; f.Coordinator.Submit(batch);
        await entered.Task.WaitAsync(Deadline); var captured = f.Snapshot(a).Request;
        batch.Clear(); f.Coordinator.SetSelectedGame(null);
        await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseSource(f.Session.Snapshot.Associations, SourceGroup.Removable, Harness.Source2));
        await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseDestination(f.Session.Snapshot.Associations, a.Game.Id, Harness.Psp2));
        Assert.Equal(captured, f.Snapshot(a).Request); Assert.False(f.Completion(a).IsCompleted);
        await f.Coordinator.CancelAsync(a.OperationId);
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("source")]
    public async Task Shared_device_loss_stops_all_dependants_including_waiters_but_not_other_copies(string device)
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var b = f.Add("b");
        var c = f.Add("c") with { SourceDevice = Harness.Source2, SourceVolume = new(Harness.Source2, "volume"), DestinationDevice = Harness.Psp2 };
        var entered = Signal(); var waiting = Signal(); var other = Signal();
        f.IO(a).OnInspect = token => Hold(entered, token);
        f.IO(b).Resources.Wait = async (observer, id, token) => { observer!.Report(new(id, GameCopyWaitReason.DestinationFile, "b.iso")); await Hold(waiting, token); };
        f.IO(c).OnInspect = token => Hold(other, token);
        f.Submit(a, b, c); await Task.WhenAll(entered.Task, waiting.Task, other.Task).WaitAsync(Deadline);
        var missing = device == "source" ? DeviceContext.Source : DeviceContext.Psp;
        await f.Change(devices => devices.Where(d => d.Key != missing).ToImmutableArray());
        Assert.Equal(GameCopyState.Failed, (await f.Completion(a).WaitAsync(Deadline)).State);
        Assert.Equal(GameCopyState.Failed, (await f.Completion(b).WaitAsync(Deadline)).State);
        Assert.False(f.Completion(c).IsCompleted); await f.Coordinator.CancelAsync(c.OperationId);
    }

    [Fact]
    public async Task Removal_reserves_before_stopping_all_dependants_and_waits_for_each_release()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var b = f.Add("b");
        var c = f.Add("c") with { DestinationDevice = Harness.Psp2 };
        var closingA = Signal(); var closingB = Signal(); var releaseA = Signal(); var releaseB = Signal();
        foreach (var item in new[] { a, b, c }) f.IO(item).OnWrite = token => Hold(Signal(), token);
        f.IO(a).BeforeOutputRelease = async () => { closingA.TrySetResult(); await releaseA.Task; };
        f.IO(b).BeforeOutputRelease = async () => { closingB.TrySetResult(); await releaseB.Task; };
        f.Submit(a, b, c); await f.Wait(() => new[] { a, b, c }.All(s => f.IO(s).OutputOpen));
        var removal = f.Coordinator.RemoveDeviceAsync(DeviceContext.Psp, f.Session.Snapshot.Inventory).AsTask();
        try
        {
            await Task.WhenAll(closingA.Task, closingB.Task).WaitAsync(Deadline);
            var late = f.Add("late"); var independent = f.Add("independent") with { DestinationDevice = Harness.Psp2 };
            var admission = f.Submit(late, independent);
            Assert.Equal(independent.OperationId, Assert.Single(admission.Accepted).OperationId);
            Assert.Equal(0, Assert.Single(admission.Incidents).SubmissionIndex);
            Assert.Equal(0, f.IO(late).SourceOpens);
            Assert.Equal(GameCopyState.Completed, (await f.Completion(independent).WaitAsync(Deadline)).State);
            releaseA.TrySetResult(); await f.Completion(a).WaitAsync(Deadline);
            Assert.False(removal.IsCompleted); Assert.Equal(0, f.Provider.Removals);
            Assert.False(f.Completion(c).IsCompleted);
        }
        finally { releaseA.TrySetResult(); releaseB.TrySetResult(); await removal.WaitAsync(Deadline); }
        await f.Provider.Removed.Task.WaitAsync(Deadline);
        Assert.Equal(GameCopyState.Failed, (await f.Completion(a)).State); Assert.Equal(GameCopyState.Failed, (await f.Completion(b)).State);
        Assert.False(f.Completion(c).IsCompleted); await f.Coordinator.CancelAsync(c.OperationId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Close_gate_survives_completion_decline_stale_decisions_and_accepted_drain(bool accept)
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var entered = Signal(); var release = Signal();
        f.IO(a).OnInspect = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        f.Submit(a); await entered.Task.WaitAsync(Deadline);
        var decision = f.Coordinator.BeginClose(); Assert.Equal(decision, f.Coordinator.BeginClose());
        var b = f.Add("b"); Assert.Empty(f.Submit(b).Accepted);
        release.TrySetResult(); await f.Completion(a).WaitAsync(Deadline);
        Assert.False(f.Coordinator.IsAdmissionOpen); Assert.Empty(f.Submit(b).Accepted);
        Assert.False(await f.Coordinator.ResolveCloseAsync(Guid.NewGuid(), false));
        Assert.True(await f.Coordinator.ResolveCloseAsync(decision, accept));
        Assert.Equal(!accept, f.Coordinator.IsAdmissionOpen);
        if (!accept)
        {
            var newer = f.Coordinator.BeginClose(); Assert.NotEqual(decision, newer);
            Assert.False(await f.Coordinator.ResolveCloseAsync(decision, false)); Assert.False(f.Coordinator.IsAdmissionOpen);
            await f.Coordinator.ResolveCloseAsync(newer, false); Assert.Single(f.Submit(b).Accepted); await f.Completion(b);
        }
        else { await f.Coordinator.ResolveCloseAsync(decision, false); Assert.False(f.Coordinator.IsAdmissionOpen); }
    }

    [Fact]
    public async Task Accepting_close_stops_all_waits_and_reserves_shared_drain_before_callbacks()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var b = f.Add("b"); var entered = Signal(); var closing = Signal(); var release = Signal();
        f.IO(a).OnInspect = token => Hold(entered, token);
        f.IO(b).OnWrite = token => Hold(Signal(), token);
        f.IO(b).BeforeOutputRelease = async () => { closing.TrySetResult(); await release.Task; };
        f.Submit(a, b); await entered.Task.WaitAsync(Deadline); await f.Wait(() => f.IO(b).OutputOpen);
        var decision = f.Coordinator.BeginClose(); var drain = f.Coordinator.ResolveCloseAsync(decision, true);
        try
        {
            await closing.Task.WaitAsync(Deadline); Assert.False(drain.IsCompleted);
            Assert.Same(drain, f.Coordinator.ResolveCloseAsync(decision, true)); Assert.False(f.Coordinator.IsAdmissionOpen);
            Assert.Empty(f.Submit(f.Add("late")).Accepted);
        }
        finally { release.TrySetResult(); await drain.WaitAsync(Deadline); }
        Assert.All(new[] { a, b }, s => Assert.Equal(GameCopyState.Cancelled, f.Completion(s).Result.State));
    }

    [Fact]
    public async Task One_failed_consumer_does_not_strand_other_owners_or_reopen_terminated_snapshots()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var b = f.Add("b");
        f.Coordinator.Submit([a, b], new Observer(value => { if (value.Request.OperationId == a.OperationId && value.Snapshot.Progress?.Work.WrittenBytes > 0) throw new IOException("Consumer failed"); }));
        Assert.Equal(GameCopyState.Failed, (await f.Completion(a).WaitAsync(Deadline)).State);
        Assert.Equal(GameCopyState.Completed, (await f.Completion(b).WaitAsync(Deadline)).State);
        var terminal = f.Snapshot(a); await f.Coordinator.CancelAsync(a.OperationId);
        Assert.Equal(terminal, f.Snapshot(a)); Assert.False(GameCopySubmissionRules.CanApply(terminal, terminal with { Revision = terminal.Revision + 1 }));
        var duplicate = f.Submit(a); Assert.Empty(duplicate.Accepted); Assert.Equal(GameCopyErrorCode.InvalidSubmission, duplicate.Incidents[0].Error.Code);
    }

    [Fact]
    public async Task Observers_can_query_and_submit_from_another_thread_without_registry_lock_deadlock()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var b = f.Add("b"); var once = 0;
        f.Coordinator.Submit([a], new Observer(_ => {
            if (Interlocked.Exchange(ref once, 1) != 0) return;
            Task.Run(() => { Assert.NotEmpty(f.Coordinator.Operations); Assert.Single(f.Submit(b).Accepted); }).WaitAsync(Deadline).GetAwaiter().GetResult();
        }));
        Assert.Equal(GameCopyState.Completed, (await f.Completion(a).WaitAsync(Deadline)).State);
        Assert.Equal(GameCopyState.Completed, (await f.Completion(b).WaitAsync(Deadline)).State);
    }

    [Fact]
    public async Task Reentrant_disposal_is_reserved_before_callbacks_and_drains_every_registered_owner()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var b = f.Add("b"); Task? disposal = null;
        f.Coordinator.Submit([a, b], new Observer(_ => { disposal ??= f.Coordinator.DisposeAsync().AsTask(); }));
        await Task.WhenAll(f.Completion(a), f.Completion(b)).WaitAsync(Deadline);
        Assert.NotNull(disposal); await disposal;
        Assert.Same(disposal, f.Coordinator.DisposeAsync().AsTask()); Assert.False(f.Coordinator.IsBusy);
        await f.Session.SetAssociationsAsync(new());
        Assert.Throws<ObjectDisposedException>(() => f.Submit(f.Add("late")));
    }

    [Fact]
    public async Task Snapshot_revisions_are_per_owner_monotonic_and_have_the_captured_request()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var b = f.Add("b"); var updates = new ConcurrentQueue<GameCopyOperationSnapshot>();
        f.Coordinator.Submit([a, b], new Observer(updates.Enqueue));
        await Task.WhenAll(f.Completion(a), f.Completion(b));
        await f.Wait(() => updates.Count(s => GameCopyRules.IsTerminal(s.Snapshot.State)) == 2);
        foreach (var item in new[] { a, b })
        {
            var sequence = updates.Where(s => s.Request.OperationId == item.OperationId).ToArray();
            Assert.True(sequence.Length > 2);
            Assert.Equal(sequence.Select(s => s.Revision).Order().Distinct(), sequence.Select(s => s.Revision));
            Assert.All(sequence, s => Assert.Equal(item.Game, s.Request.Game));
            Assert.True(GameCopyRules.IsTerminal(sequence[^1].Snapshot.State));
        }
    }

    [Fact]
    public async Task Simultaneous_password_requests_have_independent_prompts_and_cancellation()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var b = f.Add("b"); var enteredA = Signal(); var enteredB = Signal();
        f.IO(a).OpenErrorOnce = f.IO(b).OpenErrorOnce = GameCopyErrorCode.PasswordRequired;
        f.IO(a).BeforePassword = token => Hold(enteredA, token); f.IO(b).BeforePassword = token => Hold(enteredB, token);
        f.Submit(a, b); await Task.WhenAll(enteredA.Task, enteredB.Task).WaitAsync(Deadline);
        var prompt = f.Snapshot(b).Snapshot.Prompt;
        Assert.Equal(a.OperationId, f.Snapshot(a).Snapshot.Prompt!.OperationId); Assert.Equal(b.OperationId, prompt!.OperationId);
        await f.Coordinator.CancelAsync(a.OperationId);
        Assert.Null(f.Snapshot(a).Snapshot.Prompt); Assert.Equal(prompt, f.Snapshot(b).Snapshot.Prompt);
        Assert.False(f.Completion(b).IsCompleted); await f.Coordinator.CancelAsync(b.OperationId);
    }

    [Fact]
    public async Task Each_optical_submission_keeps_its_local_reader_without_mutating_shared_choices()
    {
        await using var f = await Harness.Create();
        await f.Change(devices => devices.Select(d => d.Key == DeviceContext.Source || d.Key == Harness.Source2
            ? d with { Medium = DeviceMedium.Optical, Capabilities = DeviceCapabilities.OpticalSource | DeviceCapabilities.SafeRemoval } : d).ToImmutableArray());
        var a = f.Add("a"); a = a with { Game = a.Game with { Medium = GameMedium.Dvd, DvdNumber = 1 } };
        var b = f.Add("b"); b = b with { Game = b.Game with { Medium = GameMedium.Dvd, DvdNumber = 2 }, SourceDevice = Harness.Source2, SourceVolume = new(Harness.Source2, "volume") };
        var enteredA = Signal(); var enteredB = Signal();
        f.IO(a).OnInspect = token => Hold(enteredA, token); f.IO(b).OnInspect = token => Hold(enteredB, token);
        var preferences = f.Session.Snapshot.Associations; Assert.Equal(2, f.Submit(a, b).Accepted.Length);
        await Task.WhenAll(enteredA.Task, enteredB.Task).WaitAsync(Deadline);
        Assert.Same(preferences, f.Session.Snapshot.Associations);
        Assert.Equal(DeviceContext.Source, f.IO(a).OpenedSource!.Device); Assert.Equal(Harness.Source2, f.IO(b).OpenedSource!.Device);
        await f.Coordinator.CancelAllAsync();
    }

    [Fact]
    public async Task Submission_enumerator_can_reenter_close_without_running_under_the_registry_gate()
    {
        await using var f = await Harness.Create(); var a = f.Add("a");
        IEnumerable<GameCopySubmission> Items()
        {
            yield return a;
            Task.Run(() => f.Coordinator.BeginClose()).WaitAsync(Deadline).GetAwaiter().GetResult();
        }
        Assert.Empty(f.Coordinator.Submit(Items()).Accepted);
        Assert.Equal(0, f.IO(a).SourceOpens);
    }

    [Fact]
    public async Task Simultaneous_duplicate_ids_are_admitted_once_and_never_reused_after_terminal()
    {
        await using var f = await Harness.Create(); var a = f.Add("a"); var entered = Signal();
        f.IO(a).OnInspect = token => Hold(entered, token);
        var submissions = await Task.WhenAll(Task.Run(() => f.Submit(a)), Task.Run(() => f.Submit(a)));
        Assert.Single(submissions.SelectMany(s => s.Accepted));
        Assert.Equal(GameCopyErrorCode.InvalidSubmission, Assert.Single(submissions.SelectMany(s => s.Incidents)).Error.Code);
        await f.Coordinator.CancelAllAsync(); Assert.Empty(f.Submit(a).Accepted);
    }

    [Fact]
    public async Task Observation_failure_prevents_new_admission_even_when_last_known_paths_remain()
    {
        await using var f = await Harness.Create(); var a = f.Add("a");
        f.Provider.Changes.Writer.TryComplete(new IOException("Observer failed"));
        await f.Wait(() => f.Session.Snapshot.ProviderErrors.ContainsKey("copy-test"));
        Assert.Empty(f.Submit(a).Accepted); Assert.Equal(0, f.IO(a).SourceOpens);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async ValueTask Hold(TaskCompletionSource entered, CancellationToken token) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
    private sealed class Observer(Action<GameCopyOperationSnapshot> report) : IProgress<GameCopyOperationSnapshot>
    { public void Report(GameCopyOperationSnapshot value) => report(value); }

    private sealed class Harness : IAsyncDisposable, IGameCopyFileSystem, IGameArchiveReader, IGameCopyResources, IGameCopyInteraction
    {
        internal static readonly DeviceKey Psp2 = new("copy-test", "psp2"), Source2 = new("copy-test", "source2");
        internal readonly Provider Provider = new();
        internal DeviceSessionService Session = null!;
        internal GameCopySessionCoordinator Coordinator = null!;
        private readonly ConcurrentDictionary<Guid, Fixture> fixtures = new();
        private readonly ConcurrentDictionary<string, Fixture> sources = new();
        internal static async Task<Harness> Create()
        {
            var f = new Harness();
            f.Provider.Inventory = new(1, f.Provider.Inventory.Devices.Add(DeviceContext.Device("psp2", true)).Add(DeviceContext.Device("source2") with { Capabilities = DeviceCapabilities.RemovableSource | DeviceCapabilities.SafeRemoval }));
            f.Session = new([f.Provider]); await f.Session.StartAsync();
            f.Coordinator = new(f.Session, new(f, f, f, f), MountedPathSyntax.Linux); return f;
        }
        internal GameCopySubmission Add(string name)
        {
            var id = Guid.NewGuid(); var io = new Fixture { Entries = [new(0, name + ".iso", 3)] };
            var game = Fixture.Request().Game with { Title = name, Location = "/" + name + ".zip" };
            fixtures[id] = io; sources[game.Location] = io;
            return new(id, game, DeviceContext.Source, new(DeviceContext.Source, "volume"), DeviceContext.Psp);
        }
        internal GameCopySubmission Duplicate(GameCopySubmission original, DeviceKey destination)
        { var copy = original with { OperationId = Guid.NewGuid(), DestinationDevice = destination }; fixtures[copy.OperationId] = IO(original); return copy; }
        internal Fixture IO(GameCopySubmission item) => fixtures[item.OperationId];
        internal GameCopyAdmission Submit(params GameCopySubmission[] submissions) => Coordinator.Submit(submissions);
        internal Task<GameCopyResult> Completion(GameCopySubmission item) => Coordinator.GetCompletion(item.OperationId);
        internal GameCopyOperationSnapshot Snapshot(GameCopySubmission item) => Coordinator.Operations.Single(s => s.Request.OperationId == item.OperationId);
        internal async Task Change(Func<ImmutableArray<DeviceSnapshot>, ImmutableArray<DeviceSnapshot>> change)
        {
            var old = Session.Snapshot.Inventory.Revision;
            Provider.Inventory = new(Provider.Inventory.Revision + 1, change(Provider.Inventory.Devices)); Provider.Changes.Writer.TryWrite(Provider.Inventory);
            await Wait(() => Session.Snapshot.Inventory.Revision > old);
        }
        internal async Task Wait(Func<bool> predicate) { using var timeout = new CancellationTokenSource(Deadline); while (!predicate()) await Task.Delay(5, timeout.Token); }
        public ValueTask<IGameArchiveSource> OpenSourceAsync(CopyVolumeEvidence evidence, string location, CancellationToken token) => sources[location].OpenSourceAsync(evidence, location, token);
        public ValueTask<IGameCopyDestination> OpenDestinationAsync(CopyVolumeEvidence evidence, CancellationToken token) => ValueTask.FromResult<IGameCopyDestination>(new DeferredDestination(evidence));
        public ValueTask<IGameArchiveSession> OpenAsync(IGameArchiveSource source, string? password, CancellationToken token) => ((Fixture)source).OpenAsync(source, password, token);
        public IGameCopyDestination BindDestination(Guid id, CopyVolumeEvidence volume, IGameCopyDestination raw)
        { var io = fixtures[id]; ((DeferredDestination)raw).IO = io; io.OpenedDestination = volume; return io.Resources.BindDestination(id, volume, raw); }
        public ValueTask<IGameCopyBudgetLease> ReserveAsync(Guid id, CopyVolumeEvidence volume, IGameCopyDestination destination, long pending, CancellationToken token) => fixtures[id].Resources.ReserveAsync(id, volume, destination, pending, token);
        public ValueTask<IGameCopyEntryLease> AcquireEntryAsync(Guid id, CopyVolumeEvidence volume, IGameCopyDestination destination, ImmutableArray<string> segments, bool directory, IProgress<GameCopyResourceWait>? waiting, CancellationToken token) => fixtures[id].Resources.AcquireEntryAsync(id, volume, destination, segments, directory, waiting, token);
        public ValueTask<CopyPasswordResponse> RequestPasswordAsync(CopyPasswordPrompt prompt, CancellationToken token) => fixtures[prompt.Identity.OperationId].RequestPasswordAsync(prompt, token);
        public ValueTask<CopyOverwriteResponse> ConfirmOverwriteAsync(CopyOverwritePrompt prompt, CancellationToken token) => fixtures[prompt.Identity.OperationId].ConfirmOverwriteAsync(prompt, token);
        public async ValueTask DisposeAsync() { await Coordinator.DisposeAsync(); await Session.DisposeAsync(); }
        private sealed class DeferredDestination(CopyVolumeEvidence volume) : IGameCopyDestination
        {
            internal Fixture? IO;
            public string DestinationPath => volume.Root.TrimEnd('/', '\\') + (volume.Syntax == MountedPathSyntax.Windows ? "\\" : "/") + "ISO";
            public ValueTask ValidateAsync(CancellationToken token) => IO!.ValidateAsync(token);
            public ValueTask<long?> GetAvailableBytesAsync(CancellationToken token) => IO!.GetAvailableBytesAsync(token);
            public ValueTask<CopyTargetSnapshot> InspectAsync(ImmutableArray<string> path, CancellationToken token) => IO!.InspectAsync(path, token);
            public ValueTask<GameCopySkipReason?> EnsureDirectoryAsync(ImmutableArray<string> path, CancellationToken token) => IO!.EnsureDirectoryAsync(path, token);
            public ValueTask<CopyOutputOpenResult> OpenOutputAsync(ImmutableArray<string> path, CopyFileEvidence? consent, CancellationToken token) => IO!.OpenOutputAsync(path, consent, token);
            public ValueTask DisposeAsync() => IO?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
