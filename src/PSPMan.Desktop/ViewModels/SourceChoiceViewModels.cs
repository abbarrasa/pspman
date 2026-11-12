using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Devices;

namespace PSPMan.Desktop.ViewModels;

/// <summary>One observed source option with a presentation-only action delegated to its owner.</summary>
public sealed class SourceChoice(SourceGroup group, DeviceKey device, long revision, string? mediaGeneration,
    string name, ImmutableArray<DeviceVolumeViewModel> volumes) : ViewModelBase
{
    public SourceGroup Group { get; } = group;
    public DeviceKey Device { get; } = device;
    public long Revision { get; } = revision;
    public string? MediaGeneration { get; } = mediaGeneration;
    public string Name { get; } = name;
    public ImmutableArray<DeviceVolumeViewModel> Volumes { get; } = volumes;
    public bool IsSelected { get; private set; }
    public IAsyncRelayCommand SelectCommand { get; private set; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);
    public bool CanSelect => SelectCommand.CanExecute(null);

    internal void ConfigureSelection(bool selected, Func<bool> canSelect, Func<Task> select)
    {
        IsSelected = selected;
        SelectCommand = new AsyncRelayCommand(select, canSelect);
        SelectCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(CanSelect));
    }

    internal void RefreshAvailability() { OnPropertyChanged(nameof(CanSelect)); SelectCommand.NotifyCanExecuteChanged(); }
}

public sealed class SourceVolumeChoice(SourceGroup group, VolumeKey volume, long revision, string? mediaGeneration,
    string label, string root) : ViewModelBase
{
    public SourceGroup Group { get; } = group;
    public VolumeKey Volume { get; } = volume;
    public long Revision { get; } = revision;
    public string? MediaGeneration { get; } = mediaGeneration;
    public string Label { get; } = label;
    public string Root { get; } = root;
    public bool IsSelected { get; private set; }
    public IAsyncRelayCommand SelectCommand { get; private set; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);
    public bool CanSelect => SelectCommand.CanExecute(null);

    internal void ConfigureSelection(bool selected, Func<bool> canSelect, Func<Task> select)
    {
        IsSelected = selected;
        SelectCommand = new AsyncRelayCommand(select, canSelect);
        SelectCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(CanSelect));
    }

    internal void RefreshAvailability() { OnPropertyChanged(nameof(CanSelect)); SelectCommand.NotifyCanExecuteChanged(); }
}
