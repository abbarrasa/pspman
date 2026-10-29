using PSPMan.Core.Games;

namespace PSPMan.Infraestructure.Games;

public sealed class AtomicCsvExportWriter : IGameCatalogExportWriter
{
    private readonly Action<string, ReadOnlyMemory<byte>> _writeStagingFile;

    public AtomicCsvExportWriter() : this(WriteStagingFile)
    {
    }

    internal AtomicCsvExportWriter(Action<string, ReadOnlyMemory<byte>> writeStagingFile)
    {
        _writeStagingFile = writeStagingFile ?? throw new ArgumentNullException(nameof(writeStagingFile));
    }

    public bool Exists(string destinationPath)
    {
        return File.Exists(destinationPath);
    }

    public void PublishNew(string destinationPath, ReadOnlyMemory<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (File.Exists(destinationPath))
        {
            throw new IOException("The export destination already exists.");
        }

        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("The export destination must include a directory.", nameof(destinationPath));
        var stagingPath = Path.Combine(directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            _writeStagingFile(stagingPath, content);
            File.Move(stagingPath, destinationPath);
        }
        finally
        {
            File.Delete(stagingPath);
        }
    }

    private static void WriteStagingFile(string stagingPath, ReadOnlyMemory<byte> content)
    {
        using var stream = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content.Span);
        stream.Flush(flushToDisk: true);
    }
}
