using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogCorruptionTests
{
    private static readonly GameRecord Record = new(
        Guid.NewGuid(), "Stored", GameMedium.Usb, null, "/GAMES/Stored.rar",
        GameFormat.Iso, 1_024, null);

    private static readonly GameRecordInput Input = new(
        "New", GameMedium.Usb, null, "/GAMES/New.rar",
        GameFormat.Chd, 2_048, null);

    [Fact]
    public void CorruptReadIsDistinguishedFromOrdinaryReadFailure()
    {
        var corrupt = new GameCatalogService(new FailingRepository { Corrupt = true }).List();
        var unavailable = new GameCatalogService(new FailingRepository()).List();

        Assert.False(corrupt.IsSuccess);
        Assert.True(corrupt.IsCorrupt);
        Assert.Contains("corrupt", corrupt.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.False(unavailable.IsSuccess);
        Assert.False(unavailable.IsCorrupt);
    }

    [Fact]
    public void CorruptRecordReadIsReportedWithoutReturningARecord()
    {
        var result = new GameCatalogService(new FailingRepository { Corrupt = true }).GetById(Record.Id);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsCorrupt);
        Assert.Null(result.Value);
        Assert.Contains("corrupt", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptCreatePrecheckDoesNotWrite()
    {
        var repository = new FailingRepository { Corrupt = true };
        var result = new GameCatalogService(repository).Create(Input);

        Assert.True(result.IsCorrupt);
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact]
    public void CorruptEditAndDeleteReadsAreDistinguished()
    {
        var service = new GameCatalogService(new FailingRepository { Corrupt = true });

        Assert.True(service.Edit(Record.Id, Input).IsCorrupt);
        Assert.True(service.Delete(Record.Id).IsCorrupt);
    }

    private sealed class FailingRepository : IGameRecordRepository
    {
        public bool Corrupt { get; init; }

        public int CreateCalls { get; private set; }

        private Exception Failure() => Corrupt
            ? new InvalidDataException("Catalog data is corrupt.")
            : new IOException("Catalog is unavailable.");

        public IReadOnlyList<GameRecord> List() => throw Failure();

        public GameRecord? GetById(Guid id) => throw Failure();

        public void Create(GameRecord record) => CreateCalls++;

        public void Update(GameRecord record) => throw new NotSupportedException();

        public void Delete(Guid id) => throw new NotSupportedException();
    }
}
