namespace PSPMan.Core.Games;

public interface IGameCatalogExportWriter
{
    ExportDestinationSnapshot Capture(string destinationPath);

    void Publish(string destinationPath, ReadOnlyMemory<byte> content,
        ExportDestinationSnapshot inspectedDestination, bool overwriteApproved);
}
