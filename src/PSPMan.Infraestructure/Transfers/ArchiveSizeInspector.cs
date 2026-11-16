using System.Collections.Immutable;
using PSPMan.Core.Transfers;

namespace PSPMan.Infraestructure.Transfers;

internal static class ArchiveSizeInspector
{
    internal static ImmutableArray<GameArchiveEntry> Complete(ImmutableArray<GameArchiveEntry> entries,
        Action<ImmutableArray<int>, IGameArchiveExtractionTarget> extract)
    {
        if (!entries.Any(e => !e.IsDirectory && e.SizeBytes is null)) return entries;
        var counter = new CountingTarget();
        extract(entries.Select(e => e.Index).ToImmutableArray(), counter);
        return entries.Select(e => e with { SizeBytes = e.IsDirectory ? 0 : counter.Sizes[e.Index] }).ToImmutableArray();
    }
    private sealed class CountingTarget : IGameArchiveExtractionTarget
    {
        internal readonly Dictionary<int, long> Sizes = [];
        private Counter? current;
        public ValueTask<ArchiveEntryTarget> PrepareEntryAsync(GameArchiveEntry entry, CancellationToken token)
        { current = new Counter(); return ValueTask.FromResult(new ArchiveEntryTarget(current, null)); }
        public ValueTask CompleteEntryAsync(int index, GameCopyError? error)
        { Sizes[index] = current!.Count; current.Dispose(); current = null; return ValueTask.CompletedTask; }
        private sealed class Counter : Stream
        {
            internal long Count;
            public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
            public override long Length => Count;
            public override long Position { get => Count; set => throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count)
            {
                try { Count = checked(Count + count); }
                catch (OverflowException error)
                { throw new GameCopyException(new(GameCopyErrorCode.SizeOverflow, "Actual decompressed size exceeds the supported byte range."), error); }
            }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
