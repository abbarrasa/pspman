using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

[Collection("Native archive concurrency")]
public sealed class GameArchiveConcurrencyTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);
    private static string Corpus => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers");
    private static readonly string[] ExpectedHashes = [
        "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9",
        "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106",
        "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53"];

    [ArchiveAdapterTheory]
    [InlineData("Zip.deflate.zip", null, "Zip.deflate.zip", null)]
    [InlineData("Zip.zip64.zip", null, "Zip.zip64.zip", null)]
    [InlineData("Zip.lzma.WinzipAES.zip", "test", "Zip.lzma.WinzipAES.zip", "test")]
    [InlineData("Zip.deflate.WinzipAES.zip", "test", "Zip.deflate.WinzipAES.zip", "test")]
    [InlineData("Rar4.rar", null, "Rar4.rar", null)]
    [InlineData("Rar5.rar", null, "Rar5.rar", null)]
    [InlineData("Rar5.solid.rar", null, "Rar5.solid.rar", null)]
    [InlineData("Rar.encrypted_filesAndHeader.rar", "test", "Rar.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", "test", "Rar5.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar.EncryptedParts.part02.rar", "test", "Rar.EncryptedParts.part02.rar", "test")]
    [InlineData("Rar5.multi.part03.rar", null, "Rar5.multi.part03.rar", null)]
    [InlineData("Rar2.multi.r02", null, "Rar2.multi.r02", null)]
    [InlineData("WinZip26.nocomp.multi.z01", null, "WinZip26.nocomp.multi.z01", null)]
    [InlineData("Zip.deflate.zip", null, "Rar5.solid.rar", null)]
    public async Task Independent_native_sessions_overlap_and_preserve_known_hashes(
        string first, string? firstPassword, string second, string? secondPassword)
    {
        var original = Hashes(Corpus);
        var probe = new OverlapProbe();
        await using var a = await Owner.Open(first, firstPassword, probe);
        await using var b = await Owner.Open(second, secondPassword, probe);
        a.Start(); await a.Target.Gate.Entered.Task.WaitAsync(Deadline);
        b.Start(); await b.Target.Gate.Entered.Task.WaitAsync(Deadline);
        Assert.Equal(2, probe.Active); Assert.Equal(2, probe.Peak);
        Assert.False(a.Worker!.IsCompleted); Assert.False(b.Worker!.IsCompleted);
        Assert.True(a.Source.OpenStreams > 0); Assert.True(b.Source.OpenStreams > 0);
        Assert.NotSame(a.Session, b.Session);
        // Returning one native session/module must not invalidate the still-active other session.
        a.Target.Gate.Release(); await a.Worker.WaitAsync(Deadline); await a.CloseSession();
        Assert.False(b.Worker.IsCompleted); Assert.True(b.Target.OpenOutputs > 0);
        GC.Collect(); GC.WaitForPendingFinalizers();
        b.Target.Gate.Release(); await b.Worker.WaitAsync(Deadline); await b.CloseSession();
        AssertKnown(a.Target); AssertKnown(b.Target);
        AssertReleased(a); AssertReleased(b); AssertHashes(original, Hashes(Corpus));
        Assert.True(a.Source.ReadCalls > 0 && b.Source.ReadCalls > 0);
        if (first.Contains("part", StringComparison.OrdinalIgnoreCase) || first.Contains(".r02") || first.Contains(".z01"))
        { Assert.True(a.Source.Parts.Distinct().Count() > 1); Assert.True(b.Source.Parts.Distinct().Count() > 1); }
    }

    [ArchiveAdapterFact]
    public async Task Overlap_probe_distinguishes_an_explicitly_serialized_native_control()
    {
        var probe = new OverlapProbe();
        await using var a = await Owner.Open("Zip.deflate.zip", null, probe);
        await using var b = await Owner.Open("Rar5.solid.rar", null, probe);
        using var serial = new SemaphoreSlim(1, 1);
        await serial.WaitAsync();
        a.Start(); await a.Target.Gate.Entered.Task.WaitAsync(Deadline);
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task RunSecond()
        {
            queued.TrySetResult();
            await serial.WaitAsync(b.Cancellation.Token);
            try { b.Start(); await b.Worker!; }
            finally { serial.Release(); }
        }
        var second = RunSecond();
        try
        {
            await queued.Task; Assert.False(b.Target.Gate.Entered.Task.IsCompleted); Assert.Equal(1, probe.Active);
            a.Target.Gate.Release(); await a.Worker!.WaitAsync(Deadline); serial.Release();
            await b.Target.Gate.Entered.Task.WaitAsync(Deadline); Assert.Equal(1, probe.Peak);
            b.Target.Gate.Release(); await second.WaitAsync(Deadline);
            AssertKnown(a.Target); AssertKnown(b.Target);
        }
        finally
        {
            a.Target.Gate.Release(); b.Target.Gate.Release(); b.Cancellation.Cancel();
            await Observe(second);
        }
    }

    [ArchiveAdapterTheory]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("write-failure")]
    [InlineData("close-failure")]
    [InlineData("noncooperative-dispose")]
    public async Task Stopping_one_session_retains_its_bytes_and_leaves_the_other_native_call_alive(string mode)
    {
        var original = Hashes(Corpus); var probe = new OverlapProbe();
        await using var a = await Owner.Open("Rar5.solid.rar", null, probe);
        await using var b = await Owner.Open("Rar5.solid.rar", null, probe);
        a.Target.Failure = mode; a.Target.Gate.IgnoreCancellation = mode == "noncooperative-dispose";
        a.Start(); await a.Target.Gate.Entered.Task.WaitAsync(Deadline);
        b.Start(); await b.Target.Gate.Entered.Task.WaitAsync(Deadline);
        Assert.Equal(2, probe.Peak);
        Task? disposal = null;
        if (mode is "dispose" or "noncooperative-dispose") disposal = a.Session.DisposeAsync().AsTask();
        else if (mode == "cancel") a.Cancellation.Cancel();
        else a.Target.Gate.Release();
        if (mode == "noncooperative-dispose")
        {
            Assert.False(disposal!.IsCompleted); Assert.False(a.Worker!.IsCompleted);
            Assert.True(a.Source.OpenStreams > 0); Assert.True(a.Target.OpenOutputs > 0);
            b.Target.Gate.Release(); await b.Worker!.WaitAsync(Deadline); await b.CloseSession();
            AssertKnown(b.Target); AssertReleased(b);
            Assert.False(disposal.IsCompleted); // Runtime/delegates must outlive this still-blocked callback.
            GC.Collect(); GC.WaitForPendingFinalizers(); a.Target.Gate.Release();
        }
        var error = await Record.ExceptionAsync(() => a.Worker!.WaitAsync(Deadline));
        if (mode.Contains("failure"))
        {
            var typed = Assert.IsType<GameCopyException>(error);
            Assert.Equal(mode == "close-failure" ? GameCopyErrorCode.CloseError : GameCopyErrorCode.WriteError, typed.Error.Code);
        }
        else Assert.IsAssignableFrom<OperationCanceledException>(error);
        if (disposal is not null) await disposal.WaitAsync(Deadline);
        await a.CloseSession(); AssertReleased(a);
        if (mode != "noncooperative-dispose")
        {
            Assert.False(b.Worker!.IsCompleted); Assert.True(b.Source.OpenStreams > 0);
            b.Target.Gate.Release(); await b.Worker.WaitAsync(Deadline); await b.CloseSession();
        }
        AssertKnown(b.Target); AssertReleased(b);
        Assert.Contains(a.Target.Outputs.Values, bytes => bytes.Length > 0);
        foreach (var (index, bytes) in a.Target.Outputs)
            Assert.Equal(b.Target.Outputs[index].AsSpan(0, bytes.Length).ToArray(), bytes);
        AssertHashes(original, Hashes(Corpus));
    }

    [ArchiveAdapterTheory]
    [InlineData("wrong-password", GameCopyErrorCode.IndeterminateEncryptedError)]
    [InlineData("damaged-encrypted", GameCopyErrorCode.IndeterminateEncryptedError)]
    [InlineData("wrong-header", GameCopyErrorCode.IndeterminateEncryptedError)]
    [InlineData("wrong-definite", GameCopyErrorCode.IncorrectPassword)]
    [InlineData("missing-part", GameCopyErrorCode.MissingPart)]
    [InlineData("crc", GameCopyErrorCode.CorruptArchive)]
    [InlineData("empty", GameCopyErrorCode.EmptyArchive)]
    [InlineData("source-io", GameCopyErrorCode.ReadError)]
    public async Task Password_archive_and_source_errors_do_not_escape_their_session(string scenario, GameCopyErrorCode expected)
    {
        using var area = new ProbeDirectory();
        string name = scenario == "wrong-header" ? "Rar5.encrypted_filesAndHeader.rar" :
            scenario == "wrong-definite" ? "Rar5.encrypted_filesOnly.rar" :
            scenario == "missing-part" ? "Rar5.multi.part01.rar" : "Rar.encrypted_filesOnly.rar";
        foreach (var file in Directory.EnumerateFiles(Corpus))
            if (scenario != "missing-part" || Path.GetFileName(file) != "Rar5.multi.part02.rar")
                File.Copy(file, Path.Combine(area.Path, Path.GetFileName(file)));
        if (scenario == "damaged-encrypted")
        {
            var path = Path.Combine(area.Path, name); var bytes = File.ReadAllBytes(path);
            bytes[50000] ^= 0x10; File.WriteAllBytes(path, bytes);
        }
        if (scenario == "crc")
        {
            name = "bad.zip"; var path = Path.Combine(area.Path, name);
            ArchiveCompatibilityEdgeTests.CreateStoredZip(path);
            var bytes = File.ReadAllBytes(path);
            var offset = bytes.AsSpan().IndexOf("second-unique-payload"u8); Assert.True(offset >= 0);
            bytes[offset] ^= 0x10; File.WriteAllBytes(path, bytes);
        }
        if (scenario == "empty") { name = "empty.zip"; using var zip = ZipFile.Open(Path.Combine(area.Path, name), ZipArchiveMode.Create); }
        var original = Hashes(area.Path); var probe = new OverlapProbe();
        await using var healthy = await Owner.Open("Rar5.encrypted_filesAndHeader.rar", "test", probe);
        healthy.Start(); await healthy.Target.Gate.Entered.Task.WaitAsync(Deadline);
        await using var source = new TrackedSource(name, area.Path) { FailReads = scenario == "source-io" };
        var target = new GatedTarget(new(probe)); target.Gate.Release();
        var reader = new SevenZipGameArchiveReader(GameArchiveReaderTests.RuntimeRoot);
        using var attemptLifetime = new CancellationTokenSource(Deadline);
        async Task Attempt(string password, GatedTarget destination)
        {
            await using var session = await reader.OpenAsync(source, password, attemptLifetime.Token);
            var info = await session.InspectAsync(attemptLifetime.Token);
            await session.ExtractAsync(Indices(info), destination, null, attemptLifetime.Token);
        }
        Task attempt = Attempt(scenario.StartsWith("wrong") ? "incorrect" : "test", target);
        try
        {
            var error = await Assert.ThrowsAsync<GameCopyException>(() => attempt.WaitAsync(Deadline));
            Assert.Equal(expected, error.Error.Code); Assert.Equal(0, source.OpenStreams); Assert.Equal(0, target.OpenOutputs);
            if (expected == GameCopyErrorCode.IndeterminateEncryptedError)
            {
                Assert.Equal("Incorrect password or damaged archive", error.Message);
                if (scenario == "damaged-encrypted") Assert.Contains(target.Outputs.Values, bytes => bytes.Length > 0);
            }
            if (scenario.StartsWith("wrong"))
            {
                var retry = new GatedTarget(new(probe)); retry.Gate.Release();
                attempt = Attempt("test", retry); await attempt.WaitAsync(Deadline);
                AssertKnown(retry); Assert.Equal(0, source.OpenStreams);
            }
            Assert.False(healthy.Worker!.IsCompleted); Assert.True(healthy.Target.OpenOutputs > 0);
            healthy.Target.Gate.Release(); await healthy.Worker.WaitAsync(Deadline); await healthy.CloseSession();
            AssertKnown(healthy.Target); AssertReleased(healthy); AssertHashes(original, Hashes(area.Path));
        }
        finally
        {
            // Bound even an Open/Inspect call accidentally serialized behind the held peer.
            // Release that peer before awaiting the failed attempt's native return/cleanup.
            healthy.Target.Gate.ReleaseAll(); attemptLifetime.Cancel(); await Observe(attempt);
        }
    }

    [ArchiveAdapterFact]
    public async Task A_source_callback_failure_after_writing_retains_a_prefix_without_stopping_the_peer()
    {
        using var area = new ProbeDirectory();
        var payload = Enumerable.Range(0, 2 * 1024 * 1024).Select(i => (byte)(i % 251)).ToArray();
        using (var zip = ZipFile.Open(Path.Combine(area.Path, "source.zip"), ZipArchiveMode.Create))
        using (var output = zip.CreateEntry("game.iso", CompressionLevel.NoCompression).Open()) output.Write(payload);
        var original = Hashes(area.Path); var probe = new OverlapProbe();
        await using var a = await Owner.Open("source.zip", null, probe, area.Path);
        await using var b = await Owner.Open("Rar5.solid.rar", null, probe);
        a.Start(); await a.Target.Gate.Entered.Task.WaitAsync(Deadline);
        b.Start(); await b.Target.Gate.Entered.Task.WaitAsync(Deadline); Assert.Equal(2, probe.Peak);
        // Preparation/verification and the first native output write have finished. A later
        // source callback now fails while both native extraction invocations are still active.
        a.Source.FailReads = true; a.Target.Gate.Release();
        var error = await Assert.ThrowsAsync<GameCopyException>(() => a.Worker!.WaitAsync(Deadline));
        Assert.Equal(GameCopyErrorCode.ReadError, error.Error.Code);
        await a.CloseSession(); AssertReleased(a);
        var retained = Assert.Single(a.Target.Outputs).Value;
        Assert.InRange(retained.Length, 1, payload.Length - 1);
        Assert.Equal(payload.AsSpan(0, retained.Length).ToArray(), retained);
        Assert.False(b.Worker!.IsCompleted); Assert.True(b.Target.OpenOutputs > 0);
        b.Target.Gate.Release(); await b.Worker.WaitAsync(Deadline); await b.CloseSession();
        AssertKnown(b.Target); AssertReleased(b); AssertHashes(original, Hashes(area.Path));
    }

    [ArchiveAdapterFact]
    public async Task Identically_named_sources_keep_distinct_payloads_positions_and_progress()
    {
        using var left = new ProbeDirectory(); using var right = new ProbeDirectory();
        var aBytes = Enumerable.Range(0, 190001).Select(i => (byte)(i % 251)).ToArray();
        var bBytes = Enumerable.Range(0, 230003).Select(i => (byte)(255 - i % 239)).ToArray();
        foreach (var (root, bytes) in new[] { (left.Path, aBytes), (right.Path, bBytes) })
        { using var zip = ZipFile.Open(Path.Combine(root, "game.zip"), ZipArchiveMode.Create); using var output = zip.CreateEntry("game.iso").Open(); output.Write(bytes); }
        var aOriginal = Hashes(left.Path); var bOriginal = Hashes(right.Path); var probe = new OverlapProbe();
        await using var a = await Owner.Open("game.zip", null, probe, left.Path);
        await using var b = await Owner.Open("game.zip", null, probe, right.Path);
        a.Start(); await a.Target.Gate.Entered.Task.WaitAsync(Deadline);
        b.Start(); await b.Target.Gate.Entered.Task.WaitAsync(Deadline); Assert.Equal(2, probe.Peak);
        a.Target.Gate.Release(); b.Target.Gate.Release();
        await Task.WhenAll(a.Worker!, b.Worker!).WaitAsync(Deadline);
        Assert.Equal(aBytes, Assert.Single(a.Target.Outputs).Value); Assert.Equal(bBytes, Assert.Single(b.Target.Outputs).Value);
        Assert.All(a.Progress, p => Assert.InRange(p.WrittenBytes, 0, aBytes.Length));
        Assert.All(b.Progress, p => Assert.InRange(p.WrittenBytes, 0, bBytes.Length));
        Assert.NotEmpty(a.Progress); Assert.NotEmpty(b.Progress);
        await a.CloseSession(); await b.CloseSession(); AssertReleased(a); AssertReleased(b);
        AssertHashes(aOriginal, Hashes(left.Path)); AssertHashes(bOriginal, Hashes(right.Path));
    }

    private static ImmutableArray<int> Indices(GameArchiveInspection info) => info.Entries.Select(e => e.Index).ToImmutableArray();
    private static void AssertKnown(GatedTarget target) => Assert.Equal(ExpectedHashes, target.Outputs.Values.Where(b => b.Length > 0)
        .Select(b => Convert.ToHexStringLower(SHA256.HashData(b))).Order().ToArray());
    private static void AssertReleased(Owner owner)
    { Assert.Equal(0, owner.Source.OpenStreams); Assert.Equal(0, owner.Target.OpenOutputs); Assert.Equal(owner.Target.Prepared, owner.Target.Completed); }
    private static Dictionary<string, string> Hashes(string root) => Directory.EnumerateFiles(root).Order()
        .ToDictionary(p => Path.GetFileName(p), p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
    private static void AssertHashes(Dictionary<string, string> before, Dictionary<string, string> after) => Assert.Equal(before.OrderBy(p => p.Key), after.OrderBy(p => p.Key));
    private static async Task Observe(Task task) { try { await task.WaitAsync(Deadline); } catch (Exception error) when (error is not TimeoutException) { } }

    private sealed class OverlapProbe
    {
        private int active, peak;
        public ConcurrentBag<CallbackGate> Gates { get; } = [];
        public int Active => Volatile.Read(ref active);
        public int Peak => Volatile.Read(ref peak);
        public void Enter()
        { var count = Interlocked.Increment(ref active); int old; do { old = Peak; if (old >= count) break; } while (Interlocked.CompareExchange(ref peak, count, old) != old); }
        public void Exit() => Interlocked.Decrement(ref active);
        public void ReleaseAll() { foreach (var gate in Gates) gate.Release(); }
    }
    private sealed class CallbackGate
    {
        private readonly OverlapProbe probe;
        public CallbackGate(OverlapProbe probe) { this.probe = probe; probe.Gates.Add(this); }
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IgnoreCancellation;
        public void Release() => release.TrySetResult();
        public void ReleaseAll() => probe.ReleaseAll();
        public async ValueTask Pause(CancellationToken token)
        {
            probe.Enter(); Entered.TrySetResult();
            try { if (IgnoreCancellation) await release.Task; else await release.Task.WaitAsync(token); }
            finally { probe.Exit(); }
        }
    }
    private sealed class Owner : IAsyncDisposable
    {
        public required TrackedSource Source;
        public required IGameArchiveSession Session;
        public required GameArchiveInspection Info;
        public required GatedTarget Target;
        public CancellationTokenSource Cancellation { get; } = new();
        public ConcurrentQueue<ArchiveByteProgress> Progress { get; } = new();
        public Task? Worker;
        public static async Task<Owner> Open(string name, string? password, OverlapProbe probe, string? root = null)
        {
            var source = new TrackedSource(name, root ?? Corpus); IGameArchiveSession? session = null;
            try
            {
                session = await new SevenZipGameArchiveReader(GameArchiveReaderTests.RuntimeRoot).OpenAsync(source, password, default);
                return new() { Source = source, Session = session, Info = await session.InspectAsync(default), Target = new(new(probe)) };
            }
            catch { if (session is not null) await session.DisposeAsync(); await source.DisposeAsync(); throw; }
        }
        public void Start() => Worker = Session.ExtractAsync(Indices(Info), Target, new InlineProgress(Progress.Enqueue), Cancellation.Token).AsTask();
        public Task CloseSession() => Session.DisposeAsync().AsTask();
        public async ValueTask DisposeAsync()
        {
            // Assertion/timeout cleanup releases every peer first, including a peer blocked by
            // an accidentally serialized runtime. Otherwise reverse owner disposal could deadlock.
            Target.Gate.ReleaseAll(); Cancellation.Cancel();
            try { if (Worker is not null) await Observe(Worker); }
            finally { try { await Session.DisposeAsync(); } finally { Cancellation.Dispose(); await Source.DisposeAsync(); } }
        }
    }
    private sealed class InlineProgress(Action<ArchiveByteProgress> report) : IProgress<ArchiveByteProgress>
    { public void Report(ArchiveByteProgress value) => report(value); }

    private sealed class GatedTarget(CallbackGate gate) : IGameArchiveExtractionTarget
    {
        public CallbackGate Gate => gate;
        public string? Failure;
        public Dictionary<int, byte[]> Outputs { get; } = [];
        private readonly Dictionary<int, Output> active = [];
        private int open, gated, prepared, completed;
        public int OpenOutputs => Volatile.Read(ref open);
        public int Prepared => Volatile.Read(ref prepared);
        public int Completed => Volatile.Read(ref completed);
        public ValueTask<ArchiveEntryTarget> PrepareEntryAsync(GameArchiveEntry entry, CancellationToken token)
        {
            Interlocked.Increment(ref prepared);
            if (entry.IsDirectory) return ValueTask.FromResult(new ArchiveEntryTarget(null, null));
            var output = new Output(async (bytes, ct) =>
            {
                if (bytes > 0 && Interlocked.CompareExchange(ref gated, 1, 0) == 0)
                { await gate.Pause(ct); if (Failure == "write-failure") throw new IOException("T12 controlled output fault."); }
            }, () => { Interlocked.Decrement(ref open); if (Failure == "close-failure") throw new IOException("T12 controlled close fault."); });
            active.Add(entry.Index, output); Interlocked.Increment(ref open);
            return ValueTask.FromResult(new ArchiveEntryTarget(output, null));
        }
        public async ValueTask CompleteEntryAsync(int index, GameCopyError? error)
        {
            Interlocked.Increment(ref completed);
            if (active.Remove(index, out var output))
            { Outputs.Add(index, output.Bytes); await output.DisposeAsync(); }
        }
        private sealed class Output(Func<int, CancellationToken, ValueTask> afterWrite, Action closed) : Stream
        {
            private readonly MemoryStream data = new(); private int disposed;
            public byte[] Bytes => data.ToArray();
            public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => disposed == 0;
            public override long Length => data.Length;
            public override long Position { get => data.Position; set => throw new NotSupportedException(); }
            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
            { await data.WriteAsync(buffer, token); await afterWrite(buffer.Length, token); }
            public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
            protected override void Dispose(bool disposing)
            { if (disposing && Interlocked.Exchange(ref disposed, 1) == 0) { data.Dispose(); closed(); } base.Dispose(disposing); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }

    private sealed class TrackedSource(string name, string root) : IGameArchiveSource
    {
        private int open, reads;
        public volatile bool FailReads;
        public string ArchiveName => name;
        public int OpenStreams => Volatile.Read(ref open);
        public int ReadCalls => Volatile.Read(ref reads);
        public ConcurrentBag<string> Parts { get; } = [];
        public ValueTask<string> CaptureEvidenceAsync(CancellationToken token) => ValueTask.FromResult("T12 fixed fixture snapshot");
        public ValueTask ValidateAsync(string expected, CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask<Stream> OpenPartAsync(string leaf, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (leaf.Contains('/') || leaf.Contains('\\') || leaf is "." or "..") throw new IOException("Unsafe test part.");
            var stream = new FileStream(Path.Combine(root, leaf), FileMode.Open, FileAccess.Read, FileShare.Read);
            Parts.Add(leaf); Interlocked.Increment(ref open);
            return ValueTask.FromResult<Stream>(new Input(stream, () => { Interlocked.Increment(ref reads); if (FailReads) throw new IOException("T12 controlled source fault."); }, () => Interlocked.Decrement(ref open)));
        }
        public ValueTask DisposeAsync() { Assert.Equal(0, OpenStreams); return ValueTask.CompletedTask; }
        private sealed class Input(Stream inner, Action reading, Action closed) : Stream
        {
            private int disposed;
            public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override int Read(byte[] buffer, int offset, int count) { reading(); return inner.Read(buffer, offset, count); }
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void Flush() { }
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            { if (disposing && Interlocked.Exchange(ref disposed, 1) == 0) { inner.Dispose(); closed(); } base.Dispose(disposing); }
        }
    }
}

// These cases deliberately hold native worker callbacks and force process-wide GC.
// Isolate from unrelated timed integration classes, while each case still proves
// two production native sessions overlap rather than serializing their extraction.
[CollectionDefinition("Native archive concurrency", DisableParallelization = true)]
public sealed class NativeArchiveConcurrencyCollection { }
