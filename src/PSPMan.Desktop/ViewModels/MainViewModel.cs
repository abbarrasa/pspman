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
    }

    public GameCatalogService? CatalogService { get; }

    public string? CatalogError { get; }

    public bool HasStartupError => !string.IsNullOrWhiteSpace(CatalogError);

    public bool IsCatalogAvailable => CatalogService is not null;

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}
