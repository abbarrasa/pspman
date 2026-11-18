using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel
{
    public GameCopyViewModel? Copy { get; private set; }
    public bool HasCopy => Copy is not null;
    public bool CanCopySelectedGame => Copy is { CanStart: true };

    internal void AttachCopy(GameCopyViewModel? copy)
    {
        if (Copy is not null) Copy.PropertyChanged -= OnCopyChanged;
        Copy = copy;
        if (copy is not null)
        {
            copy.SetSelectedGame(SelectedGame);
            copy.PropertyChanged += OnCopyChanged;
        }
        OnPropertyChanged(nameof(Copy)); OnPropertyChanged(nameof(HasCopy));
        OnPropertyChanged(nameof(ShowLegacyCopy));
        UpdateCopyCommand();
        UpdatePreparationCommands();
    }

    private void OnCopyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(GameCopyViewModel.CanStart)) UpdateCopyCommand();
        if (args.PropertyName is nameof(GameCopyViewModel.CanStart) or nameof(GameCopyViewModel.CanSubmitCopies)) UpdatePreparationCommands();
    }
    private void UpdateCopyCommand()
    { OnPropertyChanged(nameof(CanCopySelectedGame)); CopySelectedGameCommand.NotifyCanExecuteChanged(); }

    [RelayCommand(CanExecute = nameof(CanCopySelectedGame))]
    private Task CopySelectedGameAsync() => Copy?.StartAsync() ?? Task.CompletedTask;
}
