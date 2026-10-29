namespace PSPMan.Core.Games;

public interface IGameCatalogExportWriter
{
    bool Exists(string destinationPath);

    void PublishNew(string destinationPath, ReadOnlyMemory<byte> content);
}
