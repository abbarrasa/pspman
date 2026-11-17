using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

/// <summary>Managed lifetime evidence for the synchronization callback, not native Windows proof.</summary>
public sealed class CopyAccessStreamSynchronizationTests
{
    [Fact]
    public async Task Synchronization_follows_managed_flush_and_precedes_handle_close_and_release()
    {
        var events = new List<string>();
        var inner = new Probe(events);
        var stream = new CopyAccessStream(inner, _ => ValueTask.CompletedTask, true,
            _ => events.Add("release"), () => { Assert.False(inner.Closed); events.Add("sync"); });
        await stream.WriteAsync(new byte[] { 1 }); await stream.FlushAsync(default); await stream.DisposeAsync();
        Assert.Equal(new[] { "flush", "sync", "flush", "sync", "close", "release" }, events);
    }

    [Fact]
    public async Task Failed_synchronization_closes_owned_stream_and_preserves_error_classification()
    {
        var events = new List<string>(); var inner = new Probe(events);
        var stream = new CopyAccessStream(inner, _ => ValueTask.CompletedTask, true,
            _ => events.Add("release"), () => throw new IOException("controlled sync fault"));
        Assert.Equal(GameCopyErrorCode.WriteError, (await Assert.ThrowsAsync<GameCopyException>(() => stream.FlushAsync(default))).Error.Code);
        Assert.Equal(GameCopyErrorCode.CloseError, (await Assert.ThrowsAsync<GameCopyException>(async () => await stream.DisposeAsync())).Error.Code);
        Assert.True(inner.Closed); Assert.Equal("release", events[^1]);
    }

    [Fact]
    public async Task Native_sync_callback_must_return_before_disposal_releases_the_stream()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>(); var inner = new Probe(events); int calls = 0;
        var stream = new CopyAccessStream(inner, _ => ValueTask.CompletedTask, true, _ => events.Add("release"), () =>
        { if (Interlocked.Increment(ref calls) == 1) { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); } });
        var flush = Task.Run(() => stream.FlushAsync(default));
        Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            closing = stream.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted); Assert.False(inner.Closed);
        }
        finally { release.TrySetResult(); await flush; if (closing is not null) await closing; else await stream.DisposeAsync(); }
        Assert.True(inner.Closed); Assert.Equal("release", events[^1]);
    }

    private sealed class Probe(List<string> events) : MemoryStream
    {
        internal bool Closed;
        public override Task FlushAsync(CancellationToken token) { events.Add("flush"); return Task.CompletedTask; }
        protected override void Dispose(bool disposing) { if (disposing) { Closed = true; events.Add("close"); } base.Dispose(disposing); }
    }
}
