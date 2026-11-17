namespace PSPMan.Core.Devices;

/// <summary>Awaits dependent work release before forwarding the same observed device removal.</summary>
public interface IDeviceRemovalInterceptor
{
    ValueTask RemoveDeviceAsync(DeviceKey device, DeviceInventorySnapshot observation,
        CancellationToken cancellationToken = default);
}
