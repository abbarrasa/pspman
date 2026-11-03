using System.Text;
using Microsoft.VisualBasic.FileIO;
using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogCsvTemplateTests
{
    [Fact]
    public void GeneratesExactSevenColumnTemplateWithEveryPermittedLabel()
    {
        const string expected = "Title;Medium;DvdNumber;Location;Format;SizeBytes;Notes\r\n"
            + ";Dvd;;;ISO;;\r\n"
            + ";Usb;;;CHD;;\r\n"
            + ";MemoryCard;;;ISO;;\r\n";

        var actual = GameCatalogCsvTemplate.Generate();

        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(expected)], actual);
    }

    [Fact]
    public void ParsedExamplesLeaveRequiredFieldsEmptyAndCannotBeValidGames()
    {
        var csv = Encoding.UTF8.GetString(GameCatalogCsvTemplate.Generate()).TrimStart('\uFEFF');
        using var reader = new StringReader(csv);
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(";");
        Assert.Equal(GameCatalogCsvSchema.Headings, parser.ReadFields());

        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()!;
            rows.Add(fields);
            Assert.Equal(7, fields.Length);
            foreach (var index in new[] { 0, 2, 3, 5, 6 })
            {
                Assert.Equal(string.Empty, fields[index]);
            }

            var input = new GameRecordInput(fields[0], Enum.Parse<GameMedium>(fields[1]), null,
                fields[3], Enum.Parse<GameFormat>(fields[4], ignoreCase: true), 0, fields[6]);
            var errors = GameRecordRules.Validate(input);
            Assert.Contains(errors, error => error.Field == nameof(GameRecordInput.Title));
            Assert.Contains(errors, error => error.Field == nameof(GameRecordInput.Location));
            Assert.Contains(errors, error => error.Field == nameof(GameRecordInput.SizeBytes));
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Dvd", "Usb", "MemoryCard" }, rows.Select(row => row[1]));
        Assert.Equal(new[] { "ISO", "CHD" }, rows.Select(row => row[4]).Distinct());
    }
}
