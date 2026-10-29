namespace PSPMan.Core.Games;

public sealed record ExportDestinationSnapshot(string DestinationPath, string? ContentHash)
{
    public bool Exists => ContentHash is not null;
}
