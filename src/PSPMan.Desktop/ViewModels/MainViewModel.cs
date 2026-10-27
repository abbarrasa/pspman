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

    [RelayCommand(CanExecute = nameof(IsEditorAvailable))]
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
}
