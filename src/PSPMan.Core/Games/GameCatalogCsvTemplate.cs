namespace PSPMan.Core.Games;

public static class GameCatalogCsvTemplate
{
    public static byte[] Generate()
    {
        return GameCatalogCsvSerializer.SerializeRows(
        [
            ExampleRow(GameMedium.Dvd, GameFormat.Iso),
            ExampleRow(GameMedium.Usb, GameFormat.Chd),
            ExampleRow(GameMedium.MemoryCard, GameFormat.Iso)
        ]);
    }

    private static string[] ExampleRow(GameMedium medium, GameFormat format) =>
    [
        string.Empty,
        GameCatalogCsvSchema.GetMediumLabel(medium),
        string.Empty,
        string.Empty,
        GameCatalogCsvSchema.GetFormatLabel(format),
        string.Empty,
        string.Empty
    ];
}
