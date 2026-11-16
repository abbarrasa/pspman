using System.Collections.Immutable;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class GameArchiveReaderIoTests
{
    private static SevenZipGameArchiveReader Reader() => new(GameArchiveReaderTests.RuntimeRoot);
    [ArchiveAdapterFact]
    public async Task Archive_signatures_take_priority_over_misleading_filename_extensions()
    {
        using var area = new ProbeDirectory();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers", "Rar5.rar"), Path.Combine(area.Path, "renamed.zip"));
        await using var source = new TestArchiveSource("renamed.zip", area.Path);
        await using var session = await Reader().OpenAsync(source, null, default);
        Assert.Equal(100926, GameCopyRules.SumActualBytes((await session.InspectAsync(default)).Entries).Bytes);
    }
    [ArchiveAdapterFact]
    public async Task Reentrant_session_calls_fail_without_deadlocking_the_native_callback()
    {
        await using var source = new TestArchiveSource("Rar5.rar");
        await using var session = await Reader().OpenAsync(source, null, default);
        var info = await session.InspectAsync(default);
        var target = new TestArchiveTarget { Prepare = async (_, ct) =>
        {
            await session.InspectAsync(ct);
            return new(null, GameCopySkipReason.Link);
        }};
        var error = await Assert.ThrowsAsync<GameCopyException>(() => session.ExtractAsync(
            info.Entries.Select(e => e.Index).ToImmutableArray(), target, null, default).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Empty(target.Outputs);
    }
    [ArchiveAdapterFact]
    public async Task Missing_size_metadata_uses_a_bounded_count_pass_over_real_native_payloads()
    {
        await using var source = new TestArchiveSource("Rar5.solid.rar");
        await using var session = await Reader().OpenAsync(source, null, default);
        var info = await session.InspectAsync(default);
        var incomplete = info.Entries.Select(e => e with { SizeBytes = e.IsDirectory ? 0 : null }).ToImmutableArray();
        var measured = ArchiveSizeInspector.Complete(incomplete, (indices, target) =>
            session.ExtractAsync(indices, target, null, default).AsTask().GetAwaiter().GetResult());
        Assert.Equal(100926, GameCopyRules.SumActualBytes(measured).Bytes);
        Assert.Equal(info.Entries.Select(e => e.SizeBytes ?? 0), measured.Select(e => e.SizeBytes ?? 0));
    }

    [ArchiveAdapterFact]
    public async Task Default_reader_loads_only_assets_copied_to_the_application_directory()
    {
        await using var source = new TestArchiveSource("Zip.deflate.zip");
        await using var session = await new SevenZipGameArchiveReader().OpenAsync(source, null, default);
        Assert.Equal(100926, GameCopyRules.SumActualBytes((await session.InspectAsync(default)).Entries).Bytes);
    }
    [ArchiveAdapterTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_and_close_faults_keep_the_original_cause_and_stop_further_outputs(bool closeFailure)
    {
        await using var source = new TestArchiveSource("Rar5.rar");
        await using var session = await Reader().OpenAsync(source, null, default);
        var info = await session.InspectAsync(default);
        var fault = new IOException(closeFailure ? "Injected close failure." : "Injected write failure.");
        var sink = new FaultStream(fault, closeFailure);
        var target = new TestArchiveTarget { Prepare = (entry, _) => ValueTask.FromResult(entry.IsDirectory
            ? new ArchiveEntryTarget(null, GameCopySkipReason.Link) : new ArchiveEntryTarget(sink, null)) };
        var error = await Assert.ThrowsAsync<GameCopyException>(() => session.ExtractAsync(info.Entries.Select(e => e.Index).ToImmutableArray(), target, null, default).AsTask());
        Assert.Equal(closeFailure ? GameCopyErrorCode.CloseError : GameCopyErrorCode.WriteError, error.Error.Code);
        Assert.Same(fault, error.InnerException);
        Assert.True(sink.Closed);
        Assert.True(sink.Retained > 0);
        Assert.Equal(target.Prepared.Count, target.Completed.Count);
    }
    [ArchiveAdapterFact]
    public async Task Cancellation_during_a_pending_write_closes_output_before_native_return()
    {
        await using var source = new TestArchiveSource("Zip.deflate.zip");
        await using var session = await Reader().OpenAsync(source, null, default);
        var info = await session.InspectAsync(default);
        var sink = new PendingStream();
        var target = new TestArchiveTarget { Prepare = (entry, _) => ValueTask.FromResult(entry.IsDirectory
            ? new ArchiveEntryTarget(null, GameCopySkipReason.Link) : new ArchiveEntryTarget(sink, null)) };
        using var cancellation = new CancellationTokenSource();
        var extraction = session.ExtractAsync(info.Entries.Select(e => e.Index).ToImmutableArray(), target, null, cancellation.Token).AsTask();
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extraction.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(sink.Closed);
        Assert.Equal(GameCopyErrorCode.Cancelled, target.Completed.Last().Error?.Code);
    }
    [ArchiveAdapterFact]
    public async Task Progress_counts_actual_entry_bytes_and_solid_skips_do_not_claim_written_bytes()
    {
        await using var source = new TestArchiveSource("Rar5.solid.rar");
        await using var session = await Reader().OpenAsync(source, null, default);
        var info = await session.InspectAsync(default);
        var updates = new List<ArchiveByteProgress>();
        var target = new TestArchiveTarget { Prepare = (_, _) => ValueTask.FromResult(new ArchiveEntryTarget(null, GameCopySkipReason.OverwriteDeclined)) };
        await session.ExtractAsync(info.Entries.Select(e => e.Index).ToImmutableArray(), target, new InlineProgress(updates.Add), default);
        Assert.NotEmpty(updates);
        Assert.All(updates, p => Assert.Equal(0, p.WrittenBytes));
        foreach (var file in info.Entries.Where(e => !e.IsDirectory))
            Assert.Equal(file.SizeBytes, updates.Last(p => p.EntryIndex == file.Index).DecompressedBytes);
    }
    [ArchiveAdapterTheory]
    [InlineData("Rar.encrypted_filesAndHeader.rar")]
    [InlineData("Rar5.encrypted_filesAndHeader.rar")]
    public async Task Missing_password_requests_input_without_leaking_source_handles(string name)
    {
        await using var source = new TestArchiveSource(name);
        var error = await Assert.ThrowsAsync<GameCopyException>(() => Reader().OpenAsync(source, null, default).AsTask());
        Assert.Equal(GameCopyErrorCode.PasswordRequired, error.Error.Code);
        Assert.Equal(0, source.OpenStreams);
    }
    private sealed class InlineProgress(Action<ArchiveByteProgress> report) : IProgress<ArchiveByteProgress>
    { public void Report(ArchiveByteProgress value) => report(value); }
    private sealed class FaultStream(IOException error, bool failClose) : MemoryStream
    {
        public bool Closed { get; private set; }
        public long Retained { get; private set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            base.Write(buffer.Span[..(failClose ? buffer.Length : Math.Max(1, buffer.Length / 2))]);
            return failClose ? ValueTask.CompletedTask : ValueTask.FromException(error);
        }
        protected override void Dispose(bool disposing)
        {
            Retained = Length; Closed = true; base.Dispose(disposing);
            if (failClose) throw error;
        }
    }
    private sealed class PendingStream : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Closed { get; private set; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
}
