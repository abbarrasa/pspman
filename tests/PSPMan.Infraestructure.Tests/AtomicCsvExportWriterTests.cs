using PSPMan.Core.Games;
using PSPMan.Infraestructure.Games;

namespace PSPMan.Infraestructure.Tests;

public class AtomicCsvExportWriterTests
{
    [Fact]
    public void PublishesCompleteBytesToNewDestinationWithoutLeavingStagingFiles()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "games.csv");
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'A', (byte)';', (byte)'B', 0x0D, 0x0A];
        IGameCatalogExportWriter writer = new AtomicCsvExportWriter();

        Assert.False(writer.Exists(destination));

        writer.PublishNew(destination, bytes);

        Assert.True(writer.Exists(destination));
        Assert.Equal(bytes, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void NewFilePublicationDoesNotOverwriteAnExistingDestination()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "games.csv");
        byte[] original = [1, 2, 3];
        File.WriteAllBytes(destination, original);
        IGameCatalogExportWriter writer = new AtomicCsvExportWriter();

        Assert.True(writer.Exists(destination));

        Assert.Throws<IOException>(() => { writer.PublishNew(destination, new byte[] { 4, 5, 6 }); });

        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "PSPMan.CsvExport.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
