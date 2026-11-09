namespace PSPMan.Core.Devices;

/// <summary>
/// Platform-independent device access. The caller owns the provider and disposes it asynchronously.
/// Native authorization, handles and storage discovery belong to the implementation.
/// </summary>
public interface IDeviceProvider : IAsyncDisposable
{
    string ProviderId { get; }

    ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Observes complete inventory revisions, not individual volume deltas. Implementations must
    /// reconcile initial inventory with concurrent notifications without losing changes.
    /// Disposing the enumerator releases its subscription; cancellation stops observation.
    /// </summary>
    IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests mounting of one volume, allowing native authorization where needed.
    /// The provider revalidates the observed device/media before acting and reports stale requests.
    /// Cancellation does not imply that an already submitted native operation was rolled back.
    /// </summary>
    ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests safe removal of the physical device and its volumes, never forced removal.
    /// Failures preserve native error information; callers refresh observations after completion.
    /// Device/media identity must be revalidated before acting, as for mounting.
    /// </summary>
    ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request,
        CancellationToken cancellationToken = default);
}
