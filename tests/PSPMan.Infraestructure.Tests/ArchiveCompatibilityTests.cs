using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Readers.Rar;

namespace PSPMan.Infraestructure.Tests;

/// <summary>Test-local feasibility gate; not the production transfer adapter.</summary>
public sealed class ArchiveCompatibilityTests
{
    [Theory]
    [InlineData("Zip.deflate.zip", null)]
    [InlineData("Rar4.rar", null)]
    [InlineData("Rar5.rar", null)]
    [InlineData("Rar5.solid.rar", null)]
    [InlineData("Zip.zip64.zip", null)]
    [InlineData("Zip.deflate.WinzipAES.zip", "test")]
    [InlineData("Rar.encrypted_filesOnly.rar", "test")]
    [InlineData("Rar.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar5.encrypted_filesOnly.rar", "test")]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar5.multi.part01.rar", null)]
    [InlineData("Rar5.multi.part03.rar", null)]
    [InlineData("Rar2.multi.rar", null)]
    [InlineData("Rar2.multi.r02", null)]
    [InlineData("WinZip26.nocomp.multi.zip", null)]
    [InlineData("WinZip26.nocomp.multi.z01", null)]
    public async Task ExtractedPayloadMatchesIndependentOriginals(string name, string? password)
    {
        var result = await ArchiveCompatibilityProbe.ReadAsync(Fixture(name), password);

        Assert.Equal(100926, result.Sum(entry => entry.Size));
        Assert.Contains(result, entry => entry.Path == "exe/test.exe" && entry.Size == 45056
            && entry.Sha256 == "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106");
        Assert.Contains(result, entry => entry.Path == "jpg/test.jpg" && entry.Size == 40372
            && entry.Sha256 == "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53");
        // Upstream archives contain CRLF text; the Git source blob uses LF.
        Assert.Contains(result, entry => entry.Path == "тест.txt" && entry.Size == 15498
            && entry.Sha256 == "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9");
    }

    internal static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers", name);
}

internal sealed record ArchiveProbeEntry(string Path, long Size, string Sha256);

internal static class ArchiveCompatibilityProbe
{
    internal static async Task<IReadOnlyList<ArchiveProbeEntry>> ReadAsync(string path, string? password,
        CancellationToken cancellationToken = default, Action<long>? onBytes = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = Regex.Replace(path, @"\.z\d+$", ".zip", RegexOptions.IgnoreCase);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // Known encoding of this upstream corpus only; not a production encoding policy.
        var options = ReaderOptions.ForFilePath with
        {
            Password = password,
            ArchiveEncoding = new ArchiveEncoding { Default = Encoding.GetEncoding(866) }
        };
        using IArchive archive = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? ZipArchive.OpenArchive(path, options) : RarArchive.OpenArchive(path, options);
        var result = new List<ArchiveProbeEntry>();
        if (archive.Type == ArchiveType.Zip)
        {
            foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                using var sink = new ProbeHashStream(cancellationToken, onBytes);
                await entry.WriteToAsync(sink, new ExtractionOptions { CheckCrc = true, BufferSize = 8192 },
                    cancellationToken: cancellationToken);
                Assert.Equal(entry.Size, sink.Length);
                result.Add(sink.Complete(entry.Key!));
            }
        }
        else
        {
            using var sources = new ProbeSourceParts(FindRarParts(path));
            await using var reader = (IAsyncReader)RarReader.OpenReader(sources.Streams,
                options with { LeaveStreamOpen = true });
            while (await reader.MoveToNextEntryAsync(cancellationToken))
            {
                if (reader.Entry.IsDirectory) continue;
                await using var stream = await reader.OpenEntryStreamAsync(cancellationToken);
                result.Add(await ReadEntryAsync(reader.Entry.Key!, reader.Entry.Size, stream, cancellationToken, onBytes));
            }
        }
        return result;
    }

    internal static IReadOnlyList<ArchiveProbeEntry> ReadRarSynchronously(string path, string password)
    {
        using var sources = new ProbeSourceParts(FindRarParts(path));
        using var reader = RarReader.OpenReader(sources.Streams,
            ReaderOptions.ForExternalStream with { Password = password });
        var result = new List<ArchiveProbeEntry>();
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;
            using var stream = reader.OpenEntryStream();
            using var sink = new ProbeHashStream(CancellationToken.None, null);
            stream.CopyTo(sink, 8192);
            Assert.Equal(reader.Entry.Size, sink.Length);
            result.Add(sink.Complete(reader.Entry.Key!));
        }
        return result;
    }

    internal static IReadOnlyList<string> FindRarParts(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        var part = Regex.Match(name, @"^(.*\.part)(\d+)(\.rar)$", RegexOptions.IgnoreCase);
        if (part.Success)
        {
            return Directory.EnumerateFiles(directory).Where(file =>
                    Regex.IsMatch(Path.GetFileName(file), "^" + Regex.Escape(part.Groups[1].Value) + @"\d+\.rar$", RegexOptions.IgnoreCase))
                .OrderBy(file => int.Parse(Regex.Match(Path.GetFileName(file), @"\.part(\d+)\.", RegexOptions.IgnoreCase).Groups[1].Value))
                .ToArray();
        }
        var initial = Regex.Replace(path, @"\.r\d+$", ".rar", RegexOptions.IgnoreCase);
        var stem = Path.GetFileNameWithoutExtension(initial);
        return new[] { initial }.Concat(Directory.EnumerateFiles(directory)
            .Where(file => Regex.IsMatch(Path.GetFileName(file), "^" + Regex.Escape(stem) + @"\.r\d+$", RegexOptions.IgnoreCase))
            .OrderBy(file => int.Parse(Path.GetExtension(file)[2..]))).ToArray();
    }

    private static async Task<ArchiveProbeEntry> ReadEntryAsync(string path, long expectedSize, Stream stream,
        CancellationToken cancellationToken, Action<long>? onBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[8192];
        long size = 0;
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, count);
            size = checked(size + count);
            onBytes?.Invoke(size);
        }
        Assert.Equal(expectedSize, size);
        return new(path.Replace('\\', '/'), size, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}

internal sealed class ProbeSourceParts : IDisposable
{
    private readonly List<Stream> _streams = [];
    internal IEnumerable<Stream> Streams => _streams;
    internal ProbeSourceParts(IEnumerable<string> paths)
    {
        try
        {
            foreach (var path in paths) _streams.Add(File.OpenRead(path));
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    public void Dispose()
    {
        foreach (var stream in _streams) stream.Dispose();
    }
}

/// <summary>Bounded discard sink: hashes bytes without retaining or executing fixture contents.</summary>
internal sealed class ProbeHashStream(CancellationToken token, Action<long>? onBytes) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _length;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _length;
    public override long Position { get => _length; set => throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        token.ThrowIfCancellationRequested();
        _hash.AppendData(buffer);
        _length = checked(_length + buffer.Length);
        onBytes?.Invoke(_length);
        token.ThrowIfCancellationRequested();
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }
    internal ArchiveProbeEntry Complete(string path) =>
        new(path.Replace('\\', '/'), Length, Convert.ToHexStringLower(_hash.GetHashAndReset()));
    protected override void Dispose(bool disposing)
    {
        if (disposing) _hash.Dispose();
        base.Dispose(disposing);
    }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
