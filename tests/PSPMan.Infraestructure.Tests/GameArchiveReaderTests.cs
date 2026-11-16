using System.Collections.Immutable;
using System.Security.Cryptography;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class GameArchiveReaderTests
{
    [ArchiveAdapterTheory]
    [InlineData("Zip.deflate.zip", null)]
    [InlineData("Zip.zip64.zip", null)]
    [InlineData("Zip.lzma.WinzipAES.zip", "test")]
    [InlineData("Zip.deflate.WinzipAES.zip", "test")]
    [InlineData("Rar4.rar", null)]
    [InlineData("Rar5.rar", null)]
    [InlineData("Rar5.solid.rar", null)]
    [InlineData("Rar.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar.EncryptedParts.part01.rar", "test")]
    [InlineData("Rar5.multi.part03.rar", null)]
    [InlineData("Rar2.multi.r02", null)]
    [InlineData("WinZip26.nocomp.multi.z01", null)]
    public async Task Production_adapter_extracts_known_hashes_through_controlled_async_targets(string name, string? password)
    {
        await using var source = new TestArchiveSource(name);
        var reader = new SevenZipGameArchiveReader(RuntimeRoot);
        await using (var session = await reader.OpenAsync(source, password, default))
        {
            var inspection = await session.InspectAsync(default);
            Assert.Equal(100926, GameCopyRules.SumActualBytes(inspection.Entries).Bytes);
            var target = new TestArchiveTarget();
            await session.ExtractAsync(inspection.Entries.Select(e => e.Index).ToImmutableArray(), target, null, default);
            var hashes = target.Outputs.Where(p => p.Value.Length != 0).Select(p => Convert.ToHexStringLower(SHA256.HashData(p.Value))).Order().ToArray();
            Assert.Equal(new[] {
                "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9",
                "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106",
                "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53"
            }, hashes);
            Assert.Equal(target.Prepared.Count, target.Completed.Count);
        }
        Assert.Equal(0, source.OpenStreams);
    }
    internal static string RuntimeRoot => Environment.GetEnvironmentVariable("PSPMAN_TEST_PUBLISH_ROOT")
        ?? Path.Combine(NativeArchiveProbe.Root, "native", "PSPMan.ArchiveBridge", "artifacts");
}

internal sealed class TestArchiveSource(string name, string? directory = null) : IGameArchiveSource
{
    public string ArchiveName => name;
    public int OpenStreams { get; private set; }
    public bool Changed { get; set; }
    public ValueTask<string> CaptureEvidenceAsync(CancellationToken cancellationToken) => ValueTask.FromResult("fixture-v1");
    public ValueTask ValidateAsync(string expectedEvidence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Changed) throw new GameCopyException(new(GameCopyErrorCode.SourceChanged, "Source changed."));
        return ValueTask.CompletedTask;
    }
    public ValueTask<Stream> OpenPartAsync(string relativeName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (relativeName.Contains('/') || relativeName.Contains('\\') || relativeName is "." or "..")
            throw new IOException("Unconfined source request.");
        var path = Path.Combine(directory ?? Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers"), relativeName);
        if (!File.Exists(path)) throw new FileNotFoundException("Missing part.", path);
        OpenStreams++;
        return ValueTask.FromResult<Stream>(new CountedStream(File.OpenRead(path), () => OpenStreams--));
    }
    public ValueTask DisposeAsync() { Assert.Equal(0, OpenStreams); return ValueTask.CompletedTask; }
    private sealed class CountedStream(Stream inner, Action closed) : Stream
    {
        private bool disposed;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (!disposed) { disposed = true; inner.Dispose(); closed(); }
            base.Dispose(disposing);
        }
    }
}
internal sealed class TestArchiveTarget : IGameArchiveExtractionTarget
{
    private readonly Dictionary<int, MemoryStream> active = [];
    private readonly Dictionary<int, Stream> supplied = [];
    public Dictionary<int, byte[]> Outputs { get; } = [];
    public List<int> Prepared { get; } = [];
    public List<(int Index, GameCopyError? Error)> Completed { get; } = [];
    public Func<GameArchiveEntry, CancellationToken, ValueTask<ArchiveEntryTarget>>? Prepare { get; set; }
    public async ValueTask<ArchiveEntryTarget> PrepareEntryAsync(GameArchiveEntry entry, CancellationToken cancellationToken)
    {
        Prepared.Add(entry.Index);
        if (Prepare is not null)
        {
            var prepared = await Prepare(entry, cancellationToken);
            if (prepared.Output is not null) supplied.Add(entry.Index, prepared.Output);
            return prepared;
        }
        if (entry.IsDirectory) return new(null, GameCopySkipReason.OverwriteDeclined);
        var stream = new MemoryStream(); active.Add(entry.Index, stream);
        return new(stream, null);
    }
    public async ValueTask CompleteEntryAsync(int entryIndex, GameCopyError? error)
    {
        Completed.Add((entryIndex, error));
        if (active.Remove(entryIndex, out var stream)) { Outputs.Add(entryIndex, stream.ToArray()); stream.Dispose(); }
        if (supplied.Remove(entryIndex, out var owned)) await owned.DisposeAsync();
    }
}

public sealed class ArchiveAdapterTheoryAttribute : TheoryAttribute
{
    public ArchiveAdapterTheoryAttribute()
    {
        if (!File.Exists(Path.Combine(GameArchiveReaderTests.RuntimeRoot, "runtimes", ArchiveRuntimeResolver.CurrentRuntimeIdentifier, "native", "runtime.json"))
            && Environment.GetEnvironmentVariable("PSPMAN_REQUIRE_NATIVE_TESTS") != "1")
            Skip = "T03 native runtime package not staged; mandatory native gate remains pending.";
    }
}
public sealed class ArchiveAdapterFactAttribute : FactAttribute
{
    public ArchiveAdapterFactAttribute()
    {
        if (!File.Exists(Path.Combine(GameArchiveReaderTests.RuntimeRoot, "runtimes", ArchiveRuntimeResolver.CurrentRuntimeIdentifier, "native", "runtime.json"))
            && Environment.GetEnvironmentVariable("PSPMAN_REQUIRE_NATIVE_TESTS") != "1")
            Skip = "T03 native runtime package not staged; mandatory native gate remains pending.";
    }
}
