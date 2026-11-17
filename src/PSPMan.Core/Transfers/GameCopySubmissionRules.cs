using System.Collections.Immutable;
using PSPMan.Core.Devices;

namespace PSPMan.Core.Transfers;

/// <summary>Pure capture against one observation. Resolution is not file/device availability proof.</summary>
public static class GameCopySubmissionRules
{
    public static GameCopyAdmission Capture(IEnumerable<GameCopySubmission> submissions,
        DeviceInventorySnapshot inventory, DeviceSessionState preferences, MountedPathSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!Enum.IsDefined(syntax)) throw new ArgumentOutOfRangeException(nameof(syntax));
        var accepted = ImmutableArray.CreateBuilder<GameCopyRequest>();
        var incidents = ImmutableArray.CreateBuilder<GameCopySubmissionIncident>();
        var ids = new HashSet<Guid>();
        var index = 0;
        foreach (var submission in submissions)
        {
            ArgumentNullException.ThrowIfNull(submission);
            ArgumentNullException.ThrowIfNull(submission.Game);
            var error = CaptureOne(submission, inventory, preferences, syntax, ids, out var request);
            if (error is not null) incidents.Add(new(index, submission.Game.Id, error));
            else accepted.Add(request!);
            index++;
        }
        return new(accepted.ToImmutable(), incidents.ToImmutable());
    }

    private static GameCopyError? CaptureOne(GameCopySubmission submission, DeviceInventorySnapshot inventory,
        DeviceSessionState preferences, MountedPathSyntax syntax, HashSet<Guid> ids, out GameCopyRequest? request)
    {
        request = null;
        var game = submission.Game;
        if (submission.OperationId == Guid.Empty || !ids.Add(submission.OperationId) || !Enum.IsDefined(game.Medium))
            return new(GameCopyErrorCode.InvalidSubmission, "The copy submission identity or game medium is invalid.");
        var local = preferences;
        var group = DeviceAssociationRules.GetSourceGroup(game.Medium);
        if (submission.SourceDevice is { } sourceChoice)
            local = DeviceAssociationRules.ChooseSource(local, group, sourceChoice);
        if (submission.SourceVolume is { } volumeChoice)
        {
            if (submission.SourceDevice is { } deviceChoice && volumeChoice.Device != deviceChoice)
                return SourceError();
            local = DeviceAssociationRules.ChooseVolume(local, group, volumeChoice);
        }
        if (submission.DestinationDevice is { } pspChoice)
            local = DeviceAssociationRules.ChooseDestination(local, game.Id, pspChoice);
        var source = DeviceAssociationRules.ResolveSourceVolume(game.Medium, inventory, local);
        if (source.Status != SourceResolutionStatus.Selected || source.SelectedVolume is null) return SourceError();
        var destination = DeviceAssociationRules.ResolveDestination(game.Id, inventory, local);
        if (destination.Status != DestinationResolutionStatus.Selected)
            return DestinationError();
        // Duplicate keys cannot establish a unique captured device, even if a candidate resolver deduplicates them.
        var sources = inventory.Devices.Where(d => d.Key == source.SelectedDevice).ToArray();
        var psps = inventory.Devices.Where(d => d.Key == destination.SelectedDevice).ToArray();
        if (sources.Length != 1 || sources[0].State != DeviceState.Ready) return SourceError();
        if (psps.Length != 1 || psps[0].State != DeviceState.Ready) return DestinationError();
        var volumes = psps[0].Volumes.Where(v => v.Key.Device == psps[0].Key && v.State == DeviceState.Ready
            && !string.IsNullOrWhiteSpace(v.Root)).ToArray();
        if (volumes.Length != 1) return DestinationError();
        if (sources[0].Volumes.Count(v => v.Key == source.SelectedVolume.Key) != 1) return SourceError();
        if (MountedGamePathRules.Resolve(source.SelectedVolume.Root, game.Location, syntax).Path is null)
            return new(GameCopyErrorCode.SourceUnavailable, "The selected source path is invalid.");
        if (MountedGamePathRules.Resolve(volumes[0].Root, "/ISO", syntax).Path is null) return DestinationError();
        request = new(submission.OperationId, game, Evidence(sources[0], source.SelectedVolume), Evidence(psps[0], volumes[0]));
        return null;

        CopyVolumeEvidence Evidence(DeviceSnapshot device, VolumeSnapshot volume) => new(device.Key, device.Identity,
            device.MediaGeneration, volume.Key, volume.Identity, volume.Root!, syntax);
    }

    public static bool CanApply(GameCopyOperationSnapshot current, GameCopyOperationSnapshot next)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        return current.Revision >= 0 && next.Revision > current.Revision
            && current.Request.OperationId != Guid.Empty && next.Request == current.Request
            && current.Snapshot.OperationId == current.Request.OperationId
            && next.Snapshot.OperationId == current.Request.OperationId
            && !GameCopyRules.IsTerminal(current.Snapshot.State);
    }

    private static GameCopyError SourceError() => new(GameCopyErrorCode.SourceUnavailable,
        "Select an available source device and mounted volume for this game.");
    private static GameCopyError DestinationError() => new(GameCopyErrorCode.DestinationUnavailable,
        "Select an available PSP with one usable mounted volume for this game.");
}
