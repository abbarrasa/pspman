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

        var available = resolution.Candidates.Any(candidate => candidate.Key == preferred.Value);
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
            var selected = volumes.FirstOrDefault(volume => volume.Key == preferred.Value);
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
