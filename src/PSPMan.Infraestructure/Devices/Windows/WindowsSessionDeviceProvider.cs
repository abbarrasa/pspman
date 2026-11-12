using System.Collections.Immutable;
using PSPMan.Core.Devices;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Composes existing Windows boundaries for the platform-independent session.</summary>
internal sealed class WindowsSessionDeviceProvider : SessionProviderBridge<WindowsDeviceInventory>
{
    private readonly IWindowsDeviceApi _api;
    private readonly SnapshotApi _cached = new();
    private readonly WindowsDeviceProvider _inventory;
    private readonly WindowsDeviceObserver _observer;
    private readonly WindowsVolumeMountService _mount;
    private readonly WindowsDeviceRemovalService _remove;
    private readonly WindowsOpticalMediaEjector _optical;
    private readonly WindowsDeviceAuthorization _authorization;
    private readonly Dictionary<string, string?> _generations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _attempts = new(StringComparer.OrdinalIgnoreCase);
    public override string ProviderId => "windows";

    public WindowsSessionDeviceProvider() : this(new WindowsDeviceApi(), new WindowsDeviceNotificationApi()) { }
    internal WindowsSessionDeviceProvider(IWindowsDeviceApi api, IWindowsDeviceNotificationApi notifications)
    {
        _api = api;
        _inventory = new(_cached);
        _observer = new(ReadForObserverAsync, notifications);
        _mount = new(api, new WindowsVolumeMountApi());
        _remove = new(api, new WindowsDeviceRemovalApi(api, new WindowsRemovalNativeApi()));
        _optical = new(api);
        _authorization = new();
    }

    protected override async ValueTask<WindowsDeviceInventory> ReadNativeAsync(CancellationToken token)
    {
        _cached.Snapshot = await _api.EnumerateStorageAsync(token).ConfigureAwait(false);
        var inventory = await _inventory.GetInventoryAsync(token).ConfigureAwait(false);
        // Only complete observations may reset suppression. Labels/roots never do so.
        if (_cached.Snapshot.Errors.IsEmpty)
        {
            foreach (var previous in _generations.ToArray())
            {
                var current = inventory.Devices.SingleOrDefault(device => device.InstanceId.Equals(previous.Key, StringComparison.OrdinalIgnoreCase));
                if (current is null || current.MediaGeneration != previous.Value)
                {
                    _authorization.ForgetDevice(previous.Key);
                    _attempts.RemoveWhere(key => key.StartsWith(previous.Key + "|", StringComparison.OrdinalIgnoreCase));
                }
            }
            _generations.Clear();
            foreach (var device in inventory.Devices) _generations[device.InstanceId] = device.MediaGeneration;
        }
        return inventory;
    }

    protected override IAsyncEnumerable<WindowsDeviceInventory> ObserveNativeAsync(CancellationToken token) => _observer.ObserveAsync(token);

    protected override ImmutableArray<DeviceSnapshot> Project(WindowsDeviceInventory inventory)
    {
        if (!inventory.Errors.IsEmpty && inventory.Devices.IsEmpty)
            throw new IOException("Windows device inventory is unavailable: " + inventory.Errors[0].Code);
        return inventory.Devices.Select(device =>
        {
            var key = new DeviceKey(ProviderId, device.InstanceId);
            var volumes = device.Volumes.OrderBy(volume => volume.VolumeGuidPath, StringComparer.OrdinalIgnoreCase).Select(volume =>
                new VolumeSnapshot(new(key, volume.VolumeGuidPath ?? volume.InterfacePath), volume.Label,
                    volume.IsMounted ? volume.MountPoints.FirstOrDefault() : null, device.MediaIdentity,
                    volume.Error is not null ? DeviceState.Error : volume.IsMounted ? DeviceState.Ready
                        : volume.State == WindowsVolumeState.Available ? DeviceState.Unmounted : DeviceState.Unavailable,
                    Error(volume.Error))).ToImmutableArray();
            var capabilities = device.IsPspDestination ? DeviceCapabilities.PspDestination
                : device.Kind == WindowsDeviceKind.Optical ? DeviceCapabilities.OpticalSource : DeviceCapabilities.RemovableSource;
            capabilities |= DeviceCapabilities.Mount;
            if (device.CanEject) capabilities |= DeviceCapabilities.SafeRemoval;
            var error = Error(device.Error);
            if (device.UsbMetadata?.VendorId == 0x054c && device.PspRecognition?.Status == PspUsbRecognitionStatus.Inconclusive)
                error ??= new("PspRecognitionFailed", "PSP USB recognition is inconclusive.");
            return new DeviceSnapshot(key, device.DisplayName, device.Kind switch
            { WindowsDeviceKind.Optical => DeviceMedium.Optical, WindowsDeviceKind.Psp => DeviceMedium.Psp,
                WindowsDeviceKind.Usb => DeviceMedium.Usb, _ => DeviceMedium.MemoryCard }, capabilities,
                device.Identity, device.MediaGeneration, error is not null ? DeviceState.Error
                    : volumes.Any(volume => volume.State == DeviceState.Ready) ? DeviceState.Ready
                    : volumes.IsEmpty ? DeviceState.Unavailable : DeviceState.Unmounted, volumes, error);
        }).ToImmutableArray();
    }

    protected override async ValueTask<DeviceOperationResult> MountNativeAsync(WindowsDeviceInventory inventory, DeviceMountRequest request, CancellationToken token)
    {
        var device = _cached.Snapshot.Devices.Single(item => item.InstanceId == request.Volume.Device.DeviceId);
        var volume = _cached.Snapshot.Volumes.SingleOrDefault(item => item.VolumeGuidPath == request.Volume.VolumeId);
        if (device.StorageNumber is not { } number || volume?.VolumeGuidPath is null)
            return Failed("TargetUnavailable");
        var result = await _mount.MountAsync(new(device.InstanceId, device.InterfacePath, volume.VolumeGuidPath, number, volume.FileSystemSerialNumber), token).ConfigureAwait(false);
        if (result.Succeeded) return new(DeviceOperationStatus.Succeeded);
        var failure = result.Error is null ? null : WindowsDeviceRemovalFailure.FromError(result.Error);
        if (failure?.RequiresAuthorization != true) return new(DeviceOperationStatus.Failed, Error(result.Error));
        return await AuthorizeAsync(new(1, Guid.NewGuid(), WindowsElevatedOperation.MountVolume,
            device.InstanceId, number, volume.VolumeGuidPath, volume.FileSystemSerialNumber), failure, token).ConfigureAwait(false);
    }

    protected override async ValueTask<DeviceOperationResult> RemoveNativeAsync(WindowsDeviceInventory inventory, DeviceRemovalRequest request, CancellationToken token)
    {
        var device = _cached.Snapshot.Devices.Single(item => item.InstanceId == request.Device.DeviceId);
        WindowsDeviceRemovalResult result;
        if (device.IsOptical) result = await _optical.EjectAsync(new(device.InstanceId, device.InterfacePath, device.StorageNumber), token).ConfigureAwait(false);
        else if (device.StorageNumber is { } number) result = await _remove.RemoveAsync(new(device.InstanceId, device.InterfacePath, number), token).ConfigureAwait(false);
        else return Failed("TargetUnavailable");
        if (result.Succeeded) return new(DeviceOperationStatus.Succeeded);
        if (result.Failure?.RequiresAuthorization != true) return new(DeviceOperationStatus.Failed, FailureError(result.Failure));
        return await AuthorizeAsync(new(1, Guid.NewGuid(), device.IsOptical ? WindowsElevatedOperation.EjectOpticalMedia : WindowsElevatedOperation.RemoveDevice,
            device.InstanceId, device.StorageNumber), result.Failure, token).ConfigureAwait(false);
    }

    private async ValueTask<DeviceOperationResult> AuthorizeAsync(WindowsOperationRequest request, WindowsDeviceRemovalFailure failure, CancellationToken token)
    {
        // Core never automatically retries an attempted mount. A subsequent same-target call is explicit.
        var key = $"{request.DeviceInstanceId}|{request.Operation}|{request.VolumeGuidPath}";
        var result = await _authorization.AuthorizeAsync(request, failure, explicitRetry: !_attempts.Add(key), cancellationToken: token).ConfigureAwait(false);
        return result.Succeeded ? new(DeviceOperationStatus.Succeeded) : new(
            result.Failure?.Error.Code is "AuthorizationDenied" or "AuthorizationCancelled" ? DeviceOperationStatus.AuthorizationDenied : DeviceOperationStatus.Failed,
            FailureError(result.Failure));
    }

    private static DeviceOperationResult Failed(string code) => new(DeviceOperationStatus.Failed, new(code, "The selected Windows target is unavailable."));
    private static DeviceOperationError? Error(WindowsDeviceError? error) => error is null ? null
        : new(error.Code, $"{error.Operation}: {error.Message ?? error.Code} (native {error.NativeCode})");
    internal static DeviceOperationError? FailureError(WindowsDeviceRemovalFailure? failure) => failure is null ? null
        : Error(failure.Error)! with { Message = $"{Error(failure.Error)!.Message}; CONFIGRET {failure.ConfigurationStatus}; veto {failure.VetoType}: {failure.VetoName}; volume {failure.VolumeGuidPath}" };
    protected override ValueTask DisposeNativeAsync() => _observer.DisposeAsync();
    private sealed class SnapshotApi : IWindowsDeviceApi
    {
        public WindowsStorageSnapshot Snapshot = new([], [], []);
        public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken token = default) => new(Snapshot);
    }
}
