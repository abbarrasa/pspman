using PSPMan.Core.Transfers;
using static PSPMan.Infraestructure.Transfers.LinuxCopyFileAccess;

namespace PSPMan.Infraestructure.Transfers;

/// <summary>Owns its descriptor, validates before I/O, serializes close and retains output on failure.</summary>
internal sealed class CopyAccessStream(Stream inner, Func<CancellationToken, ValueTask> validate,
    bool writing, Action<CopyAccessStream> released) : Stream
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeGate = new();
    private Task? _disposal;
    private bool _disposed;
    public override bool CanRead => !_disposed && !writing && inner.CanRead;
    public override bool CanSeek => !_disposed && inner.CanSeek;
    public override bool CanWrite => !_disposed && writing && inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => Seek(value, SeekOrigin.Begin); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (writing) throw new NotSupportedException();
            await validate(cancellationToken).ConfigureAwait(false);
            return await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException error) { throw Failure(GameCopyErrorCode.ReadError, error.Message, error); }
        finally { _gate.Release(); }
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!writing) throw new NotSupportedException();
            await validate(cancellationToken).ConfigureAwait(false);
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException error) { throw Failure(GameCopyErrorCode.WriteError, error.Message, error); }
        finally { _gate.Release(); }
    }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override long Seek(long offset, SeekOrigin origin)
    {
        _gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Seeking only changes the pinned descriptor's offset. Validate on the next data I/O;
            // Stream.Seek has no token and must not introduce uncancellable provider awaits.
            return inner.Seek(offset, origin);
        }
        catch (IOException error) { throw Failure(writing ? GameCopyErrorCode.WriteError : GameCopyErrorCode.ReadError, error.Message, error); }
        finally { _gate.Release(); }
    }
    public override void SetLength(long value) => throw new NotSupportedException("Only conditional open may truncate destination files.");
    public override void Flush() => FlushAsync(default).GetAwaiter().GetResult();
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await validate(cancellationToken).ConfigureAwait(false);
            await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException error) { throw Failure(writing ? GameCopyErrorCode.WriteError : GameCopyErrorCode.ReadError, error.Message, error); }
        finally { _gate.Release(); }
    }
    protected override void Dispose(bool disposing)
    { if (disposing) DisposeAsync().AsTask().GetAwaiter().GetResult(); base.Dispose(disposing); }
    public override ValueTask DisposeAsync()
    { lock (_disposeGate) return new(_disposal ??= CloseAsync()); }
    private async Task CloseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            Exception? failure = null;
            if (writing)
                try { await inner.FlushAsync().ConfigureAwait(false); } catch (Exception error) { failure = error; }
            try { await inner.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure ??= error; }
            if (failure is not null) throw Failure(GameCopyErrorCode.CloseError, failure.Message, failure);
        }
        finally { released(this); _gate.Release(); }
    }
}
