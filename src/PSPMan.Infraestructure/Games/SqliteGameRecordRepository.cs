using Microsoft.Data.Sqlite;
using PSPMan.Core.Games;

namespace PSPMan.Infraestructure.Games;

public sealed class SqliteGameRecordRepository : IGameRecordRepository
{
    private const string SelectFields =
        "Id, Title, Medium, DvdNumber, Location, Format, SizeBytes, Notes";

    private readonly string _connectionString;

    public SqliteGameRecordRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var isNewCatalog = !File.Exists(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = isNewCatalog
            ? """
                CREATE TABLE GameRecords (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Title TEXT NOT NULL,
                    Medium INTEGER NOT NULL,
                    DvdNumber INTEGER,
                    Location TEXT NOT NULL,
                    Format INTEGER NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    Notes TEXT
                )
                """
            : $"SELECT {SelectFields} FROM GameRecords LIMIT 0";
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<GameRecord> List()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectFields} FROM GameRecords ORDER BY rowid";
        using var reader = command.ExecuteReader();

        var records = new List<GameRecord>();
        while (reader.Read())
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public GameRecord? GetById(Guid id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectFields} FROM GameRecords WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    public void Create(GameRecord record) =>
        throw new NotSupportedException("Catalog creation is not implemented yet.");

    public void Update(GameRecord record) =>
        throw new NotSupportedException("Catalog updates are not implemented yet.");

    public void Delete(Guid id) =>
        throw new NotSupportedException("Catalog deletion is not implemented yet.");

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static GameRecord ReadRecord(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        (GameMedium)reader.GetInt32(2),
        reader.IsDBNull(3) ? null : reader.GetInt32(3),
        reader.GetString(4),
        (GameFormat)reader.GetInt32(5),
        reader.GetInt64(6),
        reader.IsDBNull(7) ? null : reader.GetString(7));
}
