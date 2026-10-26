namespace PSPMan.Core.Games;

public enum GameMedium
{
    Dvd,
    Usb,
    MemoryCard
}

public enum GameFormat
{
    Iso,
    Chd
}

public sealed record GameRecordInput(
    string Title,
    GameMedium Medium,
    int? DvdNumber,
    string Location,
    GameFormat Format,
    long SizeBytes,
    string? Notes);

public sealed record GameRecord(
    Guid Id,
    string Title,
    GameMedium Medium,
    int? DvdNumber,
    string Location,
    GameFormat Format,
    long SizeBytes,
    string? Notes);
