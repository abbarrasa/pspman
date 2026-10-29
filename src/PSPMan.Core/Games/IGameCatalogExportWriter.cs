namespace PSPMan.Core.Games;

public interface IGameCatalogExportWriter
{
    bool Exists(string destinationPath);

    void Publish(string destinationPath, ReadOnlyMemory<byte> content, bool overwriteApproved);
}
