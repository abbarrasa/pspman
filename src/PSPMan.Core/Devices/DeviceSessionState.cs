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

    internal ImmutableDictionary<SourceGroup, DeviceAssociationEvidence> SourceEvidence { get; init; } =
        ImmutableDictionary<SourceGroup, DeviceAssociationEvidence>.Empty;
    internal ImmutableDictionary<Guid, DeviceAssociationEvidence> DestinationEvidence { get; init; } =
        ImmutableDictionary<Guid, DeviceAssociationEvidence>.Empty;
    internal ImmutableHashSet<SourceGroup> UnavailableSources { get; init; } = [];
    internal ImmutableHashSet<SourceGroup> UnavailableSourceVolumes { get; init; } = [];
    internal ImmutableHashSet<Guid> UnavailableDestinations { get; init; } = [];
}

// Evidence intentionally excludes display labels, mount paths and native handles.
internal sealed record DeviceAssociationEvidence(
    DeviceIdentity? Identity,
    string? MediaGeneration,
    ImmutableDictionary<VolumeKey, DeviceIdentity> Volumes,
    bool RequiresRecovery);
