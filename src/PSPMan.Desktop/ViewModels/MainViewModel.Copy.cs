using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel
{
    public GameCopyViewModel? Copy { get; private set; }
    public bool HasCopy => Copy is not null;
    public bool CanCopySelectedGame => Copy is { IsBusy: false };

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
        UpdateCopyCommand();
    }

    private void OnCopyChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(GameCopyViewModel.IsBusy)) UpdateCopyCommand(); }
    private void UpdateCopyCommand()
    { OnPropertyChanged(nameof(CanCopySelectedGame)); CopySelectedGameCommand.NotifyCanExecuteChanged(); }

    [RelayCommand(CanExecute = nameof(CanCopySelectedGame))]
    private Task CopySelectedGameAsync() => Copy?.StartAsync() ?? Task.CompletedTask;
}
