using System.Text;
using System.Security.Cryptography;
using PSPMan.Core.Games;
using PSPMan.Infraestructure.Games;

namespace PSPMan.Infraestructure.Tests;

public class FileGameCatalogImportSourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FingerprintCoversAllBytesIncludingBomWithoutMovingTheDecodedReader(bool bom)
    {
        using var data = new TemporarySource();
        var text = new string('a', 4095) + "🎮é\r\nsecond";
        byte[] bytes = bom ? [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);
        File.WriteAllBytes(data.FilePath, bytes);
        using var reader = new FileGameCatalogImportSource().Open(data.FilePath);
        var prefix = new char[3];
        Assert.Equal(3, reader.Read(prefix));

        var fingerprint = reader.CaptureFingerprint();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), fingerprint.Sha256);
        reader.VerifyUnchanged(fingerprint);
        Assert.Equal(text[3..], ReadAll(reader, 1));
        Assert.Equal(fingerprint, reader.CaptureFingerprint());
        reader.VerifyUnchanged(fingerprint);
        Assert.Equal(0, reader.Read(new char[1]));
    }

    [Fact]
    public void EmptyFileHasTheStandardEmptyContentHash()
    {
        using var data = new TemporarySource();
        File.WriteAllBytes(data.FilePath, []);
        using var reader = new FileGameCatalogImportSource().Open(data.FilePath);

        var fingerprint = reader.CaptureFingerprint();

        Assert.Equal(Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())), fingerprint.Sha256);
        reader.VerifyUnchanged(fingerprint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetectsChangedContentEvenWithTheSameLengthAndTimestamp(bool keepTimestamp)
    {
        using var data = new TemporarySource();
        File.WriteAllText(data.FilePath, "first", new UTF8Encoding(false));
        var timestamp = File.GetLastWriteTimeUtc(data.FilePath);
        GameCatalogImportFingerprint fingerprint;
        using (var original = new FileGameCatalogImportSource().Open(data.FilePath))
        {
            fingerprint = original.CaptureFingerprint();
        }
        File.WriteAllText(data.FilePath, "other", new UTF8Encoding(false));
        if (keepTimestamp)
        {
            File.SetLastWriteTimeUtc(data.FilePath, timestamp);
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(data.FilePath));
        }
        using var reader = new FileGameCatalogImportSource().Open(data.FilePath);

        Assert.Throws<GameCatalogImportSourceChangedException>(() => reader.VerifyUnchanged(fingerprint));
    }

    [Fact]
    public void DetectsChangedBytesInTheHeldReadHandle()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("first");
        var source = new FileGameCatalogImportSource(_ => new MemoryStream(bytes, writable: false));
        using var reader = source.Open("virtual.csv");
        var fingerprint = reader.CaptureFingerprint();
        bytes[0] = (byte)'F';

        Assert.Throws<GameCatalogImportSourceChangedException>(() => reader.VerifyUnchanged(fingerprint));
    }

    [Fact]
    public void ChecksTheCurrentPathEvenWhenTheHeldHandleStillHasOriginalBytes()
    {
        var opens = 0;
        var source = new FileGameCatalogImportSource(_ => new MemoryStream(
            Encoding.UTF8.GetBytes(++opens == 1 ? "first" : "other")));
        using var reader = source.Open("virtual.csv");
        var fingerprint = reader.CaptureFingerprint();

        Assert.Throws<GameCatalogImportSourceChangedException>(() => reader.VerifyUnchanged(fingerprint));
        Assert.Equal(2, opens);
        Assert.Equal("first", ReadAll(reader, 1));
    }

    [Fact]
    public void APathThatDisappearsDuringVerificationIsAReadFailureRatherThanAContentMismatch()
    {
        var opens = 0;
        var missing = new FileNotFoundException("Simulated removed source.");
        var source = new FileGameCatalogImportSource(_ => ++opens == 1
            ? new MemoryStream(Encoding.UTF8.GetBytes("first")) : throw missing);
        using var reader = source.Open("virtual.csv");
        var fingerprint = reader.CaptureFingerprint();

        var error = Assert.Throws<GameCatalogImportReadException>(() => reader.VerifyUnchanged(fingerprint));

        Assert.Same(missing, error.InnerException);
    }

    [Fact]
    public void CanInjectAReadFailureAtAnExactByteOffsetAfterSuccessfulReads()
    {
        using var stream = new FaultingReadStream(Encoding.UTF8.GetBytes(new string('a', 16384)));
        var source = new FileGameCatalogImportSource(_ => stream);
        using var reader = source.Open("virtual.csv");
        reader.CaptureFingerprint();
        stream.FailureOffset = 8192;
        var readCount = 0;
        var buffer = new char[1];

        var error = Assert.Throws<GameCatalogImportReadException>(() =>
        {
            int count;
            while ((count = reader.Read(buffer)) != 0)
            {
                readCount += count;
            }
        });

        Assert.Equal(8192, readCount);
        Assert.IsType<IOException>(error.InnerException);
    }

    [Fact]
    public void FingerprintFailureRestoresTheHeldStreamPositionAndReportsTheReadError()
    {
        using var stream = new FaultingReadStream(Encoding.UTF8.GetBytes(new string('a', 16384)));
        using var reader = new FileGameCatalogImportSource(_ => stream).Open("virtual.csv");
        Assert.Equal(1, reader.Read(new char[1]));
        var position = stream.Position;
        stream.FailureOffset = 8192;

        var error = Assert.Throws<GameCatalogImportReadException>(() => reader.CaptureFingerprint());

        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(position, stream.Position);
    }

    [Fact]
    public void DisposedReaderCannotCaptureOrVerifyContent()
    {
        using var data = new TemporarySource();
        File.WriteAllText(data.FilePath, "content");
        var reader = new FileGameCatalogImportSource().Open(data.FilePath);
        var fingerprint = reader.CaptureFingerprint();
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reader.CaptureFingerprint());
        Assert.Throws<ObjectDisposedException>(() => reader.VerifyUnchanged(fingerprint));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(true, 7)]
    [InlineData(false, 1)]
    [InlineData(false, 7)]
    public void ReadsAnExportIncrementallyWithOptionalBomWithoutChangingTheFile(bool bom, int chunkSize)
    {
        using var data = new TemporarySource();
        var game = new GameRecord(Guid.NewGuid(), "日本語 🎮; title", GameMedium.Usb, null,
            "/GAMES/game.rar", GameFormat.Chd, 1024, "First; \"quoted\"\r\nSecond");
        var exported = GameCatalogCsvSerializer.SerializeCatalog([game]);
        var bytes = bom ? exported : exported[3..];
        File.WriteAllBytes(data.FilePath, bytes);
        IGameCatalogImportSource source = new FileGameCatalogImportSource();

        using (var reader = source.Open(data.FilePath))
        {
            Assert.Equal(Encoding.UTF8.GetString(exported[3..]), ReadAll(reader, chunkSize));
        }

        Assert.Equal(bytes, File.ReadAllBytes(data.FilePath));
        Assert.Equal([data.FilePath], Directory.GetFiles(data.DirectoryPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyOrBomOnlySourceEndsWithoutProducingCharacters(bool bom)
    {
        using var data = new TemporarySource();
        File.WriteAllBytes(data.FilePath, bom ? [0xEF, 0xBB, 0xBF] : []);
        using var reader = new FileGameCatalogImportSource().Open(data.FilePath);

        Assert.Equal(0, reader.Read(new char[1]));
        Assert.Equal(0, reader.Read(new char[1]));
    }

    [Fact]
    public void PreservesUnicodeAcrossByteAndCharacterBufferBoundariesAndOnlyStripsTheInitialBom()
    {
        using var data = new TemporarySource();
        var text = new string('a', 4095) + "é日本語🎮\uFEFF" + new string('b', 4093) + "🎮é";
        File.WriteAllBytes(data.FilePath, Encoding.UTF8.GetBytes(text));
        using var reader = new FileGameCatalogImportSource().Open(data.FilePath);

        Assert.Equal(text, ReadAll(reader, 1));
    }

    [Theory]
    [InlineData("FF")]
    [InlineData("C328")]
    [InlineData("E282")]
    [InlineData("EFBB")]
    [InlineData("EDA080")]
    [InlineData("F4908080")]
    [InlineData("FFFE4100")]
    [InlineData("FEFF0041")]
    public void RejectsInvalidUtf8AndDoesNotAutoDetectOtherEncodings(string hex)
    {
        using var data = new TemporarySource();
        var bytes = Convert.FromHexString(hex);
        File.WriteAllBytes(data.FilePath, bytes);

        var error = Assert.Throws<GameCatalogImportReadException>(() =>
        {
            using var reader = new FileGameCatalogImportSource().Open(data.FilePath);
            ReadAll(reader, 2);
        });

        Assert.IsType<DecoderFallbackException>(error.InnerException);
        Assert.Contains("UTF-8", error.Message);
        Assert.Equal(bytes, File.ReadAllBytes(data.FilePath));
    }

    [Fact]
    public void ReportsInvalidUtf8EncounteredAfterSuccessfulReads()
    {
        using var data = new TemporarySource();
        File.WriteAllBytes(data.FilePath, [.. Encoding.UTF8.GetBytes(new string('a', 16384)), 0xFF]);
        using var reader = new FileGameCatalogImportSource().Open(data.FilePath);
        var first = new char[1];
        Assert.Equal(1, reader.Read(first));
        Assert.Equal('a', first[0]);

        var error = Assert.Throws<GameCatalogImportReadException>(() => ReadAll(reader, 13));

        Assert.IsType<DecoderFallbackException>(error.InnerException);
    }

    [Fact]
    public void MissingSourceReportsAnOpenFailureWithoutCreatingAFile()
    {
        using var data = new TemporarySource();

        var error = Assert.Throws<GameCatalogImportReadException>(() => new FileGameCatalogImportSource().Open(data.FilePath));

        Assert.IsType<FileNotFoundException>(error.InnerException);
        Assert.Empty(Directory.GetFiles(data.DirectoryPath));
    }

    [Fact]
    public void DirectoryCannotBeUsedAsASource()
    {
        using var data = new TemporarySource();

        var error = Assert.Throws<GameCatalogImportReadException>(() => new FileGameCatalogImportSource().Open(data.DirectoryPath));

        Assert.NotNull(error.InnerException);
        Assert.Empty(Directory.GetFiles(data.DirectoryPath));
    }

    [Fact]
    public void DisposingTheReaderReleasesTheFileAndPreventsFurtherReads()
    {
        using var data = new TemporarySource();
        File.WriteAllBytes(data.FilePath, Encoding.UTF8.GetBytes("Title;Medium"));
        var reader = new FileGameCatalogImportSource().Open(data.FilePath);
        reader.Read(new char[1]);
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reader.Read(new char[1]));
        using var exclusive = new FileStream(data.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanWrite);
    }

    private static string ReadAll(IGameCatalogImportReader reader, int chunkSize)
    {
        var buffer = new char[chunkSize];
        var text = new StringBuilder();
        int count;
        while ((count = reader.Read(buffer)) != 0)
        {
            Assert.InRange(count, 1, buffer.Length);
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    private sealed class FaultingReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes, writable: false);
        public long? FailureOffset { get; set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (FailureOffset is long failure)
            {
                if (Position >= failure)
                {
                    throw new IOException("Simulated interrupted source read.");
                }
                buffer = buffer[..(int)Math.Min(buffer.Length, failure - Position)];
            }
            return inner.Read(buffer);
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class TemporarySource : IDisposable
    {
        public TemporarySource()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "PSPMan.CsvImport.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }
        public string FilePath => Path.Combine(DirectoryPath, "games.csv");
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
