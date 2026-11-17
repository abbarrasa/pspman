using System.Collections.Immutable;
using PSPMan.Core.Transfers;
using Fixture = PSPMan.Core.Tests.GameCopyServiceTests.Fixture;

namespace PSPMan.Core.Tests;

public sealed class GameCopyResourceIntegrationTests
{
    [Fact]
    public async Task Explicit_directory_releases_its_entry_before_the_nested_file_without_reserving_directory_bytes()
    {
        var f = new Fixture { Entries = [new(0, "nested/", null, IsDirectory: true), new(1, "nested/game.iso", 3)] };
        Assert.Equal(GameCopyState.Completed, (await f.Run()).State);
        Assert.Equal(new long[] { 3 }, f.Resources.Reservations);
        Assert.Equal(2, f.Resources.Acquisitions); Assert.Equal(0, f.Resources.ActiveEntries);
        Assert.Equal(new byte[] { 1, 2, 3 }, f.Files["nested/game.iso"].ToArray());
    }

    [Fact]
    public async Task Actual_size_is_reserved_before_target_inspection_and_all_leases_close_before_terminal()
    {
        var f = new Fixture();
        f.BeforeTargetInspect = () => { Assert.True(f.Resources.BudgetOpen); Assert.Equal(1, f.Resources.ActiveEntries); };
        f.BeforeWrite = () => Assert.Equal(1, f.Resources.ActiveEntries);
        f.OnOutputClose = () => Assert.Equal(1, f.Resources.ActiveEntries);
        f.OnDispose = () => { Assert.False(f.Resources.BudgetOpen); Assert.Equal(0, f.Resources.ActiveEntries); };
        Assert.Equal(GameCopyState.Completed, (await f.Run()).State);
        Assert.Equal(new long[] { 3 }, f.Resources.Reservations);
        Assert.Equal(1, f.Resources.Acquisitions);
        Assert.Equal(0, f.Resources.ActiveEntries);
        Assert.False(f.Resources.BudgetOpen);
        Assert.True(f.Resources.ClosedBeforeDestination);
    }

    [Theory]
    [InlineData("../unsafe.iso", false)]
    [InlineData("game.iso", true)]
    [InlineData("game.iso", false)]
    public async Task Omission_releases_its_pending_bytes_and_any_entry(string path, bool link)
    {
        var f = new Fixture { Entries = [new(0, path, 3, IsLink: link)], Overwrite = CopyOverwriteDecision.Skip };
        f.Files["game.iso"] = new([9]);
        Assert.Equal(GameCopyState.CompletedWithSkippedFiles, (await f.Run()).State);
        Assert.Equal(new long[] { 3 }, f.Resources.Skips);
        Assert.Equal(0, f.Resources.ActiveEntries);
    }

    [Fact]
    public async Task Retry_revalidates_full_failed_entry_after_releasing_output_and_entry_before_input()
    {
        var f = new Fixture { Entries = [new(0, "first.iso", 3), new(1, "second.iso", 3)],
            ErrorEntry = 1, ExtractError = GameCopyErrorCode.IndeterminateEncryptedError, RetryOnce = true };
        f.BeforePassword = _ => { Assert.False(f.OutputOpen); Assert.Equal(0, f.Resources.ActiveEntries); return ValueTask.CompletedTask; };
        Assert.Equal(GameCopyState.Completed, (await f.Run()).State);
        Assert.Equal(new long[] { 6 }, f.Resources.Reservations);
        Assert.Equal(new long[] { 3 }, f.Resources.Revalidations);
        Assert.Equal(3, f.Resources.Acquisitions);
        Assert.Equal(1, f.OverwritePrompts);
    }

    [Fact]
    public async Task Resource_wait_is_published_and_cancellation_drains_before_terminal()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var f = new Fixture();
        f.Resources.Wait = async (waiting, id, token) => {
            waiting!.Report(new(id, GameCopyWaitReason.DestinationFile, "game.iso"));
            entered.SetResult(); await Task.Delay(Timeout.Infinite, token);
        };
        var run = f.Run();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(GameCopyState.WaitingForResource, f.Snapshots[^1].State);
            Assert.Equal(GameCopyWaitReason.DestinationFile, f.Snapshots[^1].WaitReason);
            Assert.Null(f.Snapshots[^1].Prompt);
        }
        finally { f.Cancel.Cancel(); }
        Assert.Equal(GameCopyState.Cancelled, (await run.WaitAsync(TimeSpan.FromSeconds(5))).State);
        Assert.Empty(f.Files);
        Assert.False(f.Resources.BudgetOpen);
    }

    [Fact]
    public async Task Budget_release_failure_wins_over_cancellation_and_is_not_password_retry()
    {
        var f = new Fixture { CancelDuringWrite = true };
        f.Resources.FailClose = true;
        var result = await f.Run();
        Assert.Equal(GameCopyState.Failed, result.State);
        Assert.Equal(GameCopyErrorCode.CloseError, result.Error!.Code);
        Assert.Equal(0, f.PasswordPrompts);
        Assert.True(f.Closed);
    }

    [Fact]
    public async Task Terminal_result_waits_for_budget_settlement_and_native_release()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var f = new Fixture();
        f.Resources.BeforeClose = async () => { entered.TrySetResult(); await release.Task; };
        var run = f.Run();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(run.IsCompleted); Assert.False(f.Closed);
            Assert.DoesNotContain(f.Snapshots, s => GameCopyRules.IsTerminal(s.State));
        }
        finally { release.TrySetResult(); await run; }
        Assert.Equal(GameCopyState.Completed, (await run).State);
    }

    [Fact]
    public async Task A_rejected_entry_release_stops_before_any_password_retry()
    {
        var f = new Fixture { ExtractError = GameCopyErrorCode.IndeterminateEncryptedError, RetryOnce = true };
        f.Resources.FailEntryClose = true;
        var result = await f.Run();
        Assert.Equal(GameCopyErrorCode.CloseError, result.Error!.Code);
        Assert.Equal(0, f.PasswordPrompts); Assert.True(f.Closed);
        Assert.Equal(new byte[] { 1 }, f.Files["game.iso"].ToArray());
    }
}

// Orchestration probe only; native allocation/alias acceptance lives in Infraestructure tests.
internal sealed class RecordingCopyResources : IGameCopyResources
{
    internal readonly List<long> Reservations = [], Revalidations = [], Skips = [];
    internal int ActiveEntries, Acquisitions;
    internal bool BudgetOpen, ClosedBeforeDestination, FailClose, FailEntryClose;
    internal Func<Task>? BeforeClose;
    internal Func<IProgress<GameCopyResourceWait>?, Guid, CancellationToken, Task>? Wait;
    public IGameCopyDestination BindDestination(Guid id, CopyVolumeEvidence evidence, IGameCopyDestination destination) => destination;
    public async ValueTask<IGameCopyBudgetLease> ReserveAsync(Guid id, CopyVolumeEvidence volume,
        IGameCopyDestination destination, long pendingBytes, CancellationToken token)
    {
        var error = GameCopyRules.CheckCapacity(pendingBytes, await destination.GetAvailableBytesAsync(token));
        if (error is not null) throw new GameCopyException(error);
        Reservations.Add(pendingBytes); BudgetOpen = true;
        return new Budget(this, id, volume, destination);
    }
    public async ValueTask<IGameCopyEntryLease> AcquireEntryAsync(Guid id, CopyVolumeEvidence volume,
        IGameCopyDestination destination, ImmutableArray<string> segments, bool directory,
        IProgress<GameCopyResourceWait>? waiting, CancellationToken token)
    {
        Assert.True(BudgetOpen); Assert.Equal(0, ActiveEntries);
        if (Wait is not null) await Wait(waiting, id, token);
        ActiveEntries++; Acquisitions++; return new Entry(this, id, volume);
    }
    private sealed class Budget(RecordingCopyResources owner, Guid id, CopyVolumeEvidence volume,
        IGameCopyDestination destination) : IGameCopyBudgetLease
    {
        public Guid OperationId => id;
        public CopyVolumeKey Volume => CopyVolumeKey.From(volume);
        public async ValueTask RevalidateAsync(long pending, CancellationToken token)
        {
            var error = GameCopyRules.CheckCapacity(pending, await destination.GetAvailableBytesAsync(token));
            if (error is not null) throw new GameCopyException(error);
            owner.Revalidations.Add(pending);
        }
        public ValueTask SkipPendingAsync(long bytes, CancellationToken token) { owner.Skips.Add(bytes); return ValueTask.CompletedTask; }
        public ValueTask<IGameCopyWriteLease> BeginWriteAsync(long bytes, CancellationToken token) => throw new NotSupportedException();
        public async ValueTask DisposeAsync()
        {
            if (owner.BeforeClose is not null) await owner.BeforeClose();
            Assert.Equal(0, owner.ActiveEntries); owner.BudgetOpen = false; owner.ClosedBeforeDestination = true;
            if (owner.FailClose) throw new IOException("Settlement failed");
        }
    }
    private sealed class Entry(RecordingCopyResources owner, Guid id, CopyVolumeEvidence volume) : IGameCopyEntryLease
    {
        public Guid OperationId => id;
        public CopyVolumeKey Volume => CopyVolumeKey.From(volume);
        public string TargetIdentity => "probe";
        public ValueTask DisposeAsync()
        {
            owner.ActiveEntries--;
            if (owner.FailEntryClose) throw new IOException("Entry close failed");
            return ValueTask.CompletedTask;
        }
    }
}
