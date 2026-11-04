using System.Globalization;
using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogImportRulesTests
{
    [Fact]
    public void MapsEveryCanonicalHeadingToItsColumn()
    {
        var success = GameCatalogImportRules.TryMapHeadings(GameCatalogCsvSchema.Headings, out var map, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(map);
        Assert.Equal(7, map.ColumnCount);
        Assert.Equal(7, map.Indices.Count);
        for (var index = 0; index < GameCatalogCsvSchema.Headings.Count; index++)
        {
            Assert.Equal(index, map.Indices[GameCatalogCsvSchema.Headings[index]]);
        }
    }

    [Fact]
    public void MapsReorderedMixedCaseHeadingsAndUsesLastDuplicateWhileIgnoringExtras()
    {
        string[] headings = ["Notes", "Id", "title", "FORMAT", "Location", "medium", "SizeBytes",
            "DvdNumber", "TITLE", "notes", ""];

        Assert.True(GameCatalogImportRules.TryMapHeadings(headings, out var map, out var error));

        Assert.NotNull(map);
        Assert.Null(error);
        Assert.Equal(11, map.ColumnCount);
        Assert.Equal(7, map.Indices.Count);
        Assert.Equal(8, map.Indices["Title"]);
        Assert.Equal(5, map.Indices["Medium"]);
        Assert.Equal(7, map.Indices["DvdNumber"]);
        Assert.Equal(4, map.Indices["Location"]);
        Assert.Equal(3, map.Indices["Format"]);
        Assert.Equal(6, map.Indices["SizeBytes"]);
        Assert.Equal(9, map.Indices["Notes"]);
        Assert.False(map.Indices.ContainsKey("Id"));
        Assert.False(map.Indices.ContainsKey(""));
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("Medium")]
    [InlineData("DvdNumber")]
    [InlineData("Location")]
    [InlineData("Format")]
    [InlineData("SizeBytes")]
    [InlineData("Notes")]
    public void RejectsEachMissingRequiredHeadingWithoutReturningAPartialMap(string missing)
    {
        var headings = GameCatalogCsvSchema.Headings.Where(heading => heading != missing).ToArray();

        Assert.False(GameCatalogImportRules.TryMapHeadings(headings, out var map, out var error));

        Assert.Null(map);
        Assert.NotNull(error);
        Assert.Contains(missing, error);
    }

    [Fact]
    public void EmptyHeaderReportsAllMissingFieldsInCanonicalOrder()
    {
        Assert.False(GameCatalogImportRules.TryMapHeadings([], out var map, out var error));

        Assert.Null(map);
        Assert.Equal("Missing required CSV headings: Title, Medium, DvdNumber, Location, Format, SizeBytes, Notes.", error);
    }

    [Theory]
    [InlineData(" Title")]
    [InlineData("Title ")]
    [InlineData("Ti tle")]
    [InlineData("Ti\ttle")]
    [InlineData("Title\r\n")]
    [InlineData("\u00A0Title")]
    [InlineData("Title\u2003")]
    [InlineData("Legacy Name")]
    [InlineData("Unknown\tColumn")]
    public void RejectsWhitespaceEvenInExtraColumnsAndDespiteValidRequiredColumns(string heading)
    {
        string[] headings = [.. GameCatalogCsvSchema.Headings, heading];

        Assert.False(GameCatalogImportRules.TryMapHeadings(headings, out var map, out var error));

        Assert.Null(map);
        Assert.Equal("CSV heading at column 8 must not contain whitespace.", error);
    }

    [Fact]
    public void CaseInsensitiveMappingDoesNotDependOnCurrentCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            string[] headings = ["TITLE", "MEDIUM", "DVDNUMBER", "LOCATION", "FORMAT", "SIZEBYTES", "NOTES"];

            Assert.True(GameCatalogImportRules.TryMapHeadings(headings, out var map, out var error));
            Assert.NotNull(map);
            Assert.Null(error);
            Assert.Equal(0, map.Indices["Title"]);
            Assert.Equal(5, map.Indices["SizeBytes"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
