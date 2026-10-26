namespace PSPMan.Core.Games;

public interface IGameRecordRepository
{
    IReadOnlyList<GameRecord> List();

    GameRecord? GetById(Guid id);

    void Create(GameRecord record);

    void Update(GameRecord record);

    void Delete(Guid id);
}
