namespace PSPMan.Core.Games;

public interface IGameCatalogImportSource
{
    /// <summary>Opens the selected source. The caller owns and disposes the returned reader.</summary>
    IGameCatalogImportReader Open(string sourcePath);
}

public interface IGameCatalogImportReader : IDisposable
{
    /// <summary>Reads decoded characters; zero indicates EOF when the supplied buffer is nonempty.</summary>
    int Read(Span<char> buffer);
}

public sealed class GameCatalogImportReadException(string message, Exception innerException)
    : IOException(message, innerException)
{
}
