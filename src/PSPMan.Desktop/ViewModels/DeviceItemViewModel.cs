using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Devices;

namespace PSPMan.Desktop.ViewModels;

/// <summary>A typed presentation of one immutable observation, not a device-operation service.</summary>
public sealed class DeviceItemViewModel : ViewModelBase
{
    private Func<bool> _canRemove = () => false;
    public bool CanRemove => _canRemove();
    public IAsyncRelayCommand RemoveCommand { get; private set; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);
    public string RemovalText => MediumText == "CD/DVD" ? "Eject" : "Safely remove";

    internal void ConfigureRemoval(Func<bool> canRemove, Func<Task> remove)
    { _canRemove = canRemove; RemoveCommand = new AsyncRelayCommand(remove, () => CanRemove); }
    internal void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanRemove));
        RemoveCommand.NotifyCanExecuteChanged();
        foreach (var volume in Volumes) volume.RefreshCommands();
    }
    public DeviceItemViewModel(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Key = snapshot.Key;
        Name = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? "Unnamed device" : snapshot.DisplayName;
        State = snapshot.State;
        IsPspDestination = (snapshot.Capabilities & DeviceCapabilities.PspDestination) != 0;
        IsSource = !IsPspDestination && (snapshot.Capabilities & (DeviceCapabilities.OpticalSource | DeviceCapabilities.RemovableSource)) != 0;
        MediumText = IsPspDestination ? "PSP" : snapshot.Medium switch
        {
            DeviceMedium.Optical => "CD/DVD",
            DeviceMedium.Usb => "USB",
            DeviceMedium.MemoryCard => "Memory Card",
            DeviceMedium.Psp => "PSP",
            _ => (snapshot.Capabilities & DeviceCapabilities.RemovableSource) != 0 ? "Memory Card" : "Unknown"
        };
        ErrorText = FormatError(snapshot.Error);
        Volumes = (snapshot.Volumes.IsDefault ? [] : snapshot.Volumes)
            .Select(volume => new DeviceVolumeViewModel(volume, snapshot.State)).ToImmutableArray();
        if (State == DeviceState.Ready && !Volumes.Any(volume => volume.IsAvailable)) State = DeviceState.Unavailable;
    }

    public DeviceKey Key { get; }
    public string Identifier => Key.DeviceId;
    public string Name { get; }
    public string MediumText { get; }
    public bool IsSource { get; }
    public bool IsPspDestination { get; }
    public string RoleText => IsPspDestination ? "PSP destination" : IsSource ? "Game source" : "Unavailable device";
    public DeviceState State { get; }
    public string StateText => FormatState(State);
    public bool IsPending => State is DeviceState.Detecting or DeviceState.Mounting or DeviceState.Removing;
    public string? ErrorText { get; }
    public bool HasErrors => ErrorText is not null || Volumes.Any(volume => volume.ErrorText is not null);
    public ImmutableArray<DeviceVolumeViewModel> Volumes { get; }

    internal static string? FormatError(DeviceOperationError? error) => error is null ? null
        : string.IsNullOrWhiteSpace(error.Message) ? error.Code : $"{error.Code}: {error.Message}";
    internal static string FormatState(DeviceState state) => state switch
    {
        DeviceState.Detecting => "Detecting", DeviceState.Unmounted => "Not mounted",
        DeviceState.Mounting => "Mounting", DeviceState.Ready => "Ready", DeviceState.Removing => "Removing",
        DeviceState.Error => "Error", _ => "Unavailable"
    };
}

public sealed class DeviceVolumeViewModel : ViewModelBase
{
    private Func<bool> _canRetry = () => false;
    public bool CanRetryMount => _canRetry();
    public IAsyncRelayCommand RetryMountCommand { get; private set; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);
    internal void ConfigureRetry(Func<bool> canRetry, Func<Task> retry)
    { _canRetry = canRetry; RetryMountCommand = new AsyncRelayCommand(retry, () => CanRetryMount); }
    internal void RefreshCommands()
    { OnPropertyChanged(nameof(CanRetryMount)); RetryMountCommand.NotifyCanExecuteChanged(); }
    public DeviceVolumeViewModel(VolumeSnapshot snapshot, DeviceState deviceState)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Key = snapshot.Key;
        Label = string.IsNullOrWhiteSpace(snapshot.Label) ? "Unlabelled volume" : snapshot.Label;
        ErrorText = DeviceItemViewModel.FormatError(snapshot.Error);
        var deviceAllowsRoot = deviceState is DeviceState.Ready or DeviceState.Mounting or DeviceState.Error;
        Root = deviceAllowsRoot && snapshot.State == DeviceState.Ready && snapshot.Error is null
            && !string.IsNullOrWhiteSpace(snapshot.Root) ? snapshot.Root : null;
        State = !deviceAllowsRoot ? deviceState : snapshot.Error is not null ? DeviceState.Error : snapshot.State;
        if (State == DeviceState.Ready && Root is null) State = DeviceState.Unavailable;
    }

    public VolumeKey Key { get; }
    public string Label { get; }
    public string? Root { get; }
    public bool IsAvailable => Root is not null;
    public DeviceState State { get; }
    public string StateText => DeviceItemViewModel.FormatState(State);
    public string? ErrorText { get; }
}
