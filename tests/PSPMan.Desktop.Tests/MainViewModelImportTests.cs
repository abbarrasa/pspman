using PSPMan.Core.Games;
using PSPMan.Desktop.Services;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public class MainViewModelImportTests
{
    [Fact]
    public async Task ConfirmationSavesOnlyProposalsPreservesIdsAndRefreshesSelection()
    {
        var old = new GameRecord(Guid.NewGuid(), "Old", GameMedium.Usb, null, "/old.rar", GameFormat.Iso, 1, "notes");
        var repository = new WritableImportRepository([old]);
        var source = new PreviewSource(CsvHeader + "Edited;Usb;;/old.rar;CHD;2;\nNew;Usb;;/new.rar;ISO;1;\n");
        var vm = await CreatePreviewViewModel(repository, source);
        vm.SelectedGame = old;
        Assert.False(vm.ConfirmImportCommand.CanExecute(null));
        await vm.PreviewImportCommand.ExecuteAsync(null);
        Assert.True(vm.ConfirmImportCommand.CanExecute(null));
        Assert.Empty(repository.Attempts);

        await vm.ConfirmImportCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.ImportCreatedCount);
        Assert.Equal(1, vm.ImportUpdatedCount);
        Assert.Equal(0, vm.ImportFailedCount);
        Assert.True(vm.HasImportResult);
        Assert.True(vm.ImportApplyResult!.IsSuccess);
        Assert.False(vm.HasImportPreview);
        Assert.False(vm.ConfirmImportCommand.CanExecute(null));
        await vm.ConfirmImportCommand.ExecuteAsync(null);
        Assert.Equal(2, repository.Attempts.Count);
        Assert.Equal("Edited", vm.SelectedGame!.Title);
        Assert.Equal(old.Id, vm.SelectedGame.Id);
        Assert.Equal("", vm.SelectedGame.Notes);
        Assert.Equal(2, vm.Games.Count);
        Assert.Null(vm.ImportError);
        Assert.Contains("1 created", vm.ImportStatusText!);
        Assert.True(source.LastReader!.Disposed);
    }

    [Fact]
    public async Task CancellingPendingPreviewReenablesMutationsWithoutWrites()
    {
        var repository = new WritableImportRepository([]);
        var vm = await CreatePreviewViewModel(repository, new PreviewSource(CsvHeader));
        await vm.PreviewImportCommand.ExecuteAsync(null);
        Assert.False(vm.AddGameCommand.CanExecute(null));

        vm.CancelImportCommand.Execute(null);

        Assert.False(vm.IsImportFormOpen);
        Assert.False(vm.HasImportPreview);
        Assert.True(vm.AddGameCommand.CanExecute(null));
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task ChangedSourceAtConfirmationShowsErrorAndDoesNotWrite()
    {
        var repository = new WritableImportRepository([]);
        var source = new PreviewSource(CsvHeader + "New;Usb;;/new.rar;ISO;1;\n");
        var vm = await CreatePreviewViewModel(repository, source);
        await vm.PreviewImportCommand.ExecuteAsync(null);
        source.Text += "Other;Usb;;/other.rar;ISO;1;\n";

        await vm.ConfirmImportCommand.ExecuteAsync(null);

        Assert.Contains("changed", vm.ImportError!);
        Assert.Empty(repository.Attempts);
        Assert.Empty(vm.Games);
        Assert.False(vm.ConfirmImportCommand.CanExecute(null));
        Assert.False(vm.IsImportBusy);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("corrupt")]
    [InlineData("source")]
    public async Task PartialImportShowsOnlySavedRecordsAndKeepsErrorsVisible(string failure)
    {
        var repository = new WritableImportRepository([]);
        var source = new PreviewSource(CsvHeader + "Saved;Usb;;/saved.rar;ISO;1;\n"
            + "Failed;Usb;;/failed.rar;ISO;1;\nLater;Usb;;/later.rar;ISO;1;\n");
        var vm = await CreatePreviewViewModel(repository, source);
        await vm.PreviewImportCommand.ExecuteAsync(null);
        repository.BeforeWrite = record =>
        {
            if (failure == "source") source.LastReader!.VerificationFailure = new GameCatalogImportSourceChangedException();
            else if (record.Title == "Failed")
            {
                if (failure == "corrupt") throw new InvalidDataException("Corrupt.");
                throw new IOException("Save failed.");
            }
        };

        await vm.ConfirmImportCommand.ExecuteAsync(null);

        Assert.True(vm.HasImportResult);
        Assert.False(vm.ImportApplyResult!.IsSuccess);
        Assert.Equal(failure == "ordinary" ? 2 : 1, vm.ImportCreatedCount);
        Assert.Equal(failure == "source" ? 0 : 1, vm.ImportFailedCount);
        Assert.Equal(vm.ImportCreatedCount, vm.Games.Count);
        Assert.DoesNotContain(vm.Games, record => record.Title == "Failed");
        Assert.Equal(failure == "corrupt", vm.HasStartupError);
        Assert.NotNull(vm.ImportError);
        if (failure == "source") Assert.Contains("changed", vm.ImportError!);
        else Assert.Equal(3, Assert.Single(vm.ImportSaveErrors).StartLine);
    }

    [Fact]
    public async Task FailedRefreshRetainsConfirmedUpdatesAndNewRecords()
    {
        var old = new GameRecord(Guid.NewGuid(), "Old", GameMedium.Usb, null, "/old.rar", GameFormat.Iso, 1, null);
        var repository = new WritableImportRepository([old]);
        var vm = await CreatePreviewViewModel(repository,
            new PreviewSource(CsvHeader + "Edited;Usb;;/old.rar;CHD;2;\nNew;Usb;;/new.rar;ISO;1;\n"));
        vm.SelectedGame = old;
        await vm.PreviewImportCommand.ExecuteAsync(null);
        repository.BeforeWrite = _ => repository.FailList = true;

        await vm.ConfirmImportCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Games.Count);
        Assert.Equal("Edited", vm.SelectedGame!.Title);
        Assert.Equal(1, vm.ImportUpdatedCount);
        Assert.Equal(1, vm.ImportCreatedCount);
        Assert.Contains("refreshed", vm.ImportError!);
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("apply")]
    public async Task MutationsStayBlockedDuringWorkerAndPendingReview(string phase)
    {
        var old = new GameRecord(Guid.NewGuid(), "Old", GameMedium.Usb, null, "/old.rar", GameFormat.Iso, 1, null);
        var repository = new WritableImportRepository([old]);
        var source = new PreviewSource(CsvHeader + "New;Usb;;/new.rar;ISO;1;\n");
        var vm = await CreatePreviewViewModel(repository, source);
        vm.SelectedGame = old;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action block = () => { entered.SetResult(true); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
        if (phase == "preview") source.BeforeOpen = block;
        else
        {
            await vm.PreviewImportCommand.ExecuteAsync(null);
            AssertMutationsBlocked(vm);
            repository.BeforeWrite = _ => block();
        }
        var working = phase == "preview" ? vm.PreviewImportCommand.ExecuteAsync(null) : vm.ConfirmImportCommand.ExecuteAsync(null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AssertMutationsBlocked(vm);
            // Guards must also protect direct calls, which bypass CanExecute.
            vm.AddGameCommand.Execute(null);
            vm.StartEditCommand.Execute(null);
            vm.SaveChangesCommand.Execute(null);
            vm.DeleteSelectedCommand.Execute(null);
            vm.ConfirmDeleteCommand.Execute(null);
            if (phase == "apply")
            {
                Assert.False(vm.CancelImportCommand.CanExecute(null));
                vm.CancelImportCommand.Execute(null);
                Assert.True(vm.IsImportFormOpen);
                Assert.Contains("Importing", vm.ImportStatusText!);
            }
        }
        finally { release.Set(); }
        await working;
        if (phase == "preview") AssertMutationsBlocked(vm);
        else Assert.True(vm.AddGameCommand.CanExecute(null));
    }

    private static void AssertMutationsBlocked(MainViewModel vm)
    {
        Assert.False(vm.AddGameCommand.CanExecute(null));
        Assert.False(vm.StartEditCommand.CanExecute(null));
        Assert.False(vm.SaveChangesCommand.CanExecute(null));
        Assert.False(vm.DeleteSelectedCommand.CanExecute(null));
        Assert.False(vm.ConfirmDeleteCommand.CanExecute(null));
    }

    private sealed class WritableImportRepository(IEnumerable<GameRecord> records) : IGameRecordRepository
    {
        private readonly Dictionary<Guid, GameRecord> recordsById = records.ToDictionary(record => record.Id);
        public List<GameRecord> Attempts { get; } = [];
        public Action<GameRecord>? BeforeWrite { get; set; }
        public bool FailList { get; set; }
        public IReadOnlyList<GameRecord> List() => FailList ? throw new IOException("Refresh failed.") : recordsById.Values.ToArray();
        public GameRecord? GetById(Guid id) => recordsById.GetValueOrDefault(id);
        public void Create(GameRecord record) { Attempts.Add(record); BeforeWrite?.Invoke(record); recordsById.Add(record.Id, record); }
        public void Update(GameRecord record) { Attempts.Add(record); BeforeWrite?.Invoke(record); recordsById[record.Id] = record; }
        public void Delete(Guid id) => throw new InvalidOperationException("Import must not delete.");
    }

    private const string CsvHeader = "Title;Medium;DvdNumber;Location;Format;SizeBytes;Notes\n";

    [Fact]
    public async Task PreviewExposesNewModifiedAndErrorCountsWithoutWrites()
    {
        var existing = new GameRecord(Guid.NewGuid(), "Original", GameMedium.Usb, null,
            "/existing.rar", GameFormat.Iso, 1, null);
        var repository = new ReadOnlyRepository { Records = [existing] };
        var source = new PreviewSource(CsvHeader + "New;Usb;;/new.rar;ISO;1;\n"
            + "Updated;Usb;;/existing.rar;CHD;2;\nInvalid;Usb;;/invalid.rar;ISO;bad;\n");
        var viewModel = await CreatePreviewViewModel(repository, source);

        await viewModel.PreviewImportCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasImportPreview);
        Assert.Equal(1, viewModel.ImportNewCount);
        Assert.Equal(1, viewModel.ImportModifiedCount);
        Assert.Equal(1, viewModel.ImportErrorCount);
        Assert.Equal(4, Assert.Single(viewModel.ImportRowErrors).StartLine);
        Assert.Contains("1 new", viewModel.ImportStatusText!);
        Assert.False(viewModel.IsImportBusy);
        Assert.Null(viewModel.ImportError);
        Assert.Equal(existing, Assert.Single(viewModel.Games));
        Assert.Equal(0, repository.WriteCalls);
        Assert.True(source.LastReader!.Disposed);
    }

    [Fact]
    public async Task HeaderOnlyPreviewHasZeroCountsAndNoWrites()
    {
        var repository = new ReadOnlyRepository();
        var viewModel = await CreatePreviewViewModel(repository, new PreviewSource(CsvHeader));

        await viewModel.PreviewImportCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasImportPreview);
        Assert.Equal(0, viewModel.ImportNewCount);
        Assert.Equal(0, viewModel.ImportModifiedCount);
        Assert.Equal(0, viewModel.ImportErrorCount);
        Assert.Empty(viewModel.ImportRowErrors);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("structure")]
    [InlineData("corruption")]
    public async Task FatalPreviewFailureClearsPreviousResultsAndReportsTheError(string failure)
    {
        var repository = new ReadOnlyRepository();
        var source = new PreviewSource(CsvHeader + "Game;Usb;;/game.rar;ISO;1;\n");
        var viewModel = await CreatePreviewViewModel(repository, source);
        await viewModel.PreviewImportCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasImportPreview);
        if (failure == "read") source.OpenFailure = new GameCatalogImportReadException("Cannot read CSV.", new IOException());
        if (failure == "structure") source.Text = CsvHeader + "\"unterminated";
        if (failure == "corruption") repository.ListFailure = new InvalidDataException("Corrupt catalog.");

        await viewModel.PreviewImportCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasImportPreview);
        Assert.Null(viewModel.ImportPreview);
        Assert.True(viewModel.HasImportError);
        Assert.NotNull(viewModel.ImportError);
        Assert.Equal(0, viewModel.ImportNewCount);
        Assert.Equal(0, viewModel.ImportModifiedCount);
        Assert.Equal(0, viewModel.ImportErrorCount);
        Assert.Empty(viewModel.ImportRowErrors);
        Assert.False(viewModel.IsImportBusy);
        Assert.Equal(0, repository.WriteCalls);
        Assert.Equal(failure == "corruption", viewModel.HasStartupError);
        if (failure == "corruption") Assert.False(viewModel.PreviewImportCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewRunsInBackgroundAndCancellationDiscardsLateResults(bool cancel)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new PreviewSource(CsvHeader) { BeforeOpen = () =>
        {
            entered.SetResult(SynchronizationContext.Current is null);
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test did not release preview.");
        }};
        var repository = new ReadOnlyRepository();
        var viewModel = await CreatePreviewViewModel(repository, source);
        var evaluating = viewModel.PreviewImportCommand.ExecuteAsync(null);
        try
        {
            Assert.True(await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(viewModel.IsImportBusy);
            Assert.Contains("Evaluating", viewModel.ImportStatusText!);
            Assert.False(viewModel.PreviewImportCommand.CanExecute(null));
            Assert.False(viewModel.ChooseImportFileCommand.CanExecute(null));
            if (cancel) viewModel.CancelImportCommand.Execute(null);
        }
        finally { release.Set(); }
        await evaluating;

        Assert.False(viewModel.IsImportBusy);
        Assert.Equal(!cancel, viewModel.HasImportPreview);
        Assert.Equal(!cancel, viewModel.IsImportFormOpen);
        Assert.Null(viewModel.ImportError);
        Assert.True(source.LastReader!.Disposed);
        Assert.Equal(0, repository.WriteCalls);
        if (cancel)
        {
            Assert.Null(viewModel.ImportSourcePath);
            Assert.Null(viewModel.ImportStatusText);
            Assert.True(viewModel.OpenImportCommand.CanExecute(null));
        }
    }

    [Fact]
    public async Task ChoosingAnotherSourceClearsPreviewAndRequiresEvaluationAgain()
    {
        var viewModel = await CreatePreviewViewModel(new ReadOnlyRepository(), new PreviewSource(CsvHeader));
        await viewModel.PreviewImportCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasImportPreview);

        await viewModel.ChooseImportFileCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasImportPreview);
        Assert.Empty(viewModel.ImportRowErrors);
        Assert.True(viewModel.PreviewImportCommand.CanExecute(null));
    }

    private static async Task<MainViewModel> CreatePreviewViewModel(IGameRecordRepository repository, PreviewSource source)
    {
        var viewModel = new MainViewModel(new GameCatalogService(repository), null,
            importFilePicker: new TestPicker(() => Task.FromResult<string?>("/catalog.csv")),
            importService: new GameCatalogImportService(repository, source));
        Assert.False(viewModel.PreviewImportCommand.CanExecute(null));
        viewModel.OpenImportCommand.Execute(null);
        Assert.False(viewModel.PreviewImportCommand.CanExecute(null));
        await viewModel.ChooseImportFileCommand.ExecuteAsync(null);
        return viewModel;
    }

    private sealed class PreviewSource(string text) : IGameCatalogImportSource
    {
        public string Text { get; set; } = text;
        public Exception? OpenFailure { get; set; }
        public Action? BeforeOpen { get; set; }
        public PreviewReader? LastReader { get; private set; }
        public IGameCatalogImportReader Open(string path)
        {
            BeforeOpen?.Invoke();
            if (OpenFailure is not null) throw OpenFailure;
            return LastReader = new PreviewReader(Text);
        }
    }

    private sealed class PreviewReader(string text) : IGameCatalogImportReader
    {
        private int position;
        public bool Disposed { get; private set; }
        public Exception? VerificationFailure { get; set; }
        public int Read(Span<char> buffer)
        {
            var count = Math.Min(buffer.Length, text.Length - position);
            text.AsSpan(position, count).CopyTo(buffer);
            position += count;
            return count;
        }
        public GameCatalogImportFingerprint CaptureFingerprint() => new(text);
        public void VerifyUnchanged(GameCatalogImportFingerprint expected)
        {
            if (VerificationFailure is not null) throw VerificationFailure;
            if (CaptureFingerprint() != expected) throw new GameCatalogImportSourceChangedException();
        }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task ChoosingCsvStoresItsPathWithoutReadingSourceOrWritingCatalog()
    {
        var repository = new ReadOnlyRepository();
        var picker = new TestPicker(() => Task.FromResult<string?>("/catalog.csv"));
        var viewModel = CreateViewModel(repository, picker);

        Assert.True(viewModel.OpenImportCommand.CanExecute(null));
        Assert.False(viewModel.ChooseImportFileCommand.CanExecute(null));
        viewModel.OpenImportCommand.Execute(null);
        Assert.True(viewModel.IsImportFormOpen);
        Assert.False(viewModel.OpenImportCommand.CanExecute(null));
        await viewModel.ChooseImportFileCommand.ExecuteAsync(null);

        Assert.Equal("/catalog.csv", viewModel.ImportSourcePath);
        Assert.Null(viewModel.ImportError);
        Assert.Equal(1, picker.Calls);
        Assert.Empty(viewModel.Games);
        Assert.Equal(1, repository.ListCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingSelectionOrImportDiscardsStateWithoutCatalogWrites(bool cancelExplicitly)
    {
        var repository = new ReadOnlyRepository();
        var picker = new TestPicker(() => Task.FromResult<string?>(cancelExplicitly ? "/catalog.csv" : null));
        var viewModel = CreateViewModel(repository, picker);
        viewModel.OpenImportCommand.Execute(null);
        await viewModel.ChooseImportFileCommand.ExecuteAsync(null);
        if (cancelExplicitly) viewModel.CancelImportCommand.Execute(null);

        Assert.False(viewModel.IsImportFormOpen);
        Assert.Null(viewModel.ImportSourcePath);
        Assert.Null(viewModel.ImportError);
        Assert.True(viewModel.OpenImportCommand.CanExecute(null));
        Assert.False(viewModel.CancelImportCommand.CanExecute(null));
        Assert.Equal(1, repository.ListCalls);
    }

    [Fact]
    public async Task PickerErrorKeepsImportOpenAndAllowsRetry()
    {
        var fail = true;
        var picker = new TestPicker(() => fail
            ? Task.FromException<string?>(new IOException("Unable to select CSV."))
            : Task.FromResult<string?>("/retry.csv"));
        var viewModel = CreateViewModel(new ReadOnlyRepository(), picker);
        viewModel.OpenImportCommand.Execute(null);
        await viewModel.ChooseImportFileCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsImportFormOpen);
        Assert.True(viewModel.HasImportError);
        Assert.Equal("Unable to select CSV.", viewModel.ImportError);
        Assert.Null(viewModel.ImportSourcePath);
        fail = false;
        await viewModel.ChooseImportFileCommand.ExecuteAsync(null);

        Assert.Equal("/retry.csv", viewModel.ImportSourcePath);
        Assert.False(viewModel.HasImportError);
    }

    [Theory]
    [InlineData("service")]
    [InlineData("catalog")]
    [InlineData("picker")]
    [InlineData("import-service")]
    public async Task MissingDependenciesOrCatalogErrorBlockImportEvenWhenExecutedDirectly(string missing)
    {
        var repository = new ReadOnlyRepository();
        var picker = new TestPicker(() => Task.FromResult<string?>("/catalog.csv"));
        var viewModel = new MainViewModel(missing == "service" ? null : new GameCatalogService(repository),
            missing == "catalog" ? "The catalog is unavailable." : null,
            importFilePicker: missing == "picker" ? null : picker,
            importService: missing == "import-service" ? null : new GameCatalogImportService(repository, new UnusedSource()));

        Assert.False(viewModel.OpenImportCommand.CanExecute(null));
        viewModel.OpenImportCommand.Execute(null);
        await viewModel.ChooseImportFileCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsImportFormOpen);
        Assert.Null(viewModel.ImportSourcePath);
        Assert.Equal(0, picker.Calls);
    }

    [Fact]
    public async Task LatePickerResultIsIgnoredAfterCancellation()
    {
        var completion = new TaskCompletionSource<string?>();
        var picker = new TestPicker(() => completion.Task);
        var viewModel = CreateViewModel(new ReadOnlyRepository(), picker);
        viewModel.OpenImportCommand.Execute(null);
        var choosing = viewModel.ChooseImportFileCommand.ExecuteAsync(null);
        viewModel.CancelImportCommand.Execute(null);
        completion.SetResult("/late.csv");
        await choosing;

        Assert.False(viewModel.IsImportFormOpen);
        Assert.Null(viewModel.ImportSourcePath);
        Assert.Null(viewModel.ImportError);
    }

    private static MainViewModel CreateViewModel(ReadOnlyRepository repository, IGameImportFilePicker picker) =>
        new(new GameCatalogService(repository), null, importFilePicker: picker,
            importService: new GameCatalogImportService(repository, new UnusedSource()));

    private sealed class TestPicker(Func<Task<string?>> choose) : IGameImportFilePicker
    {
        public int Calls { get; private set; }
        public Task<string?> PickFileAsync() { Calls++; return choose(); }
    }

    private sealed class UnusedSource : IGameCatalogImportSource
    {
        public IGameCatalogImportReader Open(string sourcePath) =>
            throw new InvalidOperationException("Selecting a path must not open the source.");
    }

    private sealed class ReadOnlyRepository : IGameRecordRepository
    {
        public int ListCalls { get; private set; }
        public IReadOnlyList<GameRecord> Records { get; set; } = [];
        public Exception? ListFailure { get; set; }
        public int WriteCalls { get; private set; }
        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;
            if (ListFailure is not null) throw ListFailure;
            return Records;
        }
        public GameRecord? GetById(Guid id) => throw new InvalidOperationException("No catalog reads expected.");
        public void Create(GameRecord record) { WriteCalls++; throw new InvalidOperationException("No catalog writes expected."); }
        public void Update(GameRecord record) { WriteCalls++; throw new InvalidOperationException("No catalog writes expected."); }
        public void Delete(Guid id) { WriteCalls++; throw new InvalidOperationException("No catalog writes expected."); }
    }
}
