using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogImportServiceTests
{
    private const string Header = "Title;Medium;DvdNumber;Location;Format;SizeBytes;Notes\r\n";

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
        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;
            Assert.Equal(1, ListCalls);
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
        public IGameCatalogImportReader Open(string sourcePath)
        {
            OpenCalls++;
            OpenedPath = sourcePath;
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
        public int Read(Span<char> buffer)
        {
            Assert.False(Disposed);
            var count = Math.Min(Math.Min(chunkSize, buffer.Length), text.Length - position);
            text.AsSpan(position, count).CopyTo(buffer);
            position += count;
            EndReached = count == 0;
            return count;
        }
        public GameCatalogImportFingerprint CaptureFingerprint()
        {
            Assert.Equal(0, position);
            CaptureCalls++;
            return Fingerprint;
        }
        public void VerifyUnchanged(GameCatalogImportFingerprint expected)
        {
            VerifyCalls++;
            Assert.True(EndReached);
            Assert.Equal(Fingerprint, expected);
            if (Changed) throw new GameCatalogImportSourceChangedException();
        }
        public void Dispose() => Disposed = true;
    }
}
