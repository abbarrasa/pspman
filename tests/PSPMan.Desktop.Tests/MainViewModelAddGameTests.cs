using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public class MainViewModelAddGameTests
{
    [Fact]
    public void ValidOfflineGameIsSavedSelectedAndListed()
    {
        var repository = new TrackingRepository();
        var viewModel = new MainViewModel(new GameCatalogService(repository), null);
        FillValidUsbEditor(viewModel.Editor);

        viewModel.AddGameCommand.Execute(null);

        var saved = Assert.Single(repository.Records);
        Assert.Equal("/DISCONNECTED/GAMES/Game.rar", saved.Location);
        Assert.Equal(saved, Assert.Single(viewModel.Games));
        Assert.Equal(saved, viewModel.SelectedGame);
        Assert.True(viewModel.HasGames);
        Assert.False(viewModel.IsEmpty);
        Assert.Null(viewModel.OperationError);
        Assert.Equal(string.Empty, viewModel.Editor.Title);
        Assert.Equal(string.Empty, viewModel.Editor.SizeBytesText);
        Assert.Equal(3, repository.ListCalls);
    }

    [Fact]
    public void InvalidFieldsDoNotReachStorageOrAppearSaved()
    {
        var repository = new TrackingRepository();
        var viewModel = new MainViewModel(new GameCatalogService(repository), null);
        FillValidUsbEditor(viewModel.Editor);
        viewModel.Editor.Title = " \t ";

        viewModel.AddGameCommand.Execute(null);

        Assert.Empty(repository.Records);
        Assert.Empty(viewModel.Games);
        Assert.True(viewModel.IsEmpty);
        Assert.Null(viewModel.SelectedGame);
        Assert.Equal(0, repository.CreateCalls);
        Assert.Equal(1, repository.ListCalls);
        Assert.Equal("Title is required.", viewModel.Editor.TitleError);
        Assert.Equal("Correct the fields above.", viewModel.OperationError);
        Assert.Equal(" \t ", viewModel.Editor.Title);
    }

    [Fact]
    public void DuplicateShowsConflictAndKeepsExistingRecord()
    {
        var existing = new GameRecord(Guid.NewGuid(), "Existing", GameMedium.Usb, null,
            "/DISCONNECTED/GAMES/Game.rar", GameFormat.Iso, 99, null);
        var repository = new TrackingRepository(existing);
        var viewModel = new MainViewModel(new GameCatalogService(repository), null);
        FillValidUsbEditor(viewModel.Editor);

        viewModel.AddGameCommand.Execute(null);

        Assert.Equal(existing, Assert.Single(repository.Records));
        Assert.Equal(existing, Assert.Single(viewModel.Games));
        Assert.Equal(0, repository.CreateCalls);
        Assert.Equal(2, repository.ListCalls);
        Assert.Contains("same medium", viewModel.OperationError);
        Assert.Equal("New Game", viewModel.Editor.Title);

        viewModel.Editor.Location = "/DISCONNECTED/GAMES/Other.rar";
        viewModel.AddGameCommand.Execute(null);

        Assert.Equal(2, repository.Records.Count);
        Assert.Null(viewModel.OperationError);
        Assert.Equal("/DISCONNECTED/GAMES/Other.rar", viewModel.SelectedGame!.Location);
    }

    [Fact]
    public void FailedCreateLeavesFormAndListUnchanged()
    {
        var repository = new TrackingRepository { FailCreate = true };
        var viewModel = new MainViewModel(new GameCatalogService(repository), null);
        FillValidUsbEditor(viewModel.Editor);

        viewModel.AddGameCommand.Execute(null);

        Assert.Empty(repository.Records);
        Assert.Empty(viewModel.Games);
        Assert.True(viewModel.IsEmpty);
        Assert.Null(viewModel.SelectedGame);
        Assert.Equal(1, repository.CreateCalls);
        Assert.Equal(2, repository.ListCalls);
        Assert.Equal("Unable to save the game record.", viewModel.OperationError);
        Assert.Equal("New Game", viewModel.Editor.Title);
    }

    [Fact]
    public void FailedRefreshKeepsConfirmedSaveVisibleAndReportsRefreshFailure()
    {
        var repository = new TrackingRepository { FailListOnCall = 3 };
        var viewModel = new MainViewModel(new GameCatalogService(repository), null);
        FillValidUsbEditor(viewModel.Editor);

        viewModel.AddGameCommand.Execute(null);

        var saved = Assert.Single(repository.Records);
        Assert.Equal(saved, Assert.Single(viewModel.Games));
        Assert.Equal(saved, viewModel.SelectedGame);
        Assert.Equal(3, repository.ListCalls);
        Assert.Contains("saved", viewModel.OperationError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refresh", viewModel.OperationError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, viewModel.Editor.Title);
    }

    [Fact]
    public void AddGameIsUnavailableWhenCatalogDidNotOpen()
    {
        var viewModel = new MainViewModel(null, "Unable to open the game catalog.");

        Assert.False(viewModel.AddGameCommand.CanExecute(null));
    }

    private static void FillValidUsbEditor(GameEditorViewModel editor)
    {
        editor.Title = "New Game";
        editor.SelectedMedium = editor.MediumChoices.Single(choice => choice.Value == GameMedium.Usb);
        editor.Location = "/DISCONNECTED/GAMES/Game.rar";
        editor.SelectedFormat = editor.FormatChoices.Single(choice => choice.Value == GameFormat.Chd);
        editor.SizeBytesText = "1572864";
        editor.Notes = "Offline";
    }

    private sealed class TrackingRepository(params GameRecord[] initialRecords) : IGameRecordRepository
    {
        public List<GameRecord> Records { get; } = [.. initialRecords];

        public int ListCalls { get; private set; }

        public int CreateCalls { get; private set; }

        public int? FailListOnCall { get; set; }

        public bool FailCreate { get; set; }

        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;
            if (ListCalls == FailListOnCall)
            {
                throw new IOException("Read failure.");
            }

            return Records.ToArray();
        }

        public GameRecord? GetById(Guid id) => Records.SingleOrDefault(record => record.Id == id);

        public void Create(GameRecord record)
        {
            CreateCalls++;
            if (FailCreate)
            {
                throw new IOException("Write failure.");
            }

            Records.Add(record);
        }

        public void Update(GameRecord record) => throw new NotSupportedException();

        public void Delete(Guid id) => throw new NotSupportedException();
    }
}
