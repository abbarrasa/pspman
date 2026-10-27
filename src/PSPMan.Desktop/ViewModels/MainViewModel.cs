using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Games;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private IReadOnlyList<GameRecord> _games = [];
    private Guid? _editingRecordId;
    private string? _editingGameTitle;

    public MainViewModel() : this(null, null)
    {
    }

    public MainViewModel(GameCatalogService? catalogService, string? catalogError)
    {
        CatalogService = catalogService;
        CatalogError = catalogError;

        if (catalogService is null || HasStartupError)
        {
            return;
        }

        var result = catalogService.List();
        if (result.IsSuccess)
        {
            Games = result.Value ?? [];
        }
        else
        {
            CatalogError = result.Error;
        }
    }

    public GameCatalogService? CatalogService { get; }

    public GameEditorViewModel Editor { get; } = new();

    public Guid? EditingRecordId
    {
        get => _editingRecordId;
        private set
        {
            if (SetProperty(ref _editingRecordId, value))
            {
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(IsAdding));
                AddGameCommand.NotifyCanExecuteChanged();
                StartEditCommand.NotifyCanExecuteChanged();
                SaveChangesCommand.NotifyCanExecuteChanged();
                CancelEditCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? EditingGameTitle
    {
        get => _editingGameTitle;
        private set => SetProperty(ref _editingGameTitle, value);
    }

    public string? CatalogError { get; }

    public IReadOnlyList<GameRecord> Games
    {
        get => _games;
        private set
        {
            if (SetProperty(ref _games, value))
            {
                OnPropertyChanged(nameof(HasGames));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(NeedsSelection));
            }
        }
    }

    public bool HasStartupError => !string.IsNullOrWhiteSpace(CatalogError);

    public bool IsCatalogAvailable => CatalogService is not null;

    public bool IsEditorAvailable => IsCatalogAvailable && !HasStartupError;

    public bool IsEditing => EditingRecordId.HasValue;

    public bool IsAdding => IsEditorAvailable && !IsEditing;

    public bool CanStartEdit => IsEditorAvailable && !IsEditing && SelectedGame is not null;

    public bool CanSaveChanges => IsEditorAvailable && IsEditing;

    public bool HasOperationError => !string.IsNullOrWhiteSpace(OperationError);

    public bool HasGames => Games.Count > 0;

    public bool IsEmpty => IsCatalogAvailable && !HasStartupError && !HasGames;

    public bool HasSelection => SelectedGame is not null;

    public bool NeedsSelection => HasGames && !HasSelection;

    public string? SelectedTitleText => SelectedGame?.Title;

    public string? SelectedLocationText => SelectedGame?.Location;

    public string? SelectedMediumText => SelectedGame?.Medium switch
    {
        GameMedium.Dvd => "DVD",
        GameMedium.Usb => "USB",
        GameMedium.MemoryCard => "Memory Card",
        _ => null
    };

    public string? SelectedDvdNumberText => SelectedGame?.DvdNumber?.ToString(CultureInfo.InvariantCulture)
        ?? "Not applicable";

    public string? SelectedFormatText => SelectedGame?.Format switch
    {
        GameFormat.Iso => "ISO",
        GameFormat.Chd => "CHD",
        _ => null
    };

    public string? SelectedSizeText => SelectedGame?.SizeBytes.ToString(CultureInfo.InvariantCulture);

    public string? SelectedReadableSizeText => SelectedGame is null
        ? null
        : ByteSizeFormatter.Format(SelectedGame.SizeBytes);

    public string SelectedNotesText => string.IsNullOrEmpty(SelectedGame?.Notes)
        ? "None"
        : SelectedGame.Notes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperationError))]
    public partial string? OperationError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(NeedsSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedTitleText))]
    [NotifyPropertyChangedFor(nameof(SelectedLocationText))]
    [NotifyPropertyChangedFor(nameof(SelectedMediumText))]
    [NotifyPropertyChangedFor(nameof(SelectedDvdNumberText))]
    [NotifyPropertyChangedFor(nameof(SelectedFormatText))]
    [NotifyPropertyChangedFor(nameof(SelectedSizeText))]
    [NotifyPropertyChangedFor(nameof(SelectedReadableSizeText))]
    [NotifyPropertyChangedFor(nameof(SelectedNotesText))]
    public partial GameRecord? SelectedGame { get; set; }

    partial void OnSelectedGameChanged(GameRecord? value) => StartEditCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(IsAdding))]
    private void AddGame()
    {
        OperationError = null;
        var input = Editor.ValidateFields();
        if (input is null)
        {
            OperationError = "Correct the fields above.";
            return;
        }

        var result = CatalogService!.Create(input);
        if (!result.IsSuccess)
        {
            OperationError = result.Error;
            return;
        }

        var saved = result.Value!;
        var refresh = CatalogService.List();
        if (refresh.IsSuccess)
        {
            var listed = refresh.Value ?? [];
            Games = listed.Any(game => game.Id == saved.Id) ? listed : [.. listed, saved];
        }
        else
        {
            Games = [.. Games, saved];
            OperationError = "The game record was saved, but the catalog could not be refreshed.";
        }

        SelectedGame = Games.First(game => game.Id == saved.Id);
        Editor.Reset();
    }

    [RelayCommand(CanExecute = nameof(CanStartEdit))]
    private void StartEdit()
    {
        if (SelectedGame is null)
        {
            return;
        }

        Editor.Load(SelectedGame);
        EditingGameTitle = SelectedGame.Title;
        EditingRecordId = SelectedGame.Id;
        OperationError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveChanges))]
    private void SaveChanges()
    {
        if (EditingRecordId is not Guid id)
        {
            return;
        }

        OperationError = null;
        var input = Editor.ValidateFields();
        if (input is null)
        {
            OperationError = "Correct the fields above.";
            return;
        }

        var result = CatalogService!.Edit(id, input);
        if (!result.IsSuccess)
        {
            OperationError = result.Error;
            return;
        }

        var saved = result.Value!;
        var refresh = CatalogService.List();
        if (refresh.IsSuccess)
        {
            var listed = refresh.Value ?? [];
            Games = listed.Any(game => game.Id == id)
                ? listed.Select(game => game.Id == id ? saved : game).ToArray()
                : [.. listed, saved];
        }
        else
        {
            Games = Games.Any(game => game.Id == id)
                ? Games.Select(game => game.Id == id ? saved : game).ToArray()
                : [.. Games, saved];
            OperationError = "Changes were saved, but the catalog could not be refreshed.";
        }

        SelectedGame = Games.First(game => game.Id == id);
        EndEditing();
    }

    [RelayCommand(CanExecute = nameof(IsEditing))]
    private void CancelEdit()
    {
        EndEditing();
        OperationError = null;
    }

    private void EndEditing()
    {
        EditingRecordId = null;
        EditingGameTitle = null;
        Editor.Reset();
    }
}
