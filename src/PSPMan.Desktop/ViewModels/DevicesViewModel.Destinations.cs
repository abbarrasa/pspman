using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;

namespace PSPMan.Desktop.ViewModels;

public sealed record PspDestinationChoice(Guid GameId, long GameVersion, DeviceKey Device, long Revision,
    string? MediaGeneration, string Name, ImmutableArray<DeviceVolumeViewModel> Volumes);

public sealed record GameDestinationViewModel(Guid? GameId, DestinationResolutionStatus Status,
    ImmutableArray<PspDestinationChoice> Candidates, DeviceKey? PreferredDevice, DeviceKey? SelectedDevice,
    ImmutableArray<DeviceVolumeViewModel> Volumes, string StatusText)
{
    public ImmutableArray<PspDestinationOptionViewModel> Options { get; init; } = [];
    public string? SelectedDeviceName => Candidates.FirstOrDefault(choice => choice.Device == SelectedDevice)?.Name;
    public bool HasDestinationChoices => Candidates.Length > 1 || (Status == DestinationResolutionStatus.Unavailable && !Candidates.IsEmpty);
}

public sealed partial class DevicesViewModel
{
    private Guid? _requestedGameId;
    private Guid? _selectedGameId;
    private long _selectedGameVersion;
    private DeviceSessionSnapshot? _destinationSnapshot;

    public IAsyncRelayCommand<PspDestinationChoice> ChoosePspCommand { get; private set; } = null!;
    public GameDestinationViewModel GameDestination { get; private set; } = NoGameDestination();
    public string? DestinationSelectionError { get; private set; }

    private static GameDestinationViewModel NoGameDestination() => new(null, DestinationResolutionStatus.Unavailable,
        [], null, null, [], "Select a game to choose its PSP destination.");

    private void InitializeDestinationCommands()
    {
        ChoosePspCommand = new AsyncRelayCommand<PspDestinationChoice>(SelectPspAsync, choice => IsCurrentDestination(choice));
        ChoosePspCommand.CanExecuteChanged += (_, _) => _postToUi(() =>
        {
            lock (_gate) foreach (var option in GameDestination.Options) option.RefreshAvailability();
        });
    }

    /// <summary>Receives catalog metadata only. Device choices never modify catalog records.</summary>
    public void SetSelectedGame(Guid? gameId, GameMedium? medium, string? location, MountedPathSyntax syntax)
    {
        long version;
        lock (_gate)
        {
            if (_disposed) return;
            _requestedGameId = gameId;
            version = ++_gameInputVersion;
        }
        _postToUi(() =>
        {
            lock (_gate)
            {
                if (_disposed || version != _gameInputVersion) return;
                _selectedGameId = gameId;
                _selectedGameVersion = version;
                _gameMedium = medium;
                _gameLocation = location;
                _pathSyntax = syntax;
                DestinationSelectionError = null;
                OnPropertyChanged(nameof(DestinationSelectionError));
                ProjectSourcePath();
                ProjectGameDestination();
            }
        });
    }

    private void ProjectDestination(DeviceSessionSnapshot snapshot)
    {
        _destinationSnapshot = snapshot;
        ProjectGameDestination();
    }

    private void ProjectGameDestination()
    {
        var previous = GameDestination;
        if (_selectedGameId is not { } gameId) GameDestination = NoGameDestination();
        else
        {
            var inventory = _destinationSnapshot?.Inventory ?? new DeviceInventorySnapshot(0, []);
            var associations = _destinationSnapshot?.Associations ?? new DeviceSessionState();
            var resolution = DeviceAssociationRules.ResolveDestination(gameId, inventory, associations);
            var choices = resolution.Candidates.Select(device =>
            {
                var row = new DeviceItemViewModel(device);
                return new PspDestinationChoice(gameId, _selectedGameVersion, device.Key, inventory.Revision,
                    device.MediaGeneration, row.Name, row.Volumes);
            }).ToImmutableArray();
            var selected = choices.FirstOrDefault(choice => choice.Device == resolution.SelectedDevice);
            var preferred = associations.PspDestinations.TryGetValue(gameId, out var key) ? key : (DeviceKey?)null;
            GameDestination = new(gameId, resolution.Status, choices, preferred, resolution.SelectedDevice,
                selected?.Volumes ?? [], resolution.Status switch
                {
                    DestinationResolutionStatus.Selected => "PSP destination available; not proof that a future copy will succeed.",
                    DestinationResolutionStatus.ChooseDevice => "Choose a PSP destination for this game.",
                    _ => preferred is not null ? "The selected PSP destination is unavailable. Choose another PSP explicitly."
                        : "PSP destination unavailable."
                })
            {
                Options = choices.Select(choice => new PspDestinationOptionViewModel(choice,
                    choice.Device == resolution.SelectedDevice, () => ChoosePspCommand.CanExecute(choice),
                    () => ChoosePspCommand.ExecuteAsync(choice))).ToImmutableArray()
            };
        }
        foreach (var option in previous.Options) option.RefreshAvailability();
        OnPropertyChanged(nameof(GameDestination));
        ChoosePspCommand.NotifyCanExecuteChanged();
    }

    private bool IsCurrentDestination(PspDestinationChoice? choice, DeviceSessionSnapshot? observation = null)
    {
        lock (_gate)
        {
            if (_disposed || _session is null || choice is null || _requestedGameId != choice.GameId
                || _gameInputVersion != choice.GameVersion) return false;
            var snapshot = observation ?? _session.Snapshot;
            if (snapshot.Inventory.Revision != choice.Revision) return false;
            return DeviceAssociationRules.ResolveDestination(choice.GameId, snapshot.Inventory, snapshot.Associations)
                .Candidates.Any(device => device.Key == choice.Device && device.MediaGeneration == choice.MediaGeneration);
        }
    }

    private async Task SelectPspAsync(PspDestinationChoice? choice)
    {
        var entered = false;
        try
        {
            // Serialize with source/volume changes so each operation preserves the latest other choices.
            await _sourceCommandGate.WaitAsync(_sourceLifetime.Token).ConfigureAwait(false);
            entered = true;
            var snapshot = _session?.Snapshot;
            if (snapshot is null || !IsCurrentDestination(choice, snapshot))
            {
                PostDestinationError(choice, "The PSP selection is no longer available. Choose from the current list.");
                return;
            }
            var state = DeviceAssociationRules.ChooseDestination(snapshot.Associations, choice!.GameId, choice.Device);
            state = DeviceAssociationRules.ReconcileAssociations(state, snapshot.Inventory);
            await _session!.SetAssociationsAsync(state, _sourceLifetime.Token).ConfigureAwait(false);
            PostDestinationError(choice, null);
            Update(_session.Snapshot);
        }
        catch (OperationCanceledException) when (_sourceLifetime.IsCancellationRequested) { }
        catch (Exception) { PostDestinationError(choice, "Unable to change the PSP destination."); }
        finally { if (entered) _sourceCommandGate.Release(); }
    }

    private void PostDestinationError(PspDestinationChoice? choice, string? error) => _postToUi(() =>
    {
        lock (_gate)
        {
            if (_disposed || choice is null || _requestedGameId != choice.GameId || _gameInputVersion != choice.GameVersion) return;
            DestinationSelectionError = error;
            OnPropertyChanged(nameof(DestinationSelectionError));
        }
    });
}
