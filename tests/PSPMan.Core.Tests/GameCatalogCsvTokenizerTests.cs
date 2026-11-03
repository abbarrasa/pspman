using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogCsvTokenizerTests
{
    [Theory]
    [InlineData("\n", 1)]
    [InlineData("\n", 2)]
    [InlineData("\n", 7)]
    [InlineData("\n", 100)]
    [InlineData("\r\n", 1)]
    [InlineData("\r\n", 2)]
    [InlineData("\r\n", 7)]
    [InlineData("\r\n", 100)]
    public void ReadsCellsAndSourceLinesIndependentlyOfChunkBoundaries(string newline, int chunkSize)
    {
        var csv = $"Title;Medium;Notes{newline}Game;Usb;ordinary text{newline}Other;;";
        var tokenizer = new GameCatalogCsvTokenizer();
        var records = new List<GameCatalogCsvRecord>();

        for (var offset = 0; offset < csv.Length; offset += chunkSize)
        {
            tokenizer.Push(csv.AsSpan(offset, Math.Min(chunkSize, csv.Length - offset)), records.Add);
        }
        tokenizer.Complete(records.Add);

        Assert.Equal(new long[] { 1, 2, 3 }, records.Select(record => record.StartLine));
        Assert.Equal(new[] { "Title", "Medium", "Notes" }, records[0].Cells);
        Assert.Equal(new[] { "Game", "Usb", "ordinary text" }, records[1].Cells);
        Assert.Equal(new[] { "Other", "", "" }, records[2].Cells);
    }

    [Fact]
    public void EmitsCompletedRecordsBeforeTheRestOfTheInputArrives()
    {
        var tokenizer = new GameCatalogCsvTokenizer();
        var records = new List<GameCatalogCsvRecord>();

        tokenizer.Push("first;row\r", records.Add);
        var first = Assert.Single(records);
        tokenizer.Push("\nsec", records.Add);
        Assert.Single(records);
        tokenizer.Push("ond;row\nthird", records.Add);
        Assert.Equal(2, records.Count);
        tokenizer.Complete(records.Add);

        Assert.Equal(new[] { "first", "row" }, first.Cells);
        Assert.Equal(new[] { "second", "row" }, records[1].Cells);
        Assert.Equal(new[] { "third" }, records[2].Cells);
        Assert.Equal(new long[] { 1, 2, 3 }, records.Select(record => record.StartLine));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("a;b", 1)]
    [InlineData("a;b\n", 1)]
    [InlineData("a;b\r\n", 1)]
    public void CompletionDoesNotInventAnExtraRecord(string csv, int expectedCount)
    {
        var tokenizer = new GameCatalogCsvTokenizer();
        var records = new List<GameCatalogCsvRecord>();

        tokenizer.Push(csv, records.Add);
        tokenizer.Push(ReadOnlySpan<char>.Empty, records.Add);
        tokenizer.Complete(records.Add);

        Assert.Equal(expectedCount, records.Count);
    }

    [Fact]
    public void PreservesEmptyCellsAndOrdinaryWhitespace()
    {
        var tokenizer = new GameCatalogCsvTokenizer();
        var records = new List<GameCatalogCsvRecord>();

        tokenizer.Push("; leading and trailing ;\ttext;", records.Add);
        tokenizer.Complete(records.Add);

        Assert.Equal(new[] { "", " leading and trailing ", "\ttext", "" }, Assert.Single(records).Cells);
    }

    [Fact]
    public void CannotFeedOrCompleteAnAlreadyCompletedTokenizer()
    {
        var tokenizer = new GameCatalogCsvTokenizer();
        tokenizer.Complete(_ => { });

        Assert.Throws<InvalidOperationException>(() => tokenizer.Push("more", _ => { }));
        Assert.Throws<InvalidOperationException>(() => tokenizer.Complete(_ => { }));
    }
}
