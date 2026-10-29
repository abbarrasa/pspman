using PSPMan.Core.Games;

namespace PSPMan.Infraestructure.Games;

public sealed class AtomicCsvExportWriter : IGameCatalogExportWriter
{
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
            using (var stream = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content.Span);
                stream.Flush(flushToDisk: true);
            }

            File.Move(stagingPath, destinationPath);
        }
        finally
        {
            File.Delete(stagingPath);
        }
    }
}
