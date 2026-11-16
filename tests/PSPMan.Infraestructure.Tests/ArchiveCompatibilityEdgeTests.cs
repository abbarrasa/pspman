using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;
using ArchiveError = SharpCompress.Common.InvalidFormatException;
using PasswordError = SharpCompress.Common.CryptographicException;

namespace PSPMan.Infraestructure.Tests;

public sealed class ArchiveCompatibilityEdgeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Zip.deflate.WinzipAES.zip", typeof(ArchiveError))]
    [InlineData("Zip.deflate.pkware.zip", typeof(PasswordError))]
    [InlineData("Rar.encrypted_filesOnly.rar", typeof(SharpCompress.Common.ArchiveOperationException))]
    [InlineData("Rar.encrypted_filesAndHeader.rar", typeof(ArchiveError))]
    [InlineData("Rar5.encrypted_filesOnly.rar", typeof(PasswordError))]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", typeof(PasswordError))]
    public async Task PasswordFailureShapesAndRetryAreCharacterizedWithoutAssumingOneErrorType(string name, Type expectedError)
    {
        var error = await Record.ExceptionAsync(() => ArchiveCompatibilityProbe.ReadAsync(Fixture(name), "incorrect"));
        Assert.NotNull(error);
        output.WriteLine($"{name}: {error.GetType().FullName}: {error.Message}");
        // These observations do not establish reliable wrong-password/corruption classification.
        Assert.IsType(expectedError, error);
        var result = await ArchiveCompatibilityProbe.ReadAsync(Fixture(name),
            name == "Zip.deflate.pkware.zip" ? "12345678" : "test");
        Assert.NotEmpty(result);
    }

    [Theory]
    [InlineData("Rar5.multi.part01.rar", "Rar5.multi.part02.rar")]
    [InlineData("Rar2.multi.rar", "Rar2.multi.r00")]
    [InlineData("WinZip26.nocomp.multi.zip", "WinZip26.nocomp.multi.z01")]
    public async Task MissingPartIsNotSilentlyAccepted(string first, string missing)
    {
        using var area = new ProbeDirectory();
        var stem = first.Split('.')[0];
        foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(Fixture(first))!, stem + "*"))
        {
            if (Path.GetFileName(path) != missing)
                File.Copy(path, Path.Combine(area.Path, Path.GetFileName(path)));
        }
        var error = await Record.ExceptionAsync(() => ArchiveCompatibilityProbe.ReadAsync(Path.Combine(area.Path, first), null));
        Assert.NotNull(error);
        Assert.False(error is PasswordError);
        Assert.True(error is SharpCompress.Common.SharpCompressException or IOException,
            $"Expected a library/I/O failure, received {error.GetType().FullName}.");
        output.WriteLine($"{first}, missing {missing}: {error.GetType().FullName}: {error.Message}");
    }

    [Fact]
    public async Task EmptyZipContainsNoPayload()
    {
        using var area = new ProbeDirectory();
        var path = Path.Combine(area.Path, "empty.zip");
        using (ZipFile.Open(path, ZipArchiveMode.Create)) { }
        Assert.Empty(await ArchiveCompatibilityProbe.ReadAsync(path, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlyAndLateCorruptionAreNotMistakenForPasswordErrors(bool corruptLast)
    {
        using var area = new ProbeDirectory();
        var path = Path.Combine(area.Path, "corrupt.zip");
        CreateStoredZip(path);
        var bytes = await File.ReadAllBytesAsync(path);
        var marker = Encoding.UTF8.GetBytes(corruptLast ? "second-unique-payload" : "first-unique-payload");
        var offset = bytes.AsSpan().IndexOf(marker);
        Assert.True(offset >= 0);
        bytes[offset] ^= 0x10;
        await File.WriteAllBytesAsync(path, bytes);

        var error = await Record.ExceptionAsync(() => ArchiveCompatibilityProbe.ReadAsync(path, null));
        Assert.NotNull(error);
        output.WriteLine($"CorruptLast={corruptLast}: {error.GetType().FullName}: {error.Message}");
        Assert.IsType<ArchiveError>(error);
    }

    [Theory]
    [InlineData("Zip.deflate.zip", null)]
    [InlineData("Rar5.solid.rar", null)]
    [InlineData("Rar5.multi.part01.rar", null)]
    public async Task CancellationDuringPayloadReadClosesTheArchiveAndAllowsRetry(string name, string? password)
    {
        using var area = new ProbeDirectory();
        foreach (var source in Directory.EnumerateFiles(Path.GetDirectoryName(Fixture(name))!, name.Split('.')[0] + "*"))
            File.Copy(source, Path.Combine(area.Path, Path.GetFileName(source)));
        var path = Path.Combine(area.Path, name);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveCompatibilityProbe.ReadAsync(path, password,
            cancellation.Token, onBytes: _ => cancellation.Cancel()));
        foreach (var part in Directory.EnumerateFiles(area.Path))
        {
            using var exclusive = File.Open(part, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        Assert.NotEmpty(await ArchiveCompatibilityProbe.ReadAsync(path, password));
    }

    [Fact]
    public async Task EncryptedLzmaZipIsAnExplicitLibraryLimitation()
    {
        // Characterization, not acceptance: this fixture cannot currently be copied by the proposed backend.
        var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            ArchiveCompatibilityProbe.ReadAsync(Fixture("Zip.lzma.WinzipAES.zip"), "test"));
        Assert.Contains("LZMA", error.Message);
    }

    [Fact]
    public void EncryptedMultipartRarSynchronousAlternativeAlsoFails()
    {
        var error = Assert.Throws<ArchiveError>(() =>
            ArchiveCompatibilityProbe.ReadRarSynchronously(Fixture("Rar.EncryptedParts.part01.rar"), "test"));
        Assert.Contains("encrypted stream", error.Message);
    }

    [Fact]
    public async Task EncryptedMultipartRarAsyncFailureBlocksTheCompatibilityGate()
    {
        // All six source parts are present and hash-checked; no production fallback is inferred.
        var error = await Assert.ThrowsAsync<ArchiveError>(() =>
            ArchiveCompatibilityProbe.ReadAsync(Fixture("Rar.EncryptedParts.part01.rar"), "test"));
        Assert.Contains("encrypted stream", error.Message);
    }

    [Theory]
    [InlineData("Zip.deflate.zip", null)]
    [InlineData("Zip.deflate.WinzipAES.zip", "test")]
    [InlineData("WinZip26.nocomp.multi.zip", null)]
    [InlineData("Rar4.rar", null)]
    [InlineData("Rar5.solid.rar", null)]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar5.multi.part01.rar", null)]
    public void ActualSizeIsAvailableFromArchiveMetadataBeforeCopying(string name, string? password)
    {
        var options = SharpCompress.Readers.ReaderOptions.ForFilePath with { Password = password };
        using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(Fixture(name), options);
        Assert.Equal(100926, archive.Entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.Size));
    }

    [Fact]
    public async Task FixtureManifestMatchesUnmodifiedSourceBytes()
    {
        var root = Path.GetDirectoryName(Fixture("Zip.deflate.zip"))!;
        foreach (var line in await File.ReadAllLinesAsync(Path.Combine(root, "SHA256SUMS")))
        {
            var fields = line.Split("  ", 2, StringSplitOptions.None);
            await using var source = File.OpenRead(Path.Combine(root, fields[1]));
            Assert.Equal(fields[0], Convert.ToHexStringLower(await SHA256.HashDataAsync(source)));
        }
    }

    internal static void CreateStoredZip(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var index = 0;
        foreach (var value in new[] { "first-unique-payload", "second-unique-payload" })
        {
            using var writer = new StreamWriter(archive.CreateEntry($"entry{++index}.txt", CompressionLevel.NoCompression).Open());
            writer.Write(value);
        }
    }

    private static string Fixture(string name) => ArchiveCompatibilityTests.Fixture(name);
}

internal sealed class ProbeDirectory : IDisposable
{
    internal string Path { get; } = Directory.CreateTempSubdirectory("pspman-archive-probe-").FullName;
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
