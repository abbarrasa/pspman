using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PSPMan.Core.Devices;

namespace PSPMan.Infraestructure.Devices;

/// <summary>Serializes platform observations and guards requests against the last published revision.</summary>
internal abstract class SessionProviderBridge<TInventory> : IDeviceProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _disposeGate = new();
    private Task? _disposal;
    private DeviceInventorySnapshot _snapshot = new(0, []);
    private string? _fingerprint;
    private readonly Dictionary<DeviceKey, (string Fingerprint, long ChangedAt)> _deviceRevisions = [];
    public abstract string ProviderId { get; }
    protected abstract ValueTask<TInventory> ReadNativeAsync(CancellationToken token);
    protected abstract IAsyncEnumerable<TInventory> ObserveNativeAsync(CancellationToken token);
    protected abstract ImmutableArray<DeviceSnapshot> Project(TInventory inventory);
    protected abstract ValueTask<DeviceOperationResult> MountNativeAsync(TInventory inventory, DeviceMountRequest request, CancellationToken token);
    protected abstract ValueTask<DeviceOperationResult> RemoveNativeAsync(TInventory inventory, DeviceRemovalRequest request, CancellationToken token);
    protected abstract ValueTask DisposeNativeAsync();

    protected async ValueTask<TInventory> ReadForObserverAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { return await ReadNativeAsync(token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try { await RefreshAsync(linked.Token).ConfigureAwait(false); return _snapshot; }
        finally { _gate.Release(); }
    }

    private async ValueTask<TInventory> RefreshAsync(CancellationToken token)
    {
        var inventory = await ReadNativeAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var devices = Project(inventory).OrderBy(device => device.Key.DeviceId, StringComparer.Ordinal).ToImmutableArray();
        var fingerprint = JsonSerializer.Serialize(devices);
        if (_fingerprint != fingerprint)
        {
            _snapshot = new(checked(_snapshot.Revision + 1), devices);
            _fingerprint = fingerprint;
            var present = devices.Select(device => device.Key).ToHashSet();
            foreach (var missing in _deviceRevisions.Keys.Where(key => !present.Contains(key)).ToArray()) _deviceRevisions.Remove(missing);
            foreach (var device in devices)
            {
                var current = JsonSerializer.Serialize(device);
                if (!_deviceRevisions.TryGetValue(device.Key, out var previous) || previous.Fingerprint != current)
                    _deviceRevisions[device.Key] = (current, _snapshot.Revision);
            }
        }
        return inventory;
    }

    public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        long previous = -1;
        // Native observers subscribe before their first inventory. Re-read under our gate rather
        // than assigning a newer revision to a buffered, potentially older native observation.
        await foreach (var _ in ObserveNativeAsync(linked.Token).ConfigureAwait(false))
        {
            var snapshot = await GetSnapshotAsync(linked.Token).ConfigureAwait(false);
            if (snapshot.Revision == previous) continue;
            previous = snapshot.Revision;
            yield return snapshot;
        }
    }

    public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken cancellationToken = default) =>
        OperateAsync(request.Volume.Device, request.ExpectedRevision, request.MediaGeneration, request, null, cancellationToken);
    public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken cancellationToken = default) =>
        OperateAsync(request.Device, request.ExpectedRevision, request.MediaGeneration, null, request, cancellationToken);

    private async ValueTask<DeviceOperationResult> OperateAsync(DeviceKey key, long revision, string? generation,
        DeviceMountRequest? mount, DeviceRemovalRequest? removal, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (key.ProviderId != ProviderId || revision <= 0 || revision > _snapshot.Revision
                || !_deviceRevisions.TryGetValue(key, out var previous) || previous.ChangedAt > revision) return Stale();
            var inventory = await RefreshAsync(linked.Token).ConfigureAwait(false);
            var selected = _snapshot.Devices.SingleOrDefault(device => device.Key == key);
            if (!_deviceRevisions.TryGetValue(key, out var refreshed) || refreshed.ChangedAt > revision
                || selected is null || selected.MediaGeneration != generation
                || (mount is not null && !selected.Volumes.Any(volume => volume.Key == mount.Volume))) return Stale();
            return mount is not null ? await MountNativeAsync(inventory, mount, linked.Token).ConfigureAwait(false)
                : await RemoveNativeAsync(inventory, removal!, linked.Token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static DeviceOperationResult Stale() => new(DeviceOperationStatus.StaleObservation,
        new("StaleObservation", "The selected device or media observation has changed."));

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposal ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        _lifetime.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await DisposeNativeAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
        // Keep the cancelled token source available so late calls consistently fail closed.
    }
}
