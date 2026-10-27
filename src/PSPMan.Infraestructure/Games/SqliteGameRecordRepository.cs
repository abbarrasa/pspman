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

        using var transaction = connection.BeginTransaction();
        using var indexCommand = connection.CreateCommand();
        indexCommand.Transaction = transaction;
        indexCommand.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_GameRecords_Dvd
            ON GameRecords (Medium, DvdNumber, Location COLLATE BINARY)
            WHERE Medium = 0
            """;
        indexCommand.ExecuteNonQuery();
        indexCommand.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_GameRecords_NonDvd
            ON GameRecords (Medium, Location COLLATE BINARY)
            WHERE Medium <> 0
            """;
        indexCommand.ExecuteNonQuery();
        transaction.Commit();
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

    public void Create(GameRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GameRecords (Id, Title, Medium, DvdNumber, Location, Format, SizeBytes, Notes)
            VALUES ($id, $title, $medium, $dvdNumber, $location, $format, $sizeBytes, $notes)
            """;
        AddRecordParameters(command, record);
        command.ExecuteNonQuery();
    }

    public void Update(GameRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE GameRecords
            SET Title = $title, Medium = $medium, DvdNumber = $dvdNumber,
                Location = $location, Format = $format, SizeBytes = $sizeBytes,
                Notes = $notes
            WHERE Id = $id
            """;
        AddRecordParameters(command, record);
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException("Game record not found.");
        }
    }

    public void Delete(Guid id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM GameRecords WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException("Game record not found.");
        }
    }

    private static void AddRecordParameters(SqliteCommand command, GameRecord record)
    {
        command.Parameters.AddWithValue("$id", record.Id.ToString());
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$medium", (int)record.Medium);
        command.Parameters.AddWithValue("$dvdNumber", (object?)record.DvdNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$location", record.Location);
        command.Parameters.AddWithValue("$format", (int)record.Format);
        command.Parameters.AddWithValue("$sizeBytes", record.SizeBytes);
        command.Parameters.AddWithValue("$notes", (object?)record.Notes ?? DBNull.Value);
    }

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
