namespace PSPMan.Core.Games;

public static class GameCatalogCsvSchema
{
    public static IReadOnlyList<string> Headings { get; } = Array.AsReadOnly(
        new[] { "Title", "Medium", "DvdNumber", "Location", "Format", "SizeBytes", "Notes" });

    public static string HeaderLine { get; } = string.Join(';', Headings) + "\r\n";

    public static string GetMediumLabel(GameMedium medium) => medium switch
    {
        GameMedium.Dvd => "Dvd",
        GameMedium.Usb => "Usb",
        GameMedium.MemoryCard => "MemoryCard",
        _ => throw new ArgumentOutOfRangeException(nameof(medium), "Unsupported game medium.")
    };

    public static string GetFormatLabel(GameFormat format) => format switch
    {
        GameFormat.Iso => "ISO",
        GameFormat.Chd => "CHD",
        _ => throw new ArgumentOutOfRangeException(nameof(format), "Unsupported game format.")
    };
}
