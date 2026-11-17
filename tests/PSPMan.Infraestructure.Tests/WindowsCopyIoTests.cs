using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsCopyIoTests
{
    [WindowsCopyFact]
    public async Task SpaceAndOpenFaultsDoNotCreateAnOutputFile()
    {
        using var area = new WindowsCopyTestArea();
        var fault = new IOException("Injected open failure.");
        var fs = area.CreateFileSystem(new() { AvailableBytes = () => 0,
            BeforeMutation = (stage, _) => { if (stage == "open") throw fault; } });
        await using var destination = await fs.OpenDestinationAsync(area.Evidence, default);
        Assert.Equal(GameCopyErrorCode.InsufficientSpace, GameCopyRules.CheckCapacity(1, await destination.GetAvailableBytesAsync(default))!.Code);
        Assert.Empty(Directory.GetFileSystemEntries(area.Root));
        var error = await Assert.ThrowsAsync<GameCopyException>(async () => await destination.OpenOutputAsync(["game.iso"], null, default));
        Assert.Equal(GameCopyErrorCode.WriteError, error.Error.Code);
        Assert.Same(fault, error.InnerException);
        Assert.False(File.Exists(Path.Combine(area.Root, "ISO", "game.iso")));
    }

    [WindowsCopyFact]
    public async Task WriteFailureRetainsPrefixAndClosesFile()
    {
        using var area = new WindowsCopyTestArea();
        var fault = new IOException("Injected disk-full failure.");
        FaultFile? sink = null;
        var fs = area.CreateFileSystem(new() { WrapOutput = stream => sink = new(stream, fault, close: false) });
        await using var destination = await fs.OpenDestinationAsync(area.Evidence, default);
        var output = (await destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        var error = await Assert.ThrowsAsync<GameCopyException>(async () => await output.Output.WriteAsync(new byte[] { 1, 2, 3 }));
        Assert.Equal(GameCopyErrorCode.WriteError, error.Error.Code);
        Assert.Same(fault, error.InnerException);
        await output.DisposeAsync();
        Assert.True(sink!.Closed);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(area.Root, "ISO", "game.iso")));
    }

    [WindowsCopyFact]
    public async Task CloseFailureRetainsBytesAndReleasesDirectoryBarriers()
    {
        using var area = new WindowsCopyTestArea();
        var fault = new IOException("Injected close failure.");
        FaultFile? sink = null;
        var fs = area.CreateFileSystem(new() { WrapOutput = stream => sink = new(stream, fault, close: true) });
        await using var destination = await fs.OpenDestinationAsync(area.Evidence, default);
        var output = (await destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        await output.Output.WriteAsync(new byte[] { 1, 2, 3 });
        var error = await Assert.ThrowsAsync<GameCopyException>(() => output.DisposeAsync().AsTask());
        Assert.Equal(GameCopyErrorCode.CloseError, error.Error.Code);
        Assert.Same(fault, error.InnerException);
        Assert.True(sink!.Closed);
        await destination.DisposeAsync();
        Directory.Move(area.Root, area.Root + "-released");
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(area.Root + "-released", "ISO", "game.iso")));
    }

    [WindowsCopyFact]
    public async Task CancellationDuringPendingWriteRetainsPrefixAndClosesBeforeDisposalCompletes()
    {
        using var area = new WindowsCopyTestArea();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ForwardFile? sink = null;
        var fs = area.CreateFileSystem(new() { WrapOutput = stream => sink = new PendingFile(stream, entered) });
        await using var destination = await fs.OpenDestinationAsync(area.Evidence, default);
        var output = (await destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        using var cancellation = new CancellationTokenSource();
        var writing = output.Output.WriteAsync(new byte[] { 1, 2, 3 }, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing.WaitAsync(TimeSpan.FromSeconds(10)));
        await output.DisposeAsync();
        Assert.True(sink!.Closed);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(area.Root, "ISO", "game.iso")));
    }

    private class ForwardFile(Stream inner) : Stream
    {
        protected Stream Inner => inner;
        internal bool Closed { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
        public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
        public override void Write(byte[] b, int o, int c) => inner.Write(b, o, c);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default) => inner.WriteAsync(bytes, token);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override async ValueTask DisposeAsync() { Closed = true; await inner.DisposeAsync(); }
    }
    private sealed class FaultFile(Stream inner, IOException fault, bool close) : ForwardFile(inner)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        { await Inner.WriteAsync(close ? bytes : bytes[..1], token); if (!close) throw fault; }
        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); if (close) throw fault; }
    }
    private sealed class PendingFile(Stream inner, TaskCompletionSource entered) : ForwardFile(inner)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        { await Inner.WriteAsync(bytes[..1], token); entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
    }
}
