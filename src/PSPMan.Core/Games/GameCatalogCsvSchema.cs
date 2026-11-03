namespace PSPMan.Core.Games;

public static class GameCatalogCsvSchema
{
    public static IReadOnlyList<string> Headings { get; } = Array.AsReadOnly(
        new[] { "Title", "Medium", "DvdNumber", "Location", "Format", "SizeBytes", "Notes" });

    public static string HeaderLine { get; } = string.Join(';', Headings) + "\r\n";
}
