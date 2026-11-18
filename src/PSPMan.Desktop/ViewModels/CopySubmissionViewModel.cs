using System;
using System.Collections.Immutable;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;

namespace PSPMan.Desktop.ViewModels;

public sealed record CopyDeviceOption(DeviceSnapshot Device, string Label);
public sealed record CopyVolumeOption(VolumeSnapshot Volume, string Label);

/// <summary>Editable choices for one future send, never shared session associations.</summary>
public sealed partial class CopySubmissionViewModel : ViewModelBase
{
    private DeviceSessionSnapshot observation;
    private readonly MountedPathSyntax syntax;
    private bool updating;
    public CopySubmissionViewModel(GameRecord game, DeviceSessionSnapshot snapshot, MountedPathSyntax syntax)
    {
        Game = game; observation = snapshot; this.syntax = syntax;
        Refresh(snapshot);
        updating = true;
        var source = DeviceAssociationRules.ResolveSourceVolume(game.Medium, snapshot.Inventory, snapshot.Associations);
        SelectedSource = Sources.FirstOrDefault(option => option.Device.Key == source.SelectedDevice);
        RefreshVolumes();
        SelectedVolume = Volumes.FirstOrDefault(option => option.Volume.Key == source.SelectedVolume?.Key);
        var destination = DeviceAssociationRules.ResolveDestination(game.Id, snapshot.Inventory, snapshot.Associations);
        SelectedPsp = Psps.FirstOrDefault(option => option.Device.Key == destination.SelectedDevice);
        updating = false;
        Validate();
    }
    public GameRecord Game { get; private set; }
    public string Title => Game.Title;
    [ObservableProperty] private ImmutableArray<CopyDeviceOption> sources = [];
    [ObservableProperty] private ImmutableArray<CopyVolumeOption> volumes = [];
    [ObservableProperty] private ImmutableArray<CopyDeviceOption> psps = [];
    [ObservableProperty] private CopyDeviceOption? selectedSource;
    [ObservableProperty] private CopyVolumeOption? selectedVolume;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SelectedPspRoot))] private CopyDeviceOption? selectedPsp;
    public string SelectedPspRoot => SelectedPsp?.Device.Volumes.FirstOrDefault()?.Root ?? "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool isReady;
    private GameCopyError? error;

    partial void OnSelectedSourceChanged(CopyDeviceOption? value)
    {
        if (updating) return;
        updating = true;
        SelectedVolume = null;
        RefreshVolumes();
        SelectedVolume = Volumes.Length == 1 ? Volumes[0] : null;
        updating = false;
        Validate();
    }
    partial void OnSelectedVolumeChanged(CopyVolumeOption? value) { if (!updating) Validate(); }
    partial void OnSelectedPspChanged(CopyDeviceOption? value) { if (!updating) Validate(); }

    internal void Refresh(DeviceSessionSnapshot snapshot)
    {
        observation = snapshot;
        updating = true;
        var previousSource = SelectedSource;
        var previousPsp = SelectedPsp;
        var sourceCandidates = DeviceAssociationRules.ResolveSource(Game.Medium, snapshot.Inventory).Candidates;
        var source = PreserveDevice(sourceCandidates.Select(DeviceOption).ToImmutableArray(), previousSource, out var sourceOptions);
        Sources = sourceOptions;
        SelectedSource = source;
        RefreshVolumes();
        var pspCandidates = DeviceAssociationRules.ResolveDestination(Game.Id, snapshot.Inventory, new()).Candidates;
        var psp = PreserveDevice(pspCandidates.Select(DeviceOption).ToImmutableArray(), previousPsp, out var pspOptions, destination: true);
        Psps = pspOptions;
        SelectedPsp = psp;
        updating = false;
        Validate();
    }
    internal void UpdateGame(GameRecord game)
    { Game = game; OnPropertyChanged(nameof(Game)); OnPropertyChanged(nameof(Title)); Refresh(observation); }

    private static CopyDeviceOption DeviceOption(DeviceSnapshot device) => new(device,
        string.IsNullOrWhiteSpace(device.DisplayName) ? device.Key.DeviceId : device.DisplayName);
    private static bool SameDevice(DeviceSnapshot first, DeviceSnapshot second) => first.Key == second.Key
        && first.Identity == second.Identity && first.MediaGeneration == second.MediaGeneration;
    private static bool SameVolume(VolumeSnapshot first, VolumeSnapshot second) => first.Key == second.Key
        && first.Identity == second.Identity && first.Root == second.Root;
    private static bool SameDestination(DeviceSnapshot first, DeviceSnapshot second) => SameDevice(first, second)
        && first.Volumes.Length == 1 && second.Volumes.Length == 1 && SameVolume(first.Volumes[0], second.Volumes[0]);
    private static CopyDeviceOption? PreserveDevice(ImmutableArray<CopyDeviceOption> options, CopyDeviceOption? selected,
        out ImmutableArray<CopyDeviceOption> result, bool destination = false)
    {
        result = options;
        if (selected is null) return null;
        var current = options.FirstOrDefault(option => destination ? SameDestination(option.Device, selected.Device)
            : SameDevice(option.Device, selected.Device));
        if (current is not null) return current;
        // Preserve the missing choice as a selectable placeholder; a ComboBox must not silently clear/fallback.
        var unavailable = selected with { Label = DeviceOption(selected.Device).Label + " (unavailable)" };
        result = options.Add(unavailable);
        return unavailable;
    }
    private void RefreshVolumes()
    {
        var device = SelectedSource?.Device;
        var current = observation.Inventory.Devices.FirstOrDefault(item => device is not null && SameDevice(item, device));
        var selected = SelectedVolume;
        var options = (current?.Volumes ?? []).Where(volume => volume.Key.Device == current!.Key && volume.State == DeviceState.Ready
            && !string.IsNullOrWhiteSpace(volume.Root)).Select(volume => new CopyVolumeOption(volume,
                string.IsNullOrWhiteSpace(volume.Label) ? volume.Root! : $"{volume.Label} — {volume.Root}")).ToImmutableArray();
        var match = selected is null ? null : options.FirstOrDefault(option => SameVolume(option.Volume, selected.Volume));
        if (selected is not null && match is null)
        {
            match = selected with { Label = selected.Volume.Root + " (unavailable)" };
            options = options.Add(match);
        }
        Volumes = options;
        SelectedVolume = match;
    }
    private void Validate()
    {
        error = null;
        var currentSource = observation.Inventory.Devices.FirstOrDefault(device => SelectedSource is { } selected && SameDevice(device, selected.Device));
        var currentPsp = observation.Inventory.Devices.FirstOrDefault(device => SelectedPsp is { } selected && SameDestination(device, selected.Device));
        if (currentPsp is null)
            error = new(GameCopyErrorCode.DestinationUnavailable, "Select an available PSP with one usable mounted volume for this game.");
        else if (currentSource is null || SelectedVolume is null || !currentSource.Volumes.Any(volume => SameVolume(volume, SelectedVolume.Volume)))
            error = new(GameCopyErrorCode.SourceUnavailable, "Select an available source device and mounted volume for this game.");
        else if (observation.ProviderErrors.ContainsKey(currentSource.Key.ProviderId) || observation.ProviderErrors.ContainsKey(currentPsp.Key.ProviderId))
            error = new(GameCopyErrorCode.SourceUnavailable, "A required device service is unavailable. Retry explicitly after recovery.");
        else
        {
            // All choices are explicit: clearing a draft choice cannot fall back to session preferences.
            var capture = GameCopySubmissionRules.Capture([Submission(Guid.NewGuid())], observation.Inventory, new(), syntax);
            error = capture.Incidents.FirstOrDefault()?.Error;
        }
        IsReady = error is null;
        Status = error?.Message ?? "Ready to send";
    }
    private GameCopySubmission Submission(Guid id) => new(id, Game, SelectedSource?.Device.Key,
        SelectedVolume?.Volume.Key, SelectedPsp?.Device.Key);
    internal GameCopySubmission? Prepare(Guid id, out GameCopyError? incident)
    { Validate(); incident = error; return error is null ? Submission(id) : null; }
    internal void ReportIncident(GameCopyError incident) { error = incident; IsReady = false; Status = incident.Message; }
}
