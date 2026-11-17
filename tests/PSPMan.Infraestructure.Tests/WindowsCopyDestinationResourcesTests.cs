using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

/// <summary>Prepared real Windows cases. Discovery skips on Linux are not native execution.</summary>
public sealed class WindowsCopyDestinationResourcesTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    [WindowsCopyFact]
    public async Task Separate_files_overlap_while_native_write_handles_block_replacement()
    {
        using var area = new WindowsCopyTestArea(); var owner = new CopyDestinationResources();
        var aEntered = Signal(); var bEntered = Signal(); var release = Signal();
        CopyFileAccessHooks Hooks(TaskCompletionSource entered) => new() { WrapOutput = stream => new TestStream(stream,
            async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); }) };
        await using var a = await Context.Open(area, owner, Hooks(aEntered));
        await using var b = await Context.Open(area, owner, Hooks(bEntered));
        await using var ae = await a.Entry(["shared", "a.iso"]);
        await using var be = await b.Entry(["shared", "b.iso"]);
        await using var af = (await a.Destination.OpenOutputAsync(["shared", "a.iso"], null, default)).File!;
        await using var bf = (await b.Destination.OpenOutputAsync(["shared", "b.iso"], null, default)).File!;
        var first = af.Output.WriteAsync(new byte[] { 1, 2, 3 }).AsTask();
        var second = bf.Output.WriteAsync(new byte[] { 4, 5, 6 }).AsTask();
        try
        {
            await Task.WhenAll(aEntered.Task, bEntered.Task).WaitAsync(Deadline);
            Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
            Assert.Throws<IOException>(() => File.Move(Path.Combine(area.Root, "ISO", "shared", "a.iso"), Path.Combine(area.Root, "ISO", "moved")));
            Assert.Throws<IOException>(() => Directory.Move(area.Root, area.Root + "-moved"));
        }
        finally { release.TrySetResult(); await Task.WhenAll(first, second).WaitAsync(Deadline); }
        await ae.DisposeAsync(); await be.DisposeAsync();
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(area.Root, "ISO", "shared", "a.iso")));
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(Path.Combine(area.Root, "ISO", "shared", "b.iso")));
    }

    [WindowsCopyTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Equivalent_names_wait_cancellably_then_reinspect_and_require_current_consent(bool shortAlias)
    {
        using var area = new WindowsCopyTestArea(); var owner = new CopyDestinationResources();
        await using var a = await Context.Open(area, owner); await using var b = await Context.Open(area, owner);
        string name = shortAlias ? "Long game archive destination.iso" : "Game.iso";
        var ae = await a.Entry([name]);
        await using var af = (await a.Destination.OpenOutputAsync([name], null, default)).File!;
        await af.Output.WriteAsync(new byte[] { 1, 2, 3 });
        string alias = shortAlias ? ShortLeaf(Path.Combine(area.Root, "ISO", name)) : "gAME.ISO";
        if (shortAlias) Assert.NotEqual(name, alias); // Required host has 8.3 generation enabled.
        using var cancel = new CancellationTokenSource(); var waiting = Signal();
        var pending = b.Entry([alias], new WaitProgress(_ => waiting.TrySetResult()), cancel.Token).AsTask();
        await waiting.Task.WaitAsync(Deadline); Assert.False(pending.IsCompleted);
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        waiting = Signal(); pending = b.Entry([alias], new WaitProgress(_ => waiting.TrySetResult())).AsTask();
        try { await waiting.Task.WaitAsync(Deadline); Assert.False(pending.IsCompleted); }
        finally { await ae.DisposeAsync(); }
        await using var be = await pending.WaitAsync(Deadline);
        var current = await b.Destination.InspectAsync([alias], default);
        Assert.Equal(3, current.Evidence!.Length);
        Assert.True((await b.Destination.OpenOutputAsync([alias], null, default)).TargetChanged);
        File.WriteAllText(Path.Combine(area.Root, "ISO", name), "external change");
        Assert.True((await b.Destination.OpenOutputAsync([alias], current.Evidence, default)).TargetChanged);
        var fresh = (await b.Destination.InspectAsync([alias], default)).Evidence;
        await using var output = (await b.Destination.OpenOutputAsync([alias], fresh, default)).File!;
        await output.Output.WriteAsync(new byte[] { 9 });
    }

    [WindowsCopyFact]
    public async Task Case_sensitive_directory_has_distinct_native_claims_for_case_only_names()
    {
        using var area = new WindowsCopyTestArea(); string directory = Path.Combine(area.Root, "ISO");
        Directory.CreateDirectory(directory); WindowsCopyTestArea.EnableCaseSensitivity(directory);
        var owner = new CopyDestinationResources();
        await using var a = await Context.Open(area, owner); await using var b = await Context.Open(area, owner);
        await using var ae = await a.Entry(["Game.iso"]); await using var be = await b.Entry(["game.iso"]);
        Assert.NotEqual(ae.TargetIdentity, be.TargetIdentity);
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [WindowsCopyTheory]
    [InlineData("hard-link")]
    [InlineData("junction")]
    [InlineData("structure")]
    [InlineData("name")]
    [InlineData("unsafe")]
    public async Task Native_claims_preserve_link_skip_and_structural_name_failures(string mode)
    {
        using var area = new WindowsCopyTestArea(); Directory.CreateDirectory(Path.Combine(area.Root, "ISO"));
        string outside = Path.Combine(area.Outside, "keep"); File.WriteAllText(outside, "untouched");
        ImmutableArray<string> path = ["target.iso"];
        if (mode == "hard-link") WindowsCopyTestArea.CreateHardLink(Path.Combine(area.Root, "ISO", "target.iso"), outside);
        if (mode == "junction") { WindowsCopyTestArea.CreateJunction(Path.Combine(area.Root, "ISO", "link"), area.Outside); path = ["link", "target.iso"]; }
        if (mode == "structure") { File.WriteAllText(Path.Combine(area.Root, "ISO", "parent"), "keep"); path = ["parent", "target.iso"]; }
        if (mode == "name") path = ["NUL.iso"];
        if (mode == "unsafe") path = ["..", "target.iso"];
        await using var a = await Context.Open(area, new());
        if (mode is "structure" or "name")
        {
            var error = await Assert.ThrowsAsync<GameCopyException>(async () => await a.Entry(path));
            Assert.Equal(mode == "name" ? GameCopyErrorCode.InvalidName : GameCopyErrorCode.FileDirectoryConflict, error.Error.Code);
        }
        else
        {
            await using var entry = await a.Entry(path);
            Assert.Equal(mode == "unsafe" ? GameCopySkipReason.UnsafePath : GameCopySkipReason.Link,
                (await a.Destination.OpenOutputAsync(path, null, default)).SkipReason);
        }
        Assert.Equal("untouched", File.ReadAllText(outside));
    }

    [WindowsCopyTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_drains_native_chunks_without_double_counting_the_sample(bool allocatedBeforeWait)
    {
        using var area = new WindowsCopyTestArea(); long allocated = 0; var entered = Signal(); var release = Signal();
        var owner = new CopyDestinationResources();
        var hooks = new CopyFileAccessHooks { AvailableBytes = () => 100 - Interlocked.Read(ref allocated),
            WrapOutput = stream => new TestStream(stream, async (bytes, token) =>
            { if (allocatedBeforeWait) Interlocked.Add(ref allocated, bytes.Length); entered.TrySetResult();
                await release.Task.WaitAsync(token); if (!allocatedBeforeWait) Interlocked.Add(ref allocated, bytes.Length); }) };
        await using var a = await Context.Open(area, owner, hooks, 70);
        await using var rawB = await area.CreateFileSystem(new() { AvailableBytes = () => 100 - Interlocked.Read(ref allocated) }).OpenDestinationAsync(area.Evidence, default);
        var bId = Guid.NewGuid(); var b = owner.BindDestination(bId, area.Evidence, rawB);
        await using var ae = await a.Entry(["game.iso"]);
        await using var file = (await a.Destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        var write = file.Output.WriteAsync(new byte[30]).AsTask();
        await entered.Task.WaitAsync(Deadline);
        var admission = owner.ReserveAsync(bId, area.Evidence, b, 30, default).AsTask();
        try { Assert.False(admission.IsCompleted); }
        finally { release.TrySetResult(); await write.WaitAsync(Deadline); }
        await using var budget = await admission.WaitAsync(Deadline);
        await Assert.ThrowsAsync<GameCopyException>(async () => await budget.RevalidateAsync(31, default));
    }

    [WindowsCopyFact]
    public async Task Buffered_output_is_flushed_and_retry_reserves_full_remaining_bytes()
    {
        using var area = new WindowsCopyTestArea(); string path = Path.Combine(area.Root, "ISO", "game.iso");
        await using var a = await Context.Open(area, new(), new() { WrapOutput = s => new BufferedStream(s, 8192),
            AvailableBytes = () => 100 - (File.Exists(path) ? new FileInfo(path).Length : 0) }, 30);
        await using var entry = await a.Entry(["game.iso"]);
        await using var file = (await a.Destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        await file.Output.WriteAsync(new byte[30]);
        await a.Budget.RevalidateAsync(70, default);
        Assert.Equal(30, new FileInfo(path).Length);
        await Assert.ThrowsAsync<GameCopyException>(async () => await a.Budget.RevalidateAsync(71, default));
    }

    [WindowsCopyTheory]
    [InlineData("partial")]
    [InlineData("uncertain")]
    [InlineData("flush")]
    [InlineData("close")]
    [InlineData("cancel")]
    public async Task Fault_or_cancel_retains_bytes_releases_claims_and_all_native_handles(string mode)
    {
        using var area = new WindowsCopyTestArea(); var owner = new CopyDestinationResources(); var entered = Signal();
        var a = await Context.Open(area, owner, new() { WrapOutput = s => new TestStream(s, mode == "cancel" ? async (_, token) =>
            { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } : null, mode) });
        var entry = await a.Entry(["game.iso"]);
        var file = (await a.Destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        using var cancel = new CancellationTokenSource();
        var write = file.Output.WriteAsync(new byte[] { 1, 2, 3 }, cancel.Token).AsTask();
        if (mode == "cancel") { await entered.Task.WaitAsync(Deadline); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write); }
        else if (mode is "partial" or "uncertain") await Assert.ThrowsAsync<GameCopyException>(() => write);
        else await write;
        if (mode is "flush" or "close") await Assert.ThrowsAsync<GameCopyException>(async () => await entry.DisposeAsync());
        else await entry.DisposeAsync();
        await file.DisposeAsync(); await a.DisposeAsync();
        string path = Path.Combine(area.Root, "ISO", "game.iso");
        Assert.Equal(mode is "partial" or "uncertain" ? new byte[] { 1 } : new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Directory.Move(area.Root, area.Root + "-released"); // All root/parent pins must be released.
    }

    [WindowsCopyFact]
    public async Task Independent_source_handles_keep_positions_and_archive_bytes_unchanged()
    {
        using var area = new WindowsCopyTestArea(); byte[] data = Enumerable.Range(0, 8192).Select(i => (byte)i).ToArray();
        string path = Path.Combine(area.Root, "archive.zip"); File.WriteAllBytes(path, data); var hash = SHA256.HashData(data);
        await using (var a = await area.FileSystem.OpenSourceAsync(area.Evidence, "/archive.zip", default))
        await using (var b = await area.FileSystem.OpenSourceAsync(area.Evidence, "/archive.zip", default))
        {
            await using var first = await a.OpenPartAsync("archive.zip", default);
            await using var second = await b.OpenPartAsync("archive.zip", default);
            Assert.NotSame(first, second); first.Position = 200; second.Position = 400;
            var left = new byte[20]; var right = new byte[20];
            await Task.WhenAll(first.ReadExactlyAsync(left).AsTask(), second.ReadExactlyAsync(right).AsTask());
            Assert.Equal(data[200..220], left); Assert.Equal(data[400..420], right);
        }
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [WindowsCopyFact]
    public async Task Changed_generation_fails_admission_without_destination_mutation()
    {
        using var area = new WindowsCopyTestArea();
        await using var raw = await area.FileSystem.OpenDestinationAsync(area.Evidence, default);
        var owner = new CopyDestinationResources(); var id = Guid.NewGuid(); var destination = owner.BindDestination(id, area.Evidence, raw);
        area.Generation = "changed";
        Assert.Equal(GameCopyErrorCode.DestinationChanged, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await owner.ReserveAsync(id, area.Evidence, destination, 1, default))).Error.Code);
        Assert.Empty(Directory.GetFileSystemEntries(area.Root));
    }

    [WindowsCopyFact]
    public async Task A_late_external_creation_is_existing_output_and_never_gets_the_own_empty_exemption()
    {
        using var area = new WindowsCopyTestArea(); string path = Path.Combine(area.Root, "ISO", "game.iso"); bool created = false;
        await using var a = await Context.Open(area, new(), new() { BeforeMutation = (stage, _) =>
        {
            if (stage != "claim" || created) return;
            created = true; File.WriteAllText(path, "external file");
        }});
        await using var entry = await a.Entry(["game.iso"]);
        Assert.True(created);
        Assert.Equal(CopyTargetKind.File, (await a.Destination.InspectAsync(["game.iso"], default)).Kind);
        Assert.True((await a.Destination.OpenOutputAsync(["game.iso"], null, default)).TargetChanged);
        Assert.Equal("external file", File.ReadAllText(path));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class WaitProgress(Action<GameCopyResourceWait> action) : IProgress<GameCopyResourceWait>
    { public void Report(GameCopyResourceWait value) => action(value); }
    private sealed class Context(CopyDestinationResources owner, WindowsCopyTestArea area, Guid id,
        IGameCopyDestination raw, IGameCopyDestination destination, IGameCopyBudgetLease budget) : IAsyncDisposable
    {
        internal IGameCopyDestination Destination => destination;
        internal IGameCopyBudgetLease Budget => budget;
        internal ValueTask<IGameCopyEntryLease> Entry(ImmutableArray<string> path, IProgress<GameCopyResourceWait>? waiting = null, CancellationToken token = default) =>
            owner.AcquireEntryAsync(id, area.Evidence, destination, path, false, waiting, token);
        internal static async Task<Context> Open(WindowsCopyTestArea area, CopyDestinationResources owner, CopyFileAccessHooks? hooks = null, long bytes = 100)
        {
            var raw = await area.CreateFileSystem(hooks).OpenDestinationAsync(area.Evidence, default);
            try
            {
                var id = Guid.NewGuid(); var destination = owner.BindDestination(id, area.Evidence, raw);
                return new(owner, area, id, raw, destination, await owner.ReserveAsync(id, area.Evidence, destination, bytes, default));
            }
            catch { await raw.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync() { try { await budget.DisposeAsync(); } finally { await raw.DisposeAsync(); } }
    }
    private sealed class TestStream(Stream inner, Func<ReadOnlyMemory<byte>, CancellationToken, Task>? pause = null, string? fault = null) : Stream
    {
        public override bool CanRead => false; public override bool CanWrite => true; public override bool CanSeek => fault != "uncertain";
        public override long Length => inner.Length; public override long Position { get => fault == "uncertain" ? throw new NotSupportedException() : inner.Position; set => throw new NotSupportedException(); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        {
            await inner.WriteAsync(fault is "partial" or "uncertain" ? bytes[..1] : bytes, token);
            if (fault is "partial" or "uncertain") throw new IOException("T14 partial write.");
            if (pause is not null) await pause(bytes, token);
        }
        public override void Write(byte[] b, int o, int c) => WriteAsync(b.AsMemory(o, c)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { if (fault == "flush") throw new IOException("T14 flush fault."); inner.Flush(); }
        public override Task FlushAsync(CancellationToken token) => fault == "flush" ? Task.FromException(new IOException("T14 flush fault.")) : inner.FlushAsync(token);
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); if (fault == "close") throw new IOException("T14 close fault."); } base.Dispose(disposing); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
    }
    private static string ShortLeaf(string path)
    {
        var value = new StringBuilder(32768); uint size = GetShortPathName(path, value, (uint)value.Capacity);
        Assert.InRange(size, 1u, (uint)value.Capacity - 1); return Path.GetFileName(value.ToString());
    }
    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetShortPathName(string path, StringBuilder output, uint capacity);
}
