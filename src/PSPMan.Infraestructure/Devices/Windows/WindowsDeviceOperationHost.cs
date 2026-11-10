namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>A pure selection check, NOT peer authentication or authority to execute without revalidation.</summary>
public sealed record WindowsOperationTargetValidation(bool IsValid, WindowsDeviceError? Error = null,
    WindowsVolumeMountTarget? MountTarget = null, WindowsDeviceRemovalTarget? RemovalTarget = null,
    WindowsOpticalMediaTarget? OpticalTarget = null);

/// <summary>Protocol target validation only. One-shot execution and transport arrive in later tasks.</summary>
public sealed partial class WindowsDeviceOperationHost
{
    public static WindowsOperationTargetValidation ValidateTarget(WindowsOperationRequest request, WindowsStorageSnapshot inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        try { WindowsDeviceAuthorization.ValidateRequest(request); }
        catch (InvalidDataException) { return Rejected(new("InvalidOperationRequest", "ValidateOperationRequest")); }
        var inventoryError = inventory.Errors.FirstOrDefault(error =>
            request.Operation != WindowsElevatedOperation.EjectOpticalMedia || error.Code != "NoMedia");
        if (inventoryError is not null) return Rejected(inventoryError);
        var devices = inventory.Devices.Where(device => string.Equals(device.InstanceId,
            request.DeviceInstanceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (devices.Length != 1) return Rejected(new("TargetUnavailable", "ValidateOperationTarget"));
        var device = devices[0];
        // Interface paths and capabilities come solely from this trusted snapshot, never the wire payload.
        switch (request.Operation)
        {
            case WindowsElevatedOperation.MountVolume:
                var mount = new WindowsVolumeMountTarget(device.InstanceId, device.InterfacePath,
                    request.VolumeGuidPath!, request.StorageNumber!.Value, request.FileSystemSerialNumber);
                var mountError = WindowsVolumeMountService.ValidateTarget(inventory, mount, out _);
                return mountError is null ? new(true, MountTarget: mount) : Rejected(mountError);
            case WindowsElevatedOperation.RemoveDevice:
                var removal = new WindowsDeviceRemovalTarget(device.InstanceId, device.InterfacePath, request.StorageNumber!.Value);
                var removalError = WindowsDeviceRemovalApi.ValidateTarget(inventory, removal);
                return removalError is null ? new(true, RemovalTarget: removal) : Rejected(removalError.Error);
            case WindowsElevatedOperation.EjectOpticalMedia:
                var optical = new WindowsOpticalMediaTarget(device.InstanceId, device.InterfacePath, request.StorageNumber);
                var opticalError = WindowsOpticalMediaEjector.ValidateTarget(inventory, optical, out _);
                return opticalError is null ? new(true, OpticalTarget: optical) : Rejected(opticalError.Error);
            default:
                return Rejected(new("InvalidOperationRequest", "ValidateOperationRequest"));
        }
    }

    private static WindowsOperationTargetValidation Rejected(WindowsDeviceError error) => new(false, error);
}
