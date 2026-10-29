using PSPMan.Core.Games;
using PSPMan.Desktop.Services;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public class MainViewModelExportTests
{
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
}
