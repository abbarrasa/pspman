using PSPMan.Core.Games;
using PSPMan.Infraestructure.Games;

namespace PSPMan.Infraestructure.Tests;

public class SqliteGameCatalogImportTests
{
    [Fact]
    public void EditedExportUpdatesOnlyLastValidDuplicateAndPreservesUnchangedRecord()
    {
        using var data = new TemporaryImportData();
        var repository = new SqliteGameRecordRepository(data.CatalogPath);
        var edited = new GameRecord(Guid.NewGuid(), "Original", GameMedium.Dvd, 2,
            "/GAMES/edit.rar", GameFormat.Iso, 1024, "Original notes");
        var unchanged = new GameRecord(Guid.NewGuid(), "Unchanged", GameMedium.Usb, null,
            "/GAMES/present.rar", GameFormat.Chd, 2048, "Keep these notes");
        repository.Create(edited);
        repository.Create(unchanged);
        var exported = GameCatalogCsvSerializer.SerializeCatalog([edited, unchanged]);
        var writer = new AtomicCsvExportWriter();
        writer.Publish(data.CsvPath, exported, writer.Capture(data.CsvPath), overwriteApproved: false);
        var csv = System.Text.Encoding.UTF8.GetString(exported);
        csv = csv.Replace("Original;Dvd;2;/GAMES/edit.rar;ISO;1024;Original notes",
            "First edit;Dvd;2;/GAMES/edit.rar;CHD;4096;First notes", StringComparison.Ordinal);
        csv += "Last valid;Dvd;2;/GAMES/edit.rar;CHD;8192;\r\n"
            + "Invalid later;Dvd;2;/GAMES/edit.rar;ISO;bad;Invalid notes\r\n";
        // The decoded text already contains the export's BOM.
        File.WriteAllText(data.CsvPath, csv, new System.Text.UTF8Encoding(false));
        var catalogBefore = File.ReadAllBytes(data.CatalogPath);
        var gameBefore = File.ReadAllBytes(data.GamePath);
        var tracked = new TrackingRepository(repository);
        var import = new GameCatalogImportService(tracked, new FileGameCatalogImportSource());

        var preview = import.Preview(data.CsvPath);

        Assert.True(preview.IsSuccess, preview.Error);
        Assert.Equal(0, preview.Value!.NewCount);
        Assert.Equal(1, preview.Value.ModifiedCount);
        Assert.Equal(1, preview.Value.ErrorCount);
        var proposal = Assert.Single(preview.Value.ModifiedGames);
        Assert.Equal(4, proposal.StartLine);
        Assert.Equal(edited.Id, proposal.ExistingId);
        Assert.Equal("Last valid", proposal.Input.Title);
        Assert.Equal(5, Assert.Single(preview.Value.RowErrors).StartLine);
        Assert.Equal(0, tracked.WriteCalls);
        Assert.Equal(catalogBefore, File.ReadAllBytes(data.CatalogPath));
        var confirmation = import.PrepareConfirmedImport(preview.Value);
        Assert.True(confirmation.IsSuccess, confirmation.Error);
        using var session = confirmation.Value!;

        var result = import.Apply(session);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.SaveErrors);
        var expected = edited with { Title = "Last valid", Format = GameFormat.Chd, SizeBytes = 8192, Notes = "" };
        Assert.Equal(expected, Assert.Single(result.SavedRecords));
        Assert.Equal(1, tracked.WriteCalls);
        Assert.Equal(expected, Assert.Single(tracked.Updates));
        Assert.Equal(1, tracked.ListCalls);
        var reopened = new SqliteGameRecordRepository(data.CatalogPath);
        Assert.Equal(2, reopened.List().Count);
        Assert.Equal(expected, reopened.GetById(edited.Id));
        Assert.Equal(unchanged, reopened.GetById(unchanged.Id));
        Assert.Equal(gameBefore, File.ReadAllBytes(data.GamePath));
    }

    [Fact]
    public void ExportedCatalogImportsAsNoOpPreservingRecordsCatalogBytesAndGameFiles()
    {
        using var data = new TemporaryImportData();
        var repository = new SqliteGameRecordRepository(data.CatalogPath);
        var catalog = new GameCatalogService(repository);
        GameRecordInput[] inputs =
        [
            new(" \u2003=SUM(1;2)", GameMedium.Dvd, 2, "/GAMES/missing-dvd.rar", GameFormat.Iso,
                5_000_000_000, "Éxito; commas, colons: \"quoted\"\r\nSecond line\nThird line"),
            new("+Formula title", GameMedium.Usb, null, "/GAMES/missing-usb.rar", GameFormat.Chd,
                2_048, "\t \u2003+SUM(1;2)"),
            new("-Formula title", GameMedium.MemoryCard, null, "/GAMES/missing-card.rar", GameFormat.Iso,
                1_024, "  @formula-like notes"),
            new("@Formula title", GameMedium.Usb, null, "/GAMES/present.rar", GameFormat.Chd,
                1, "\t  Ordinary leading whitespace; \"quotes\""),
            new("  Pokémon, azul: edición  ", GameMedium.Dvd, 1, "/GAMES/empty-notes.rar", GameFormat.Iso,
                512, ""),
            new("Absent notes", GameMedium.MemoryCard, null, "/GAMES/null-notes.rar", GameFormat.Chd,
                long.MaxValue, null)
        ];
        foreach (var input in inputs)
        {
            var created = catalog.Create(input);
            Assert.True(created.IsSuccess, created.Error);
        }
        var originalRecords = repository.List().OrderBy(record => record.Id).ToArray();
        var catalogBytes = File.ReadAllBytes(data.CatalogPath);
        var gameBytes = File.ReadAllBytes(data.GamePath);
        Assert.False(Directory.Exists(data.OfflineMediumPath));
        var csvBytes = GameCatalogCsvSerializer.SerializeCatalog(originalRecords);
        var writer = new AtomicCsvExportWriter();
        writer.Publish(data.CsvPath, csvBytes, writer.Capture(data.CsvPath), overwriteApproved: false);
        var trackedRepository = new TrackingRepository(repository);
        var import = new GameCatalogImportService(trackedRepository, new FileGameCatalogImportSource());

        var preview = import.Preview(data.CsvPath);

        Assert.True(preview.IsSuccess, preview.Error);
        Assert.Equal(0, preview.Value!.NewCount);
        Assert.Equal(0, preview.Value.ModifiedCount);
        Assert.Equal(0, preview.Value.ErrorCount);
        Assert.Empty(preview.Value.NewGames);
        Assert.Empty(preview.Value.ModifiedGames);
        Assert.Empty(preview.Value.RowErrors);
        Assert.Equal(catalogBytes, File.ReadAllBytes(data.CatalogPath));
        var confirmation = import.PrepareConfirmedImport(preview.Value);
        Assert.True(confirmation.IsSuccess, confirmation.Error);
        using var session = confirmation.Value!;
        Assert.Equal(catalogBytes, File.ReadAllBytes(data.CatalogPath));

        var result = import.Apply(session);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.IsCorrupt);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.SavedRecords);
        Assert.Empty(result.SaveErrors);
        Assert.Equal(0, trackedRepository.WriteCalls);
        Assert.Equal(1, trackedRepository.ListCalls);
        Assert.Equal(originalRecords, new SqliteGameRecordRepository(data.CatalogPath).List().OrderBy(record => record.Id));
        Assert.Equal(catalogBytes, File.ReadAllBytes(data.CatalogPath));
        Assert.Equal(csvBytes, File.ReadAllBytes(data.CsvPath));
        Assert.Equal(gameBytes, File.ReadAllBytes(data.GamePath));
        Assert.False(Directory.Exists(data.OfflineMediumPath));
        Assert.Single(Directory.GetFiles(data.MediumPath, "*", SearchOption.AllDirectories));
    }

    private sealed class TrackingRepository(IGameRecordRepository repository) : IGameRecordRepository
    {
        public int ListCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public List<GameRecord> Updates { get; } = [];
        public IReadOnlyList<GameRecord> List() { ListCalls++; return repository.List(); }
        public GameRecord? GetById(Guid id) => repository.GetById(id);
        public void Create(GameRecord record) { WriteCalls++; repository.Create(record); }
        public void Update(GameRecord record) { WriteCalls++; Updates.Add(record); repository.Update(record); }
        public void Delete(Guid id) { WriteCalls++; repository.Delete(id); }
    }

    private sealed class TemporaryImportData : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "PSPMan.Import.RoundTrip.Tests", Guid.NewGuid().ToString("N"));
        public string CatalogPath => Path.Combine(root, "catalog.db");
        public string CsvPath => Path.Combine(root, "catalog.csv");
        public string MediumPath => Path.Combine(root, "medium");
        public string OfflineMediumPath => Path.Combine(root, "disconnected-medium");
        public string GamePath => Path.Combine(MediumPath, "GAMES", "present.rar");
        public TemporaryImportData()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GamePath)!);
            File.WriteAllBytes(GamePath, [0, 1, 2, 255]);
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
