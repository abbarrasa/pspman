using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace PSPMan.Desktop.ViewModels;

/// <summary>Presentation action for one immutable, game-scoped destination choice.</summary>
public sealed class PspDestinationOptionViewModel : ViewModelBase
{
    public PspDestinationOptionViewModel(PspDestinationChoice choice, bool selected, Func<bool> canSelect, Func<Task> select)
    {
        Choice = choice;
        IsSelected = selected;
        SelectCommand = new AsyncRelayCommand(select, canSelect);
        SelectCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(CanSelect));
    }

    public PspDestinationChoice Choice { get; }
    public string Name => Choice.Name;
    public ImmutableArray<DeviceVolumeViewModel> Volumes => Choice.Volumes;
    public bool IsSelected { get; }
    public IAsyncRelayCommand SelectCommand { get; }
    public bool CanSelect => SelectCommand.CanExecute(null);

    internal void RefreshAvailability() { OnPropertyChanged(nameof(CanSelect)); SelectCommand.NotifyCanExecuteChanged(); }
}
