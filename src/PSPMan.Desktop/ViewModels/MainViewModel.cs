using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Games;
using PSPMan.Desktop.Services;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private IReadOnlyList<GameRecord> _games = [];
    private string? _catalogError;
    private Guid? _editingRecordId;
    private string? _editingGameTitle;
    private Guid? _pendingDeleteRecordId;
    private string? _pendingDeleteTitle;

    public MainViewModel() : this(null, null)
    {
    }

    public MainViewModel(GameCatalogService? catalogService, string? catalogError,
        IGameExportFolderPicker? exportFolderPicker = null,
        IGameCatalogExportWriter? exportWriter = null, string? catalogPath = null)
    {
        _exportFolderPicker = exportFolderPicker;
        _exportWriter = exportWriter;
        _catalogPath = catalogPath;
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
                DeleteSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? EditingGameTitle
    {
        get => _editingGameTitle;
        private set => SetProperty(ref _editingGameTitle, value);
    }

    public Guid? PendingDeleteRecordId
    {
        get => _pendingDeleteRecordId;
        private set
        {
            if (SetProperty(ref _pendingDeleteRecordId, value))
            {
                OnPropertyChanged(nameof(IsDeletePending));
                DeleteSelectedCommand.NotifyCanExecuteChanged();
                ConfirmDeleteCommand.NotifyCanExecuteChanged();
                CancelDeleteCommand.NotifyCanExecuteChanged();
                StartEditCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? PendingDeleteTitle
    {
        get => _pendingDeleteTitle;
        private set => SetProperty(ref _pendingDeleteTitle, value);
    }

    public string? CatalogError
    {
        get => _catalogError;
        private set
        {
            if (SetProperty(ref _catalogError, value))
            {
                OnPropertyChanged(nameof(HasStartupError));
                OnPropertyChanged(nameof(HasAnyError));
                OnPropertyChanged(nameof(HasDistinctOperationError));
                OnPropertyChanged(nameof(IsEditorAvailable));
                OnPropertyChanged(nameof(IsAdding));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(CanOpenExport));
                OnPropertyChanged(nameof(CanExportCatalog));
                AddGameCommand.NotifyCanExecuteChanged();
                OpenExportCommand.NotifyCanExecuteChanged();
                ChooseExportFolderCommand.NotifyCanExecuteChanged();
                ExportCatalogCommand.NotifyCanExecuteChanged();
                ConfirmExportReplacementCommand.NotifyCanExecuteChanged();
                StartEditCommand.NotifyCanExecuteChanged();
                SaveChangesCommand.NotifyCanExecuteChanged();
                DeleteSelectedCommand.NotifyCanExecuteChanged();
                ConfirmDeleteCommand.NotifyCanExecuteChanged();
            }
        }
    }

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

    public bool IsDeletePending => PendingDeleteRecordId.HasValue;

    public bool IsAdding => IsEditorAvailable && !IsEditing;

    public bool CanStartEdit => IsEditorAvailable && !IsEditing && !IsDeletePending && SelectedGame is not null;

    public bool CanStartDelete => IsEditorAvailable && !IsEditing && !IsDeletePending && SelectedGame is not null;

    public bool CanConfirmDelete => IsEditorAvailable && IsDeletePending;

    public bool CanSaveChanges => IsEditorAvailable && IsEditing;

    public bool HasOperationError => !string.IsNullOrWhiteSpace(OperationError);

    public bool HasAnyError => HasStartupError || HasOperationError;

    public bool HasDistinctOperationError => HasOperationError && OperationError != CatalogError;

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
    [NotifyPropertyChangedFor(nameof(HasAnyError))]
    [NotifyPropertyChangedFor(nameof(HasDistinctOperationError))]
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

    partial void OnSelectedGameChanged(GameRecord? value)
    {
        StartEditCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

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
            ReportCorruption(result);
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
            ReportCorruption(refresh);
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
            ReportCorruption(result);
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
            ReportCorruption(refresh);
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

    [RelayCommand(CanExecute = nameof(CanStartDelete))]
    private void DeleteSelected()
    {
        if (SelectedGame is null)
        {
            return;
        }

        PendingDeleteTitle = SelectedGame.Title;
        PendingDeleteRecordId = SelectedGame.Id;
        OperationError = null;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmDelete))]
    private void ConfirmDelete()
    {
        if (PendingDeleteRecordId is not Guid id)
        {
            return;
        }

        OperationError = null;
        var result = CatalogService!.Delete(id);
        if (!result.IsSuccess)
        {
            ReportCorruption(result);
            OperationError = result.Error;
            return;
        }

        var selectedId = SelectedGame?.Id;
        var refresh = CatalogService.List();
        Games = refresh.IsSuccess
            ? (refresh.Value ?? []).Where(game => game.Id != id).ToArray()
            : Games.Where(game => game.Id != id).ToArray();
        SelectedGame = selectedId == id
            ? null
            : Games.FirstOrDefault(game => game.Id == selectedId);
        PendingDeleteRecordId = null;
        PendingDeleteTitle = null;

        if (!refresh.IsSuccess)
        {
            ReportCorruption(refresh);
            OperationError = "The game record was deleted, but the catalog could not be refreshed.";
        }
    }

    private void ReportCorruption<T>(GameCatalogResult<T> result)
    {
        if (result.IsCorrupt)
        {
            CatalogError = result.Error;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDeletePending))]
    private void CancelDelete()
    {
        PendingDeleteRecordId = null;
        PendingDeleteTitle = null;
        OperationError = null;
    }

    private void EndEditing()
    {
        EditingRecordId = null;
        EditingGameTitle = null;
        Editor.Reset();
    }
}
