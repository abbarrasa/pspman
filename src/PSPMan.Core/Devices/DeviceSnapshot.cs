using System.Collections.Immutable;

namespace PSPMan.Core.Devices;

/// <summary>A provider-scoped key, independent of labels and current mount roots.</summary>
public readonly record struct DeviceKey(string ProviderId, string DeviceId);

public readonly record struct VolumeKey(DeviceKey Device, string VolumeId);

public enum DeviceIdentityConfidence
{
    SessionOnly,
    Persistent
}

/// <summary>Persistent metadata is evidence for reconciliation, not a uniqueness guarantee.</summary>
public sealed record DeviceIdentity(string? PersistentId, DeviceIdentityConfidence Confidence);

/// <summary>Informative classification; source eligibility is expressed by capabilities.</summary>
public enum DeviceMedium
{
    Unknown,
    Optical,
    Usb,
    MemoryCard,
    Psp
}

[Flags]
public enum DeviceCapabilities
{
    None = 0,
    OpticalSource = 1,
    RemovableSource = 2,
    PspDestination = 4,
    Mount = 8,
    SafeRemoval = 16
}

public enum DeviceState
{
    Detecting,
    Unmounted,
    Mounting,
    Ready,
    Removing,
    Unavailable,
    Error
}

/// <summary>Preserves a native error without exposing native handles to Core.</summary>
public sealed record DeviceOperationError(string Code, string Message, VolumeKey? Volume = null);

/// <summary>A missing root means no currently usable mount path is observed.</summary>
public sealed record VolumeSnapshot(
    VolumeKey Key,
    string? Label,
    string? Root,
    DeviceIdentity Identity,
    DeviceState State,
    DeviceOperationError? Error = null);

public sealed record DeviceSnapshot(
    DeviceKey Key,
    string? DisplayName,
    DeviceMedium Medium,
    DeviceCapabilities Capabilities,
    DeviceIdentity Identity,
    string? MediaGeneration,
    DeviceState State,
    ImmutableArray<VolumeSnapshot> Volumes,
    DeviceOperationError? Error = null);

/// <summary>A complete provider inventory with a monotonically increasing observation revision.</summary>
public sealed record DeviceInventorySnapshot(long Revision, ImmutableArray<DeviceSnapshot> Devices);

public enum DeviceOperationStatus
{
    Succeeded,
    Failed,
    AuthorizationDenied,
    Cancelled,
    StaleObservation
}

/// <summary>Operation completion does not replace a fresh observation of device state.</summary>
public sealed record DeviceOperationResult(DeviceOperationStatus Status, DeviceOperationError? Error = null);

public sealed record DeviceMountRequest(VolumeKey Volume, long ExpectedRevision, string? MediaGeneration);

public sealed record DeviceRemovalRequest(DeviceKey Device, long ExpectedRevision, string? MediaGeneration);
