using System.Collections.Immutable;

namespace PSPMan.Core.Devices;

/// <summary>Explicit source and destination choices for one execution; never persisted or shared between sessions.</summary>
public sealed record DeviceSessionState(
    DeviceKey? OpticalSource = null,
    DeviceKey? RemovableSource = null,
    VolumeKey? OpticalVolume = null,
    VolumeKey? RemovableVolume = null)
{
    public ImmutableDictionary<Guid, DeviceKey> PspDestinations { get; init; } =
        ImmutableDictionary<Guid, DeviceKey>.Empty;
}
