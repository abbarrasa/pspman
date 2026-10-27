using PSPMan.Core.Games;

namespace PSPMan.Desktop.Tests;

public class CatalogStartupTests
{
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
}
