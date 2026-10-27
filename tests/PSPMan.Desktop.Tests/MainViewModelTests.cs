using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public class MainViewModelTests
{
    [Fact]
    public void EmptyCatalogShowsEmptyStateWithoutDetails()
    {
        var viewModel = new MainViewModel(new GameCatalogService(new FakeRepository()), null);

        Assert.True(viewModel.IsCatalogAvailable);
        Assert.Empty(viewModel.Games);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasGames);
        Assert.False(viewModel.HasSelection);
        Assert.Null(viewModel.SelectedGame);
        Assert.Null(viewModel.CatalogError);
    }

    [Fact]
    public void SavedRecordsCanBeSelectedWithEverySavedValueAvailable()
    {
        var dvd = new GameRecord(Guid.NewGuid(), "Key of Heaven", GameMedium.Dvd, 2,
            "/DevHook/GAMES/Key of Heaven.rar", GameFormat.Iso, 987_654_321, "Disc two");
        var usb = new GameRecord(Guid.NewGuid(), "Other", GameMedium.Usb, null,
            "/GAMES/Other.rar", GameFormat.Chd, 123, null);
        var viewModel = new MainViewModel(new GameCatalogService(new FakeRepository(dvd, usb)), null);

        Assert.Equal([dvd, usb], viewModel.Games);
        Assert.True(viewModel.HasGames);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasSelection);

        viewModel.SelectedGame = dvd;

        Assert.True(viewModel.HasSelection);
        Assert.Equal(dvd, viewModel.SelectedGame);
        Assert.Equal("DVD", viewModel.SelectedMediumText);
        Assert.Equal("2", viewModel.SelectedDvdNumberText);
        Assert.Equal("ISO", viewModel.SelectedFormatText);
        Assert.Equal("987654321", viewModel.SelectedSizeText);
        Assert.Equal("Disc two", viewModel.SelectedNotesText);

        viewModel.SelectedGame = usb;
        Assert.Equal("USB", viewModel.SelectedMediumText);
        Assert.Equal("Not applicable", viewModel.SelectedDvdNumberText);
        Assert.Equal("CHD", viewModel.SelectedFormatText);
        Assert.Equal("123", viewModel.SelectedSizeText);
        Assert.Equal("None", viewModel.SelectedNotesText);
    }

    [Fact]
    public void ListFailureShowsErrorInsteadOfEmptyState()
    {
        var viewModel = new MainViewModel(new GameCatalogService(new FakeRepository { FailList = true }), null);

        Assert.Empty(viewModel.Games);
        Assert.False(viewModel.IsEmpty);
        Assert.True(viewModel.HasStartupError);
        Assert.Contains("Unable to list", viewModel.CatalogError);
    }

    [Fact]
    public void UnavailableCatalogDoesNotAppearEmpty()
    {
        var viewModel = new MainViewModel(null, "The game catalog is corrupt.");

        Assert.False(viewModel.IsCatalogAvailable);
        Assert.False(viewModel.IsEmpty);
        Assert.Empty(viewModel.Games);
        Assert.True(viewModel.HasStartupError);
    }

    private sealed class FakeRepository(params GameRecord[] records) : IGameRecordRepository
    {
        public bool FailList { get; set; }

        public IReadOnlyList<GameRecord> List() => FailList
            ? throw new IOException("Catalog cannot be read.")
            : records;

        public GameRecord? GetById(Guid id) => records.FirstOrDefault(record => record.Id == id);

        public void Create(GameRecord record) => throw new NotSupportedException();

        public void Update(GameRecord record) => throw new NotSupportedException();

        public void Delete(Guid id) => throw new NotSupportedException();
    }
}
