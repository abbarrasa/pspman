using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogServiceTests
{
    private static readonly GameRecord SavedGame = new(
        Guid.NewGuid(),
        "Key of Heaven",
        GameMedium.Dvd,
        2,
        "/DevHook/GAMES/Key of Heaven.rar",
        GameFormat.Iso,
        1_073_741_824,
        "Second disc");

    [Fact]
    public void ListReturnsAnEmptyCatalogAsASuccess()
    {
        var service = new GameCatalogService(new FakeRepository());

        var result = service.List();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ListReturnsSavedRecordsWithoutChangingTheirFields()
    {
        var service = new GameCatalogService(new FakeRepository(SavedGame));

        var result = service.List();

        Assert.True(result.IsSuccess);
        Assert.Equal(SavedGame, Assert.Single(result.Value!));
        Assert.Null(result.Error);
    }

    [Fact]
    public void GetByIdReturnsSavedDetails()
    {
        var service = new GameCatalogService(new FakeRepository(SavedGame));

        var result = service.GetById(SavedGame.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(SavedGame, result.Value);
        Assert.Equal("/DevHook/GAMES/Key of Heaven.rar", result.Value!.Location);
        Assert.Equal(1_073_741_824, result.Value.SizeBytes);
        Assert.Null(result.Error);
    }

    [Fact]
    public void GetByIdReportsAnAbsentRecordWithoutAStorageFailure()
    {
        var service = new GameCatalogService(new FakeRepository(SavedGame));

        var result = service.GetById(Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ListFailureDoesNotReturnAnInventedEmptyCatalog()
    {
        var service = new GameCatalogService(new FakeRepository { FailReads = true });

        var result = service.List();

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void GetByIdFailureDoesNotLookLikeAnAbsentRecord()
    {
        var service = new GameCatalogService(new FakeRepository(SavedGame) { FailReads = true });

        var result = service.GetById(SavedGame.Id);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private sealed class FakeRepository(params GameRecord[] records) : IGameRecordRepository
    {
        public bool FailReads { get; init; }

        public IReadOnlyList<GameRecord> List()
        {
            if (FailReads)
            {
                throw new InvalidOperationException("Read failed.");
            }

            return records;
        }

        public GameRecord? GetById(Guid id)
        {
            if (FailReads)
            {
                throw new InvalidOperationException("Read failed.");
            }

            return records.FirstOrDefault(record => record.Id == id);
        }
    }
}
