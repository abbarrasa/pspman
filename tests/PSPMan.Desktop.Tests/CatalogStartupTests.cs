using PSPMan.Core.Games;
using PSPMan.Desktop.Services;

namespace PSPMan.Desktop.Tests;

public class CatalogStartupTests
{
    [Fact]
    public async Task StartupComposesExportAgainstDisposableApplicationData()
    {
        using var data = new TemporaryApplicationData();
        var picker = new FixedExportFolderPicker(data.Path);
        var viewModel = CatalogStartup.CreateMainViewModel(data.Path, picker);
        var catalogBefore = File.ReadAllBytes(data.CatalogPath);

        Assert.True(viewModel.OpenExportCommand.CanExecute(null));
        viewModel.OpenExportCommand.Execute(null);
        await viewModel.ChooseExportFolderCommand.ExecuteAsync(null);
        viewModel.ExportFileName = "catalog-export.csv";
        Assert.True(viewModel.ExportCatalogCommand.CanExecute(null));
        viewModel.ExportCatalogCommand.Execute(null);

        var exportedPath = System.IO.Path.Combine(data.Path, "catalog-export.csv");
        Assert.Equal(exportedPath, viewModel.ExportedPath);
        Assert.Equal(GameCatalogCsvSerializer.SerializeEmptyCatalog(), File.ReadAllBytes(exportedPath));
        Assert.Equal(catalogBefore, File.ReadAllBytes(data.CatalogPath));
        Assert.Equal(1, picker.CallCount);
        Assert.Null(viewModel.ExportError);
    }

    [Fact]
    public void FreshApplicationDataCreatesAnAvailableEmptyCatalog()
    {
        using var data = new TemporaryApplicationData();

        var viewModel = CatalogStartup.CreateMainViewModel(data.Path);

        Assert.True(viewModel.IsCatalogAvailable);
        Assert.NotNull(viewModel.CatalogService);
        Assert.Null(viewModel.CatalogError);
        Assert.False(viewModel.HasStartupError);
        Assert.True(File.Exists(data.CatalogPath));
        Assert.Empty(viewModel.CatalogService.List().Value!);
        Assert.True(viewModel.IsEmpty);
        Assert.Empty(viewModel.Games);
    }

    [Fact]
    public void ExistingCatalogIsReopenedThroughTheService()
    {
        using var data = new TemporaryApplicationData();
        var first = CatalogStartup.CreateMainViewModel(data.Path);
        var input = new GameRecordInput(
            "Stored Game", GameMedium.Usb, null, "/GAMES/Stored.rar",
            GameFormat.Chd, 123_456, null);
        var saved = first.CatalogService!.Create(input);
        Assert.True(saved.IsSuccess);

        var reopened = CatalogStartup.CreateMainViewModel(data.Path);

        Assert.True(reopened.IsCatalogAvailable);
        Assert.Equal(saved.Value, Assert.Single(reopened.CatalogService!.List().Value!));
        Assert.Equal(saved.Value, Assert.Single(reopened.Games));
    }

    [Fact]
    public void AddGameCommandPersistsAnOfflineRecordAcrossStartup()
    {
        using var data = new TemporaryApplicationData();
        var viewModel = CatalogStartup.CreateMainViewModel(data.Path);
        viewModel.Editor.Title = "Offline Game";
        viewModel.Editor.SelectedMedium = viewModel.Editor.MediumChoices.Single(choice =>
            choice.Value == GameMedium.MemoryCard);
        viewModel.Editor.Location = "/DISCONNECTED/GAMES/Offline.rar";
        viewModel.Editor.SelectedFormat = viewModel.Editor.FormatChoices.Single(choice =>
            choice.Value == GameFormat.Iso);
        viewModel.Editor.SizeBytesText = "1048576";

        viewModel.AddGameCommand.Execute(null);

        var created = Assert.Single(viewModel.Games);
        Assert.Null(viewModel.OperationError);
        Assert.Equal(created, viewModel.SelectedGame);

        var reopened = CatalogStartup.CreateMainViewModel(data.Path);

        Assert.Equal(created, Assert.Single(reopened.Games));
        Assert.Null(reopened.CatalogError);
    }

    [Fact]
    public void SaveChangesCommandPersistsAnOfflineEditAcrossStartup()
    {
        using var data = new TemporaryApplicationData();
        var first = CatalogStartup.CreateMainViewModel(data.Path);
        var created = first.CatalogService!.Create(new GameRecordInput(
            "Original", GameMedium.Dvd, 2, "/OFFLINE/Original.rar",
            GameFormat.Iso, 1_024, null));
        Assert.True(created.IsSuccess);

        var viewModel = CatalogStartup.CreateMainViewModel(data.Path);
        viewModel.SelectedGame = Assert.Single(viewModel.Games);
        viewModel.StartEditCommand.Execute(null);
        viewModel.Editor.Title = "Corrected";
        viewModel.Editor.Location = "/OFFLINE/Corrected.rar";
        viewModel.Editor.SizeBytesText = "1572864";
        viewModel.SaveChangesCommand.Execute(null);

        Assert.Null(viewModel.OperationError);
        Assert.Equal(created.Value!.Id, viewModel.SelectedGame!.Id);
        Assert.Equal("Corrected", viewModel.SelectedGame.Title);
        Assert.Null(viewModel.SelectedGame.Notes);

        var reopened = CatalogStartup.CreateMainViewModel(data.Path);
        Assert.Equal(viewModel.SelectedGame, Assert.Single(reopened.Games));
    }

    [Fact]
    public void ConfirmDeleteRemovesOnlyTheCatalogRecordAndLeavesGameFileUntouched()
    {
        using var data = new TemporaryApplicationData();
        var gameFile = System.IO.Path.Combine(data.Path, "GAMES", "Stored.rar");
        var fileContents = new byte[] { 1, 2, 3, 4 };
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(gameFile)!);
        File.WriteAllBytes(gameFile, fileContents);
        var first = CatalogStartup.CreateMainViewModel(data.Path);
        var deleted = first.CatalogService!.Create(new GameRecordInput(
            "Deleted", GameMedium.Usb, null, "/GAMES/Stored.rar",
            GameFormat.Iso, 1_024, null));
        var retained = first.CatalogService.Create(new GameRecordInput(
            "Retained", GameMedium.Usb, null, "/GAMES/Retained.rar",
            GameFormat.Chd, 2_048, null));
        Assert.True(deleted.IsSuccess);
        Assert.True(retained.IsSuccess);

        var viewModel = CatalogStartup.CreateMainViewModel(data.Path);
        viewModel.SelectedGame = viewModel.Games.Single(record => record.Id == deleted.Value!.Id);
        viewModel.DeleteSelectedCommand.Execute(null);
        viewModel.ConfirmDeleteCommand.Execute(null);

        Assert.Equal(retained.Value, Assert.Single(viewModel.Games));
        Assert.Equal(fileContents, File.ReadAllBytes(gameFile));
        var reopened = CatalogStartup.CreateMainViewModel(data.Path);
        Assert.Equal(retained.Value, Assert.Single(reopened.Games));
    }

    [Fact]
    public void CorruptCatalogProducesAnErrorWithoutReplacingIt()
    {
        using var data = new TemporaryApplicationData();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(data.CatalogPath)!);
        var original = new byte[] { 0, 1, 2, 3, 4, 5 };
        File.WriteAllBytes(data.CatalogPath, original);

        var viewModel = CatalogStartup.CreateMainViewModel(data.Path);

        Assert.False(viewModel.IsCatalogAvailable);
        Assert.Null(viewModel.CatalogService);
        Assert.True(viewModel.HasStartupError);
        Assert.Contains("corrupt", viewModel.CatalogError!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(data.CatalogPath));
    }

    [Fact]
    public void UnavailableApplicationDataProducesAnErrorWithoutAService()
    {
        using var data = new TemporaryApplicationData();
        File.WriteAllText(System.IO.Path.Combine(data.Path, "PSPMan"), "Occupied path");

        var viewModel = CatalogStartup.CreateMainViewModel(data.Path);

        Assert.False(viewModel.IsCatalogAvailable);
        Assert.Null(viewModel.CatalogService);
        Assert.True(viewModel.HasStartupError);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.CatalogError));
        Assert.False(File.Exists(data.CatalogPath));
    }

    private sealed class TemporaryApplicationData : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "PSPMan.Desktop.Tests", Guid.NewGuid().ToString());

        public TemporaryApplicationData() => Directory.CreateDirectory(Path);

        public string CatalogPath => System.IO.Path.Combine(Path, "PSPMan", "catalog.db");

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FixedExportFolderPicker(string folderPath) : IGameExportFolderPicker
    {
        public int CallCount { get; private set; }

        public Task<string?> PickFolderAsync()
        {
            CallCount++;
            return Task.FromResult<string?>(folderPath);
        }
    }
}
