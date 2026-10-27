using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public class MainViewModelDeleteGameTests
{
    [Fact]
    public void RequestAndCancelLeaveCatalogUnchanged()
    {
        var record = Record("First");
        var repository = new TrackingRepository(record);
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = record;

        viewModel.DeleteSelectedCommand.Execute(null);

        Assert.True(viewModel.IsDeletePending);
        Assert.Equal(record.Id, viewModel.PendingDeleteRecordId);
        Assert.Equal(record.Title, viewModel.PendingDeleteTitle);
        Assert.Equal(0, repository.DeleteCalls);
        Assert.False(viewModel.DeleteSelectedCommand.CanExecute(null));

        viewModel.CancelDeleteCommand.Execute(null);

        Assert.False(viewModel.IsDeletePending);
        Assert.Null(viewModel.PendingDeleteRecordId);
        Assert.Null(viewModel.PendingDeleteTitle);
        Assert.Equal(record, Assert.Single(viewModel.Games));
        Assert.Equal(record, Assert.Single(repository.Records));
        Assert.Equal(record, viewModel.SelectedGame);
        Assert.Equal(0, repository.DeleteCalls);
    }

    [Fact]
    public void ConfirmDeletesOnlyTheRequestedRecordAfterSelectionChanges()
    {
        var first = Record("First");
        var second = Record("Second");
        var repository = new TrackingRepository(first, second);
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = first;
        viewModel.DeleteSelectedCommand.Execute(null);
        viewModel.SelectedGame = second;

        viewModel.ConfirmDeleteCommand.Execute(null);

        Assert.Equal(first.Id, repository.DeletedId);
        Assert.Equal(second, Assert.Single(repository.Records));
        Assert.Equal(second, Assert.Single(viewModel.Games));
        Assert.Equal(second, viewModel.SelectedGame);
        Assert.False(viewModel.IsDeletePending);
        Assert.Null(viewModel.OperationError);
    }

    [Fact]
    public void FailedDeleteKeepsRecordVisibleAndPendingForRetry()
    {
        var record = Record("First");
        var repository = new TrackingRepository(record) { FailDelete = true };
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = record;
        viewModel.DeleteSelectedCommand.Execute(null);

        viewModel.ConfirmDeleteCommand.Execute(null);

        Assert.Equal(record, Assert.Single(repository.Records));
        Assert.Equal(record, Assert.Single(viewModel.Games));
        Assert.Equal(record, viewModel.SelectedGame);
        Assert.True(viewModel.IsDeletePending);
        Assert.Equal("Unable to delete the game record.", viewModel.OperationError);
        Assert.True(viewModel.ConfirmDeleteCommand.CanExecute(null));
    }

    [Fact]
    public void FailedRefreshKeepsConfirmedDeletionVisibleAndReportsRefreshFailure()
    {
        var record = Record("First");
        var repository = new TrackingRepository(record) { FailListOnCall = 2 };
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = record;
        viewModel.DeleteSelectedCommand.Execute(null);

        viewModel.ConfirmDeleteCommand.Execute(null);

        Assert.Empty(repository.Records);
        Assert.Empty(viewModel.Games);
        Assert.Null(viewModel.SelectedGame);
        Assert.False(viewModel.IsDeletePending);
        Assert.Contains("deleted", viewModel.OperationError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refresh", viewModel.OperationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeleteIsDisabledWhileEditing()
    {
        var record = Record("First");
        var viewModel = ViewModel(new TrackingRepository(record));
        viewModel.SelectedGame = record;
        viewModel.StartEditCommand.Execute(null);

        Assert.False(viewModel.DeleteSelectedCommand.CanExecute(null));
    }

    private static GameRecord Record(string title) => new(
        Guid.NewGuid(), title, GameMedium.Usb, null, $"/GAMES/{title}.rar",
        GameFormat.Iso, 1_024, null);

    private static MainViewModel ViewModel(TrackingRepository repository) =>
        new(new GameCatalogService(repository), null);

    private sealed class TrackingRepository(params GameRecord[] records) : IGameRecordRepository
    {
        public List<GameRecord> Records { get; } = [.. records];

        public int ListCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public Guid? DeletedId { get; private set; }

        public int? FailListOnCall { get; set; }

        public bool FailDelete { get; set; }

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

        public void Create(GameRecord record) => throw new NotSupportedException();

        public void Update(GameRecord record) => throw new NotSupportedException();

        public void Delete(Guid id)
        {
            DeleteCalls++;
            if (FailDelete)
            {
                throw new IOException("Write failure.");
            }

            DeletedId = id;
            Records.RemoveAll(record => record.Id == id);
        }
    }
}
