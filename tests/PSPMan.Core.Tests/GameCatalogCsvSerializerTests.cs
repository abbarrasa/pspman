using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;
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
    [InlineData("One;Two", "\"One;Two\"")]
    [InlineData("A\"B", "\"A\"\"B\"")]
    [InlineData("Line\nbreak", "\"Line\nbreak\"")]
    [InlineData("Line\rbreak", "\"Line\rbreak\"")]
    [InlineData("Line\r\nbreak", "\"Line\r\nbreak\"")]
    public void EscapesSpecialNotesAndPreservesOneLogicalRow(string notes, string encodedNotes)
    {
        GameRecord[] records =
        [
            new(Guid.NewGuid(), "Game", GameMedium.Dvd, 1, "/game.iso", GameFormat.Iso, 1, notes)
        ];

        var bytes = GameCatalogCsvSerializer.SerializeCatalog(records);
        var csv = Encoding.UTF8.GetString(bytes);
        var rows = ParseRows(bytes);

        Assert.Contains($";{encodedNotes}\r\n", csv);
        Assert.Equal(2, rows.Count);
        Assert.Equal(8, rows[1].Length);
        Assert.Equal(notes, rows[1][7]);
    }

    [Fact]
    public void EscapesPunctuationAndMultilineFieldsWithoutChangingOtherValues()
    {
        const string title = "El Niño; Vol. 2: \"Return\"";
        const string location = "/GAMES/árcade;redux.iso";
        const string notes = "First; second, third: \"quoted\"\r\nNext line\nFinal line";
        GameRecord[] records =
        [
            new(Guid.NewGuid(), title, GameMedium.Dvd, 2, location, GameFormat.Iso, 1_572_864, notes),
            new(Guid.NewGuid(), "Plain, Game: Vol. 2", GameMedium.Usb, null,
                "/GAMES/plain.iso", GameFormat.Iso, 2_048, string.Empty)
        ];

        var bytes = GameCatalogCsvSerializer.SerializeCatalog(records);
        var csv = Encoding.UTF8.GetString(bytes);
        var rows = ParseRows(bytes);

        Assert.Contains("\"El Niño; Vol. 2: \"\"Return\"\"\"", csv);
        Assert.Contains("\"/GAMES/árcade;redux.iso\"", csv);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal(8, row.Length));
        Assert.Equal(title, rows[1][0]);
        Assert.Equal(location, rows[1][3]);
        Assert.Equal(notes, rows[1][7]);
        Assert.Equal("Plain, Game: Vol. 2", rows[2][0]);
        Assert.Equal(string.Empty, rows[2][7]);
    }

    [Fact]
    public void NullNotesRemainAnEmptyCell()
    {
        GameRecord[] records =
        [
            new(Guid.NewGuid(), "Game", GameMedium.Usb, null, "/game.iso", GameFormat.Iso, 1, null)
        ];

        var rows = ParseRows(GameCatalogCsvSerializer.SerializeCatalog(records));

        Assert.Equal(2, rows.Count);
        Assert.Equal(string.Empty, rows[1][7]);
    }

    private static List<string[]> ParseRows(byte[] bytes)
    {
        using var reader = new StringReader(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(";");

        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            rows.Add(parser.ReadFields() ?? []);
        }

        return rows;
    }
}
