using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogServiceTests
{
    [Fact]
    public void ConstructorRejectsMissingRepository()
    {
        Assert.Throws<ArgumentNullException>(() => new GameCatalogService(null!));
    }

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

    [Fact]
    public void EditUpdatesOwnRecordWithoutTreatingItAsADuplicate()
    {
        var repository = new FakeRepository(SavedGame);
        var service = new GameCatalogService(repository);
        var input = InputFrom(SavedGame) with
        {
            Title = "Key of Heaven (corrected)",
            Notes = "Updated notes"
        };

        var result = service.Edit(SavedGame.Id, input);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(SavedGame.Id, result.Value.Id);
        Assert.Equal(input.Title, result.Value.Title);
        Assert.Equal(input.Notes, result.Value.Notes);
        Assert.Equal(result.Value, Assert.Single(repository.Records));
        Assert.Equal(result.Value, service.GetById(SavedGame.Id).Value);
        Assert.Equal(1, repository.UpdateCalls);
    }

    [Fact]
    public void EditAllowsAnOfflineLocationWithoutCheckingForItsFile()
    {
        var repository = new FakeRepository(SavedGame);
        var service = new GameCatalogService(repository);
        var input = InputFrom(SavedGame) with { Location = "/DisconnectedMedium/GAMES/Key of Heaven.rar" };

        var result = service.Edit(SavedGame.Id, input);

        Assert.True(result.IsSuccess);
        Assert.Equal(input.Location, Assert.Single(repository.Records).Location);
    }

    [Fact]
    public void EditRejectsIdentityOfAnotherRecordAndLeavesBothUnchanged()
    {
        var other = SavedGame with
        {
            Id = Guid.NewGuid(),
            Medium = GameMedium.Usb,
            DvdNumber = null,
            Location = ValidInput.Location
        };
        var repository = new FakeRepository(SavedGame, other);
        var service = new GameCatalogService(repository);

        var result = service.Edit(SavedGame.Id, ValidInput);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains("already", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Contains(SavedGame, repository.Records);
        Assert.Contains(other, repository.Records);
    }

    [Fact]
    public void EditReturnsFieldErrorsBeforeAnyRepositoryCall()
    {
        var repository = new FakeRepository(SavedGame);
        var service = new GameCatalogService(repository);
        var input = InputFrom(SavedGame) with { Title = "\u00A0\t", SizeBytes = 0 };

        var result = service.Edit(SavedGame.Id, input);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.ValidationErrors, error => error.Field == nameof(GameRecordInput.Title));
        Assert.Contains(result.ValidationErrors, error => error.Field == nameof(GameRecordInput.SizeBytes));
        Assert.Equal(0, repository.GetByIdCalls);
        Assert.Equal(0, repository.ListCalls);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal(SavedGame, Assert.Single(repository.Records));
    }

    [Fact]
    public void EditOfMissingRecordFailsWithoutCreatingOne()
    {
        var repository = new FakeRepository(SavedGame);
        var service = new GameCatalogService(repository);

        var result = service.Edit(Guid.NewGuid(), ValidInput);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal(SavedGame, Assert.Single(repository.Records));
    }

    [Fact]
    public void EditReportsReadFailureWithoutWriting()
    {
        var repository = new FakeRepository(SavedGame) { FailReads = true };
        var service = new GameCatalogService(repository);

        var result = service.Edit(SavedGame.Id, InputFrom(SavedGame));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal(SavedGame, Assert.Single(repository.Records));
    }

    [Fact]
    public void EditReportsDuplicateCheckFailureWithoutWriting()
    {
        var repository = new FakeRepository(SavedGame) { FailLists = true };
        var service = new GameCatalogService(repository);

        var result = service.Edit(SavedGame.Id, InputFrom(SavedGame));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal(SavedGame, Assert.Single(repository.Records));
    }

    [Fact]
    public void EditReportsWriteFailureAndKeepsSavedRecord()
    {
        var repository = new FakeRepository(SavedGame) { FailWrites = true };
        var service = new GameCatalogService(repository);

        var result = service.Edit(SavedGame.Id, InputFrom(SavedGame) with { Title = "Changed" });

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(SavedGame, Assert.Single(repository.Records));
    }

    [Fact]
    public void EditNormalizesZeroDvdNumberWhenChangingToUsb()
    {
        var repository = new FakeRepository(SavedGame);
        var service = new GameCatalogService(repository);

        var result = service.Edit(SavedGame.Id, ValidInput);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DvdNumber);
        Assert.Null(Assert.Single(repository.Records).DvdNumber);
    }

    [Fact]
    public void DeleteRemovesOnlyTheSelectedCatalogRecord()
    {
        var other = SavedGame with { Id = Guid.NewGuid(), Location = "/Other/game.rar" };
        var repository = new FakeRepository(SavedGame, other);
        var service = new GameCatalogService(repository);

        var result = service.Delete(SavedGame.Id);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Null(result.Error);
        Assert.Equal(1, repository.DeleteCalls);
        Assert.Equal(other, Assert.Single(repository.Records));
    }

    [Fact]
    public void DeleteOfMissingRecordReportsFailureWithoutWriting()
    {
        var repository = new FakeRepository(SavedGame);
        var service = new GameCatalogService(repository);

        var result = service.Delete(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(0, repository.DeleteCalls);
        Assert.Equal(SavedGame, Assert.Single(repository.Records));
    }

    [Fact]
    public void DeleteReportsReadFailureWithoutWriting()
    {
        var repository = new FakeRepository(SavedGame) { FailReads = true };
        var service = new GameCatalogService(repository);

        var result = service.Delete(SavedGame.Id);

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(0, repository.DeleteCalls);
        Assert.Equal(SavedGame, Assert.Single(repository.Records));
    }

    [Fact]
    public void DeleteReportsWriteFailureAndKeepsSavedRecord()
    {
        var repository = new FakeRepository(SavedGame) { FailWrites = true };
        var service = new GameCatalogService(repository);

        var result = service.Delete(SavedGame.Id);

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(1, repository.DeleteCalls);
        Assert.Equal(SavedGame, Assert.Single(repository.Records));
    }

    private static GameRecordInput InputFrom(GameRecord record) => new(
        record.Title,
        record.Medium,
        record.DvdNumber,
        record.Location,
        record.Format,
        record.SizeBytes,
        record.Notes);

    private sealed class FakeRepository(params GameRecord[] records) : IGameRecordRepository
    {
        private readonly List<GameRecord> _records = [.. records];

        public bool FailReads { get; init; }

        public bool FailLists { get; init; }

        public bool FailWrites { get; init; }

        public int ListCalls { get; private set; }

        public int CreateCalls { get; private set; }

        public int GetByIdCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public IReadOnlyList<GameRecord> Records => _records;

        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;

            if (FailReads || FailLists)
            {
                throw new InvalidOperationException("Read failed.");
            }

            return _records.ToArray();
        }

        public GameRecord? GetById(Guid id)
        {
            GetByIdCalls++;

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

        public void Update(GameRecord record)
        {
            UpdateCalls++;

            if (FailWrites)
            {
                throw new InvalidOperationException("Write failed.");
            }

            var index = _records.FindIndex(existing => existing.Id == record.Id);
            if (index < 0)
            {
                throw new InvalidOperationException("Record not found.");
            }

            _records[index] = record;
        }

        public void Delete(Guid id)
        {
            DeleteCalls++;

            if (FailWrites)
            {
                throw new InvalidOperationException("Write failed.");
            }

            var index = _records.FindIndex(record => record.Id == id);
            if (index < 0)
            {
                throw new InvalidOperationException("Record not found.");
            }

            _records.RemoveAt(index);
        }
    }
}
