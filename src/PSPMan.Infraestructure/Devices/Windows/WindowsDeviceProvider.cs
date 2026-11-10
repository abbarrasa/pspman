using System.Collections.Immutable;
using PSPMan.Core.Devices;

namespace PSPMan.Infraestructure.Devices.Windows;

public enum WindowsDeviceKind { Usb, Optical, MemoryCard, Psp }

public sealed record WindowsPhysicalDevice(
    string InstanceId,
    string? ParentInstanceId,
    string InterfacePath,
    string DisplayName,
    WindowsDeviceKind Kind,
    bool IsSourceEligible,
    bool CanEject,
    ImmutableArray<WindowsVolumeReference> Volumes,
    WindowsDeviceError? Error)
{
    public WindowsUsbMetadata? UsbMetadata { get; init; }
    public PspUsbRecognitionResult? PspRecognition { get; init; }
    public bool IsPspDestination => PspRecognition?.Status == PspUsbRecognitionStatus.Recognized;
    public DeviceIdentity Identity { get; init; } = new(null, DeviceIdentityConfidence.SessionOnly);
    public DeviceIdentity MediaIdentity { get; init; } = new(null, DeviceIdentityConfidence.SessionOnly);
    public string? MediaGeneration { get; init; }
}

public sealed record WindowsDeviceInventory(
    ImmutableArray<WindowsPhysicalDevice> Devices,
    ImmutableArray<WindowsDeviceError> Errors);

/// <summary>Normalizes Windows physical storage without depending on Win32 or probing game files.</summary>
public sealed class WindowsDeviceProvider(IWindowsDeviceApi api)
{
    private readonly IWindowsDeviceApi _api = api ?? throw new ArgumentNullException(nameof(api));
    private readonly WindowsUsbIdentityMapper _usbIdentity = new();

    public async ValueTask<WindowsDeviceInventory> GetInventoryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await _api.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var errors = snapshot.Errors.ToBuilder();
        // Never use a shared hub/controller parent as the grouping key for distinct disks.
        var devices = snapshot.Devices.DistinctBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray();
        var owners = devices.Where(device => device.StorageNumber is not null)
            .GroupBy(device => device.StorageNumber!.Value).ToDictionary(group => group.Key, group => group.Count());
        var ambiguous = owners.Where(pair => pair.Value > 1).Select(pair => pair.Key).ToHashSet();
        if (snapshot.Volumes.Any(volume => volume.StorageNumber is { } number && ambiguous.Contains(number)))
            errors.Add(new WindowsDeviceError("AmbiguousStorageParent", "AssociateVolumes"));

        var result = devices.Where(IsSupported).Select(device => new WindowsPhysicalDevice(
            device.InstanceId, device.ParentInstanceId, device.InterfacePath,
            string.IsNullOrWhiteSpace(device.DisplayName) ? "Removable device" : device.DisplayName,
            device.IsOptical ? WindowsDeviceKind.Optical
                : device.Bus == WindowsStorageBus.Usb ? WindowsDeviceKind.Usb : WindowsDeviceKind.MemoryCard,
            IsSourceEligible: true, device.CanEject,
            snapshot.Volumes.Where(volume => device.StorageNumber is { } number
                && volume.StorageNumber == number && !ambiguous.Contains(number))
                .DistinctBy(volume => volume.VolumeGuidPath ?? volume.InterfacePath, StringComparer.OrdinalIgnoreCase)
                .Select(volume => volume.State == WindowsVolumeState.Available && volume.Error is null
                    ? volume : volume with { MountPoints = [] }).ToImmutableArray(),
            device.Error)).ToImmutableArray();
        return new WindowsDeviceInventory(_usbIdentity.Map(result, devices), errors.ToImmutable());
    }

    private static bool IsSupported(WindowsStorageDevice device) => device.IsOptical || device.IsRemovable
        || device.Bus is WindowsStorageBus.Usb or WindowsStorageBus.Sd or WindowsStorageBus.Mmc;
}
