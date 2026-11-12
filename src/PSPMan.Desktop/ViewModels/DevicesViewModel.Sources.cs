using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;

namespace PSPMan.Desktop.ViewModels;

public sealed record SourceSelectionViewModel(SourceResolutionStatus Status, ImmutableArray<SourceChoice> Candidates,
    ImmutableArray<SourceVolumeChoice> Volumes, DeviceKey? PreferredDevice, DeviceKey? SelectedDevice,
    VolumeKey? SelectedVolume, string? Root, string StatusText)
{
    public bool HasMultipleVolumes => Volumes.Length > 1;
    public string? SelectedDeviceName => Candidates.FirstOrDefault(choice => choice.Device == SelectedDevice)?.Name;
    public string? SelectedVolumeLabel => Volumes.FirstOrDefault(choice => choice.Volume == SelectedVolume)?.Label;
}

public sealed partial class DevicesViewModel
{
    private readonly SemaphoreSlim _sourceCommandGate = new(1, 1);
    private readonly CancellationTokenSource _sourceLifetime = new();
    private GameMedium? _gameMedium;
    private string? _gameLocation;
    private MountedPathSyntax _pathSyntax;
    private long _gameInputVersion;

    public IAsyncRelayCommand<SourceChoice> ChooseSourceCommand { get; private set; } = null!;
    public IAsyncRelayCommand<SourceVolumeChoice> ChooseSourceVolumeCommand { get; private set; } = null!;
    public SourceSelectionViewModel OpticalSource { get; private set; } = EmptySource();
    public SourceSelectionViewModel RemovableSource { get; private set; } = EmptySource();
    public string? ConstructedSourcePath { get; private set; }
    public string? SourcePathError { get; private set; }
    public string SourcePathStatusText { get; private set; } = "Select a game to resolve its source.";
    public string? SourceSelectionError { get; private set; }

    private static SourceSelectionViewModel EmptySource() => new(SourceResolutionStatus.Unavailable, [], [], null, null, null, null, "Source unavailable.");
    private void InitializeSourceCommands()
    {
        ChooseSourceCommand = new AsyncRelayCommand<SourceChoice>(choice => SelectSourceAsync(choice, null), choice => IsCurrentChoice(choice, null));
        ChooseSourceVolumeCommand = new AsyncRelayCommand<SourceVolumeChoice>(choice => SelectSourceAsync(null, choice), choice => IsCurrentChoice(null, choice));
        ChooseSourceCommand.CanExecuteChanged += (_, _) => PostSourceAvailability();
        ChooseSourceVolumeCommand.CanExecuteChanged += (_, _) => PostSourceAvailability();
    }

    private void PostSourceAvailability() => _postToUi(() =>
    {
        lock (_gate) { RefreshSourceChoices(OpticalSource); RefreshSourceChoices(RemovableSource); }
    });

    private static void RefreshSourceChoices(SourceSelectionViewModel source)
    {
        foreach (var choice in source.Candidates) choice.RefreshAvailability();
        foreach (var choice in source.Volumes) choice.RefreshAvailability();
    }

    private bool IsCurrentChoice(SourceChoice? source, SourceVolumeChoice? volume, DeviceSessionSnapshot? observation = null)
    {
        lock (_gate)
        {
            if (_disposed || _session is null || (source is null && volume is null)) return false;
            var snapshot = observation ?? _session.Snapshot;
            var group = source?.Group ?? volume!.Group;
            if (group is not (SourceGroup.Optical or SourceGroup.Removable)) return false;
            var key = source?.Device ?? volume!.Volume.Device;
            var revision = source?.Revision ?? volume!.Revision;
            var generation = source is not null ? source.MediaGeneration : volume!.MediaGeneration;
            var medium = group == SourceGroup.Optical ? GameMedium.Dvd : GameMedium.Usb;
            var candidates = DeviceAssociationRules.ResolveSource(medium, snapshot.Inventory).Candidates;
            var device = candidates.SingleOrDefault(device => device.Key == key);
            if (revision != snapshot.Inventory.Revision || device is null || device.MediaGeneration != generation) return false;
            if (volume is null) return true;
            var resolution = DeviceAssociationRules.ResolveSourceVolume(medium, snapshot.Inventory, snapshot.Associations);
            return resolution.SelectedDevice == key && resolution.Candidates.Any(candidate => candidate.Key == volume.Volume);
        }
    }

    private async Task SelectSourceAsync(SourceChoice? source, SourceVolumeChoice? volume)
    {
        var entered = false;
        try
        {
            await _sourceCommandGate.WaitAsync(_sourceLifetime.Token).ConfigureAwait(false);
            entered = true;
            var snapshot = _session?.Snapshot;
            if (snapshot is null || !IsCurrentChoice(source, volume, snapshot))
            {
                PostSourceError("The source selection is no longer available. Choose from the current list.");
                return;
            }
            // Carry evidence from this exact observation into the serialized Core update. A media
            // change before Core applies it must become unavailable, not an implicit fresh choice.
            var state = source is not null
                ? DeviceAssociationRules.ChooseSource(snapshot.Associations, source.Group, source.Device)
                : DeviceAssociationRules.ChooseVolume(snapshot.Associations, volume!.Group, volume.Volume);
            state = DeviceAssociationRules.ReconcileAssociations(state, snapshot.Inventory);
            await _session!.SetAssociationsAsync(state, _sourceLifetime.Token).ConfigureAwait(false);
            PostSourceError(null);
            Update(_session.Snapshot);
        }
        catch (OperationCanceledException) when (_sourceLifetime.IsCancellationRequested) { }
        catch (Exception) { PostSourceError("Unable to change the source selection."); }
        finally { if (entered) _sourceCommandGate.Release(); }
    }

    private void PostSourceError(string? error) => _postToUi(() =>
    {
        lock (_gate)
        {
            if (_disposed) return;
            SourceSelectionError = error;
            OnPropertyChanged(nameof(SourceSelectionError));
        }
    });

    private void ProjectSources(DeviceSessionSnapshot snapshot)
    {
        var oldOptical = OpticalSource;
        var oldRemovable = RemovableSource;
        OpticalSource = ProjectSource(SourceGroup.Optical, snapshot);
        RemovableSource = ProjectSource(SourceGroup.Removable, snapshot);
        RefreshSourceChoices(oldOptical);
        RefreshSourceChoices(oldRemovable);
        ProjectSourcePath();
        OnPropertyChanged(nameof(OpticalSource));
        OnPropertyChanged(nameof(RemovableSource));
        ChooseSourceCommand.NotifyCanExecuteChanged();
        ChooseSourceVolumeCommand.NotifyCanExecuteChanged();
    }

    private SourceSelectionViewModel ProjectSource(SourceGroup group, DeviceSessionSnapshot snapshot)
    {
        var medium = group == SourceGroup.Optical ? GameMedium.Dvd : GameMedium.Usb;
        var source = DeviceAssociationRules.ResolveSource(medium, snapshot.Inventory, snapshot.Associations);
        var volume = DeviceAssociationRules.ResolveSourceVolume(medium, snapshot.Inventory, snapshot.Associations);
        var choices = source.Candidates.Select(device =>
        {
            var row = new DeviceItemViewModel(device);
            var choice = new SourceChoice(group, device.Key, snapshot.Inventory.Revision, device.MediaGeneration, row.Name, row.Volumes);
            choice.ConfigureSelection(source.SelectedDevice == device.Key, () => ChooseSourceCommand.CanExecute(choice),
                () => ChooseSourceCommand.ExecuteAsync(choice));
            return choice;
        }).ToImmutableArray();
        var selected = source.Candidates.SingleOrDefault(device => device.Key == volume.SelectedDevice);
        var volumes = volume.Candidates.Select(candidate =>
        {
            var choice = new SourceVolumeChoice(group, candidate.Key, snapshot.Inventory.Revision,
                selected?.MediaGeneration, string.IsNullOrWhiteSpace(candidate.Label) ? "Unlabelled volume" : candidate.Label, candidate.Root!);
            choice.ConfigureSelection(volume.SelectedVolume?.Key == candidate.Key, () => ChooseSourceVolumeCommand.CanExecute(choice),
                () => ChooseSourceVolumeCommand.ExecuteAsync(choice));
            return choice;
        }).ToImmutableArray();
        var preferred = group == SourceGroup.Optical ? snapshot.Associations.OpticalSource : snapshot.Associations.RemovableSource;
        return new(volume.Status, choices, volumes, preferred, volume.SelectedDevice, volume.SelectedVolume?.Key,
            volume.SelectedVolume?.Root, volume.Status switch
            {
                SourceResolutionStatus.ChooseDevice => "Choose a source device.",
                SourceResolutionStatus.ChooseVolume => "Choose a source volume.",
                SourceResolutionStatus.Selected => "Source selected.",
                _ => preferred is not null ? "The selected source is unavailable. Choose another source explicitly." : "Source unavailable."
            });
    }

    /// <summary>Updates source metadata while retaining the currently requested game identity.</summary>
    public void SetGameSource(GameMedium? medium, string? location, MountedPathSyntax syntax)
    {
        lock (_gate) SetSelectedGame(_requestedGameId, medium, location, syntax);
    }

    private void ProjectSourcePath()
    {
        ConstructedSourcePath = null;
        SourcePathError = null;
        if (_gameMedium is null) SourcePathStatusText = "Select a game to resolve its source.";
        else if (_gameMedium is not (GameMedium.Dvd or GameMedium.Usb or GameMedium.MemoryCard))
            SourcePathStatusText = SourcePathError = "Unsupported game medium.";
        else
        {
            var source = _gameMedium == GameMedium.Dvd ? OpticalSource : RemovableSource;
            if (source.Status != SourceResolutionStatus.Selected) SourcePathStatusText = source.StatusText;
            else
            {
                var result = MountedGamePathRules.Resolve(source.Root, _gameLocation, _pathSyntax);
                ConstructedSourcePath = result.Path;
                SourcePathError = result.Error;
                SourcePathStatusText = result.Error ?? "Constructed path only; not proof that the game file exists or can be accessed.";
            }
        }
        OnPropertyChanged(nameof(ConstructedSourcePath));
        OnPropertyChanged(nameof(SourcePathError));
        OnPropertyChanged(nameof(SourcePathStatusText));
    }

    private void DisposeSourceCommands()
    {
        _sourceLifetime.Cancel();
        _postToUi(() => { ChooseSourceCommand.NotifyCanExecuteChanged(); ChooseSourceVolumeCommand.NotifyCanExecuteChanged(); });
    }
}
