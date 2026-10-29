using PSPMan.Core.Games;
using PSPMan.Desktop.Services;
using PSPMan.Desktop.ViewModels;
using PSPMan.Infraestructure.Games;

namespace PSPMan.Desktop.Tests;

public class MainViewModelExportTests
{
    [Fact]
    public async Task ExistingDestinationRequiresConfirmationAndDeclinePreservesBytes()
    {
        var folder = CreateExportFolder();
        try
        {
            var destination = Path.Combine(folder, "pspman-games.csv");
            var original = new byte[] { 0, 1, 2, 255 };
            File.WriteAllBytes(destination, original);
            var viewModel = CreateViewModelWithRealWriter(folder);

            viewModel.OpenExportCommand.Execute(null);
            await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
            viewModel.ExportCatalogCommand.Execute(null);

            Assert.True(viewModel.IsReplacementPending);
            Assert.Equal(destination, viewModel.PendingReplacementPath);
            Assert.Null(viewModel.ExportedPath);
            Assert.Equal(original, File.ReadAllBytes(destination));

            viewModel.CancelExportReplacementCommand.Execute(null);

            Assert.False(viewModel.IsReplacementPending);
            Assert.True(viewModel.IsExportFormOpen);
            Assert.Null(viewModel.ExportedPath);
            Assert.Equal(original, File.ReadAllBytes(destination));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ApprovalReplacesTheInspectedFileAndReportsSuccess()
    {
        var folder = CreateExportFolder();
        try
        {
            var destination = Path.Combine(folder, "pspman-games.csv");
            File.WriteAllText(destination, "previous export");
            var viewModel = CreateViewModelWithRealWriter(folder);

            viewModel.OpenExportCommand.Execute(null);
            await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
            viewModel.ExportCatalogCommand.Execute(null);
            viewModel.ConfirmExportReplacementCommand.Execute(null);

            Assert.Equal(GameCatalogCsvSerializer.SerializeEmptyCatalog(), File.ReadAllBytes(destination));
            Assert.Equal(destination, viewModel.ExportedPath);
            Assert.False(viewModel.IsReplacementPending);
            Assert.False(viewModel.IsExportFormOpen);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ChangingFileNameInvalidatesPendingReplacement()
    {
        var folder = CreateExportFolder();
        try
        {
            var destination = Path.Combine(folder, "pspman-games.csv");
            File.WriteAllText(destination, "previous export");
            var viewModel = CreateViewModelWithRealWriter(folder);

            viewModel.OpenExportCommand.Execute(null);
            await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
            viewModel.ExportCatalogCommand.Execute(null);
            Assert.True(viewModel.IsReplacementPending);

            viewModel.ExportFileName = "another.csv";

            Assert.False(viewModel.IsReplacementPending);
            Assert.False(viewModel.ConfirmExportReplacementCommand.CanExecute(null));
            Assert.Equal("previous export", File.ReadAllText(destination));
            Assert.False(File.Exists(Path.Combine(folder, "another.csv")));
            Assert.Null(viewModel.ExportedPath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ChangedFileAfterPromptIsNotReplacedByOldConsent()
    {
        var folder = CreateExportFolder();
        try
        {
            var destination = Path.Combine(folder, "pspman-games.csv");
            File.WriteAllText(destination, "original export");
            var viewModel = CreateViewModelWithRealWriter(folder);

            viewModel.OpenExportCommand.Execute(null);
            await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
            viewModel.ExportCatalogCommand.Execute(null);
            File.WriteAllText(destination, "newer export");

            viewModel.ConfirmExportReplacementCommand.Execute(null);

            Assert.Equal("newer export", File.ReadAllText(destination));
            Assert.Null(viewModel.ExportedPath);
            Assert.Contains("write", viewModel.ExportError, StringComparison.OrdinalIgnoreCase);
            Assert.False(viewModel.IsReplacementPending);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateExportFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Replace.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static MainViewModel CreateViewModelWithRealWriter(string folder) =>
        new(new GameCatalogService(new EmptyRepository()), null,
            new FakePicker(() => Task.FromResult<string?>(folder)),
            new AtomicCsvExportWriter(), Path.Combine(folder, "catalog.db"));

    [Fact]
    public async Task InvalidDestinationIsReportedBeforeCatalogReadOrWriterAccess()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Tests", Guid.NewGuid().ToString("N"));
        var repository = new MutableRepository([]);
        var writer = new FakeWriter();
        var viewModel = new MainViewModel(new GameCatalogService(repository), null,
            new FakePicker(() => Task.FromResult<string?>(folder)), writer, Path.Combine(folder, "catalog.db"));

        viewModel.OpenExportCommand.Execute(null);
        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
        viewModel.ExportFileName = "../outside.csv";
        viewModel.ExportCatalogCommand.Execute(null);

        Assert.Contains("valid .csv file name", viewModel.ExportError);
        Assert.Equal(1, repository.ListCalls);
        Assert.Equal(0, writer.CaptureCalls);
        Assert.Equal(0, writer.PublishCalls);
        Assert.Null(viewModel.ExportedPath);
    }

    [Fact]
    public async Task OrdinaryCatalogReadFailureIsReportedAndCanBeRetried()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Tests", Guid.NewGuid().ToString("N"));
        var repository = new MutableRepository([]);
        var writer = new FakeWriter();
        var viewModel = new MainViewModel(new GameCatalogService(repository), null,
            new FakePicker(() => Task.FromResult<string?>(folder)), writer, Path.Combine(folder, "catalog.db"));

        viewModel.OpenExportCommand.Execute(null);
        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
        repository.ListFailure = new IOException("Offline catalog storage.");
        viewModel.ExportCatalogCommand.Execute(null);

        Assert.Contains("read", viewModel.ExportError, StringComparison.OrdinalIgnoreCase);
        Assert.Null(viewModel.CatalogError);
        Assert.True(viewModel.ExportCatalogCommand.CanExecute(null));
        Assert.Equal(0, writer.CaptureCalls);
        Assert.Null(viewModel.ExportedPath);
    }

    [Fact]
    public async Task CorruptCatalogReadReportsCorruptionAndBlocksFurtherExport()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Tests", Guid.NewGuid().ToString("N"));
        var repository = new MutableRepository([]);
        var writer = new FakeWriter();
        var viewModel = new MainViewModel(new GameCatalogService(repository), null,
            new FakePicker(() => Task.FromResult<string?>(folder)), writer, Path.Combine(folder, "catalog.db"));

        viewModel.OpenExportCommand.Execute(null);
        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
        repository.ListFailure = new InvalidDataException("Corrupt catalog data.");
        viewModel.ExportCatalogCommand.Execute(null);

        Assert.Contains("corrupt", viewModel.ExportError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(viewModel.ExportError, viewModel.CatalogError);
        Assert.False(viewModel.ExportCatalogCommand.CanExecute(null));
        Assert.False(viewModel.OpenExportCommand.CanExecute(null));
        Assert.Equal(0, writer.CaptureCalls);
        Assert.Null(viewModel.ExportedPath);
    }

    [Fact]
    public async Task DestinationInspectionFailureIsDistinctFromWriteFailure()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Tests", Guid.NewGuid().ToString("N"));
        var writer = new FakeWriter { CaptureFailure = new UnauthorizedAccessException("Access denied.") };
        var viewModel = new MainViewModel(new GameCatalogService(new EmptyRepository()), null,
            new FakePicker(() => Task.FromResult<string?>(folder)), writer, Path.Combine(folder, "catalog.db"));

        viewModel.OpenExportCommand.Execute(null);
        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
        viewModel.ExportCatalogCommand.Execute(null);

        Assert.Contains("inspect", viewModel.ExportError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, writer.CaptureCalls);
        Assert.Equal(0, writer.PublishCalls);
        Assert.Null(viewModel.ExportedPath);
    }

    [Fact]
    public async Task MissingExportFolderReportsWriteFailureWithoutPartialFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Tests", Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(folder, "pspman-games.csv");
        var viewModel = CreateViewModelWithRealWriter(folder);

        viewModel.OpenExportCommand.Execute(null);
        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
        viewModel.ExportCatalogCommand.Execute(null);

        Assert.Contains("write", viewModel.ExportError, StringComparison.OrdinalIgnoreCase);
        Assert.Null(viewModel.ExportedPath);
        Assert.False(File.Exists(destination));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task ExportReadsEverySavedRecordAfreshRegardlessOfSelectionOrOfflineMedia()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Tests", Guid.NewGuid().ToString("N"));
        var catalog = Path.Combine(folder, "catalog.db");
        var first = new GameRecord(Guid.NewGuid(), "First", GameMedium.Dvd, 1,
            "/disconnected/DVD/first.rar", GameFormat.Iso, 1024, null);
        var second = new GameRecord(Guid.NewGuid(), "Second", GameMedium.Usb, null,
            "/disconnected/USB/second.rar", GameFormat.Chd, 2048, "Offline");
        var repository = new MutableRepository([first]);
        var writer = new FakeWriter();
        var viewModel = new MainViewModel(new GameCatalogService(repository), null,
            new FakePicker(() => Task.FromResult<string?>(folder)), writer, catalog);
        viewModel.SelectedGame = first;
        repository.Records = [first, second];

        viewModel.OpenExportCommand.Execute(null);
        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
        viewModel.ExportCatalogCommand.Execute(null);

        Assert.Equal(2, repository.ListCalls);
        Assert.Equal(Path.Combine(folder, "pspman-games.csv"), writer.PublishedPath);
        Assert.Equal(GameCatalogCsvSerializer.SerializeCatalog([first, second]), writer.PublishedBytes);
        var csv = System.Text.Encoding.UTF8.GetString(writer.PublishedBytes!);
        Assert.Equal(1, csv.Split("First;").Length - 1);
        Assert.Equal(1, csv.Split("Second;").Length - 1);
        Assert.True(csv.IndexOf("First;", StringComparison.Ordinal) <
            csv.IndexOf("Second;", StringComparison.Ordinal));
        Assert.Equal(writer.PublishedPath, viewModel.ExportedPath);
        Assert.Null(viewModel.ExportError);
    }

    [Fact]
    public async Task EmptyCatalogExportsHeaderOnly()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Tests", Guid.NewGuid().ToString("N"));
        var writer = new FakeWriter();
        var viewModel = new MainViewModel(new GameCatalogService(new EmptyRepository()), null,
            new FakePicker(() => Task.FromResult<string?>(folder)), writer, Path.Combine(folder, "catalog.db"));

        viewModel.OpenExportCommand.Execute(null);
        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
        viewModel.ExportCatalogCommand.Execute(null);

        Assert.Equal(GameCatalogCsvSerializer.SerializeEmptyCatalog(), writer.PublishedBytes);
        Assert.Equal(writer.PublishedPath, viewModel.ExportedPath);
    }

    [Fact]
    public async Task PublicationFailureDoesNotReportSuccess()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Tests", Guid.NewGuid().ToString("N"));
        var writer = new FakeWriter { FailPublication = true };
        var viewModel = new MainViewModel(new GameCatalogService(new EmptyRepository()), null,
            new FakePicker(() => Task.FromResult<string?>(folder)), writer, Path.Combine(folder, "catalog.db"));

        viewModel.OpenExportCommand.Execute(null);
        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
        viewModel.ExportCatalogCommand.Execute(null);

        Assert.Null(viewModel.ExportedPath);
        Assert.Contains("write", viewModel.ExportError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyCatalogCanOpenChooseFolderAndCancelWithoutWriting()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Form.Tests", Guid.NewGuid().ToString("N"));
        var picker = new FakePicker(() => Task.FromResult<string?>(folder));
        var viewModel = new MainViewModel(new GameCatalogService(new EmptyRepository()), null, picker);

        Assert.Empty(viewModel.Games);
        Assert.True(viewModel.OpenExportCommand.CanExecute(null));

        viewModel.OpenExportCommand.Execute(null);
        Assert.True(viewModel.IsExportFormOpen);
        Assert.Equal("pspman-games.csv", viewModel.ExportFileName);
        Assert.True(viewModel.ChooseExportFolderCommand.CanExecute(null));

        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
        Assert.Equal(folder, viewModel.ExportFolderPath);
        Assert.Equal(1, picker.Calls);

        viewModel.ExportFileName = "collection.csv";
        viewModel.CancelExportCommand.Execute(null);

        Assert.False(viewModel.IsExportFormOpen);
        Assert.Null(viewModel.ExportFolderPath);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task CancellingFolderPickerClosesExportForm()
    {
        var picker = new FakePicker(() => Task.FromResult<string?>(null));
        var viewModel = new MainViewModel(new GameCatalogService(new EmptyRepository()), null, picker);
        viewModel.OpenExportCommand.Execute(null);

        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsExportFormOpen);
        Assert.Null(viewModel.ExportFolderPath);
        Assert.Equal(1, picker.Calls);
    }

    [Fact]
    public void ReopeningFormResetsFolderAndSuggestedName()
    {
        var picker = new FakePicker(() => Task.FromResult<string?>(null));
        var viewModel = new MainViewModel(new GameCatalogService(new EmptyRepository()), null, picker);
        viewModel.OpenExportCommand.Execute(null);
        viewModel.ExportFileName = "custom.csv";
        viewModel.CancelExportCommand.Execute(null);

        viewModel.OpenExportCommand.Execute(null);

        Assert.True(viewModel.IsExportFormOpen);
        Assert.Null(viewModel.ExportFolderPath);
        Assert.Equal("pspman-games.csv", viewModel.ExportFileName);
    }

    [Fact]
    public void UnavailableOrCorruptCatalogCannotOpenExportForm()
    {
        var picker = new FakePicker(() => Task.FromResult<string?>(null));
        var unavailable = new MainViewModel(null, "Catalog unavailable.", picker);
        var corrupt = new MainViewModel(new GameCatalogService(new EmptyRepository()),
            "Catalog data is corrupt.", picker);

        Assert.False(unavailable.OpenExportCommand.CanExecute(null));
        Assert.False(corrupt.OpenExportCommand.CanExecute(null));
        Assert.False(unavailable.IsExportFormOpen);
        Assert.False(corrupt.IsExportFormOpen);
        Assert.Equal(0, picker.Calls);
    }

    [Fact]
    public void MissingPickerCannotOpenExportForm()
    {
        var viewModel = new MainViewModel(new GameCatalogService(new EmptyRepository()), null);

        Assert.False(viewModel.OpenExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task PickerFailureLeavesFormOpenWithError()
    {
        var picker = new FakePicker(() => throw new InvalidOperationException("No usable local path."));
        var viewModel = new MainViewModel(new GameCatalogService(new EmptyRepository()), null, picker);
        viewModel.OpenExportCommand.Execute(null);

        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsExportFormOpen);
        Assert.Null(viewModel.ExportFolderPath);
        Assert.Contains("local path", viewModel.ExportError);
    }

    private sealed class FakePicker(Func<Task<string?>> pick) : IGameExportFolderPicker
    {
        public int Calls { get; private set; }

        public Task<string?> PickFolderAsync()
        {
            Calls++;
            return pick();
        }
    }

    private sealed class EmptyRepository : IGameRecordRepository
    {
        public IReadOnlyList<GameRecord> List() => [];

        public GameRecord? GetById(Guid id) => null;

        public void Create(GameRecord record) => throw new NotSupportedException();

        public void Update(GameRecord record) => throw new NotSupportedException();

        public void Delete(Guid id) => throw new NotSupportedException();
    }

    private sealed class MutableRepository(IReadOnlyList<GameRecord> records) : IGameRecordRepository
    {
        public IReadOnlyList<GameRecord> Records { get; set; } = records;
        public Exception? ListFailure { get; set; }
        public int ListCalls { get; private set; }

        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;
            if (ListFailure is not null)
            {
                throw ListFailure;
            }
            return Records;
        }

        public GameRecord? GetById(Guid id) => Records.FirstOrDefault(record => record.Id == id);
        public void Create(GameRecord record) => throw new NotSupportedException();
        public void Update(GameRecord record) => throw new NotSupportedException();
        public void Delete(Guid id) => throw new NotSupportedException();
    }

    private sealed class FakeWriter : IGameCatalogExportWriter
    {
        public bool FailPublication { get; set; }
        public Exception? CaptureFailure { get; set; }
        public int CaptureCalls { get; private set; }
        public int PublishCalls { get; private set; }
        public string? PublishedPath { get; private set; }
        public byte[]? PublishedBytes { get; private set; }

        public ExportDestinationSnapshot Capture(string destinationPath)
        {
            CaptureCalls++;
            if (CaptureFailure is not null)
            {
                throw CaptureFailure;
            }
            return new(destinationPath, null);
        }

        public void Publish(string destinationPath, ReadOnlyMemory<byte> content,
            ExportDestinationSnapshot inspectedDestination, bool overwriteApproved)
        {
            PublishCalls++;
            Assert.False(overwriteApproved);
            if (FailPublication)
            {
                throw new IOException("Cannot publish export.");
            }

            PublishedPath = destinationPath;
            PublishedBytes = content.ToArray();
        }
    }
}
