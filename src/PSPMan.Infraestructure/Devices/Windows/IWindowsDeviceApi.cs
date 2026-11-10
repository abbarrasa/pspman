using System.Collections.Immutable;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Read-only native storage enumeration; no mount, eject or file-content operations.</summary>
public interface IWindowsDeviceApi
{
    ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default);
}

public enum WindowsStorageBus { Unknown, Usb, Sd, Mmc, Sata, Other }

/// <summary>A current storage association, not persistent identity across reconnections.</summary>
public readonly record struct WindowsStorageNumber(uint DeviceType, uint DeviceNumber);

public sealed record WindowsDeviceError(string Code, string Operation, int? NativeCode = null, string? Message = null);

public sealed record WindowsStorageDevice(
    string InstanceId,
    string? ParentInstanceId,
    string InterfacePath,
    string DisplayName,
    WindowsStorageNumber? StorageNumber,
    WindowsStorageBus Bus,
    bool IsOptical,
    bool IsRemovable,
    bool CanEject,
    WindowsDeviceError? Error = null);

public enum WindowsVolumeState { Unknown, Available, NoMedia, Unavailable }

/// <summary>Observed volume identity and current roots; neither labels nor drive letters establish identity.</summary>
public sealed record WindowsVolumeReference(string InterfacePath, WindowsStorageNumber? StorageNumber)
{
    public string? VolumeGuidPath { get; init; }
    public string? Label { get; init; }
    public ImmutableArray<string> MountPoints { get; init; } = [];
    public WindowsVolumeState State { get; init; }
    public WindowsDeviceError? Error { get; init; }
    public bool IsMounted => State == WindowsVolumeState.Available && Error is null && !MountPoints.IsDefaultOrEmpty;
}

public sealed record WindowsStorageSnapshot(
    ImmutableArray<WindowsStorageDevice> Devices,
    ImmutableArray<WindowsVolumeReference> Volumes,
    ImmutableArray<WindowsDeviceError> Errors);
