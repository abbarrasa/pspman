namespace PSPMan.Core.Games;

public sealed class GameCatalogResult<T>
{
    private GameCatalogResult(
        bool isSuccess,
        T? value,
        string? error,
        IReadOnlyList<GameRecordValidationError> validationErrors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        ValidationErrors = validationErrors;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public string? Error { get; }

    public IReadOnlyList<GameRecordValidationError> ValidationErrors { get; }

    public static GameCatalogResult<T> Success(T? value) => new(true, value, null, []);

    public static GameCatalogResult<T> Failure(string error) => new(false, default, error, []);

    public static GameCatalogResult<T> InvalidInput(IReadOnlyList<GameRecordValidationError> errors) =>
        new(false, default, "Invalid game record.", errors.ToArray());
}

public sealed class GameCatalogService(IGameRecordRepository repository)
{
    private readonly IGameRecordRepository _repository = repository
        ?? throw new ArgumentNullException(nameof(repository));

    public GameCatalogResult<IReadOnlyList<GameRecord>> List()
    {
        try
        {
            return GameCatalogResult<IReadOnlyList<GameRecord>>.Success(_repository.List());
        }
        catch (Exception)
        {
            return GameCatalogResult<IReadOnlyList<GameRecord>>.Failure("Unable to list game records.");
        }
    }

    public GameCatalogResult<GameRecord> GetById(Guid id)
    {
        try
        {
            return GameCatalogResult<GameRecord>.Success(_repository.GetById(id));
        }
        catch (Exception)
        {
            return GameCatalogResult<GameRecord>.Failure("Unable to read the game record.");
        }
    }

    public GameCatalogResult<GameRecord> Create(GameRecordInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var validationErrors = GameRecordRules.Validate(input);
        if (validationErrors.Count > 0)
        {
            return GameCatalogResult<GameRecord>.InvalidInput(validationErrors);
        }

        try
        {
            if (_repository.List().Any(existing => GameRecordRules.HasSameIdentity(input, existing)))
            {
                return GameCatalogResult<GameRecord>.Failure(
                    "A game record with the same medium, location, and DVD number already exists.");
            }

            var record = new GameRecord(
                Guid.NewGuid(),
                input.Title,
                input.Medium,
                GameRecordRules.NormalizeDvdNumber(input.DvdNumber),
                input.Location,
                input.Format,
                input.SizeBytes,
                input.Notes);

            _repository.Create(record);
            return GameCatalogResult<GameRecord>.Success(record);
        }
        catch (Exception)
        {
            return GameCatalogResult<GameRecord>.Failure("Unable to save the game record.");
        }
    }

    public GameCatalogResult<GameRecord> Edit(Guid id, GameRecordInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var validationErrors = GameRecordRules.Validate(input);
        if (validationErrors.Count > 0)
        {
            return GameCatalogResult<GameRecord>.InvalidInput(validationErrors);
        }

        try
        {
            var existing = _repository.GetById(id);
            if (existing is null)
            {
                return GameCatalogResult<GameRecord>.Failure("Game record not found.");
            }

            if (_repository.List().Any(other =>
                    other.Id != id && GameRecordRules.HasSameIdentity(input, other)))
            {
                return GameCatalogResult<GameRecord>.Failure(
                    "A game record with the same medium, location, and DVD number already exists.");
            }

            var updated = existing with
            {
                Title = input.Title,
                Medium = input.Medium,
                DvdNumber = GameRecordRules.NormalizeDvdNumber(input.DvdNumber),
                Location = input.Location,
                Format = input.Format,
                SizeBytes = input.SizeBytes,
                Notes = input.Notes
            };

            _repository.Update(updated);
            return GameCatalogResult<GameRecord>.Success(updated);
        }
        catch (Exception)
        {
            return GameCatalogResult<GameRecord>.Failure("Unable to update the game record.");
        }
    }
}
