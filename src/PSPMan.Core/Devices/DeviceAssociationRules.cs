using System.Collections.Immutable;
using PSPMan.Core.Games;

namespace PSPMan.Core.Devices;

public enum SourceGroup
{
    Optical,
    Removable
}

public enum SourceResolutionStatus
{
    Unavailable,
    Selected,
    ChooseDevice,
    ChooseVolume
}

/// <summary>Device resolution only; volume choice and file availability are evaluated separately.</summary>
public sealed record SourceResolution(
    SourceResolutionStatus Status,
    ImmutableArray<DeviceSnapshot> Candidates,
    DeviceKey? SelectedDevice);

/// <summary>Volume resolution uses observed roots; it does not prove game-file existence.</summary>
public sealed record SourceVolumeResolution(
    SourceResolutionStatus Status,
    DeviceKey? SelectedDevice,
    ImmutableArray<VolumeSnapshot> Candidates,
    VolumeSnapshot? SelectedVolume);

public enum DestinationResolutionStatus
{
    Unavailable,
    Selected,
    ChooseDevice
}

/// <summary>Mounted PSP candidates, without asserting that a future copy will succeed.</summary>
public sealed record DestinationResolution(
    DestinationResolutionStatus Status,
    ImmutableArray<DeviceSnapshot> Candidates,
    DeviceKey? SelectedDevice);

public static class DeviceAssociationRules
{
    public static SourceGroup GetSourceGroup(GameMedium medium) => medium switch
    {
        GameMedium.Dvd => SourceGroup.Optical,
        GameMedium.Usb or GameMedium.MemoryCard => SourceGroup.Removable,
        _ => throw new ArgumentOutOfRangeException(nameof(medium), medium, "Unsupported game medium.")
    };

    /// <summary>
    /// Resolves physical source candidates when no session preference applies. No file probes,
    /// DVD-number matching or volume selection take place here.
    /// </summary>
    public static SourceResolution ResolveSource(GameMedium medium, DeviceInventorySnapshot inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var requiredCapability = GetSourceGroup(medium) == SourceGroup.Optical
            ? DeviceCapabilities.OpticalSource
            : DeviceCapabilities.RemovableSource;
        var candidates = ImmutableArray.CreateBuilder<DeviceSnapshot>();
        var seen = new HashSet<DeviceKey>();

        if (!inventory.Devices.IsDefaultOrEmpty)
        {
            foreach (var device in inventory.Devices)
            {
                if (IsMountedSource(device, requiredCapability) && seen.Add(device.Key))
                {
                    candidates.Add(device);
                }
            }
        }

        var status = candidates.Count switch
        {
            0 => SourceResolutionStatus.Unavailable,
            1 => SourceResolutionStatus.Selected,
            _ => SourceResolutionStatus.ChooseDevice
        };
        return new SourceResolution(status, candidates.ToImmutable(),
            candidates.Count == 1 ? candidates[0].Key : null);
    }

    /// <summary>Replaces only the explicit source choice in the specified session group.</summary>
    public static DeviceSessionState ChooseSource(DeviceSessionState state, SourceGroup group, DeviceKey device)
    {
        ArgumentNullException.ThrowIfNull(state);
        state = state with
        {
            SourceEvidence = state.SourceEvidence.Remove(group),
            UnavailableSources = state.UnavailableSources.Remove(group),
            UnavailableSourceVolumes = state.UnavailableSourceVolumes.Remove(group)
        };
        return group switch
        {
            SourceGroup.Optical => state with
            {
                OpticalSource = device,
                OpticalVolume = state.OpticalSource == device ? state.OpticalVolume : null
            },
            SourceGroup.Removable => state with
            {
                RemovableSource = device,
                RemovableVolume = state.RemovableSource == device ? state.RemovableVolume : null
            },
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unsupported source group.")
        };
    }

    /// <summary>
    /// Applies explicit session preferences without replacing an unavailable choice with another
    /// candidate. This does not perform volume selection or reconnection identity recovery.
    /// </summary>
    public static SourceResolution ResolveSource(GameMedium medium, DeviceInventorySnapshot inventory,
        DeviceSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var resolution = ResolveSource(medium, inventory);
        var preferred = GetSourceGroup(medium) == SourceGroup.Optical
            ? state.OpticalSource
            : state.RemovableSource;
        if (preferred is null)
        {
            return resolution;
        }

        var available = !state.UnavailableSources.Contains(GetSourceGroup(medium))
            && resolution.Candidates.Any(candidate => candidate.Key == preferred.Value);
        return resolution with
        {
            Status = available ? SourceResolutionStatus.Selected : SourceResolutionStatus.Unavailable,
            SelectedDevice = available ? preferred : null
        };
    }

    /// <summary>Retains an explicit device/volume choice for the corresponding source group.</summary>
    public static DeviceSessionState ChooseVolume(DeviceSessionState state, SourceGroup group, VolumeKey volume)
    {
        var chosen = ChooseSource(state, group, volume.Device);
        return group == SourceGroup.Optical
            ? chosen with { OpticalVolume = volume }
            : chosen with { RemovableVolume = volume };
    }

    /// <summary>Resolves a mounted source volume without file probes or automatic fallback.</summary>
    public static SourceVolumeResolution ResolveSourceVolume(GameMedium medium,
        DeviceInventorySnapshot inventory, DeviceSessionState state)
    {
        var source = ResolveSource(medium, inventory, state);
        if (source.Status != SourceResolutionStatus.Selected)
        {
            return new SourceVolumeResolution(source.Status, null, [], null);
        }

        var device = source.Candidates.First(candidate => candidate.Key == source.SelectedDevice);
        var volumes = device.Volumes
            .Where(volume => IsAvailableVolume(device.Key, volume))
            .DistinctBy(volume => volume.Key)
            .ToImmutableArray();
        var preferred = GetSourceGroup(medium) == SourceGroup.Optical
            ? state.OpticalVolume
            : state.RemovableVolume;

        if (preferred is not null)
        {
            var selected = state.UnavailableSourceVolumes.Contains(GetSourceGroup(medium))
                ? null : volumes.FirstOrDefault(volume => volume.Key == preferred.Value);
            return new SourceVolumeResolution(
                selected is null ? SourceResolutionStatus.Unavailable : SourceResolutionStatus.Selected,
                device.Key, volumes, selected);
        }

        return volumes.Length switch
        {
            0 => new SourceVolumeResolution(SourceResolutionStatus.Unavailable, device.Key, volumes, null),
            1 => new SourceVolumeResolution(SourceResolutionStatus.Selected, device.Key, volumes, volumes[0]),
            _ => new SourceVolumeResolution(SourceResolutionStatus.ChooseVolume, device.Key, volumes, null)
        };
    }

    /// <summary>Replaces only the specified game's PSP choice in this session.</summary>
    public static DeviceSessionState ChooseDestination(DeviceSessionState state, Guid gameId, DeviceKey psp)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with
        {
            PspDestinations = state.PspDestinations.SetItem(gameId, psp),
            DestinationEvidence = state.DestinationEvidence.Remove(gameId),
            UnavailableDestinations = state.UnavailableDestinations.Remove(gameId)
        };
    }

    /// <summary>Resolves mounted PSPs, preserving explicit unavailable choices without fallback.</summary>
    public static DestinationResolution ResolveDestination(Guid gameId, DeviceInventorySnapshot inventory,
        DeviceSessionState state)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(state);
        var candidates = inventory.Devices.IsDefaultOrEmpty
            ? ImmutableArray<DeviceSnapshot>.Empty
            : inventory.Devices.Where(device =>
                (device.Capabilities & DeviceCapabilities.PspDestination) != 0
                && device.State is DeviceState.Ready or DeviceState.Mounting or DeviceState.Error
                && !device.Volumes.IsDefaultOrEmpty
                && device.Volumes.Any(volume => IsAvailableVolume(device.Key, volume)))
                .DistinctBy(device => device.Key)
                .ToImmutableArray();

        if (state.PspDestinations.TryGetValue(gameId, out var preferred))
        {
            var available = !state.UnavailableDestinations.Contains(gameId)
                && candidates.Any(device => device.Key == preferred);
            return new DestinationResolution(
                available ? DestinationResolutionStatus.Selected : DestinationResolutionStatus.Unavailable,
                candidates, available ? preferred : null);
        }

        return candidates.Length switch
        {
            0 => new DestinationResolution(DestinationResolutionStatus.Unavailable, candidates, null),
            1 => new DestinationResolution(DestinationResolutionStatus.Selected, candidates, candidates[0].Key),
            _ => new DestinationResolution(DestinationResolutionStatus.ChooseDevice, candidates, null)
        };
    }

    /// <summary>
    /// Reconciles session choices against a complete observation. Invoke after explicit choices
    /// while that observation is current, and for every subsequent inventory, including removal.
    /// Identity evidence never stores or uses mount roots for recognition.
    /// </summary>
    public static DeviceSessionState ReconcileAssociations(DeviceSessionState state, DeviceInventorySnapshot inventory)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inventory);
        var devices = inventory.Devices.IsDefault ? ImmutableArray<DeviceSnapshot>.Empty : inventory.Devices;
        foreach (var group in new[] { SourceGroup.Optical, SourceGroup.Removable })
        {
            var key = group == SourceGroup.Optical ? state.OpticalSource : state.RemovableSource;
            if (key is null)
            {
                continue;
            }

            state.SourceEvidence.TryGetValue(group, out var previous);
            var (device, evidence) = ReconcileDevice(key.Value, previous, devices, requireMedium: true);
            var volumeKey = group == SourceGroup.Optical ? state.OpticalVolume : state.RemovableVolume;
            var volume = device is null || volumeKey is null ? null
                : ReconcileVolume(volumeKey.Value, previous, device,
                    previous?.RequiresRecovery == true || device.Key != key.Value
                    || state.UnavailableSourceVolumes.Contains(group));
            if (volume is not null && volumeKey is not null)
            {
                evidence = evidence with { Volumes = evidence.Volumes.Remove(volumeKey.Value)
                    .SetItem(volume.Key, volume.Identity) };
            }

            state = state with
            {
                SourceEvidence = state.SourceEvidence.SetItem(group, evidence),
                UnavailableSources = device is null ? state.UnavailableSources.Add(group) : state.UnavailableSources.Remove(group),
                UnavailableSourceVolumes = volumeKey is not null && volume is null
                    ? state.UnavailableSourceVolumes.Add(group) : state.UnavailableSourceVolumes.Remove(group)
            };
            state = group == SourceGroup.Optical
                ? state with { OpticalSource = device?.Key ?? key, OpticalVolume = volume?.Key ?? volumeKey }
                : state with { RemovableSource = device?.Key ?? key, RemovableVolume = volume?.Key ?? volumeKey };
        }

        foreach (var (gameId, key) in state.PspDestinations)
        {
            state.DestinationEvidence.TryGetValue(gameId, out var previous);
            var (device, evidence) = ReconcileDevice(key, previous, devices, requireMedium: false);
            state = state with
            {
                PspDestinations = state.PspDestinations.SetItem(gameId, device?.Key ?? key),
                DestinationEvidence = state.DestinationEvidence.SetItem(gameId, evidence),
                UnavailableDestinations = device is null
                    ? state.UnavailableDestinations.Add(gameId) : state.UnavailableDestinations.Remove(gameId)
            };
        }

        return state;
    }

    private static (DeviceSnapshot? Device, DeviceAssociationEvidence Evidence) ReconcileDevice(
        DeviceKey key, DeviceAssociationEvidence? previous, ImmutableArray<DeviceSnapshot> devices, bool requireMedium)
    {
        var present = devices.Where(device => device.State != DeviceState.Unavailable).ToArray();
        var sameKey = present.Where(device => device.Key == key).ToArray();
        if (previous is null)
        {
            if (sameKey.Length == 1)
            {
                var current = sameKey[0];
                var volumes = current.Volumes.IsDefault ? ImmutableDictionary<VolumeKey, DeviceIdentity>.Empty
                    : current.Volumes.Where(volume => volume.Key.Device == current.Key)
                        .GroupBy(volume => volume.Key).Where(group => group.Count() == 1)
                        .ToImmutableDictionary(group => group.Key, group => group.Single().Identity);
                return (current, new DeviceAssociationEvidence(current.Identity, current.MediaGeneration, volumes, false));
            }

            return (null, new DeviceAssociationEvidence(null, null, ImmutableDictionary<VolumeKey, DeviceIdentity>.Empty, true));
        }

        if (!previous.RequiresRecovery && sameKey.Length == 1
            && sameKey[0].Identity == previous.Identity
            && MediumMatches(previous, sameKey[0], requireMedium, recovering: false))
        {
            return (sameKey[0], previous);
        }

        if (IsPersistent(previous.Identity))
        {
            var matches = present.Where(device => device.Key.ProviderId == key.ProviderId
                && IsPersistent(device.Identity)
                && device.Identity.PersistentId == previous.Identity!.PersistentId).ToArray();
            if (matches.Length == 1 && MediumMatches(previous, matches[0], requireMedium, recovering: true))
            {
                return (matches[0], previous with { RequiresRecovery = false });
            }
        }

        return (null, previous with { RequiresRecovery = true });
    }

    private static bool MediumMatches(DeviceAssociationEvidence previous, DeviceSnapshot current,
        bool requireMedium, bool recovering)
    {
        if (previous.MediaGeneration != current.MediaGeneration)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(previous.MediaGeneration) || !requireMedium)
        {
            return true;
        }

        var identities = previous.Volumes.Values.Where(IsPersistent).ToArray();
        if (identities.Length == 0)
        {
            return !recovering;
        }

        return !current.Volumes.IsDefault && identities.Any(identity => current.Volumes.Count(volume =>
            volume.Key.Device == current.Key && IsPersistent(volume.Identity)
            && volume.Identity.PersistentId == identity.PersistentId) == 1);
    }

    private static VolumeSnapshot? ReconcileVolume(VolumeKey key, DeviceAssociationEvidence? previous,
        DeviceSnapshot device, bool recovering)
    {
        if (device.Volumes.IsDefault)
        {
            return null;
        }

        var volumes = device.Volumes.Where(volume => volume.Key.Device == device.Key).ToArray();
        var sameKey = volumes.Where(volume => volume.Key == key).ToArray();
        if (previous is null)
        {
            return sameKey.Length == 1 ? sameKey[0] : null;
        }

        if (!previous.Volumes.TryGetValue(key, out var identity))
        {
            return null;
        }

        if (!recovering && sameKey.Length == 1 && sameKey[0].Identity == identity)
        {
            return sameKey[0];
        }

        if (!IsPersistent(identity))
        {
            return null;
        }

        var matches = volumes.Where(volume => IsPersistent(volume.Identity)
            && volume.Identity.PersistentId == identity.PersistentId).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool IsPersistent(DeviceIdentity? identity) =>
        identity?.Confidence == DeviceIdentityConfidence.Persistent && !string.IsNullOrWhiteSpace(identity.PersistentId);

    private static bool IsMountedSource(DeviceSnapshot device, DeviceCapabilities requiredCapability)
    {
        if (device.Medium == DeviceMedium.Psp
            || (device.Capabilities & DeviceCapabilities.PspDestination) != 0
            || (device.Capabilities & requiredCapability) == 0
            || device.State is not (DeviceState.Ready or DeviceState.Mounting or DeviceState.Error)
            || device.Volumes.IsDefaultOrEmpty)
        {
            return false;
        }

        // An error on another volume must not hide a volume that is still mounted and available.
        return device.Volumes.Any(volume => IsAvailableVolume(device.Key, volume));
    }

    private static bool IsAvailableVolume(DeviceKey device, VolumeSnapshot volume) =>
        volume.Key.Device == device
        && volume.State == DeviceState.Ready
        && !string.IsNullOrWhiteSpace(volume.Root);
}
