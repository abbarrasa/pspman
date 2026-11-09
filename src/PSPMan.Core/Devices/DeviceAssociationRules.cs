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
    ChooseDevice
}

/// <summary>Device resolution only; volume choice and file availability are evaluated separately.</summary>
public sealed record SourceResolution(
    SourceResolutionStatus Status,
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
        return group switch
        {
            SourceGroup.Optical => state with { OpticalSource = device },
            SourceGroup.Removable => state with { RemovableSource = device },
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
        return device.Volumes.Any(volume => volume.Key.Device == device.Key
            && volume.State == DeviceState.Ready
            && !string.IsNullOrWhiteSpace(volume.Root));
    }
}
