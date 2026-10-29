using System.Security.Cryptography;
using PSPMan.Core.Games;

namespace PSPMan.Infraestructure.Games;

public sealed class AtomicCsvExportWriter : IGameCatalogExportWriter
{
    private readonly Action<string, ReadOnlyMemory<byte>> _writeStagingFile;
    private readonly Action<string, string> _replaceFile;

    public AtomicCsvExportWriter() : this(WriteStagingFile, ReplaceFile)
    {
    }

    internal AtomicCsvExportWriter(Action<string, ReadOnlyMemory<byte>> writeStagingFile)
        : this(writeStagingFile, ReplaceFile)
    {
    }

    internal AtomicCsvExportWriter(
        Action<string, ReadOnlyMemory<byte>> writeStagingFile,
        Action<string, string> replaceFile)
    {
        _writeStagingFile = writeStagingFile ?? throw new ArgumentNullException(nameof(writeStagingFile));
        _replaceFile = replaceFile ?? throw new ArgumentNullException(nameof(replaceFile));
    }

    public ExportDestinationSnapshot Capture(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!File.Exists(destinationPath))
        {
            return new ExportDestinationSnapshot(destinationPath, null);
        }

        using var stream = new FileStream(destinationPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return new ExportDestinationSnapshot(destinationPath,
            Convert.ToHexString(SHA256.HashData(stream)));
    }

    public void Publish(string destinationPath, ReadOnlyMemory<byte> content,
        ExportDestinationSnapshot inspectedDestination, bool overwriteApproved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(inspectedDestination);

        if (inspectedDestination.Exists && !overwriteApproved)
        {
            throw new IOException("The export destination already exists.");
        }

        EnsureDestinationUnchanged(destinationPath, inspectedDestination);

        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("The export destination must include a directory.", nameof(destinationPath));
        var stagingPath = Path.Combine(directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            _writeStagingFile(stagingPath, content);
            EnsureDestinationUnchanged(destinationPath, inspectedDestination);
            if (inspectedDestination.Exists)
            {
                _replaceFile(stagingPath, destinationPath);
            }
            else
            {
                File.Move(stagingPath, destinationPath);
            }
        }
        finally
        {
            File.Delete(stagingPath);
        }
    }

    private void EnsureDestinationUnchanged(string destinationPath,
        ExportDestinationSnapshot inspectedDestination)
    {
        if (Capture(destinationPath) != inspectedDestination)
        {
            throw new IOException("The export destination changed after it was inspected.");
        }
    }

    private static void WriteStagingFile(string stagingPath, ReadOnlyMemory<byte> content)
    {
        using var stream = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content.Span);
        stream.Flush(flushToDisk: true);
    }

    private static void ReplaceFile(string stagingPath, string destinationPath)
    {
        File.Replace(stagingPath, destinationPath, destinationBackupFileName: null);
    }
}
