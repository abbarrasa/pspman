using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogImportServiceTests
{
    private const string Header = "Title;Medium;DvdNumber;Location;Format;SizeBytes;Notes\r\n";
    private const string ValidRow = "Game;Usb;;/GAMES/game.rar;ISO;1024;\r\n";

    [Fact]
    public void AlreadyCancelledPreviewDoesNotAccessRepositoryOrSource()
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow, 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv", cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("The CSV import preview was cancelled.", result.Error);
        Assert.Equal(0, repository.ListCalls);
        Assert.Equal(0, source.OpenCalls);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("open")]
    [InlineData("capture")]
    [InlineData("read")]
    [InlineData("eof")]
    [InlineData("verify")]
    public void CancellationBetweenOperationsDiscardsPreviewAndDisposesAnyOpenedReader(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow, 1);
        switch (stage)
        {
            case "catalog": repository.AfterList = cancellation.Cancel; break;
            case "open": source.AfterOpen = cancellation.Cancel; break;
            case "capture": source.Reader.AfterCapture = cancellation.Cancel; break;
            case "read": source.Reader.AfterRead = count => { if (count > 0) cancellation.Cancel(); }; break;
            case "eof": source.Reader.AfterRead = count => { if (count == 0) cancellation.Cancel(); }; break;
            case "verify": source.Reader.AfterVerify = cancellation.Cancel; break;
        }

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv", cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("The CSV import preview was cancelled.", result.Error);
        Assert.False(result.IsCorrupt);
        Assert.Equal(stage == "catalog" ? 0 : 1, source.OpenCalls);
        Assert.Equal(stage != "catalog", source.Reader.Disposed);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData(Header)]
    [InlineData(Header + " \t\r\n\r\n")]
    [InlineData("Title;Medium;DvdNumber;Location;Format;SizeBytes;Notes")]
    public void HeaderOnlySourceHasZeroProposedChangesAndErrors(string csv)
    {
        var repository = new ReadOnlyRepository([
            new(Guid.NewGuid(), "Saved", GameMedium.Usb, null, "/saved.rar", GameFormat.Iso, 1, null)]);
        var source = new TestSource(csv, 1);

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal(0, result.Value.NewCount);
        Assert.Equal(0, result.Value.ModifiedCount);
        Assert.Equal(0, result.Value.ErrorCount);
        Assert.Equal(1, repository.ListCalls);
        Assert.Equal(0, repository.WriteCalls);
        Assert.True(source.Reader.Disposed);
    }

    [Theory]
    [InlineData("", "does not contain a header")]
    [InlineData(" \t\n\r\n", "does not contain a header")]
    [InlineData("Title;Notes\n", "Missing required CSV headings")]
    [InlineData("Title;Medium;DvdNumber;Location;Format;SizeBytes;Notes;Unknown Heading\n", "must not contain whitespace")]
    [InlineData(Header + ValidRow + "\"unfinished", "Unterminated quoted field")]
    [InlineData(Header + ValidRow + "Bad\"quote;Usb", "Unexpected quote")]
    public void FatalCsvErrorsDiscardAllProposalsAndReleaseTheReader(string csv, string reason)
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(csv, 1);

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(result.IsCorrupt);
        Assert.Contains(reason, result.Error);
        Assert.True(source.Reader.Disposed);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("capture")]
    [InlineData("read")]
    [InlineData("verify")]
    public void SourceFailuresNeverReturnAPartialPreviewOrMarkTheCatalogCorrupt(string stage)
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow + ValidRow, 1);
        var failure = new GameCatalogImportReadException("Simulated source failure.", new IOException());
        switch (stage)
        {
            case "open": source.OpenFailure = failure; break;
            case "capture": source.Reader.CaptureFailure = failure; break;
            case "read":
                source.Reader.ReadFailure = failure;
                source.Reader.FailAtPosition = Header.Length + ValidRow.Length;
                break;
            case "verify": source.Reader.VerificationFailure = failure; break;
        }

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(failure.Message, result.Error);
        Assert.False(result.IsCorrupt);
        Assert.Equal(stage != "open", source.Reader.Disposed);
        if (stage == "read") Assert.Equal(Header.Length + ValidRow.Length, source.Reader.CharactersRead);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CatalogCorruptionIsDistinguishedFromOrdinaryReadFailures(bool corrupt)
    {
        var repository = new ReadOnlyRepository([])
        {
            ListFailure = corrupt ? new InvalidDataException("Corrupt catalog.") : new IOException("Unavailable catalog.")
        };
        var source = new TestSource(Header + ValidRow, 1);

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(corrupt, result.IsCorrupt);
        Assert.NotNull(result.Error);
        Assert.Equal(0, source.OpenCalls);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public void InvalidSourceDataIsNotMistakenForCatalogCorruption()
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow, 1) { OpenFailure = new InvalidDataException("Source data error.") };

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(result.IsCorrupt);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public void PreviewsFinalValidCandidatesAndRowErrorsWithoutWriting(int chunkSize)
    {
        var editedId = Guid.NewGuid();
        GameRecord[] existing =
        [
            new(Guid.NewGuid(), "Same", GameMedium.Usb, null, "/GAMES/same.rar", GameFormat.Iso, 1024, null),
            new(editedId, "Original", GameMedium.Usb, null, "/GAMES/edit.rar", GameFormat.Iso, 1024, "old notes")
        ];
        var repository = new ReadOnlyRepository(existing);
        var source = new TestSource(Header
            + "New first;Usb;;/GAMES/new.rar;ISO;1024;old\r\n"
            + "Same;Usb;;/GAMES/same.rar;ISO;1024;\r\n"
            + "Edited;Usb;;/GAMES/edit.rar;CHD;2048;\r\n"
            + "New last;Usb;;/GAMES/new.rar;CHD;3072;\"two\r\nlines\"\r\n"
            + "Invalid last;Usb;;/GAMES/new.rar;ISO;bad;\r\n"
            + "short\r\n"
            + "Other;MemoryCard;;/GAMES/other.rar;ISO;2048;\r\n", chunkSize);
        var service = new GameCatalogImportService(repository, source);

        var result = service.Preview("chosen.csv");

        Assert.True(result.IsSuccess, result.Error);
        var preview = Assert.IsType<GameCatalogImportPreview>(result.Value);
        Assert.Equal(2, preview.NewCount);
        Assert.Equal(1, preview.ModifiedCount);
        Assert.Equal(2, preview.ErrorCount);
        Assert.Equal(new long[] { 5, 9 }, preview.NewGames.Select(candidate => candidate.StartLine));
        Assert.Equal("New last", preview.NewGames[0].Input.Title);
        Assert.Equal("two\r\nlines", preview.NewGames[0].Input.Notes);
        var edit = Assert.Single(preview.ModifiedGames);
        Assert.Equal(editedId, edit.ExistingId);
        Assert.Equal(4, edit.StartLine);
        Assert.Equal("", edit.Input.Notes);
        Assert.Equal(new long[] { 7, 8 }, preview.RowErrors.Select(error => error.StartLine));
        Assert.Equal("SizeBytes", Assert.Single(preview.RowErrors[0].ValidationErrors).Field);
        Assert.Equal("chosen.csv", preview.SourcePath);
        Assert.Equal(source.Reader.Fingerprint, preview.Fingerprint);
        Assert.Equal(1, repository.ListCalls);
        Assert.Equal(0, repository.WriteCalls);
        Assert.Equal(1, source.OpenCalls);
        Assert.Equal("chosen.csv", source.OpenedPath);
        Assert.Equal(1, source.Reader.CaptureCalls);
        Assert.Equal(1, source.Reader.VerifyCalls);
        Assert.True(source.Reader.EndReached);
        Assert.True(source.Reader.Disposed);
        Assert.Equal(existing, repository.Records);
    }

    [Fact]
    public void IdenticalMatchesAreAbsentFromEveryPreviewCount()
    {
        var repository = new ReadOnlyRepository([
            new(Guid.NewGuid(), "Same", GameMedium.Usb, null, "/GAMES/same.rar", GameFormat.Iso, 1024, null)]);
        var source = new TestSource(Header + "Same;Usb;;/GAMES/same.rar;ISO;1024;\r\n", 1);

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(0, result.Value.NewCount);
        Assert.Equal(0, result.Value.ModifiedCount);
        Assert.Equal(0, result.Value.ErrorCount);
        Assert.Empty(result.Value.NewGames);
        Assert.Empty(result.Value.ModifiedGames);
        Assert.Empty(result.Value.RowErrors);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public void CombinesReorderedRepeatedAndExtraColumnsWithFormulaProtection()
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource("Notes;Title;Medium;DvdNumber;Location;Format;SizeBytes;Ignored;TITLE\n"
            + "\"\t=SUM(1;2)\";superseded;Usb;;/GAMES/offline.rar;ISO;1024;discarded;\"\t+Game\"\n", 1);

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.True(result.IsSuccess, result.Error);
        var preview = Assert.IsType<GameCatalogImportPreview>(result.Value);
        var added = Assert.Single(preview.NewGames);
        Assert.Equal("+Game", added.Input.Title);
        Assert.Equal("=SUM(1;2)", added.Input.Notes);
        Assert.Equal(0, preview.ErrorCount);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public void DoesNotPublishAPreviewWhenFinalSourceVerificationFails()
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + "Game;Usb;;/GAMES/game.rar;ISO;1024;\n", 7);
        source.Reader.Changed = true;

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.Equal(1, source.Reader.VerifyCalls);
        Assert.True(source.Reader.Disposed);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public void PreviewCollectionsCannotBeModifiedByTheCaller()
    {
        var source = new TestSource(Header + "Game;Usb;;/GAMES/game.rar;ISO;1024;\nshort\n", 7);
        var result = new GameCatalogImportService(new ReadOnlyRepository([]), source).Preview("chosen.csv");
        var preview = Assert.IsType<GameCatalogImportPreview>(result.Value);

        Assert.Throws<NotSupportedException>(() => ((IList<GameCatalogImportCandidate>)preview.NewGames).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<GameCatalogImportModification>)preview.ModifiedGames).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<GameCatalogImportRowError>)preview.RowErrors).Clear());
    }

    private sealed class ReadOnlyRepository(IReadOnlyList<GameRecord> records) : IGameRecordRepository
    {
        public IReadOnlyList<GameRecord> Records { get; } = records;
        public int ListCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public Exception? ListFailure { get; set; }
        public Action? AfterList { get; set; }
        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;
            Assert.Equal(1, ListCalls);
            if (ListFailure is not null) throw ListFailure;
            AfterList?.Invoke();
            return Records;
        }
        public GameRecord? GetById(Guid id) => throw new InvalidOperationException("Preview must use the catalog snapshot.");
        public void Create(GameRecord record) => RejectWrite();
        public void Update(GameRecord record) => RejectWrite();
        public void Delete(Guid id) => RejectWrite();
        private void RejectWrite()
        {
            WriteCalls++;
            throw new InvalidOperationException("Preview must never write to the catalog.");
        }
    }

    private sealed class TestSource(string text, int chunkSize) : IGameCatalogImportSource
    {
        public TestReader Reader { get; } = new(text, chunkSize);
        public int OpenCalls { get; private set; }
        public string? OpenedPath { get; private set; }
        public Exception? OpenFailure { get; set; }
        public Action? AfterOpen { get; set; }
        public IGameCatalogImportReader Open(string sourcePath)
        {
            OpenCalls++;
            OpenedPath = sourcePath;
            if (OpenFailure is not null) throw OpenFailure;
            AfterOpen?.Invoke();
            return Reader;
        }
    }

    private sealed class TestReader(string text, int chunkSize) : IGameCatalogImportReader
    {
        private int position;
        public GameCatalogImportFingerprint Fingerprint { get; } = new("test-content-fingerprint");
        public bool Disposed { get; private set; }
        public bool EndReached { get; private set; }
        public bool Changed { get; set; }
        public int CaptureCalls { get; private set; }
        public int VerifyCalls { get; private set; }
        public int CharactersRead => position;
        public int? FailAtPosition { get; set; }
        public Exception? ReadFailure { get; set; }
        public Exception? CaptureFailure { get; set; }
        public Exception? VerificationFailure { get; set; }
        public Action<int>? AfterRead { get; set; }
        public Action? AfterCapture { get; set; }
        public Action? AfterVerify { get; set; }
        public int Read(Span<char> buffer)
        {
            Assert.False(Disposed);
            if (FailAtPosition is int failurePosition && position >= failurePosition && ReadFailure is not null)
                throw ReadFailure;
            var count = Math.Min(Math.Min(chunkSize, buffer.Length), text.Length - position);
            text.AsSpan(position, count).CopyTo(buffer);
            position += count;
            EndReached = count == 0;
            AfterRead?.Invoke(count);
            return count;
        }
        public GameCatalogImportFingerprint CaptureFingerprint()
        {
            Assert.Equal(0, position);
            CaptureCalls++;
            if (CaptureFailure is not null) throw CaptureFailure;
            AfterCapture?.Invoke();
            return Fingerprint;
        }
        public void VerifyUnchanged(GameCatalogImportFingerprint expected)
        {
            VerifyCalls++;
            Assert.True(EndReached);
            Assert.Equal(Fingerprint, expected);
            if (VerificationFailure is not null) throw VerificationFailure;
            if (Changed) throw new GameCatalogImportSourceChangedException();
            AfterVerify?.Invoke();
        }
        public void Dispose() => Disposed = true;
    }
}
