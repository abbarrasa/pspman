using PSPMan.Core.Games;
using PSPMan.Desktop.Services;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public class MainViewModelImportTests
{
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
        public IReadOnlyList<GameRecord> List() { ListCalls++; return []; }
        public GameRecord? GetById(Guid id) => throw new InvalidOperationException("No catalog reads expected.");
        public void Create(GameRecord record) => throw new InvalidOperationException("No catalog writes expected.");
        public void Update(GameRecord record) => throw new InvalidOperationException("No catalog writes expected.");
        public void Delete(Guid id) => throw new InvalidOperationException("No catalog writes expected.");
    }
}
