using System.Collections.Immutable;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;

namespace PSPMan.Core.Tests;

public sealed class GameCopyServiceTests
{
    [Fact]
    public async Task Actual_size_not_catalogue_size_controls_capacity_and_completion_after_release()
    {
        var f = new Fixture { Available = 3 };
        var result = await f.Run();
        Assert.Equal(GameCopyState.Completed, result.State);
        Assert.Equal(new byte[] { 1, 2, 3 }, f.Files["game.iso"].ToArray());
        Assert.True(f.Closed);
        Assert.Equal(1m, f.Snapshots[^1].Progress!.Fraction);
        Assert.All(f.Snapshots.Where(s => s.State == GameCopyState.Completed), _ => Assert.True(f.TerminalObservedClosed));
        Assert.Equal(3, f.Snapshots[^1].Progress!.Work.TotalBytes);
    }

    [Theory]
    [InlineData(2, GameCopyErrorCode.InsufficientSpace)]
    [InlineData(-1, GameCopyErrorCode.UnknownFreeSpace)]
    public async Task Capacity_failure_never_mutates_destination(long available, GameCopyErrorCode code)
    {
        var f = new Fixture { Available = available < 0 ? null : available };
        var result = await f.Run();
        Assert.Equal(code, result.Error!.Code);
        Assert.Empty(f.Files);
        Assert.Equal(0, f.Mutations);
    }

    [Theory]
    [InlineData("../escape.iso", false)]
    [InlineData("game.iso", true)]
    public async Task All_unsafe_or_link_entries_complete_with_skips(string path, bool link)
    {
        var f = new Fixture { Entries = [new(0, path, 3, IsLink: link)] };
        var result = await f.Run();
        Assert.Equal(GameCopyState.CompletedWithSkippedFiles, result.State);
        Assert.Equal(1, result.SkippedEntries);
        Assert.Empty(f.Files);
    }

    [Fact]
    public async Task Declined_overwrite_preserves_original_and_completes_with_skips()
    {
        var f = new Fixture { Overwrite = CopyOverwriteDecision.Skip };
        f.Files["game.iso"] = new([9]);
        var result = await f.Run();
        Assert.Equal(GameCopyState.CompletedWithSkippedFiles, result.State);
        Assert.Equal(new byte[] { 9 }, f.Files["game.iso"].ToArray());
        Assert.Equal(1, f.OverwritePrompts);
    }

    [Fact]
    public async Task Changed_destination_requires_fresh_consent()
    {
        var f = new Fixture { ChangeOnce = true };
        f.Files["game.iso"] = new([9]);
        Assert.Equal(GameCopyState.Completed, (await f.Run()).State);
        Assert.Equal(2, f.OverwritePrompts);
    }

    [Fact]
    public async Task Duplicate_entry_requires_overwrite_consent()
    {
        var f = new Fixture { Entries = [new(0, "game.iso", 3), new(1, "game.iso", 3)] };
        Assert.Equal(GameCopyState.Completed, (await f.Run()).State);
        Assert.Equal(1, f.OverwritePrompts);
        Assert.Equal(2, f.Mutations);
    }

    [Theory]
    [InlineData(GameCopyErrorCode.InvalidName)]
    [InlineData(GameCopyErrorCode.FileDirectoryConflict)]
    public async Task Structural_failure_stops_without_writing(GameCopyErrorCode code)
    {
        var f = new Fixture { TargetError = code };
        Assert.Equal(code, (await f.Run()).Error!.Code);
        Assert.Empty(f.Files);
    }

    [Fact]
    public async Task Empty_archive_fails_without_mutation()
    {
        var f = new Fixture { Entries = [] };
        Assert.Equal(GameCopyErrorCode.EmptyArchive, (await f.Run()).Error!.Code);
        Assert.Equal(0, f.Mutations);
    }

    [Theory]
    [InlineData(GameCopyErrorCode.CorruptArchive)]
    [InlineData(GameCopyErrorCode.MissingPart)]
    [InlineData(GameCopyErrorCode.WriteError)]
    public async Task Late_failure_retains_partial_bytes_and_releases_files(GameCopyErrorCode code)
    {
        var f = new Fixture { ExtractError = code };
        Assert.Equal(code, (await f.Run()).Error!.Code);
        Assert.Equal(new byte[] { 1 }, f.Files["game.iso"].ToArray());
        Assert.True(f.Closed);
        Assert.True(f.Snapshots[^1].Progress!.Fraction < 1);
    }

    [Fact]
    public async Task Cancellation_retains_overwritten_bytes_without_restoring_original()
    {
        var f = new Fixture { CancelDuringWrite = true };
        f.Files["game.iso"] = new([9, 9, 9, 9]);
        Assert.Equal(GameCopyState.Cancelled, (await f.Run()).State);
        Assert.Equal(new byte[] { 1 }, f.Files["game.iso"].ToArray());
        Assert.True(f.Closed);
    }

    [Fact]
    public async Task Precancelled_operation_never_opens_sources()
    {
        var f = new Fixture();
        f.Cancel.Cancel();
        Assert.Equal(GameCopyState.Cancelled, (await f.Run()).State);
        Assert.Equal(0, f.SourceOpens);
    }

    [Fact]
    public async Task Close_failure_is_failure_even_after_cancellation()
    {
        var f = new Fixture { CancelDuringWrite = true, FailClose = true };
        Assert.Equal(GameCopyErrorCode.CloseError, (await f.Run()).Error!.Code);
        Assert.Equal(new byte[] { 1 }, f.Files["game.iso"].ToArray());
    }

    [Theory]
    [InlineData(GameCopyErrorCode.PasswordRequired)]
    [InlineData(GameCopyErrorCode.IncorrectPassword)]
    public async Task Opening_password_failure_allows_retry(GameCopyErrorCode code)
    {
        var f = new Fixture { OpenErrorOnce = code };
        Assert.Equal(GameCopyState.Completed, (await f.Run()).State);
        Assert.Equal(1, f.PasswordPrompts);
        Assert.Equal("correct", f.LastPassword);
    }

    [Fact]
    public async Task Indeterminate_error_closes_outputs_before_password_decision_and_reconfirms_current_file()
    {
        var f = new Fixture { ExtractError = GameCopyErrorCode.IndeterminateEncryptedError, RetryOnce = true };
        Assert.Equal(GameCopyState.Completed, (await f.Run()).State);
        Assert.Equal(1, f.PasswordPrompts);
        Assert.Equal(1, f.OverwritePrompts);
        Assert.True(f.PasswordObservedOutputClosed);
        Assert.Equal(new byte[] { 1, 2, 3 }, f.Files["game.iso"].ToArray());
    }

    [Fact]
    public async Task Password_dismissal_cancels_and_preserves_output()
    {
        var f = new Fixture { ExtractError = GameCopyErrorCode.IndeterminateEncryptedError, PasswordCancel = true };
        Assert.Equal(GameCopyState.Cancelled, (await f.Run()).State);
        Assert.Equal(new byte[] { 1 }, f.Files["game.iso"].ToArray());
    }

    [Fact]
    public async Task Stale_prompt_response_never_authorizes_writes()
    {
        var f = new Fixture { StaleOnce = true };
        f.Files["game.iso"] = new([9]);
        Assert.Equal(GameCopyState.Completed, (await f.Run()).State);
        Assert.Equal(2, f.OverwritePrompts);
    }

    internal sealed class Fixture : IGameCopyFileSystem, IGameArchiveSource, IGameArchiveReader,
        IGameCopyDestination, IGameCopyInteraction, IProgress<GameCopySnapshot>
    {
        public ImmutableArray<GameArchiveEntry> Entries = [new(0, "game.iso", 3)];
        public readonly Dictionary<string, MemoryStream> Files = [];
        public readonly List<GameCopySnapshot> Snapshots = [];
        public readonly List<ImmutableArray<int>> Batches = [];
        public readonly CancellationTokenSource Cancel = new();
        internal readonly RecordingCopyResources Resources = new();
        public long? Available = 1000;
        public CopyOverwriteDecision Overwrite = CopyOverwriteDecision.Overwrite;
        public GameCopyErrorCode? TargetError, ExtractError, OpenErrorOnce;
        public GameCopyErrorCode? ValidationError;
        public Action? AfterPassword, BeforeWrite, BeforeTargetInspect, OnDispose, OnOutputClose;
        public Func<CancellationToken, ValueTask>? OnInspect, BeforePrompt, BeforePassword, OnWrite;
        public Func<ValueTask>? BeforeOutputRelease;
        public int ErrorEntry;
        public bool FailSessionClose, StalePasswordOnce;
        public bool ChangeOnce, CancelDuringWrite, FailClose, RetryOnce, PasswordCancel, StaleOnce;
        public bool OutputOpen, Closed, TerminalObservedClosed, PasswordObservedOutputClosed;
        public int Mutations, OverwritePrompts, PasswordPrompts, SourceOpens;
        public string? LastPassword;
        public CopyVolumeEvidence? OpenedSource, OpenedDestination;
        public string ArchiveName => "game.zip";
        public string DestinationPath => "/psp/ISO";
        public static GameCopyRequest Request()
        {
            var key = new DeviceKey("test", "source");
            var id = new DeviceIdentity("id", DeviceIdentityConfidence.Persistent);
            var volume = new CopyVolumeEvidence(key, id, "g1", new(key, "volume"), id, "/psp", MountedPathSyntax.Linux);
            return new(Guid.NewGuid(), new(Guid.NewGuid(), "Game", GameMedium.Usb, null, "/game.zip", GameFormat.Iso, 999999, null), volume, volume);
        }
        public Task<GameCopyResult> Run() => new GameCopyService(this, this, this, Resources).CopyAsync(Request(), this, Cancel.Token);
        public void Report(GameCopySnapshot value) { Snapshots.Add(value); if (GameCopyRules.IsTerminal(value.State)) TerminalObservedClosed = Closed && !OutputOpen; }
        public ValueTask<IGameArchiveSource> OpenSourceAsync(CopyVolumeEvidence source, string location, CancellationToken token)
        { token.ThrowIfCancellationRequested(); OpenedSource = source; SourceOpens++; return ValueTask.FromResult<IGameArchiveSource>(this); }
        public ValueTask<IGameCopyDestination> OpenDestinationAsync(CopyVolumeEvidence destination, CancellationToken token)
        { OpenedDestination = destination; return ValueTask.FromResult<IGameCopyDestination>(this); }
        public ValueTask<string> CaptureEvidenceAsync(CancellationToken token) => ValueTask.FromResult("opaque");
        public ValueTask<Stream> OpenPartAsync(string name, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateAsync(string expectedEvidence, CancellationToken token) { Assert.Equal("opaque", expectedEvidence); token.ThrowIfCancellationRequested(); if (ValidationError is { } error) throw new GameCopyException(new(error, "Identity changed")); return ValueTask.CompletedTask; }
        public ValueTask ValidateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask<long?> GetAvailableBytesAsync(CancellationToken token) => ValueTask.FromResult(Available);
        public ValueTask<IGameArchiveSession> OpenAsync(IGameArchiveSource source, string? password, CancellationToken token)
        {
            LastPassword = password;
            if (OpenErrorOnce is { } error) { OpenErrorOnce = null; throw new GameCopyException(new(error, "Open failure")); }
            return ValueTask.FromResult<IGameArchiveSession>(new Session(this));
        }
        public ValueTask<CopyTargetSnapshot> InspectAsync(ImmutableArray<string> path, CancellationToken token)
        {
            BeforeTargetInspect?.Invoke();
            var name = string.Join('/', path);
            return ValueTask.FromResult(new CopyTargetSnapshot(TargetError == GameCopyErrorCode.FileDirectoryConflict ? CopyTargetKind.Directory : Files.ContainsKey(name) ? CopyTargetKind.File : CopyTargetKind.Missing,
                Files.ContainsKey(name) ? new(name, Files[name].Length, Mutations.ToString()) : null, TargetError != GameCopyErrorCode.InvalidName));
        }
        public ValueTask<GameCopySkipReason?> EnsureDirectoryAsync(ImmutableArray<string> path, CancellationToken token) => ValueTask.FromResult<GameCopySkipReason?>(null);
        public ValueTask<CopyOutputOpenResult> OpenOutputAsync(ImmutableArray<string> path, CopyFileEvidence? consent, CancellationToken token)
        {
            if (ChangeOnce) { ChangeOnce = false; Mutations++; return ValueTask.FromResult(new CopyOutputOpenResult(null, true)); }
            var name = string.Join('/', path);
            Assert.Equal(Files.ContainsKey(name), consent is not null);
            var stream = new MemoryStream(); Files[name] = stream; Mutations++; OutputOpen = true;
            return ValueTask.FromResult(new CopyOutputOpenResult(new OutputFile(this, stream)));
        }
        public async ValueTask<CopyOverwriteResponse> ConfirmOverwriteAsync(CopyOverwritePrompt prompt, CancellationToken token)
        {
            if (BeforePrompt is not null) await BeforePrompt(token);
            OverwritePrompts++;
            var identity = prompt.Identity;
            if (StaleOnce) { StaleOnce = false; identity = new(Guid.NewGuid(), Guid.NewGuid()); }
            return new CopyOverwriteResponse(identity, Overwrite);
        }
        public async ValueTask<CopyPasswordResponse> RequestPasswordAsync(CopyPasswordPrompt prompt, CancellationToken token)
        {
            if (BeforePassword is not null) await BeforePassword(token);
            PasswordPrompts++; PasswordObservedOutputClosed = !OutputOpen;
            AfterPassword?.Invoke();
            var identity = prompt.Identity;
            if (StalePasswordOnce) { StalePasswordOnce = false; identity = new(Guid.NewGuid(), Guid.NewGuid()); }
            return new CopyPasswordResponse(identity, PasswordCancel ? CopyPasswordDecision.Cancel : CopyPasswordDecision.Retry, "correct");
        }
        public ValueTask DisposeAsync() { Closed = true; OnDispose?.Invoke(); return ValueTask.CompletedTask; }
        private sealed class OutputFile(Fixture f, MemoryStream stream) : IGameCopyOutputFile
        {
            public Stream Output => stream;
            public async ValueTask DisposeAsync() { f.OnOutputClose?.Invoke(); if (f.BeforeOutputRelease is not null) await f.BeforeOutputRelease(); f.OutputOpen = false; if (f.FailClose) throw new IOException("Close failed"); }
        }
        private sealed class Session(Fixture f) : IGameArchiveSession
        {
            public async ValueTask<GameArchiveInspection> InspectAsync(CancellationToken token)
            { if (f.OnInspect is not null) await f.OnInspect(token); return new GameArchiveInspection(f.Entries, "inspection-not-opaque"); }
            public async ValueTask ExtractAsync(ImmutableArray<int> indices, IGameArchiveExtractionTarget target, IProgress<ArchiveByteProgress>? progress, CancellationToken token)
            {
                f.Batches.Add(indices);
                foreach (var index in indices)
                {
                    token.ThrowIfCancellationRequested();
                    var entry = f.Entries.Single(e => e.Index == index);
                    var output = await target.PrepareEntryAsync(entry, token);
                    GameCopyError? error = null;
                    try
                    {
                        if (output.Output is not null)
                        {
                            f.BeforeWrite?.Invoke();
                            if (f.OnWrite is not null) await f.OnWrite(token);
                            await output.Output.WriteAsync(new byte[] { 1 }, token);
                            progress?.Report(new(index, 1, 1));
                            if (f.CancelDuringWrite) { f.Cancel.Cancel(); token.ThrowIfCancellationRequested(); }
                            if (f.ExtractError is { } code && index == f.ErrorEntry)
                            {
                                if (f.RetryOnce) { f.RetryOnce = false; f.ExtractError = null; }
                                error = new(code, "Decode failed", index); throw new GameCopyException(error);
                            }
                            await output.Output.WriteAsync(new byte[] { 2, 3 }, token);
                            progress?.Report(new(index, 3, 3));
                        }
                    }
                    catch (OperationCanceledException) { error = new(GameCopyErrorCode.Cancelled, "Cancelled", index); throw; }
                    finally { await target.CompleteEntryAsync(index, error); }
                }
            }
            public ValueTask DisposeAsync() { if (f.FailSessionClose) throw new IOException("Session close failed"); return ValueTask.CompletedTask; }
        }
    }
}
