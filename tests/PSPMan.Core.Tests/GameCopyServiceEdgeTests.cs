using PSPMan.Core.Transfers;
using Fixture = PSPMan.Core.Tests.GameCopyServiceTests.Fixture;

namespace PSPMan.Core.Tests;

public sealed class GameCopyServiceEdgeTests
{
    [Fact]
    public async Task Retry_extracts_only_uncompleted_entries_and_reconfirms_failed_output()
    {
        var f = new Fixture { Entries = [new(0, "first.iso", 3), new(1, "second.iso", 3)],
            ErrorEntry = 1, ExtractError = GameCopyErrorCode.IndeterminateEncryptedError, RetryOnce = true };
        Assert.Equal(GameCopyState.Completed, (await f.Run()).State);
        Assert.Equal(new[] { 0, 1 }, f.Batches[0]);
        Assert.Equal(new[] { 1 }, f.Batches[1]);
        Assert.Equal(3, f.Mutations);
        Assert.Equal(1, f.OverwritePrompts);
        Assert.Equal(6, f.Snapshots[^1].Progress!.Work.WrittenBytes);
        var fractions = f.Snapshots.Where(s => s.Progress is not null).Select(s => s.Progress!.Fraction).ToArray();
        Assert.Equal(fractions.Order(), fractions);
    }

    [Fact]
    public async Task Changed_source_after_password_decision_prevents_retry_writes()
    {
        var f = new Fixture { ExtractError = GameCopyErrorCode.IndeterminateEncryptedError, RetryOnce = true };
        f.AfterPassword = () => f.ValidationError = GameCopyErrorCode.SourceChanged;
        Assert.Equal(GameCopyErrorCode.SourceChanged, (await f.Run()).Error!.Code);
        Assert.Equal(1, f.Mutations);
        Assert.Equal(new byte[] { 1 }, f.Files["game.iso"].ToArray());
    }

    [Fact]
    public async Task Retry_checks_remaining_capacity_before_overwriting_failed_output()
    {
        var f = new Fixture { ExtractError = GameCopyErrorCode.IndeterminateEncryptedError, RetryOnce = true };
        f.AfterPassword = () => f.Available = 0;
        Assert.Equal(GameCopyErrorCode.InsufficientSpace, (await f.Run()).Error!.Code);
        Assert.Equal(1, f.Mutations);
        Assert.Equal(0, f.OverwritePrompts);
    }

    [Fact]
    public async Task Changed_inspection_after_retry_is_rejected()
    {
        var f = new Fixture { ExtractError = GameCopyErrorCode.IndeterminateEncryptedError, RetryOnce = true };
        f.AfterPassword = () => f.Entries = [new(0, "changed.iso", 3)];
        Assert.Equal(GameCopyErrorCode.SourceChanged, (await f.Run()).Error!.Code);
        Assert.Equal(1, f.Mutations);
    }

    [Fact]
    public async Task Stale_password_response_is_ignored()
    {
        var f = new Fixture { OpenErrorOnce = GameCopyErrorCode.PasswordRequired, StalePasswordOnce = true };
        Assert.Equal(GameCopyState.Completed, (await f.Run()).State);
        Assert.Equal(2, f.PasswordPrompts);
    }

    [Fact]
    public async Task Session_close_failure_prevents_password_retry()
    {
        var f = new Fixture { ExtractError = GameCopyErrorCode.IndeterminateEncryptedError, FailSessionClose = true };
        Assert.Equal(GameCopyErrorCode.CloseError, (await f.Run()).Error!.Code);
        Assert.Equal(0, f.PasswordPrompts);
        Assert.False(f.OutputOpen);
    }

    [Fact]
    public async Task Cancellation_during_final_release_wins_before_success_publication()
    {
        var f = new Fixture();
        f.OnDispose = f.Cancel.Cancel;
        Assert.Equal(GameCopyState.Cancelled, (await f.Run()).State);
        Assert.DoesNotContain(f.Snapshots, s => s.Progress?.Fraction == 1);
        Assert.Equal(3, f.Files["game.iso"].Length);
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("prompt")]
    [InlineData("write")]
    public async Task Cancellation_unblocks_pending_IO_or_interaction_and_releases_owners(string stage)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var f = new Fixture();
        async ValueTask Wait(CancellationToken token) { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
        if (stage == "inspect") f.OnInspect = Wait;
        if (stage == "write") f.OnWrite = Wait;
        if (stage == "prompt") { f.BeforePrompt = Wait; f.Files["game.iso"] = new([9]); }
        var running = f.Run();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(running.IsCompleted);
        f.Cancel.Cancel();
        Assert.Equal(GameCopyState.Cancelled, (await running.WaitAsync(TimeSpan.FromSeconds(5))).State);
        Assert.True(f.Closed);
        Assert.False(f.OutputOpen);
        Assert.Null(f.Snapshots[^1].Prompt);
        if (stage == "prompt") Assert.Equal(new byte[] { 9 }, f.Files["game.iso"].ToArray());
        if (stage == "write") Assert.Empty(f.Files["game.iso"].ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_does_not_wait_for_a_stale_noncooperative_prompt_response(bool password)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var f = new Fixture();
        async ValueTask Wait(CancellationToken _) { entered.SetResult(); await release.Task; }
        if (password) { f.OpenErrorOnce = GameCopyErrorCode.PasswordRequired; f.BeforePassword = Wait; }
        else { f.Files["game.iso"] = new([9]); f.BeforePrompt = Wait; }
        var running = f.Run();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            f.Cancel.Cancel();
            Assert.Equal(GameCopyState.Cancelled, (await running.WaitAsync(TimeSpan.FromSeconds(2))).State);
            Assert.True(f.Closed);
            Assert.Null(f.Snapshots[^1].Prompt);
        }
        finally { release.SetResult(); await running; }
        Assert.Equal(0, f.Mutations);
    }

    [Theory]
    [InlineData(GameCopyErrorCode.CorruptArchive)]
    [InlineData(GameCopyErrorCode.MissingArchive)]
    [InlineData(GameCopyErrorCode.MissingPart)]
    [InlineData(GameCopyErrorCode.SourceUnavailable)]
    [InlineData(GameCopyErrorCode.DestinationUnavailable)]
    public async Task Preparation_failure_does_not_create_ISO_or_prompt_for_password(GameCopyErrorCode code)
    {
        var f = new Fixture { OpenErrorOnce = code };
        Assert.Equal(code, (await f.Run()).Error!.Code);
        Assert.Empty(f.Files);
        Assert.Equal(0, f.Mutations);
        Assert.Equal(0, f.PasswordPrompts);
    }

    [Theory]
    [InlineData(null, GameCopyErrorCode.UnknownActualSize)]
    [InlineData(-1L, GameCopyErrorCode.InvalidActualSize)]
    public async Task Unusable_actual_size_prevents_writes(long? size, GameCopyErrorCode code)
    {
        var f = new Fixture { Entries = [new(0, "game.iso", size)] };
        Assert.Equal(code, (await f.Run()).Error!.Code);
        Assert.Empty(f.Files);
    }

    [Fact]
    public async Task Overwrite_cancel_keeps_existing_file_and_releases_owners()
    {
        var f = new Fixture { Overwrite = CopyOverwriteDecision.Cancel };
        f.Files["game.iso"] = new([9]);
        Assert.Equal(GameCopyState.Cancelled, (await f.Run()).State);
        Assert.Equal(new byte[] { 9 }, f.Files["game.iso"].ToArray());
        Assert.True(f.Closed);
    }

    [Fact]
    public async Task Cancellation_publishes_stopping_before_closing_the_current_output()
    {
        var f = new Fixture { CancelDuringWrite = true };
        var stoppingObserved = false;
        f.OnOutputClose = () => stoppingObserved = f.Snapshots[^1].State == GameCopyState.Stopping;
        Assert.Equal(GameCopyState.Cancelled, (await f.Run()).State);
        Assert.True(stoppingObserved);
    }

    [Fact]
    public async Task Unclassified_interaction_exception_does_not_publish_password_text()
    {
        const string secret = "private-password";
        var f = new Fixture { BeforePrompt = _ => throw new InvalidOperationException(secret) };
        f.Files["game.iso"] = new([9]);
        var result = await f.Run();
        Assert.Equal(GameCopyState.Failed, result.State);
        Assert.DoesNotContain(secret, result.Error!.Message);
        Assert.DoesNotContain(f.Snapshots, s => s.Error?.Message.Contains(secret) == true);
        Assert.Equal(new byte[] { 9 }, f.Files["game.iso"].ToArray());
    }

    [Fact]
    public async Task IO_callback_exception_fails_and_closes_already_created_file()
    {
        var f = new Fixture { BeforeWrite = () => throw new IOException("Read failed") };
        Assert.Equal(GameCopyState.Failed, (await f.Run()).State);
        Assert.True(f.Closed);
        Assert.False(f.OutputOpen);
        Assert.Empty(f.Files["game.iso"].ToArray());
    }
}
