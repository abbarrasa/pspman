using System.Globalization;
using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogImportRulesTests
{
    [Fact]
    public void ClassifiesNewModifiedAndUnchangedCandidatesWithoutChangingInputs()
    {
        var original = new GameRecordInput("Game", GameMedium.Usb, null, "/GAMES/game.rar", GameFormat.Iso, 1024, "notes");
        var unchanged = original with { Location = "/GAMES/unchanged.rar" };
        var added = original with { Location = "/GAMES/new.rar" };
        var edited = original with { Title = "Edited", Notes = "" };
        var id = Guid.NewGuid();
        GameRecord[] snapshot = [SavedImportRecord(id, original), SavedImportRecord(Guid.NewGuid(), unchanged)];
        var before = snapshot.ToArray();
        var candidates = ImportCandidates(new(8, edited), new(2, added), new(5, unchanged));

        var result = GameCatalogImportRules.ClassifyCandidates(candidates, snapshot);

        Assert.Equal(1, result.NewCount);
        Assert.Equal(1, result.ModifiedCount);
        Assert.Equal(new GameCatalogImportCandidate(2, added), Assert.Single(result.NewGames));
        var modification = Assert.Single(result.ModifiedGames);
        Assert.Equal(id, modification.ExistingId);
        Assert.Equal(8, modification.StartLine);
        Assert.Equal(edited, modification.Input);
        Assert.Equal(before, snapshot);
        Assert.Equal(3, candidates.Count);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData("", null)]
    [InlineData("", "")]
    [InlineData("same notes", "same notes")]
    public void OmitsIdenticalMatchesIncludingEquivalentEmptyNotes(string? existingNotes, string? importedNotes)
    {
        var input = new GameRecordInput("Game", GameMedium.Usb, null, "/GAMES/game.rar", GameFormat.Iso, 1024, importedNotes);
        var existing = SavedImportRecord(Guid.NewGuid(), input with { Notes = existingNotes });

        var result = GameCatalogImportRules.ClassifyCandidates(ImportCandidates(new GameCatalogImportCandidate(2, input)), [existing]);

        Assert.Equal(0, result.NewCount);
        Assert.Equal(0, result.ModifiedCount);
        Assert.Empty(result.NewGames);
        Assert.Empty(result.ModifiedGames);
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("TitleCase")]
    [InlineData("TitleWhitespace")]
    [InlineData("Format")]
    [InlineData("SizeBytes")]
    [InlineData("Notes")]
    [InlineData("ClearNotes")]
    [InlineData("NotesWhitespace")]
    public void EachEditableDifferenceProposesAnEditPreservingTheExistingId(string field)
    {
        var original = new GameRecordInput("Game", GameMedium.Usb, null, "/GAMES/game.rar", GameFormat.Iso, 1024, "notes");
        var edited = field switch
        {
            "Title" => original with { Title = "Different" },
            "TitleCase" => original with { Title = "GAME" },
            "TitleWhitespace" => original with { Title = " Game " },
            "Format" => original with { Format = GameFormat.Chd },
            "SizeBytes" => original with { SizeBytes = 2048 },
            "Notes" => original with { Notes = "Different" },
            "ClearNotes" => original with { Notes = "" },
            _ => original with { Notes = " notes " }
        };
        var existing = SavedImportRecord(Guid.NewGuid(), original);

        var result = GameCatalogImportRules.ClassifyCandidates(ImportCandidates(new GameCatalogImportCandidate(7, edited)), [existing]);

        Assert.Equal(0, result.NewCount);
        Assert.Equal(1, result.ModifiedCount);
        var modification = Assert.Single(result.ModifiedGames);
        Assert.Equal(existing.Id, modification.ExistingId);
        Assert.Equal(edited, modification.Input);
        Assert.Equal(7, modification.StartLine);
    }

    [Fact]
    public void EmptyCatalogClassifiesEveryFinalCandidateAsNewInSourceLineOrder()
    {
        var first = new GameRecordInput("Same", GameMedium.Usb, null, "/GAMES/first.rar", GameFormat.Iso, 1024, "");
        var second = first with { Location = "/GAMES/second.rar" };
        var result = GameCatalogImportRules.ClassifyCandidates(ImportCandidates(new(8, second), new(2, first)), []);

        Assert.Equal(2, result.NewCount);
        Assert.Equal(0, result.ModifiedCount);
        Assert.Equal(new long[] { 2, 8 }, result.NewGames.Select(candidate => candidate.StartLine));
    }

    [Fact]
    public void EmptyCandidatesProposeNoChanges()
    {
        var result = GameCatalogImportRules.ClassifyCandidates(ImportCandidates(), []);

        Assert.Equal(0, result.NewCount);
        Assert.Equal(0, result.ModifiedCount);
    }

    [Fact]
    public void ClassifiesOnlyTheLastValidRowOnceAndOmitsAReturnToOriginalValues()
    {
        var candidates = new Dictionary<GameCatalogImportIdentity, GameCatalogImportCandidate>();
        var map = CanonicalImportMap();
        var original = ValidImportCells();
        Assert.True(GameCatalogImportRules.TryMapGameRow(original, map, out var input, out _));
        var existing = SavedImportRecord(Guid.NewGuid(), input!);
        var changed = original.ToArray();
        changed[0] = "Changed";
        Assert.True(GameCatalogImportRules.SelectLastValid(new(2, changed), map, candidates, out _));
        Assert.True(GameCatalogImportRules.SelectLastValid(new(3, changed), map, candidates, out _));
        var proposed = GameCatalogImportRules.ClassifyCandidates(candidates, [existing]);
        Assert.Equal(1, proposed.ModifiedCount);
        Assert.Equal(3, Assert.Single(proposed.ModifiedGames).StartLine);

        Assert.True(GameCatalogImportRules.SelectLastValid(new(4, original), map, candidates, out _));
        var unchanged = GameCatalogImportRules.ClassifyCandidates(candidates, [existing]);
        Assert.Equal(0, unchanged.NewCount);
        Assert.Equal(0, unchanged.ModifiedCount);
    }

    [Fact]
    public void CatalogMatchingUsesCaseSensitiveLocationMediumAndDvdIdentity()
    {
        var original = new GameRecordInput("Game", GameMedium.Dvd, 1, "/GAMES/game.rar", GameFormat.Iso, 1024, "");
        var candidates = ImportCandidates(
            new(2, original with { Location = "/GAMES/Game.rar" }),
            new(3, original with { Medium = GameMedium.Usb, DvdNumber = null }),
            new(4, original with { DvdNumber = 2 }));

        var result = GameCatalogImportRules.ClassifyCandidates(candidates, [SavedImportRecord(Guid.NewGuid(), original)]);

        Assert.Equal(3, result.NewCount);
        Assert.Equal(0, result.ModifiedCount);
    }

    [Fact]
    public void EnumeratesTheCatalogSnapshotOnlyOnce()
    {
        var count = 0;
        var original = new GameRecordInput("Game", GameMedium.Usb, null, "/GAMES/game.rar", GameFormat.Iso, 1024, "");
        IEnumerable<GameRecord> Snapshot()
        {
            count++;
            Assert.Equal(1, count);
            yield return SavedImportRecord(Guid.NewGuid(), original);
        }

        var result = GameCatalogImportRules.ClassifyCandidates(ImportCandidates(
            new(2, original), new(3, original with { Location = "/GAMES/new.rar" })), Snapshot());

        Assert.Equal(1, count);
        Assert.Equal(1, result.NewCount);
        Assert.Equal(0, result.ModifiedCount);
    }

    private static Dictionary<GameCatalogImportIdentity, GameCatalogImportCandidate> ImportCandidates(
        params GameCatalogImportCandidate[] candidates) => candidates.ToDictionary(candidate => GameCatalogImportRules.Identity(candidate.Input));

    private static GameRecord SavedImportRecord(Guid id, GameRecordInput input) => new(
        id, input.Title, input.Medium, input.DvdNumber, input.Location, input.Format, input.SizeBytes, input.Notes);

    [Fact]
    public void LastValidRowReplacesAllEditableValuesAndSourceLineForTheSameIdentity()
    {
        var candidates = new Dictionary<GameCatalogImportIdentity, GameCatalogImportCandidate>();
        var map = CanonicalImportMap();
        var first = ValidImportCells();
        first[6] = "old notes";
        Assert.True(GameCatalogImportRules.SelectLastValid(new(2, first), map, candidates, out var firstError));
        Assert.Null(firstError);
        var last = ValidImportCells();
        last[0] = "Different title";
        last[4] = "CHD";
        last[5] = "2048";
        last[6] = "";

        Assert.True(GameCatalogImportRules.SelectLastValid(new(9, last), map, candidates, out var error));

        Assert.Null(error);
        var candidate = Assert.Single(candidates).Value;
        Assert.Equal(9, candidate.StartLine);
        Assert.Equal(new GameRecordInput("Different title", GameMedium.Usb, null,
            "/GAMES/game.rar", GameFormat.Chd, 2048, ""), candidate.Input);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "bad medium")]
    [InlineData(2, "1")]
    [InlineData(4, "bad format")]
    [InlineData(5, "bad size")]
    public void LaterInvalidRowDoesNotReplaceTheEarlierValidCandidate(int index, string value)
    {
        var candidates = new Dictionary<GameCatalogImportIdentity, GameCatalogImportCandidate>();
        var map = CanonicalImportMap();
        Assert.True(GameCatalogImportRules.SelectLastValid(new(2, ValidImportCells()), map, candidates, out _));
        var earlier = Assert.Single(candidates).Value;
        var invalid = ValidImportCells();
        invalid[index] = value;

        Assert.False(GameCatalogImportRules.SelectLastValid(new(4, invalid), map, candidates, out var error));

        Assert.NotNull(error);
        Assert.Equal(4, error.StartLine);
        Assert.Same(earlier, Assert.Single(candidates).Value);
    }

    [Theory]
    [InlineData("Usb", "", "/GAMES/Game.rar")]
    [InlineData("Usb", "", "/OTHER/game.rar")]
    [InlineData("MemoryCard", "", "/GAMES/game.rar")]
    [InlineData("Dvd", "1", "/GAMES/game.rar")]
    public void DifferentMediumOrOrdinalLocationKeepsSeparateCandidates(string medium, string dvdNumber, string location)
    {
        var candidates = new Dictionary<GameCatalogImportIdentity, GameCatalogImportCandidate>();
        var map = CanonicalImportMap();
        Assert.True(GameCatalogImportRules.SelectLastValid(new(2, ValidImportCells()), map, candidates, out _));
        var second = ValidImportCells();
        second[1] = medium;
        second[2] = dvdNumber;
        second[3] = location;

        Assert.True(GameCatalogImportRules.SelectLastValid(new(3, second), map, candidates, out var error));

        Assert.Null(error);
        Assert.Equal(2, candidates.Count);
        Assert.Equal(new long[] { 2, 3 }, candidates.Values.Select(candidate => candidate.StartLine).Order());
    }

    [Fact]
    public void DifferentDvdNumbersStaySeparateAndLastValidRowWinsWithinOneDisc()
    {
        var candidates = new Dictionary<GameCatalogImportIdentity, GameCatalogImportCandidate>();
        var map = CanonicalImportMap();
        var row = ValidImportCells();
        row[1] = "Dvd";
        row[2] = "1";
        Assert.True(GameCatalogImportRules.SelectLastValid(new(2, row), map, candidates, out _));
        row[2] = "2";
        Assert.True(GameCatalogImportRules.SelectLastValid(new(3, row), map, candidates, out _));
        row[0] = "Replacement for disc two";
        Assert.True(GameCatalogImportRules.SelectLastValid(new(4, row), map, candidates, out _));

        Assert.Equal(2, candidates.Count);
        Assert.Equal("Game", candidates.Single(pair => pair.Key.DvdNumber == 1).Value.Input.Title);
        Assert.Equal(4, candidates.Single(pair => pair.Key.DvdNumber == 2).Value.StartLine);
        Assert.Equal("Replacement for disc two", candidates.Single(pair => pair.Key.DvdNumber == 2).Value.Input.Title);
    }

    [Theory]
    [InlineData(GameMedium.Dvd, 0, null)]
    [InlineData(GameMedium.Dvd, 2, 2)]
    [InlineData(GameMedium.Usb, 2, null)]
    [InlineData(GameMedium.MemoryCard, 2, null)]
    public void IdentityUsesTheSameDvdNormalizationAsExistingDomainRules(GameMedium medium, int number, int? expected)
    {
        var input = new GameRecordInput("Title", medium, number, "/GAMES/game.rar", GameFormat.Iso, 1024, "notes");
        var key = GameCatalogImportRules.Identity(input);
        var existing = new GameRecord(Guid.NewGuid(), "Another title", medium, expected,
            input.Location, GameFormat.Chd, 2048, null);

        Assert.Equal(expected, key.DvdNumber);
        Assert.Equal(medium, key.Medium);
        Assert.Equal(input.Location, key.Location);
        Assert.True(GameRecordRules.HasSameIdentity(input, existing));
        Assert.Equal(key, GameCatalogImportRules.Identity(new GameRecordInput(existing.Title, existing.Medium,
            existing.DvdNumber, existing.Location, existing.Format, existing.SizeBytes, existing.Notes)));
    }

    [Fact]
    public void ZeroAndEmptyNumbersCollapseForNonDvdAndInvalidOnlyInputCreatesNoCandidates()
    {
        var candidates = new Dictionary<GameCatalogImportIdentity, GameCatalogImportCandidate>();
        var map = CanonicalImportMap();
        Assert.False(GameCatalogImportRules.SelectLastValid(new(2, []), map, candidates, out var error));
        Assert.NotNull(error);
        Assert.Empty(candidates);
        Assert.True(GameCatalogImportRules.SelectLastValid(new(3, ValidImportCells()), map, candidates, out _));
        var row = ValidImportCells();
        row[2] = "0";
        Assert.True(GameCatalogImportRules.SelectLastValid(new(4, row), map, candidates, out error));

        Assert.Null(error);
        Assert.Equal(4, Assert.Single(candidates).Value.StartLine);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    public void ShortSourceRowsReturnOneErrorWithoutMappingFields(int count)
    {
        var record = new GameCatalogCsvRecord(12, ValidImportCells().Take(count).ToArray());

        Assert.False(GameCatalogImportRules.TryMapSourceRow(record, CanonicalImportMap(), out var input, out var error));

        Assert.Null(input);
        Assert.NotNull(error);
        Assert.Equal(12, error.StartLine);
        Assert.Equal($"Row has {count} cells; expected at least 7.", error.Message);
        Assert.Empty(error.ValidationErrors);
    }

    [Theory]
    [InlineData("Ignored")]
    [InlineData("Title")]
    public void WidthIncludesExtraAndRepeatedHeadingColumns(string additionalHeading)
    {
        Assert.True(GameCatalogImportRules.TryMapHeadings(
            [.. GameCatalogCsvSchema.Headings, additionalHeading], out var map, out _));

        Assert.False(GameCatalogImportRules.TryMapSourceRow(
            new GameCatalogCsvRecord(4, ValidImportCells()), map!, out var input, out var error));

        Assert.Null(input);
        Assert.NotNull(error);
        Assert.Equal(4, error.StartLine);
        Assert.Equal("Row has 7 cells; expected at least 8.", error.Message);
        Assert.Empty(error.ValidationErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoresSurplusCellsBeyondTheFullHeader(bool extendedHeader)
    {
        string[] headings = extendedHeader ? [.. GameCatalogCsvSchema.Headings, "Ignored"] : [.. GameCatalogCsvSchema.Headings];
        Assert.True(GameCatalogImportRules.TryMapHeadings(headings, out var map, out _));
        string[] cells = extendedHeader
            ? [.. ValidImportCells(), "unmapped column", "invalid extra", "=formula"]
            : [.. ValidImportCells(), "invalid extra", "=formula"];
        var before = cells.ToArray();

        Assert.True(GameCatalogImportRules.TryMapSourceRow(new GameCatalogCsvRecord(3, cells), map!, out var input, out var error));

        Assert.Null(error);
        Assert.Equal(new GameRecordInput("Game", GameMedium.Usb, null, "/GAMES/game.rar", GameFormat.Iso, 1024, ""), input);
        Assert.Equal(before, cells);
    }

    [Theory]
    [InlineData(0, "", "Title")]
    [InlineData(3, "bad-path", "Location")]
    [InlineData(1, "invalid", "Medium")]
    [InlineData(5, "invalid", "SizeBytes")]
    public void SourceRowErrorsPreserveStartLineFieldAndReason(int index, string text, string field)
    {
        var cells = ValidImportCells();
        cells[index] = text;
        const long startLine = 2147483650;

        Assert.False(GameCatalogImportRules.TryMapSourceRow(
            new GameCatalogCsvRecord(startLine, cells), CanonicalImportMap(), out var input, out var error));

        Assert.Null(input);
        Assert.NotNull(error);
        Assert.Equal(startLine, error.StartLine);
        var validationError = Assert.Single(error.ValidationErrors);
        Assert.Equal(field, validationError.Field);
        Assert.Contains(validationError.Message, error.Message);
    }

    [Fact]
    public void MultipleInvalidFieldsProduceOneSourceRowErrorWithAllReasons()
    {
        var cells = ValidImportCells();
        cells[0] = "";
        cells[3] = "";

        Assert.False(GameCatalogImportRules.TryMapSourceRow(
            new GameCatalogCsvRecord(5, cells), CanonicalImportMap(), out var input, out var error));

        Assert.Null(input);
        Assert.NotNull(error);
        Assert.Equal(5, error.StartLine);
        Assert.Equal(new[] { "Title", "Location" }, error.ValidationErrors.Select(item => item.Field));
        Assert.All(error.ValidationErrors, item => Assert.Contains(item.Message, error.Message));
    }

    [Fact]
    public void ContinuesMappingValidRowsAfterShortAndInvalidMultilineRows()
    {
        const string csv = "short\r\nGame;Usb;;/GAMES/game.rar;ISO;bad;\"first\r\nsecond\"\r\n"
            + "Good;Usb;;/GAMES/good.rar;ISO;1024;\r\n";
        var tokenizer = new GameCatalogCsvTokenizer();
        var records = new List<GameCatalogCsvRecord>();
        foreach (var character in csv)
        {
            tokenizer.Push(character.ToString(), records.Add);
        }
        tokenizer.Complete(records.Add);
        var inputs = new List<GameRecordInput>();
        var errors = new List<GameCatalogImportRowError>();
        var map = CanonicalImportMap();
        foreach (var record in records)
        {
            if (GameCatalogImportRules.TryMapSourceRow(record, map, out var input, out var error))
            {
                inputs.Add(input!);
            }
            else
            {
                errors.Add(error!);
            }
        }

        Assert.Equal(new long[] { 1, 2 }, errors.Select(error => error.StartLine));
        Assert.Equal(4, records[2].StartLine);
        Assert.Equal("Good", Assert.Single(inputs).Title);
    }

    [Theory]
    [InlineData("\t=1+1", "=1+1")]
    [InlineData("\t+SUM(1;2)", "+SUM(1;2)")]
    [InlineData("\t-10", "-10")]
    [InlineData("\t@CMD", "@CMD")]
    [InlineData("\t \u2003=1", " \u2003=1")]
    [InlineData("\t\t=1", "\t=1")]
    [InlineData(" \t=1", " \t=1")]
    [InlineData("\tordinary", "\tordinary")]
    [InlineData("\t \t", "\t \t")]
    [InlineData("=1", "=1")]
    [InlineData("", "")]
    public void RemovesExactlyOneInitialProtectionTabOnlyForFormulaLikeText(string text, string expected)
    {
        Assert.Equal(expected, GameCatalogImportRules.UndoFormulaProtection(text));
    }

    [Fact]
    public void MapsReorderedFieldsAndPreservesTextWithoutMutatingCells()
    {
        Assert.True(GameCatalogImportRules.TryMapHeadings(
            ["Notes", "Format", "Title", "SizeBytes", "Location", "DvdNumber", "Medium", "title"],
            out var map, out _));
        string[] cells = ["\t \u2003=SUM(1;2) \"quoted\"\r\nNext", "cHd", "superseded", "2048",
            "/GAMES/Case Sensitive;é.rar", "0", "uSb", "\t\t+Title"];
        var before = cells.ToArray();

        Assert.True(GameCatalogImportRules.TryMapGameRow(cells, map!, out var input, out var errors));

        Assert.Empty(errors);
        Assert.Equal(new GameRecordInput("\t+Title", GameMedium.Usb, null,
            "/GAMES/Case Sensitive;é.rar", GameFormat.Chd, 2048, " \u2003=SUM(1;2) \"quoted\"\r\nNext"), input);
        Assert.Equal(before, cells);
    }

    [Theory]
    [InlineData(0, "", "Title")]
    [InlineData(0, " \t\u2003", "Title")]
    [InlineData(3, "", "Location")]
    [InlineData(3, "relative/game.rar", "Location")]
    [InlineData(3, "/GAMES/../game.rar", "Location")]
    [InlineData(3, "/GAMES\\game.rar", "Location")]
    [InlineData(3, "/GAMES/game\t.rar", "Location")]
    [InlineData(3, "\t/GAMES/game.rar", "Location")]
    [InlineData(1, "Dvd", "DvdNumber")]
    [InlineData(2, "2", "DvdNumber")]
    public void UsesExistingDomainRulesAndReturnsNoRecordForInvalidFields(int index, string value, string field)
    {
        var cells = ValidImportCells();
        cells[index] = value;

        Assert.False(GameCatalogImportRules.TryMapGameRow(cells, CanonicalImportMap(), out var input, out var errors));

        Assert.Null(input);
        Assert.Equal(field, Assert.Single(errors).Field);
    }

    [Theory]
    [InlineData("Dvd", "2", true)]
    [InlineData("Dvd", "", false)]
    [InlineData("Dvd", "0", false)]
    [InlineData("Usb", "", true)]
    [InlineData("Usb", "0", true)]
    [InlineData("Usb", "1", false)]
    [InlineData("MemoryCard", "", true)]
    [InlineData("MemoryCard", "0", true)]
    [InlineData("MemoryCard", "1", false)]
    public void ValidatesDvdNumberAgainstTheSelectedMedium(string medium, string number, bool expected)
    {
        var cells = ValidImportCells();
        cells[1] = medium;
        cells[2] = number;

        Assert.Equal(expected, GameCatalogImportRules.TryMapGameRow(cells, CanonicalImportMap(), out var input, out var errors));
        if (expected)
        {
            Assert.NotNull(input);
            Assert.Empty(errors);
        }
        else
        {
            Assert.Null(input);
            Assert.Equal("DvdNumber", Assert.Single(errors).Field);
        }
    }

    [Theory]
    [InlineData(2500, true)]
    [InlineData(2501, false)]
    public void ValidatesNotesLengthAfterRemovingProtection(int length, bool expected)
    {
        var cells = ValidImportCells();
        cells[6] = "\t=" + new string('x', length - 1);

        Assert.Equal(expected, GameCatalogImportRules.TryMapGameRow(cells, CanonicalImportMap(), out var input, out var errors));
        if (expected)
        {
            Assert.NotNull(input);
            Assert.Equal(length, input.Notes!.Length);
            Assert.Empty(errors);
        }
        else
        {
            Assert.Null(input);
            Assert.Equal("Notes", Assert.Single(errors).Field);
        }
    }

    [Fact]
    public void NotesLimitCountsUtf16CodeUnits()
    {
        var cells = ValidImportCells();
        cells[6] = string.Concat(Enumerable.Repeat("🎮", 1250));
        Assert.True(GameCatalogImportRules.TryMapGameRow(cells, CanonicalImportMap(), out _, out _));
        cells[6] += "x";
        Assert.False(GameCatalogImportRules.TryMapGameRow(cells, CanonicalImportMap(), out var input, out var errors));
        Assert.Null(input);
        Assert.Equal("Notes", Assert.Single(errors).Field);
    }

    [Fact]
    public void KeepsOrdinaryWhitespacePunctuationAndEmptyNotes()
    {
        var cells = ValidImportCells();
        cells[0] = " \tGame; \"title\", volume: 2 ";
        cells[6] = "\tOrdinary notes; \"quoted\"\r\n次 🎮 ";
        Assert.True(GameCatalogImportRules.TryMapGameRow(cells, CanonicalImportMap(), out var input, out _));
        Assert.Equal(cells[0], input!.Title);
        Assert.Equal(cells[6], input.Notes);
        cells[6] = string.Empty;
        Assert.True(GameCatalogImportRules.TryMapGameRow(cells, CanonicalImportMap(), out input, out _));
        Assert.Equal(string.Empty, input!.Notes);
    }

    [Fact]
    public void ReportsAllTypedConversionFailuresWithoutReturningARecord()
    {
        var cells = ValidImportCells();
        cells[1] = "bad";
        cells[2] = "bad";
        cells[4] = "bad";
        cells[5] = "bad";

        Assert.False(GameCatalogImportRules.TryMapGameRow(cells, CanonicalImportMap(), out var input, out var errors));

        Assert.Null(input);
        Assert.Equal(new[] { "DvdNumber", "Format", "Medium", "SizeBytes" }, errors.Select(error => error.Field).Order());
    }

    private static string[] ValidImportCells() => ["Game", "Usb", "", "/GAMES/game.rar", "ISO", "1024", ""];

    private static GameCatalogCsvHeaderMap CanonicalImportMap()
    {
        Assert.True(GameCatalogImportRules.TryMapHeadings(GameCatalogCsvSchema.Headings, out var map, out _));
        return map!;
    }

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
