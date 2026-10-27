using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public class MainViewModelEditGameTests
{
    [Fact]
    public void SelectedRecordLoadsIntoEditorAndTargetSurvivesSelectionChange()
    {
        var first = Record("First", "/OFFLINE/First.rar", GameMedium.Dvd, 2);
        var second = Record("Second", "/OFFLINE/Second.rar", GameMedium.Usb);
        var viewModel = ViewModel(new TrackingRepository(first, second));

        Assert.False(viewModel.StartEditCommand.CanExecute(null));
        viewModel.SelectedGame = first;
        viewModel.StartEditCommand.Execute(null);

        Assert.True(viewModel.IsEditing);
        Assert.Equal(first.Id, viewModel.EditingRecordId);
        Assert.Equal("First", viewModel.EditingGameTitle);
        Assert.Equal(first.Title, viewModel.Editor.Title);
        Assert.Equal(first.Medium, viewModel.Editor.SelectedMedium!.Value);
        Assert.Equal("2", viewModel.Editor.DvdNumberText);
        Assert.Equal(first.Location, viewModel.Editor.Location);
        Assert.Equal(first.Format, viewModel.Editor.SelectedFormat!.Value);
        Assert.Equal(first.SizeBytes.ToString(), viewModel.Editor.SizeBytesText);
        Assert.Equal(first.Notes, viewModel.Editor.Notes);
        Assert.False(viewModel.AddGameCommand.CanExecute(null));

        viewModel.SelectedGame = second;

        Assert.Equal(first.Id, viewModel.EditingRecordId);
        Assert.Equal(first.Title, viewModel.Editor.Title);
        Assert.Equal("First", viewModel.EditingGameTitle);
    }

    [Fact]
    public void ValidOfflineEditUpdatesTargetListAndDetails()
    {
        var first = Record("First", "/OFFLINE/First.rar", GameMedium.Usb);
        var second = Record("Second", "/OFFLINE/Second.rar", GameMedium.Usb);
        var repository = new TrackingRepository(first, second);
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = first;
        viewModel.StartEditCommand.Execute(null);
        viewModel.SelectedGame = second;
        viewModel.Editor.Title = "Corrected";
        viewModel.Editor.Location = "/OFFLINE/Corrected.rar";
        viewModel.Editor.SelectedFormat = viewModel.Editor.FormatChoices.Single(choice => choice.Value == GameFormat.Chd);
        viewModel.Editor.SizeBytesText = "1572864";
        viewModel.Editor.Notes = "Updated notes";

        viewModel.SaveChangesCommand.Execute(null);

        var updated = repository.Records.Single(record => record.Id == first.Id);
        Assert.Equal(first.Id, updated.Id);
        Assert.Equal("Corrected", updated.Title);
        Assert.Equal("/OFFLINE/Corrected.rar", updated.Location);
        Assert.Equal(GameFormat.Chd, updated.Format);
        Assert.Equal(1_572_864, updated.SizeBytes);
        Assert.Equal("Updated notes", updated.Notes);
        Assert.Equal(second, repository.Records.Single(record => record.Id == second.Id));
        Assert.Equal(updated, viewModel.Games.Single(record => record.Id == first.Id));
        Assert.Equal(updated, viewModel.SelectedGame);
        Assert.Equal("Corrected", viewModel.SelectedTitleText);
        Assert.Equal("1.5 MiB", viewModel.SelectedReadableSizeText);
        Assert.False(viewModel.IsEditing);
        Assert.Null(viewModel.OperationError);
        Assert.Equal(string.Empty, viewModel.Editor.Title);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal(3, repository.ListCalls);
    }

    [Fact]
    public void EditingOwnRecordWithUnchangedIdentityIsAllowed()
    {
        var original = Record("First", "/OFFLINE/First.rar", GameMedium.Dvd, 1);
        var repository = new TrackingRepository(original);
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = original;
        viewModel.StartEditCommand.Execute(null);
        viewModel.Editor.Title = "Renamed";

        viewModel.SaveChangesCommand.Execute(null);

        Assert.Equal("Renamed", Assert.Single(repository.Records).Title);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Null(viewModel.OperationError);
    }

    [Fact]
    public void InvalidEditLeavesSavedValuesAndDraftIntact()
    {
        var original = Record("First", "/OFFLINE/First.rar", GameMedium.Usb);
        var repository = new TrackingRepository(original);
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = original;
        viewModel.StartEditCommand.Execute(null);
        viewModel.Editor.Title = " \t ";

        viewModel.SaveChangesCommand.Execute(null);

        Assert.Equal(original, Assert.Single(repository.Records));
        Assert.Equal(original, Assert.Single(viewModel.Games));
        Assert.Equal(original, viewModel.SelectedGame);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.True(viewModel.IsEditing);
        Assert.Equal(" \t ", viewModel.Editor.Title);
        Assert.Equal("Title is required.", viewModel.Editor.TitleError);
        Assert.Equal("Correct the fields above.", viewModel.OperationError);
    }

    [Fact]
    public void DuplicateEditShowsConflictWithoutChangingEitherRecord()
    {
        var first = Record("First", "/OFFLINE/First.rar", GameMedium.Usb);
        var second = Record("Second", "/OFFLINE/Second.rar", GameMedium.Usb);
        var repository = new TrackingRepository(first, second);
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = first;
        viewModel.StartEditCommand.Execute(null);
        viewModel.Editor.Location = second.Location;

        viewModel.SaveChangesCommand.Execute(null);

        Assert.Equal([first, second], repository.Records);
        Assert.Equal(first, viewModel.SelectedGame);
        Assert.Equal(first, viewModel.Games.Single(record => record.Id == first.Id));
        Assert.Equal(0, repository.UpdateCalls);
        Assert.True(viewModel.IsEditing);
        Assert.Equal(second.Location, viewModel.Editor.Location);
        Assert.Contains("same medium", viewModel.OperationError);
    }

    [Fact]
    public void FailedUpdateKeepsSavedRecordAndDraft()
    {
        var original = Record("First", "/OFFLINE/First.rar", GameMedium.Usb);
        var repository = new TrackingRepository(original) { FailUpdate = true };
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = original;
        viewModel.StartEditCommand.Execute(null);
        viewModel.Editor.Title = "Changed";

        viewModel.SaveChangesCommand.Execute(null);

        Assert.Equal(original, Assert.Single(repository.Records));
        Assert.Equal(original, Assert.Single(viewModel.Games));
        Assert.Equal(original, viewModel.SelectedGame);
        Assert.Equal("Changed", viewModel.Editor.Title);
        Assert.True(viewModel.IsEditing);
        Assert.Equal("Unable to update the game record.", viewModel.OperationError);
    }

    [Fact]
    public void FailedRefreshShowsConfirmedEditWithoutClaimingItFailed()
    {
        var original = Record("First", "/OFFLINE/First.rar", GameMedium.Usb);
        var repository = new TrackingRepository(original) { FailListOnCall = 3 };
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = original;
        viewModel.StartEditCommand.Execute(null);
        viewModel.Editor.Title = "Changed";

        viewModel.SaveChangesCommand.Execute(null);

        var saved = Assert.Single(repository.Records);
        Assert.Equal("Changed", saved.Title);
        Assert.Equal(saved, Assert.Single(viewModel.Games));
        Assert.Equal(saved, viewModel.SelectedGame);
        Assert.False(viewModel.IsEditing);
        Assert.Contains("saved", viewModel.OperationError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refresh", viewModel.OperationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancelEditDiscardsDraftWithoutUpdatingCatalog()
    {
        var original = Record("First", "/OFFLINE/First.rar", GameMedium.Usb);
        var repository = new TrackingRepository(original);
        var viewModel = ViewModel(repository);
        viewModel.SelectedGame = original;
        viewModel.StartEditCommand.Execute(null);
        viewModel.Editor.Title = "Draft";

        viewModel.CancelEditCommand.Execute(null);

        Assert.False(viewModel.IsEditing);
        Assert.Equal(string.Empty, viewModel.Editor.Title);
        Assert.Equal(original, Assert.Single(repository.Records));
        Assert.Equal(0, repository.UpdateCalls);
        Assert.True(viewModel.AddGameCommand.CanExecute(null));
    }

    private static GameRecord Record(string title, string location, GameMedium medium, int? dvdNumber = null) =>
        new(Guid.NewGuid(), title, medium, dvdNumber, location, GameFormat.Iso, 1_024, "Stored notes");

    private static MainViewModel ViewModel(TrackingRepository repository) =>
        new(new GameCatalogService(repository), null);

    private sealed class TrackingRepository(params GameRecord[] records) : IGameRecordRepository
    {
        public List<GameRecord> Records { get; } = [.. records];

        public int ListCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public int? FailListOnCall { get; set; }

        public bool FailUpdate { get; set; }

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

        public void Update(GameRecord record)
        {
            UpdateCalls++;
            if (FailUpdate)
            {
                throw new IOException("Write failure.");
            }

            var index = Records.FindIndex(existing => existing.Id == record.Id);
            Records[index] = record;
        }

        public void Delete(Guid id) => throw new NotSupportedException();
    }
}
