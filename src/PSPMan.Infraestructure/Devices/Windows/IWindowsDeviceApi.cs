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

/// <summary>Only the volume's physical association; roots, labels and GUID identity follow in T25.</summary>
public sealed record WindowsVolumeReference(string InterfacePath, WindowsStorageNumber? StorageNumber);

public sealed record WindowsStorageSnapshot(
    ImmutableArray<WindowsStorageDevice> Devices,
    ImmutableArray<WindowsVolumeReference> Volumes,
    ImmutableArray<WindowsDeviceError> Errors);
