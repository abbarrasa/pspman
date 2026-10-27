using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PSPMan.Core.Games;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
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

    public string? CatalogError { get; }

    public IReadOnlyList<GameRecord> Games { get; } = [];

    public bool HasStartupError => !string.IsNullOrWhiteSpace(CatalogError);

    public bool IsCatalogAvailable => CatalogService is not null;

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

    public string SelectedNotesText => string.IsNullOrEmpty(SelectedGame?.Notes)
        ? "None"
        : SelectedGame.Notes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(NeedsSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedTitleText))]
    [NotifyPropertyChangedFor(nameof(SelectedLocationText))]
    [NotifyPropertyChangedFor(nameof(SelectedMediumText))]
    [NotifyPropertyChangedFor(nameof(SelectedDvdNumberText))]
    [NotifyPropertyChangedFor(nameof(SelectedFormatText))]
    [NotifyPropertyChangedFor(nameof(SelectedSizeText))]
    [NotifyPropertyChangedFor(nameof(SelectedNotesText))]
    public partial GameRecord? SelectedGame { get; set; }
}
