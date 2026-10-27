using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public class MainViewModelCatalogErrorTests
{
    private static readonly GameRecord Record = new(
        Guid.NewGuid(), "Stored", GameMedium.Usb, null, "/GAMES/Stored.rar",
        GameFormat.Iso, 1_024, null);

    [Fact]
    public void CorruptStartupReadBlocksEveryCatalogMutation()
    {
        var repository = new MutableRepository { CorruptListOnCall = 1 };
        var viewModel = ViewModel(repository);
        FillEditor(viewModel);
        viewModel.SelectedGame = Record;

        Assert.True(viewModel.HasStartupError);
        Assert.Contains("corrupt", viewModel.CatalogError!, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsEditorAvailable);
        Assert.False(viewModel.AddGameCommand.CanExecute(null));
        Assert.False(viewModel.StartEditCommand.CanExecute(null));
        Assert.False(viewModel.DeleteSelectedCommand.CanExecute(null));
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact]
    public void CorruptionDuringCreateBlocksRetryAndPreservesDraft()
    {
        var repository = new MutableRepository { CorruptListOnCall = 2 };
        var viewModel = ViewModel(repository);
        FillEditor(viewModel);

        viewModel.AddGameCommand.Execute(null);

        Assert.True(viewModel.HasStartupError);
        Assert.False(viewModel.IsEditorAvailable);
        Assert.False(viewModel.AddGameCommand.CanExecute(null));
        Assert.Empty(viewModel.Games);
        Assert.Equal(0, repository.CreateCalls);
        Assert.Equal("New", viewModel.Editor.Title);
        Assert.Contains("corrupt", viewModel.CatalogError!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("corrupt", viewModel.OperationError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptionAfterConfirmedSaveKeepsSavedRecordVisibleAndBlocksWrites()
    {
        var repository = new MutableRepository { CorruptListOnCall = 3 };
        var viewModel = ViewModel(repository);
        FillEditor(viewModel);

        viewModel.AddGameCommand.Execute(null);

        Assert.Equal(1, repository.CreateCalls);
        Assert.Equal(Assert.Single(repository.Records), Assert.Single(viewModel.Games));
        Assert.True(viewModel.HasStartupError);
        Assert.False(viewModel.AddGameCommand.CanExecute(null));
        Assert.Contains("saved", viewModel.OperationError!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refresh", viewModel.OperationError!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("corrupt", viewModel.CatalogError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrdinaryReadFailureDoesNotPermanentlyBlockRetry()
    {
        var repository = new MutableRepository { FailListOnCall = 2 };
        var viewModel = ViewModel(repository);
        FillEditor(viewModel);

        viewModel.AddGameCommand.Execute(null);

        Assert.False(viewModel.HasStartupError);
        Assert.True(viewModel.IsEditorAvailable);
        Assert.True(viewModel.AddGameCommand.CanExecute(null));
        Assert.Equal(0, repository.CreateCalls);
        Assert.NotNull(viewModel.OperationError);
    }

    [Fact]
    public void CorruptionDuringDeleteBlocksConfirmationWithoutHidingRecord()
    {
        var repository = new MutableRepository { CorruptGetById = true };
        repository.Records.Add(Record);
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = Record;
        viewModel.DeleteSelectedCommand.Execute(null);

        viewModel.ConfirmDeleteCommand.Execute(null);

        Assert.Equal(Record, Assert.Single(viewModel.Games));
        Assert.True(viewModel.IsDeletePending);
        Assert.Contains("corrupt", viewModel.CatalogError!, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.ConfirmDeleteCommand.CanExecute(null));
        Assert.False(viewModel.StartEditCommand.CanExecute(null));
        Assert.True(viewModel.CancelDeleteCommand.CanExecute(null));
    }

    private static MainViewModel ViewModel(MutableRepository repository) =>
        new(new GameCatalogService(repository), null);

    private static void FillEditor(MainViewModel viewModel)
    {
        viewModel.Editor.Title = "New";
        viewModel.Editor.SelectedMedium = viewModel.Editor.MediumChoices.Single(choice =>
            choice.Value == GameMedium.Usb);
        viewModel.Editor.Location = "/GAMES/New.rar";
        viewModel.Editor.SelectedFormat = viewModel.Editor.FormatChoices.Single(choice =>
            choice.Value == GameFormat.Iso);
        viewModel.Editor.SizeBytesText = "2048";
    }

    private sealed class MutableRepository : IGameRecordRepository
    {
        public List<GameRecord> Records { get; } = [];

        public int ListCalls { get; private set; }

        public int CreateCalls { get; private set; }

        public int? CorruptListOnCall { get; init; }

        public int? FailListOnCall { get; init; }

        public bool CorruptGetById { get; init; }

        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;
            if (ListCalls == CorruptListOnCall)
            {
                throw new InvalidDataException("Catalog data is corrupt.");
            }

            if (ListCalls == FailListOnCall)
            {
                throw new IOException("Storage unavailable.");
            }

            return Records.ToArray();
        }

        public GameRecord? GetById(Guid id) => CorruptGetById
            ? throw new InvalidDataException("Catalog data is corrupt.")
            : Records.SingleOrDefault(record => record.Id == id);

        public void Create(GameRecord record)
        {
            CreateCalls++;
            Records.Add(record);
        }

        public void Update(GameRecord record) => throw new NotSupportedException();

        public void Delete(Guid id) => throw new NotSupportedException();
    }
}
