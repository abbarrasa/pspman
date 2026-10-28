using System.Text;
using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogCsvSerializerTests
{
    [Fact]
    public void EmptyCatalogProducesExactUtf8CsvWithBomAndHeaderOnly()
    {
        const string header = "Title;Medium;DVD Number;Location;Format;Decompressed Size (bytes);Approximate Size (binary units);Notes\r\n";
        byte[] expected = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(header)];

        var actual = GameCatalogCsvSerializer.SerializeEmptyCatalog();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EmptyCatalogHasExactlyEightEnglishColumnsAndNoDataRows()
    {
        var csv = Encoding.UTF8.GetString(GameCatalogCsvSerializer.SerializeEmptyCatalog());
        var lines = csv.TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.None);

        Assert.Equal(2, lines.Length);
        Assert.Equal(string.Empty, lines[1]);
        Assert.Equal(
            ["Title", "Medium", "DVD Number", "Location", "Format", "Decompressed Size (bytes)",
                "Approximate Size (binary units)", "Notes"],
            lines[0].Split(';'));
    }
}
