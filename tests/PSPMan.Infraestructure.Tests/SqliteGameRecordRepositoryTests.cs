using Microsoft.Data.Sqlite;
using PSPMan.Core.Games;
using PSPMan.Infraestructure.Games;

namespace PSPMan.Infraestructure.Tests;

public class SqliteGameRecordRepositoryTests
{
    private static readonly GameRecord DvdGame = new(
        Guid.NewGuid(),
        "Key of Heaven",
        GameMedium.Dvd,
        2,
        "/DevHook/GAMES/Key of Heaven.rar",
        GameFormat.Iso,
        1_073_741_824,
        "Second disc");

    private static readonly GameRecord UsbGame = new(
        Guid.NewGuid(),
        "Offline Game",
        GameMedium.Usb,
        null,
        "/GAMES/Offline Game.rar",
        GameFormat.Chd,
        5_000_000_000,
        null);

    [Fact]
    public void FreshCatalogCreatesSchemaAndListsNoRecords()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);

        Assert.True(File.Exists(catalog.Path));
        Assert.Empty(repository.List());
        Assert.Null(repository.GetById(Guid.NewGuid()));
    }

    [Fact]
    public void ReadsAllRecordFieldsAfterReopeningCatalog()
    {
        using var catalog = new TemporaryCatalog();
        _ = new SqliteGameRecordRepository(catalog.Path);
        Seed(catalog.Path, DvdGame);
        Seed(catalog.Path, UsbGame);

        var reopened = new SqliteGameRecordRepository(catalog.Path);
        var records = reopened.List();

        Assert.Equal(2, records.Count);
        Assert.Equal(DvdGame, reopened.GetById(DvdGame.Id));
        Assert.Equal(UsbGame, reopened.GetById(UsbGame.Id));
        Assert.Contains(DvdGame, records);
        Assert.Contains(UsbGame, records);
        Assert.Null(reopened.GetById(Guid.NewGuid()));
    }

    [Fact]
    public void ExistingUnreadableCatalogIsNotReplaced()
    {
        using var catalog = new TemporaryCatalog();
        var original = new byte[] { 0, 1, 2, 3, 4, 5 };
        File.WriteAllBytes(catalog.Path, original);

        Assert.ThrowsAny<Exception>(() => new SqliteGameRecordRepository(catalog.Path));
        Assert.Equal(original, File.ReadAllBytes(catalog.Path));
    }

    private static void Seed(string path, GameRecord record)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GameRecords (Id, Title, Medium, DvdNumber, Location, Format, SizeBytes, Notes)
            VALUES ($id, $title, $medium, $dvdNumber, $location, $format, $sizeBytes, $notes)
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString());
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$medium", (int)record.Medium);
        command.Parameters.AddWithValue("$dvdNumber", (object?)record.DvdNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$location", record.Location);
        command.Parameters.AddWithValue("$format", (int)record.Format);
        command.Parameters.AddWithValue("$sizeBytes", record.SizeBytes);
        command.Parameters.AddWithValue("$notes", (object?)record.Notes ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private sealed class TemporaryCatalog : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "PSPMan.Tests", Guid.NewGuid().ToString());

        public TemporaryCatalog() => Directory.CreateDirectory(_directory);

        public string Path => System.IO.Path.Combine(_directory, "catalog.db");

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
