using PSPMan.Core.Transfers;

namespace PSPMan.Core.Tests;

public sealed class GameCopyRulesTests
{
    [Theory]
    [InlineData("../escape.iso")]
    [InlineData("a/../escape.iso")]
    [InlineData("a/./game.iso")]
    [InlineData("/absolute.iso")]
    [InlineData("\\\\server\\share\\game.iso")]
    [InlineData("C:game.iso")]
    [InlineData("C:/game.iso")]
    [InlineData("a//game.iso")]
    [InlineData("a\0game.iso")]
    [InlineData("")]
    [InlineData(null)]
    public void Unsafe_entries_are_skipped(string? path)
    {
        var result = GameCopyRules.ClassifyEntry(path);
        Assert.False(result.IsSafe);
        Assert.Equal(GameCopySkipReason.UnsafePath, result.SkipReason);
        Assert.Empty(result.Segments);
    }
    [Theory]
    [InlineData("folder/game.iso")]
    [InlineData("folder\\game.iso")]
    [InlineData("日本語/🎮.iso")]
    public void Safe_paths_preserve_structure_and_unicode(string path)
    {
        var result = GameCopyRules.ClassifyEntry(path);
        Assert.True(result.IsSafe);
        Assert.Equal(2, result.Segments.Length);
    }
    [Fact]
    public void Directory_trailing_separator_is_allowed_but_links_are_always_skipped()
    {
        Assert.True(GameCopyRules.ClassifyEntry("folder/", isDirectory: true).IsSafe);
        Assert.False(GameCopyRules.ClassifyEntry("folder/").IsSafe);
        Assert.Equal(GameCopySkipReason.Link, GameCopyRules.ClassifyEntry("folder/game.iso", isLink: true).SkipReason);
    }
    [Fact]
    public void Actual_totals_keep_duplicates_and_distinguish_zero_byte_files_from_empty_archives()
    {
        Assert.Equal(12, GameCopyRules.SumActualBytes([new(0, "same", 5), new(1, "same", 7)]).Bytes);
        Assert.Equal(0, GameCopyRules.SumActualBytes([new(0, "empty", 0)]).Bytes);
        Assert.Null(GameCopyRules.SumActualBytes([new(0, "empty", 0)]).Error);
        Assert.Equal(GameCopyErrorCode.EmptyArchive, GameCopyRules.SumActualBytes([]).Error?.Code);
        Assert.Equal(GameCopyErrorCode.UnknownActualSize, GameCopyRules.SumActualBytes([new(0, "a", null)]).Error?.Code);
        Assert.Equal(GameCopyErrorCode.InvalidActualSize, GameCopyRules.SumActualBytes([new(0, "a", -1)]).Error?.Code);
        Assert.Equal(GameCopyErrorCode.SizeOverflow, GameCopyRules.SumActualBytes([new(0, "a", long.MaxValue), new(1, "b", 1)]).Error?.Code);
    }
    [Fact]
    public void Capacity_uses_actual_bytes_without_overwrite_credit_or_catalogue_estimates()
    {
        Assert.Null(GameCopyRules.CheckCapacity(10, 10));
        Assert.Equal(GameCopyErrorCode.InsufficientSpace, GameCopyRules.CheckCapacity(11, 10)?.Code);
        Assert.Equal(GameCopyErrorCode.UnknownFreeSpace, GameCopyRules.CheckCapacity(1, null)?.Code);
        Assert.Equal(GameCopyErrorCode.UnknownFreeSpace, GameCopyRules.CheckCapacity(1, -1)?.Code);
    }
    [Fact]
    public void Native_target_evidence_controls_renewed_consent_and_structural_errors()
    {
        var evidence = new CopyFileEvidence("file", 10, "v1");
        var target = new CopyTargetSnapshot(CopyTargetKind.File, evidence);
        Assert.False(GameCopyRules.NeedsOverwriteConsent(target, evidence));
        Assert.True(GameCopyRules.NeedsOverwriteConsent(target with { Evidence = evidence with { ChangeEvidence = "v2" } }, evidence));
        Assert.True(GameCopyRules.NeedsOverwriteConsent(target, null));
        Assert.False(GameCopyRules.NeedsOverwriteConsent(new(CopyTargetKind.Missing, null), null));
        Assert.Equal(GameCopyErrorCode.FileDirectoryConflict, GameCopyRules.ValidateTarget(new(0, "a", 10), target with { Kind = CopyTargetKind.Directory }).Error?.Code);
        Assert.Equal(GameCopyErrorCode.InvalidName, GameCopyRules.ValidateTarget(new(0, "a", 10), target with { IsNameCompatible = false }).Error?.Code);
    }
    [Fact]
    public void Progress_supports_large_totals_skips_and_retry_without_false_completion()
    {
        var work = new GameCopyWork(long.MaxValue, long.MaxValue, 0, long.MaxValue, long.MaxValue);
        Assert.True(GameCopyRules.AdvanceProgress(work, 0).Fraction < 1);
        Assert.Equal(1, GameCopyRules.AdvanceProgress(work, 0, true).Fraction);
        Assert.Equal(.8m, GameCopyRules.AdvanceProgress(new(10, 1, 1, 1, 1), .8m).Fraction);
        Assert.Equal(.9995m, GameCopyRules.AdvanceProgress(new(10, 1, 1, 1, 1), .9995m).Fraction);
        Assert.Throws<ArgumentOutOfRangeException>(() => GameCopyRules.AdvanceProgress(new(10, 1, 1, 1, 1), 1));
        Assert.Equal(1, GameCopyRules.AdvanceProgress(new(0, 0, 0, 0, 0), 0, true).Fraction);
        Assert.Throws<ArgumentOutOfRangeException>(() => GameCopyRules.AdvanceProgress(new(10, 11, 0, 10, 0), 0));
    }
    [Fact]
    public void Completion_waits_for_closed_files_and_preserves_skipped_and_stop_outcomes()
    {
        Assert.Null(GameCopyRules.ReduceOutcome(GameCopyStopReason.None, 0, false));
        Assert.Equal(GameCopyState.Completed, GameCopyRules.ReduceOutcome(GameCopyStopReason.None, 0, true));
        Assert.Equal(GameCopyState.CompletedWithSkippedFiles, GameCopyRules.ReduceOutcome(GameCopyStopReason.None, 10, true));
        Assert.Equal(GameCopyState.Cancelled, GameCopyRules.ReduceOutcome(GameCopyStopReason.Cancelled, 10, true));
        Assert.Equal(GameCopyState.Failed, GameCopyRules.ReduceOutcome(GameCopyStopReason.Failed, 0, true));
    }
    [Fact]
    public void Stale_prompts_and_terminal_responses_are_rejected()
    {
        var prompt = new GameCopyPrompt(Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new GameCopySnapshot(prompt.OperationId, "Game", "/PSP/ISO", GameCopyState.AwaitingOverwrite, Prompt: prompt);
        Assert.True(GameCopyRules.MatchesPrompt(snapshot, prompt));
        Assert.False(GameCopyRules.MatchesPrompt(snapshot, prompt with { PromptId = Guid.NewGuid() }));
        Assert.False(GameCopyRules.MatchesPrompt(snapshot, prompt with { OperationId = Guid.NewGuid() }));
        Assert.False(GameCopyRules.MatchesPrompt(snapshot with { State = GameCopyState.Stopping }, prompt));
    }
    [Fact]
    public void Indeterminate_errors_wait_for_input_only_after_outputs_close()
    {
        Assert.Equal(GameCopyState.Stopping, GameCopyRules.StateAfterArchiveError(GameCopyErrorCode.IndeterminateEncryptedError, false));
        Assert.Equal(GameCopyState.AwaitingPasswordDecision, GameCopyRules.StateAfterArchiveError(GameCopyErrorCode.IndeterminateEncryptedError, true));
        Assert.Equal(GameCopyState.AwaitingPassword, GameCopyRules.StateAfterArchiveError(GameCopyErrorCode.IncorrectPassword, true));
        Assert.Equal(GameCopyState.Failed, GameCopyRules.StateAfterArchiveError(GameCopyErrorCode.ReadError, true));
        Assert.Equal(GameCopyState.Failed, GameCopyRules.StateAfterArchiveError(GameCopyErrorCode.CorruptArchive, true));
    }
    [Fact]
    public async Task Cancel_and_completion_have_one_atomic_winner()
    {
        for (var i = 0; i < 100; i++)
        {
            var gate = new GameCopyTerminalGate();
            bool cancelled = false;
            GameCopyState? outcome = null;
            await Task.WhenAll(Task.Run(() => cancelled = gate.RequestStop(GameCopyStopReason.Cancelled)),
                Task.Run(() => outcome = gate.TryFinish(0, true)));
            Assert.Equal(cancelled ? GameCopyState.Cancelled : GameCopyState.Completed, outcome);
            Assert.False(gate.RequestStop(GameCopyStopReason.Failed));
            Assert.Null(gate.TryFinish(0, true));
        }
    }

    [Fact]
    public void Close_failure_before_terminal_publication_overrides_a_pending_cancel()
    {
        var gate = new GameCopyTerminalGate();
        Assert.True(gate.RequestStop(GameCopyStopReason.Cancelled));
        Assert.Null(gate.TryFinish(0, false));
        Assert.True(gate.RequestStop(GameCopyStopReason.Failed));
        Assert.Equal(GameCopyState.Failed, gate.TryFinish(0, true));
        Assert.False(gate.RequestStop(GameCopyStopReason.Cancelled));
    }

    [Fact]
    public void Unsafe_target_skips_take_priority_over_name_or_structural_failure()
    {
        var target = new CopyTargetSnapshot(CopyTargetKind.Directory, null, IsNameCompatible: false, IsConfined: false);
        var result = GameCopyRules.ValidateTarget(new(0, "game.iso", 100), target);
        Assert.Equal(GameCopySkipReason.UnsafePath, result.SkipReason);
        Assert.Null(result.Error);
        Assert.Equal(GameCopySkipReason.Link, GameCopyRules.ValidateTarget(new(0, "link", 10, IsLink: true), target).SkipReason);
        Assert.Equal(GameCopySkipReason.UnsafePath, GameCopyRules.ValidateTarget(new(0, "../game.iso", 10), target).SkipReason);
    }

    [Fact]
    public void Native_case_facts_do_not_depend_on_the_host_operating_system()
    {
        var sameNativeFile = new CopyFileEvidence("inode-1", 10, "version-1");
        var observedForLowerCasePath = new CopyTargetSnapshot(CopyTargetKind.File, sameNativeFile);
        Assert.False(GameCopyRules.NeedsOverwriteConsent(observedForLowerCasePath, sameNativeFile));
        var distinctNativeFile = sameNativeFile with { Identity = "inode-2" };
        Assert.True(GameCopyRules.NeedsOverwriteConsent(observedForLowerCasePath with { Evidence = distinctNativeFile }, sameNativeFile));
        Assert.True(GameCopyRules.NeedsOverwriteConsent(observedForLowerCasePath, null)); // A new duplicate entry gets a fresh decision.
    }

    [Fact]
    public void Non_drive_colons_are_names_whose_compatibility_is_decided_by_the_destination()
    {
        var entry = new GameArchiveEntry(0, "folder/name:variant.iso", 10);
        Assert.True(GameCopyRules.ClassifyEntry(entry.Path).IsSafe);
        Assert.Null(GameCopyRules.ValidateTarget(entry, new(CopyTargetKind.Missing, null)).Error);
        Assert.Equal(GameCopyErrorCode.InvalidName,
            GameCopyRules.ValidateTarget(entry, new(CopyTargetKind.Missing, null, IsNameCompatible: false)).Error?.Code);
    }

    [Fact]
    public void Directories_can_merge_and_directory_only_archives_have_zero_actual_bytes()
    {
        var entry = new GameArchiveEntry(0, "folder/", null, IsDirectory: true);
        var total = GameCopyRules.SumActualBytes([entry]);
        Assert.Equal(0, total.Bytes);
        Assert.Equal(0, total.FileCount);
        Assert.Null(total.Error);
        Assert.Null(GameCopyRules.ValidateTarget(entry, new(CopyTargetKind.Directory, null)).Error);
        Assert.Equal(GameCopyErrorCode.FileDirectoryConflict,
            GameCopyRules.ValidateTarget(entry, new(CopyTargetKind.File, new("id", 1, "v1"))).Error?.Code);
    }

    [Fact]
    public void No_password_is_published_in_requests_snapshots_results_or_progress()
    {
        foreach (var type in new[] { typeof(GameCopyRequest), typeof(GameCopySnapshot), typeof(GameCopyResult), typeof(GameCopyProgress), typeof(GameCopyError) })
            Assert.DoesNotContain(type.GetProperties(), p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(IGameCopyDestination).GetMethods(), method =>
            method.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("Restore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unresolved_work_cannot_finish_and_zero_byte_work_does_not_divide_by_zero()
    {
        Assert.Equal(0, GameCopyRules.AdvanceProgress(new(0, 0, 0, 0, 0), 0).Fraction);
        Assert.Throws<ArgumentException>(() => GameCopyRules.AdvanceProgress(new(10, 5, 5, 5, 5), 0, true));
        var skipped = GameCopyRules.AdvanceProgress(new(10, 0, 0, 10, 10), 0, true);
        Assert.Equal(1, skipped.Fraction);
        Assert.Equal(0, skipped.Work.WrittenBytes);
    }
}
