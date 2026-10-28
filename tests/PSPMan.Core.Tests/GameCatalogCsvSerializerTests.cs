using System.Globalization;
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

    [Fact]
    public void SerializesEveryRecordInInputOrderWithoutInternalIds()
    {
        var dvdId = Guid.NewGuid();
        var usbId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        GameRecord[] records =
        [
            new(dvdId, "Shared Title", GameMedium.Dvd, 2, "/GAMES/disc-two.iso", GameFormat.Iso,
                1_572_864, "Disc two"),
            new(usbId, "Shared Title", GameMedium.Usb, 7, "/GAMES/usb.chd", GameFormat.Chd,
                2_048, null),
            new(cardId, "Card Game", GameMedium.MemoryCard, null, "/GAMES/card.iso", GameFormat.Iso,
                1, string.Empty)
        ];

        var csv = Encoding.UTF8.GetString(GameCatalogCsvSerializer.SerializeCatalog(records));

        Assert.Equal(
            "\uFEFFTitle;Medium;DVD Number;Location;Format;Decompressed Size (bytes);Approximate Size (binary units);Notes\r\n"
            + "Shared Title;DVD;2;/GAMES/disc-two.iso;ISO;1572864;1.5 MiB;Disc two\r\n"
            + "Shared Title;USB;;/GAMES/usb.chd;CHD;2048;2 KiB;\r\n"
            + "Card Game;Memory Card;;/GAMES/card.iso;ISO;1;1 B;\r\n",
            csv);
        Assert.DoesNotContain(dvdId.ToString(), csv);
        Assert.DoesNotContain(usbId.ToString(), csv);
        Assert.DoesNotContain(cardId.ToString(), csv);
    }

    [Fact]
    public void SerializingNoRecordsMatchesTheHeaderOnlyOutput()
    {
        Assert.Equal(GameCatalogCsvSerializer.SerializeEmptyCatalog(),
            GameCatalogCsvSerializer.SerializeCatalog([]));
    }

    [Fact]
    public void SerializesBytesWithoutLocaleDependentGrouping()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            GameRecord[] records =
            [
                new(Guid.NewGuid(), "Game", GameMedium.Dvd, 1, "/game.iso", GameFormat.Iso,
                    1_572_864, null)
            ];

            var csv = Encoding.UTF8.GetString(GameCatalogCsvSerializer.SerializeCatalog(records));

            Assert.Contains(";1572864;1.5 MiB;\r\n", csv);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData("One;Two")]
    [InlineData("A\"B")]
    [InlineData("Line\nbreak")]
    public void RejectsFieldsThatNeedEscapingUntilEscapingIsImplemented(string notes)
    {
        GameRecord[] records =
        [
            new(Guid.NewGuid(), "Game", GameMedium.Dvd, 1, "/game.iso", GameFormat.Iso, 1, notes)
        ];

        Assert.Throws<ArgumentException>(() => GameCatalogCsvSerializer.SerializeCatalog(records));
    }
}
