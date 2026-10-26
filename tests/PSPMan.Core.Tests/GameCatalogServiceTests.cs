using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogServiceTests
{
    private static readonly GameRecordInput ValidInput = new(
        "New Game",
        GameMedium.Usb,
        0,
        "/DisconnectedMedium/GAMES/New Game.rar",
        GameFormat.Chd,
        5_000_000_000,
        "Stored offline");

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

    [Fact]
    public void CreateSavesAValidRecordWithoutCheckingTheMedium()
    {
        var repository = new FakeRepository();
        var service = new GameCatalogService(repository);

        var result = service.Create(ValidInput);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
        Assert.Equal(ValidInput.Title, result.Value.Title);
        Assert.Equal(ValidInput.Location, result.Value.Location);
        Assert.Equal(ValidInput.SizeBytes, result.Value.SizeBytes);
        Assert.Null(result.Value.DvdNumber);
        Assert.Equal(result.Value, Assert.Single(repository.Records));
        Assert.Equal(result.Value, Assert.Single(service.List().Value!));
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public void CreateReturnsFieldErrorsBeforeReadingOrWriting()
    {
        var repository = new FakeRepository();
        var service = new GameCatalogService(repository);

        var result = service.Create(ValidInput with
        {
            Title = "\t\u00A0",
            Location = "relative/game.rar",
            SizeBytes = 0
        });

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains(result.ValidationErrors, error => error.Field == nameof(GameRecordInput.Title));
        Assert.Contains(result.ValidationErrors, error => error.Field == nameof(GameRecordInput.Location));
        Assert.Contains(result.ValidationErrors, error => error.Field == nameof(GameRecordInput.SizeBytes));
        Assert.Equal(0, repository.ListCalls);
        Assert.Equal(0, repository.CreateCalls);
        Assert.Empty(repository.Records);
    }

    [Fact]
    public void CreateRejectsDuplicateIdentityDespiteOtherFieldChanges()
    {
        var repository = new FakeRepository(SavedGame);
        var service = new GameCatalogService(repository);
        var duplicate = new GameRecordInput(
            "Different title",
            GameMedium.Dvd,
            SavedGame.DvdNumber,
            SavedGame.Location,
            GameFormat.Chd,
            42,
            "Different notes");

        var result = service.Create(duplicate);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains("already", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, repository.CreateCalls);
        Assert.Equal(SavedGame, Assert.Single(repository.Records));
    }

    [Fact]
    public void CreateAcceptsLocationThatDiffersOnlyByCase()
    {
        var repository = new FakeRepository(SavedGame);
        var service = new GameCatalogService(repository);
        var candidate = new GameRecordInput(
            SavedGame.Title,
            SavedGame.Medium,
            SavedGame.DvdNumber,
            "/DevHook/GAMES/key of heaven.rar",
            SavedGame.Format,
            SavedGame.SizeBytes,
            SavedGame.Notes);

        var result = service.Create(candidate);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, repository.Records.Count);
    }

    [Fact]
    public void CreatePreservesPositiveDvdNumber()
    {
        var repository = new FakeRepository();
        var service = new GameCatalogService(repository);
        var dvd = ValidInput with { Medium = GameMedium.Dvd, DvdNumber = 2 };

        var result = service.Create(dvd);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, Assert.Single(repository.Records).DvdNumber);
    }

    [Fact]
    public void CreateReportsReadFailureWithoutWriting()
    {
        var repository = new FakeRepository { FailReads = true };
        var service = new GameCatalogService(repository);

        var result = service.Create(ValidInput);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(0, repository.CreateCalls);
        Assert.Empty(repository.Records);
    }

    [Fact]
    public void CreateReportsWriteFailureWithoutClaimingSuccess()
    {
        var repository = new FakeRepository(SavedGame) { FailWrites = true };
        var service = new GameCatalogService(repository);

        var result = service.Create(ValidInput);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(SavedGame, Assert.Single(repository.Records));
    }

    private sealed class FakeRepository(params GameRecord[] records) : IGameRecordRepository
    {
        private readonly List<GameRecord> _records = [.. records];

        public bool FailReads { get; init; }

        public bool FailWrites { get; init; }

        public int ListCalls { get; private set; }

        public int CreateCalls { get; private set; }

        public IReadOnlyList<GameRecord> Records => _records;

        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;

            if (FailReads)
            {
                throw new InvalidOperationException("Read failed.");
            }

            return _records.ToArray();
        }

        public GameRecord? GetById(Guid id)
        {
            if (FailReads)
            {
                throw new InvalidOperationException("Read failed.");
            }

            return _records.FirstOrDefault(record => record.Id == id);
        }

        public void Create(GameRecord record)
        {
            CreateCalls++;

            if (FailWrites)
            {
                throw new InvalidOperationException("Write failed.");
            }

            _records.Add(record);
        }
    }
}
