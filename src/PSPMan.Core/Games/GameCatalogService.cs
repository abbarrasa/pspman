namespace PSPMan.Core.Games;

public sealed class GameCatalogResult<T>
{
    private GameCatalogResult(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public string? Error { get; }

    public static GameCatalogResult<T> Success(T? value) => new(true, value, null);

    public static GameCatalogResult<T> Failure(string error) => new(false, default, error);
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
}
