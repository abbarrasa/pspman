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

    [Fact]
    public void CreatePersistsAllFieldsAfterReopening()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        var game = DvdGame with { Title = "Game'); DROP TABLE GameRecords; --" };

        repository.Create(game);

        var reopened = new SqliteGameRecordRepository(catalog.Path);
        Assert.Equal(game, reopened.GetById(game.Id));
        Assert.Equal(game, Assert.Single(reopened.List()));
    }

    [Fact]
    public void DuplicateDvdKeyIsRejectedWithoutChangingSavedRow()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        repository.Create(DvdGame);
        var duplicate = DvdGame with
        {
            Id = Guid.NewGuid(),
            Title = "Other title",
            Format = GameFormat.Chd,
            Notes = null
        };

        Assert.Throws<SqliteException>(() => repository.Create(duplicate));

        var reopened = new SqliteGameRecordRepository(catalog.Path);
        Assert.Equal(DvdGame, Assert.Single(reopened.List()));
    }

    [Theory]
    [InlineData(GameMedium.Usb)]
    [InlineData(GameMedium.MemoryCard)]
    public void DuplicateNonDvdKeyIsRejectedWithoutChangingSavedRow(GameMedium medium)
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        var original = UsbGame with { Medium = medium };
        repository.Create(original);
        var duplicate = original with { Id = Guid.NewGuid(), Title = "Other title" };

        Assert.Throws<SqliteException>(() => repository.Create(duplicate));

        var reopened = new SqliteGameRecordRepository(catalog.Path);
        Assert.Equal(original, Assert.Single(reopened.List()));
    }

    [Theory]
    [InlineData(GameMedium.Dvd)]
    [InlineData(GameMedium.Usb)]
    public void LocationCaseDifferenceRemainsDistinct(GameMedium medium)
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        var original = DvdGame with
        {
            Medium = medium,
            DvdNumber = medium == GameMedium.Dvd ? 2 : null
        };
        var other = original with
        {
            Id = Guid.NewGuid(),
            Location = original.Location.ToLowerInvariant()
        };

        repository.Create(original);
        repository.Create(other);

        Assert.Equal(2, new SqliteGameRecordRepository(catalog.Path).List().Count);
    }

    [Fact]
    public void DvdNumberAndMediumDistinguishKeys()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        var otherDvd = DvdGame with { Id = Guid.NewGuid(), DvdNumber = 3 };
        var usb = DvdGame with { Id = Guid.NewGuid(), Medium = GameMedium.Usb, DvdNumber = null };
        var memoryCard = usb with { Id = Guid.NewGuid(), Medium = GameMedium.MemoryCard };

        repository.Create(DvdGame);
        repository.Create(otherDvd);
        repository.Create(usb);
        repository.Create(memoryCard);

        Assert.Equal(4, new SqliteGameRecordRepository(catalog.Path).List().Count);
    }

    [Fact]
    public void ExistingCatalogReceivesUniquenessIndexes()
    {
        using var catalog = new TemporaryCatalog();
        CreateLegacyCatalog(catalog.Path);
        Seed(catalog.Path, DvdGame);

        var repository = new SqliteGameRecordRepository(catalog.Path);
        var duplicate = DvdGame with { Id = Guid.NewGuid() };

        Assert.Throws<SqliteException>(() => repository.Create(duplicate));
        Assert.Equal(DvdGame, Assert.Single(repository.List()));
    }

    [Fact]
    public void FailedIndexMigrationPreservesRowsAndLeavesNoPartialIndexes()
    {
        using var catalog = new TemporaryCatalog();
        CreateLegacyCatalog(catalog.Path);
        Seed(catalog.Path, UsbGame);
        Seed(catalog.Path, UsbGame with { Id = Guid.NewGuid(), Title = "Duplicate" });

        Assert.Throws<SqliteException>(() => new SqliteGameRecordRepository(catalog.Path));

        using var connection = OpenConnection(catalog.Path);
        using var countRows = connection.CreateCommand();
        countRows.CommandText = "SELECT COUNT(*) FROM GameRecords";
        Assert.Equal(2L, countRows.ExecuteScalar());
        using var countIndexes = connection.CreateCommand();
        countIndexes.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND tbl_name = 'GameRecords'
              AND name NOT LIKE 'sqlite_autoindex%'
            """;
        Assert.Equal(0L, countIndexes.ExecuteScalar());
    }

    [Fact]
    public void UpdatePersistsEveryChangedFieldAndLeavesOtherRecordsAlone()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        repository.Create(DvdGame);
        repository.Create(UsbGame);
        var updated = DvdGame with
        {
            Title = "Changed'); DELETE FROM GameRecords; --",
            Medium = GameMedium.MemoryCard,
            DvdNumber = null,
            Location = "/GAMES/Changed.rar",
            Format = GameFormat.Chd,
            SizeBytes = 6_000_000_000,
            Notes = null
        };

        repository.Update(updated);

        var reopened = new SqliteGameRecordRepository(catalog.Path);
        Assert.Equal(updated, reopened.GetById(DvdGame.Id));
        Assert.Equal(UsbGame, reopened.GetById(UsbGame.Id));
        Assert.Equal(2, reopened.List().Count);
    }

    [Theory]
    [InlineData(GameMedium.Dvd)]
    [InlineData(GameMedium.Usb)]
    [InlineData(GameMedium.MemoryCard)]
    public void ConflictingUpdateLeavesBothSavedRowsUnchanged(GameMedium medium)
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        var original = medium == GameMedium.Dvd ? DvdGame : UsbGame with { Medium = medium };
        var target = original with
        {
            Id = Guid.NewGuid(),
            DvdNumber = medium == GameMedium.Dvd ? 3 : null,
            Location = "/Other/game.rar"
        };
        repository.Create(original);
        repository.Create(target);
        var conflict = target with
        {
            Title = "Failed change",
            DvdNumber = original.DvdNumber,
            Location = original.Location
        };

        Assert.Throws<SqliteException>(() => repository.Update(conflict));

        var reopened = new SqliteGameRecordRepository(catalog.Path);
        Assert.Equal(original, reopened.GetById(original.Id));
        Assert.Equal(target, reopened.GetById(target.Id));
    }

    [Fact]
    public void UpdateOfMissingIdFailsWithoutInsertingOrChangingRecords()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        repository.Create(DvdGame);

        Assert.Throws<InvalidOperationException>(() => repository.Update(
            DvdGame with { Id = Guid.NewGuid(), Title = "Missing" }));

        Assert.Equal(DvdGame, Assert.Single(new SqliteGameRecordRepository(catalog.Path).List()));
    }

    [Fact]
    public void DeleteRemovesOnlyTheSelectedCatalogRow()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        repository.Create(DvdGame);
        repository.Create(UsbGame);

        repository.Delete(DvdGame.Id);

        var reopened = new SqliteGameRecordRepository(catalog.Path);
        Assert.Null(reopened.GetById(DvdGame.Id));
        Assert.Equal(UsbGame, Assert.Single(reopened.List()));
    }

    [Fact]
    public void DeleteOfMissingIdFailsWithoutChangingRecords()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        repository.Create(DvdGame);

        Assert.Throws<InvalidOperationException>(() => repository.Delete(Guid.NewGuid()));

        Assert.Equal(DvdGame, Assert.Single(new SqliteGameRecordRepository(catalog.Path).List()));
    }

    [Fact]
    public void InvalidDatabaseIsReportedAsCorruptWithoutChangingItsBytes()
    {
        using var catalog = new TemporaryCatalog();
        var original = new byte[] { 0, 1, 2, 3, 4, 5 };
        File.WriteAllBytes(catalog.Path, original);

        var error = Assert.Throws<InvalidDataException>(() => new SqliteGameRecordRepository(catalog.Path));

        Assert.Contains("corrupt", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(catalog.Path));
    }

    [Fact]
    public void InvalidSavedFieldsAreReportedBeforeAnyCatalogWrite()
    {
        using var catalog = new TemporaryCatalog();
        _ = new SqliteGameRecordRepository(catalog.Path);
        Seed(catalog.Path, DvdGame with { Title = " " });
        var original = File.ReadAllBytes(catalog.Path);

        var error = Assert.Throws<InvalidDataException>(() => new SqliteGameRecordRepository(catalog.Path));

        Assert.Contains("corrupt", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(catalog.Path));
    }

    [Fact]
    public void InvalidSavedIdIsReportedBeforeAnyCatalogWrite()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        repository.Create(DvdGame);
        using (var connection = OpenConnection(catalog.Path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE GameRecords SET Id = 'invalid-id'";
            command.ExecuteNonQuery();
        }

        var original = File.ReadAllBytes(catalog.Path);
        Assert.Throws<InvalidDataException>(() => new SqliteGameRecordRepository(catalog.Path));
        Assert.Equal(original, File.ReadAllBytes(catalog.Path));
    }

    [Fact]
    public void CorruptRecordIntroducedAfterStartupIsReportedOnRead()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        repository.Create(DvdGame);
        using (var connection = OpenConnection(catalog.Path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE GameRecords SET Title = ' ' WHERE Id = $id";
            command.Parameters.AddWithValue("$id", DvdGame.Id.ToString());
            command.ExecuteNonQuery();
        }

        var original = File.ReadAllBytes(catalog.Path);

        Assert.Throws<InvalidDataException>(() => repository.List());
        Assert.Throws<InvalidDataException>(() => repository.GetById(DvdGame.Id));
        Assert.Equal(original, File.ReadAllBytes(catalog.Path));
    }

    [Fact]
    public void DatabaseDamageAfterStartupIsReportedAsCorrupt()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        File.WriteAllBytes(catalog.Path, [0, 1, 2, 3, 4, 5]);
        var original = File.ReadAllBytes(catalog.Path);

        Assert.Throws<InvalidDataException>(() => repository.List());
        Assert.Throws<InvalidDataException>(() => repository.Create(DvdGame));
        Assert.Equal(original, File.ReadAllBytes(catalog.Path));
    }

    [Fact]
    public void MissingCatalogReadFailsWithoutRecreatingDatabase()
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        repository.Create(DvdGame);
        var movedPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(catalog.Path)!, "moved.db");
        File.Move(catalog.Path, movedPath);
        var original = File.ReadAllBytes(movedPath);

        Assert.ThrowsAny<Exception>(() => repository.List());

        Assert.False(File.Exists(catalog.Path));
        Assert.Equal(original, File.ReadAllBytes(movedPath));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public void MissingCatalogWriteFailsWithoutRecreatingDatabase(string operation)
    {
        using var catalog = new TemporaryCatalog();
        var repository = new SqliteGameRecordRepository(catalog.Path);
        repository.Create(DvdGame);
        var movedPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(catalog.Path)!, "moved.db");
        File.Move(catalog.Path, movedPath);
        var original = File.ReadAllBytes(movedPath);

        Assert.ThrowsAny<Exception>(() =>
        {
            switch (operation)
            {
                case "create": repository.Create(UsbGame); break;
                case "update": repository.Update(DvdGame with { Title = "Changed" }); break;
                case "delete": repository.Delete(DvdGame.Id); break;
            }
        });

        Assert.False(File.Exists(catalog.Path));
        Assert.Equal(original, File.ReadAllBytes(movedPath));
    }

    private static void CreateLegacyCatalog(string path)
    {
        using var connection = OpenConnection(path);
        using var command = connection.CreateCommand();
        command.CommandText = """
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
            """;
        command.ExecuteNonQuery();
    }

    private static void Seed(string path, GameRecord record)
    {
        using var connection = OpenConnection(path);
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

    private static SqliteConnection OpenConnection(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
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
