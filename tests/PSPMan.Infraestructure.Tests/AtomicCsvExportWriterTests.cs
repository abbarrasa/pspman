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

        Assert.False(writer.Capture(destination).Exists);

        writer.Publish(destination, bytes, writer.Capture(destination), overwriteApproved: false);

        Assert.True(writer.Capture(destination).Exists);
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

        Assert.True(writer.Capture(destination).Exists);

        Assert.Throws<IOException>(() => { writer.Publish(destination, new byte[] { 4, 5, 6 }, writer.Capture(destination), overwriteApproved: false); });

        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void FailedStagingWriteReportsErrorAndRemovesPartialFile()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "games.csv");
        var failure = new IOException("Simulated interrupted write.");
        IGameCatalogExportWriter writer = new AtomicCsvExportWriter((stagingPath, _) =>
        {
            File.WriteAllBytes(stagingPath, [1, 2]);
            throw failure;
        });

        var reported = Assert.Throws<IOException>(() => writer.Publish(destination, new byte[] { 1, 2, 3 }, writer.Capture(destination), overwriteApproved: false));

        Assert.Same(failure, reported);
        Assert.False(writer.Capture(destination).Exists);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void FailedPublicationPreservesDestinationCreatedDuringStagingAndRemovesStagingFile()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "games.csv");
        byte[] original = [9, 8, 7];
        IGameCatalogExportWriter writer = new AtomicCsvExportWriter((stagingPath, content) =>
        {
            File.WriteAllBytes(stagingPath, content.ToArray());
            File.WriteAllBytes(destination, original);
        });

        Assert.Throws<IOException>(() => writer.Publish(destination, new byte[] { 1, 2, 3 }, writer.Capture(destination), overwriteApproved: false));

        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void ApprovedReplacementPublishesCompleteBytesAndRemovesStagingFile()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "games.csv");
        File.WriteAllBytes(destination, [1, 2, 3]);
        byte[] replacement = [4, 5, 6, 7];
        IGameCatalogExportWriter writer = new AtomicCsvExportWriter();

        writer.Publish(destination, replacement, writer.Capture(destination), overwriteApproved: true);

        Assert.Equal(replacement, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void OverwriteApprovalAlsoAllowsPublishingToANewDestination()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "games.csv");
        byte[] content = [4, 5, 6];
        IGameCatalogExportWriter writer = new AtomicCsvExportWriter();

        writer.Publish(destination, content, writer.Capture(destination), overwriteApproved: true);

        Assert.Equal(content, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void ApprovedReplacementPreservesOriginalWhenStagingWriteFails()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "games.csv");
        byte[] original = [1, 2, 3];
        File.WriteAllBytes(destination, original);
        var failure = new IOException("Simulated staging failure.");
        IGameCatalogExportWriter writer = new AtomicCsvExportWriter((stagingPath, _) =>
        {
            File.WriteAllBytes(stagingPath, [4]);
            throw failure;
        });

        var reported = Assert.Throws<IOException>(() => writer.Publish(destination, new byte[] { 4, 5, 6 }, writer.Capture(destination), overwriteApproved: true));

        Assert.Same(failure, reported);
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void ApprovedReplacementPreservesOriginalWhenPublicationFails()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "games.csv");
        byte[] original = [1, 2, 3];
        byte[] replacement = [4, 5, 6];
        File.WriteAllBytes(destination, original);
        var failure = new IOException("Simulated replacement failure.");
        IGameCatalogExportWriter writer = new AtomicCsvExportWriter(
            (stagingPath, content) => File.WriteAllBytes(stagingPath, content.ToArray()),
            (stagingPath, targetPath) =>
            {
                Assert.Equal(replacement, File.ReadAllBytes(stagingPath));
                Assert.Equal(original, File.ReadAllBytes(targetPath));
                throw failure;
            });

        var reported = Assert.Throws<IOException>(() => writer.Publish(destination, replacement, writer.Capture(destination), overwriteApproved: true));

        Assert.Same(failure, reported);
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void ApprovedExportRejectsDestinationCreatedAfterInspection()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "games.csv");
        IGameCatalogExportWriter writer = new AtomicCsvExportWriter();
        var inspected = writer.Capture(destination);
        byte[] otherContent = [9, 8, 7];
        File.WriteAllBytes(destination, otherContent);

        Assert.Throws<IOException>(() => writer.Publish(destination, new byte[] { 1, 2, 3 }, inspected, overwriteApproved: true));

        Assert.Equal(otherContent, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void ApprovedReplacementRejectsSameLengthChangeDuringStaging()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "games.csv");
        File.WriteAllBytes(destination, [1, 2, 3]);
        byte[] otherContent = [9, 8, 7];
        IGameCatalogExportWriter writer = new AtomicCsvExportWriter((stagingPath, content) =>
        {
            File.WriteAllBytes(stagingPath, content.ToArray());
            File.WriteAllBytes(destination, otherContent);
        });
        var inspected = writer.Capture(destination);

        Assert.Throws<IOException>(() => writer.Publish(destination, new byte[] { 4, 5, 6 }, inspected, overwriteApproved: true));

        Assert.Equal(otherContent, File.ReadAllBytes(destination));
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
