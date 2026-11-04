using System.Text;
using PSPMan.Core.Games;
using PSPMan.Infraestructure.Games;

namespace PSPMan.Infraestructure.Tests;

public class FileGameCatalogImportSourceTests
{
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
