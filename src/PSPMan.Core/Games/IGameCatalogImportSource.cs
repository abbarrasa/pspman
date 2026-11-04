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

    /// <summary>Hashes all source bytes, including the BOM, without changing the decoded read position.</summary>
    GameCatalogImportFingerprint CaptureFingerprint();

    /// <summary>Checks both the open handle and the current source path against the expected content.</summary>
    void VerifyUnchanged(GameCatalogImportFingerprint expected);
}

public sealed record GameCatalogImportFingerprint(string Sha256);

public sealed class GameCatalogImportSourceChangedException()
    : IOException("The CSV source has changed since it was evaluated.")
{
}

public sealed class GameCatalogImportReadException(string message, Exception innerException)
    : IOException(message, innerException)
{
}
