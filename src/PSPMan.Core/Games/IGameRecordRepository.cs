namespace PSPMan.Core.Games;

public interface IGameRecordRepository
{
    IReadOnlyList<GameRecord> List();

    GameRecord? GetById(Guid id);
}
