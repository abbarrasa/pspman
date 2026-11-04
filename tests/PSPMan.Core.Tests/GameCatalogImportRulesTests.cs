using System.Globalization;
using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogImportRulesTests
{
    [Theory]
    [InlineData("Dvd", GameMedium.Dvd)]
    [InlineData("DVD", GameMedium.Dvd)]
    [InlineData("uSb", GameMedium.Usb)]
    [InlineData("MemoryCard", GameMedium.MemoryCard)]
    [InlineData("memorycard", GameMedium.MemoryCard)]
    public void ParsesOnlyPermittedMediumLabelsIgnoringCase(string text, GameMedium expected)
    {
        Assert.True(GameCatalogImportRules.TryParseMedium(text, out var value, out var error));
        Assert.Equal(expected, value);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("ISO", GameFormat.Iso)]
    [InlineData("iso", GameFormat.Iso)]
    [InlineData("cHd", GameFormat.Chd)]
    public void ParsesOnlyPermittedFormatLabelsIgnoringCase(string text, GameFormat expected)
    {
        Assert.True(GameCatalogImportRules.TryParseFormat(text, out var value, out var error));
        Assert.Equal(expected, value);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Medium", "")]
    [InlineData("Medium", "Memory Card")]
    [InlineData("Medium", " Usb")]
    [InlineData("Medium", "Usb\t")]
    [InlineData("Medium", "Memory\u2003Card")]
    [InlineData("Medium", "\u00A0Dvd")]
    [InlineData("Medium", "0")]
    [InlineData("Medium", "Dvd,Usb")]
    [InlineData("Medium", "ISO")]
    [InlineData("Format", "")]
    [InlineData("Format", " ISO")]
    [InlineData("Format", "ISO\r\n")]
    [InlineData("Format", "C\tHD")]
    [InlineData("Format", "CHD\u00A0")]
    [InlineData("Format", "1")]
    [InlineData("Format", "Iso,Chd")]
    [InlineData("Format", "CSO")]
    public void RejectsInvalidChoiceLabelsWithTheCorrectField(string field, string text)
    {
        GameRecordValidationError? error;
        var success = field == "Medium"
            ? GameCatalogImportRules.TryParseMedium(text, out _, out error)
            : GameCatalogImportRules.TryParseFormat(text, out _, out error);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Equal(field, error.Field);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("0", null)]
    [InlineData("000", null)]
    [InlineData("1", 1)]
    [InlineData("0002", 2)]
    [InlineData("2147483647", int.MaxValue)]
    public void ParsesDvdNumbersAndNormalizesZero(string text, int? expected)
    {
        Assert.True(GameCatalogImportRules.TryParseDvdNumber(text, out var value, out var error));
        Assert.Equal(expected, value);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("0002", 2L)]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void ParsesPositiveInt64ByteCounts(string text, long expected)
    {
        Assert.True(GameCatalogImportRules.TryParseSizeBytes(text, out var value, out var error));
        Assert.Equal(expected, value);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-0")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("\t1")]
    [InlineData("1\u00A0")]
    [InlineData("1.5")]
    [InlineData("1,5")]
    [InlineData("1,000")]
    [InlineData("1.000")]
    [InlineData("1e3")]
    [InlineData("text")]
    [InlineData("9223372036854775808")]
    public void RejectsMalformedOrOverflowingNumbersByField(string text)
    {
        Assert.False(GameCatalogImportRules.TryParseDvdNumber(text, out var dvdNumber, out var dvdError));
        Assert.Null(dvdNumber);
        Assert.NotNull(dvdError);
        Assert.Equal("DvdNumber", dvdError.Field);
        Assert.False(GameCatalogImportRules.TryParseSizeBytes(text, out _, out var sizeError));
        Assert.NotNull(sizeError);
        Assert.Equal("SizeBytes", sizeError.Field);
    }

    [Fact]
    public void RejectsDvdInt32OverflowEvenWhenTheSameValueIsAValidSize()
    {
        Assert.False(GameCatalogImportRules.TryParseDvdNumber("2147483648", out _, out var error));
        Assert.NotNull(error);
        Assert.Equal("DvdNumber", error.Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("000")]
    public void RejectsMissingOrZeroSize(string text)
    {
        Assert.False(GameCatalogImportRules.TryParseSizeBytes(text, out _, out var error));
        Assert.NotNull(error);
        Assert.Equal("SizeBytes", error.Field);
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("tr-TR")]
    public void TypedValueParsingIsIndependentOfCurrentCulture(string culture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.True(GameCatalogImportRules.TryParseMedium("MEMORYCARD", out var medium, out _));
            Assert.Equal(GameMedium.MemoryCard, medium);
            Assert.True(GameCatalogImportRules.TryParseFormat("iso", out var format, out _));
            Assert.Equal(GameFormat.Iso, format);
            Assert.True(GameCatalogImportRules.TryParseDvdNumber("2147483647", out var dvdNumber, out _));
            Assert.Equal(int.MaxValue, dvdNumber);
            Assert.True(GameCatalogImportRules.TryParseSizeBytes("9223372036854775807", out var size, out _));
            Assert.Equal(long.MaxValue, size);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

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
