using System.ComponentModel;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel
{
    public GameCopyViewModel? Copy { get; private set; }
    public bool HasCopy => Copy is not null;

    internal void AttachCopy(GameCopyViewModel? copy)
    {
        if (Copy is not null) Copy.PropertyChanged -= OnCopyChanged;
        Copy = copy;
        if (copy is not null)
        {
            copy.PropertyChanged += OnCopyChanged;
            EnableCopyPreparation();
        }
        OnPropertyChanged(nameof(Copy)); OnPropertyChanged(nameof(HasCopy));
        OnPropertyChanged(nameof(ShowCopyDrafts));
        UpdatePreparationCommands();
    }

    private void OnCopyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(GameCopyViewModel.CanSubmitCopies)) UpdatePreparationCommands();
        if (args.PropertyName is nameof(GameCopyViewModel.HasCloseConfirmation) or nameof(GameCopyViewModel.IsClosing))
            OnPropertyChanged(nameof(ShowCopyDrafts));
    }
}
