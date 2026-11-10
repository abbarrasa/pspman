using System.Collections.Immutable;
using System.Threading.Channels;

namespace PSPMan.Core.Devices;

public sealed record DeviceSessionSnapshot(
    DeviceInventorySnapshot Inventory,
    DeviceSessionState Associations,
    ImmutableDictionary<string, long> ProviderRevisions,
    ImmutableDictionary<string, DeviceOperationError> ProviderErrors);

/// <summary>Owns provider observation, automatic mounting and disposal; performs no UI work.</summary>
public sealed class DeviceSessionService : IAsyncDisposable
{
    private readonly IDeviceProvider[] _providers;
    private readonly object _lifecycle = new();
    private readonly Channel<SessionUpdate> _updates = Channel.CreateUnbounded<SessionUpdate>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly Dictionary<string, DeviceInventorySnapshot> _inventories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedObservers = new(StringComparer.Ordinal);
    private readonly HashSet<MountAttempt> _mountAttempts = [];
    private readonly Dictionary<DeviceKey, MountWork> _activeMounts = [];
    private readonly Dictionary<DeviceKey, RemovalWork> _activeRemovals = [];
    private readonly Dictionary<MountAttempt, DeviceOperationError> _mountErrors = [];
    private readonly Dictionary<DeviceKey, DeviceOperationError> _removalErrors = [];
    private readonly HashSet<RemovalSuppression> _removalSuppressions = [];
    private readonly List<Task> _mountTasks = [];
    private readonly List<Task> _removalTasks = [];
    private readonly object _mountTaskGate = new();
    private readonly SemaphoreSlim _authorizationGate = new(1, 1);
    private CancellationTokenSource? _lifetime;
    private Task[] _workers = [];
    private Task _processor = Task.CompletedTask;
    private Task? _disposeTask;
    private DeviceSessionSnapshot _snapshot = new(new DeviceInventorySnapshot(0, []), new DeviceSessionState(),
        ImmutableDictionary<string, long>.Empty, ImmutableDictionary<string, DeviceOperationError>.Empty);

    public DeviceSessionService(IEnumerable<IDeviceProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
        if (_providers.Any(provider => provider is null || string.IsNullOrWhiteSpace(provider.ProviderId))
            || _providers.Select(provider => provider.ProviderId).Distinct(StringComparer.Ordinal).Count() != _providers.Length)
        {
            throw new ArgumentException("Device providers must have distinct nonempty identifiers.", nameof(providers));
        }
    }

    public DeviceSessionSnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>Raised serially on a worker thread. Subscribers must return promptly and marshal UI work themselves.</summary>
    public event EventHandler<DeviceSessionSnapshot>? SnapshotChanged;

    /// <summary>Completes after each initial inventory or its observable error has been processed.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_lifetime is not null) throw new InvalidOperationException("Device observation has already started.");
            cancellationToken.ThrowIfCancellationRequested();
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _processor = Task.Run(ProcessUpdatesAsync);
            var ready = _providers.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            _workers = _providers.Select((provider, index) =>
                Task.Run(() => ObserveProviderAsync(provider, ready[index], _lifetime.Token))).ToArray();
            return Task.WhenAll(ready.Select(item => item.Task));
        }
    }

    /// <summary>Captures identity evidence for explicit choices against the current serialized observation.</summary>
    public async ValueTask SetAssociationsAsync(DeviceSessionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_lifetime is null) throw new InvalidOperationException("Device observation has not started.");
        }
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _updates.Writer.WriteAsync(new SessionUpdate(Associations: state, Applied: applied), cancellationToken).ConfigureAwait(false);
        await applied.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Explicitly retries a volume whose previous mount attempt failed.</summary>
    public async ValueTask RetryMountAsync(VolumeKey volume, CancellationToken cancellationToken = default)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_lifetime is null) throw new InvalidOperationException("Device observation has not started.");
        }
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _updates.Writer.WriteAsync(new SessionUpdate(RetryVolume: volume, Applied: applied), cancellationToken)
            .ConfigureAwait(false);
        await applied.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests safe removal of a complete physical device and all its volumes.</summary>
    public async ValueTask RemoveDeviceAsync(DeviceKey device, CancellationToken cancellationToken = default)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_lifetime is null) throw new InvalidOperationException("Device observation has not started.");
        }
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _updates.Writer.WriteAsync(new SessionUpdate(RemoveDevice: device, Applied: applied), cancellationToken)
            .ConfigureAwait(false);
        await applied.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ObserveProviderAsync(IDeviceProvider provider, TaskCompletionSource ready, CancellationToken token)
    {
        try
        {
            await using var changes = provider.WatchChangesAsync(token).GetAsyncEnumerator(token);
            // Start enumeration before requesting the initial inventory. Providers are responsible
            // for buffering/reconciling native registration races as specified by IDeviceProvider.
            var firstChange = changes.MoveNextAsync().AsTask();
            var watch = Task.Run(() => PumpChangesAsync(provider.ProviderId, changes, firstChange, token));
            try
            {
                var initial = await provider.GetSnapshotAsync(token).ConfigureAwait(false);
                var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await _updates.Writer.WriteAsync(new SessionUpdate(provider.ProviderId, initial, Applied: applied), token).ConfigureAwait(false);
                await applied.Task.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                await ReportErrorAsync(provider.ProviderId, exception, terminal: false).ConfigureAwait(false);
            }
            finally { ready.TrySetResult(); }
            await watch.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            await ReportErrorAsync(provider.ProviderId, exception, terminal: true).ConfigureAwait(false);
        }
        finally { ready.TrySetResult(); }
    }

    private async Task PumpChangesAsync(string providerId, IAsyncEnumerator<DeviceInventorySnapshot> changes,
        Task<bool> next, CancellationToken token)
    {
        try
        {
            while (await next.ConfigureAwait(false))
            {
                await _updates.Writer.WriteAsync(new SessionUpdate(providerId, changes.Current), token).ConfigureAwait(false);
                next = changes.MoveNextAsync().AsTask();
            }
            if (!token.IsCancellationRequested)
            {
                await ReportErrorAsync(providerId, new IOException("Device observation ended unexpectedly."), terminal: true).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            await ReportErrorAsync(providerId, exception, terminal: true).ConfigureAwait(false);
        }
    }

    private async Task ReportErrorAsync(string providerId, Exception exception, bool terminal)
    {
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _updates.Writer.WriteAsync(new SessionUpdate(providerId,
            Error: new DeviceOperationError("DeviceObservationFailed", exception.Message), Terminal: terminal, Applied: applied)).ConfigureAwait(false);
        await applied.Task.ConfigureAwait(false);
    }

    private async Task ProcessUpdatesAsync()
    {
        await foreach (var update in _updates.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                Apply(update);
                update.Applied?.TrySetResult();
            }
            catch (Exception exception) { update.Applied?.TrySetException(exception); }
        }
    }

    private void Apply(SessionUpdate update)
    {
        if (update.RetryVolume is { } retryVolume)
        {
            if (!_mountErrors.Keys.Any(attempt => attempt.Volume == retryVolume))
                throw new InvalidOperationException("The volume has no failed mount attempt to retry.");

            foreach (var attempt in _mountErrors.Keys.Where(attempt => attempt.Volume == retryVolume).ToArray())
            {
                _mountErrors.Remove(attempt);
                _mountAttempts.Remove(attempt);
            }
            Publish(Snapshot, Snapshot.ProviderErrors, Snapshot.ProviderRevisions, associations: null);
            update.Applied?.TrySetResult();
            return;
        }

        if (update.RemoveDevice is { } deviceKey)
        {
            if (_activeMounts.ContainsKey(deviceKey))
                throw new InvalidOperationException("The device has an active mount operation.");
            if (_activeRemovals.ContainsKey(deviceKey))
                throw new InvalidOperationException("The device is already being removed.");

            var observed = _inventories.Values
                .SelectMany(inventory => inventory.Devices.Select(device => (Inventory: inventory, Device: device)))
                .FirstOrDefault(item => item.Device.Key == deviceKey);
            if (observed.Device is null)
                throw new InvalidOperationException("The device is not currently available.");
            if ((observed.Device.Capabilities & DeviceCapabilities.SafeRemoval) == 0)
                throw new InvalidOperationException("The device does not support safe removal.");
            if (observed.Device.State is DeviceState.Unavailable or DeviceState.Detecting)
                throw new InvalidOperationException("The device is not currently available.");

            _removalErrors.Remove(deviceKey);
            _removalSuppressions.RemoveWhere(item => item.Device == deviceKey);
            var removalWork = new RemovalWork(
                _providers.Single(provider => provider.ProviderId == deviceKey.ProviderId),
                new DeviceRemovalRequest(deviceKey, observed.Inventory.Revision, observed.Device.MediaGeneration),
                new RemovalSuppression(deviceKey, observed.Device.MediaGeneration));
            _activeRemovals.Add(deviceKey, removalWork);
            Publish(Snapshot, Snapshot.ProviderErrors, Snapshot.ProviderRevisions, associations: null);
            lock (_mountTaskGate)
            {
                var token = _lifetime!.Token;
                _removalTasks.Add(Task.Run(() => RemoveAndObserveAsync(removalWork, token)));
            }
            update.Applied?.TrySetResult();
            return;
        }

        var completedMount = false;
        if (update.CompletedMount is { } work
            && _activeMounts.TryGetValue(work.Request.Volume.Device, out var active) && ReferenceEquals(active, work))
        {
            _activeMounts.Remove(work.Request.Volume.Device);
            completedMount = true;
            if (update.MountResult is { Status: not DeviceOperationStatus.Succeeded } result)
            {
                _mountErrors[work.Attempt] = result.Error
                    ?? new DeviceOperationError("MountFailed", "The volume could not be mounted.", work.Request.Volume);
            }
        }
        var completedRemoval = false;
        if (update.CompletedRemoval is { } removal
            && _activeRemovals.TryGetValue(removal.Request.Device, out var activeRemoval)
            && ReferenceEquals(activeRemoval, removal))
        {
            _activeRemovals.Remove(removal.Request.Device);
            completedRemoval = true;
            if (update.RemovalResult is { Status: DeviceOperationStatus.Succeeded })
                _removalSuppressions.Add(removal.Suppression);
            else
                _removalErrors[removal.Request.Device] = update.RemovalResult?.Error
                    ?? new DeviceOperationError("RemovalFailed", "The device could not be safely removed.");
        }
        var current = Snapshot;
        var errors = current.ProviderErrors;
        var revisions = current.ProviderRevisions;
        if (update.ProviderId is { } providerId)
        {
            if (update.Error is { } error)
            {
                _inventories.Remove(providerId);
                errors = errors.SetItem(providerId, error);
                if (update.Terminal) _failedObservers.Add(providerId);
            }
            else if (update.Inventory is { } inventory)
            {
                if (_failedObservers.Contains(providerId)
                    || (revisions.TryGetValue(providerId, out var previous) && inventory.Revision <= previous))
                {
                    if (completedMount || completedRemoval) Publish(current, errors, revisions, update.Associations);
                    return;
                }
                try
                {
                    var normalized = Normalize(providerId, inventory);
                    _inventories[providerId] = normalized;
                    InvalidateStaleOperations(providerId, normalized);
                }
                catch (InvalidDataException exception)
                {
                    _inventories.Remove(providerId);
                    errors = errors.SetItem(providerId, new DeviceOperationError("InvalidDeviceInventory", exception.Message));
                    Publish(current, errors, revisions, update.Associations);
                    return;
                }
                revisions = revisions.SetItem(providerId, inventory.Revision);
                errors = errors.Remove(providerId);
            }
        }
        Publish(current, errors, revisions, update.Associations);
    }

    private void InvalidateStaleOperations(string providerId, DeviceInventorySnapshot inventory)
    {
        foreach (var (deviceKey, work) in _activeMounts
                     .Where(item => item.Key.ProviderId == providerId)
                     .ToArray())
        {
            if (!IsCurrentDevice(inventory, deviceKey, work.Request.MediaGeneration))
                _activeMounts.Remove(deviceKey);
        }

        foreach (var (deviceKey, work) in _activeRemovals
                     .Where(item => item.Key.ProviderId == providerId)
                     .ToArray())
        {
            if (!IsCurrentDevice(inventory, deviceKey, work.Request.MediaGeneration))
                _activeRemovals.Remove(deviceKey);
        }
    }

    private static bool IsCurrentDevice(DeviceInventorySnapshot inventory, DeviceKey key, string? mediaGeneration)
    {
        var device = inventory.Devices.FirstOrDefault(device => device.Key == key);
        return device is not null
            && device.State != DeviceState.Unavailable
            && device.MediaGeneration == mediaGeneration;
    }

    private void Publish(DeviceSessionSnapshot previous, ImmutableDictionary<string, DeviceOperationError> errors,
        ImmutableDictionary<string, long> revisions, DeviceSessionState? associations)
    {
        var scheduled = PrepareMounts();
        var inventory = new DeviceInventorySnapshot(previous.Inventory.Revision + 1,
            _providers.Where(provider => _inventories.ContainsKey(provider.ProviderId))
                .SelectMany(provider => _inventories[provider.ProviderId].Devices)
                .Select(WithMountState).ToImmutableArray());
        var snapshot = new DeviceSessionSnapshot(inventory,
            DeviceAssociationRules.ReconcileAssociations(associations ?? previous.Associations, inventory), revisions, errors);
        Volatile.Write(ref _snapshot, snapshot);
        lock (_mountTaskGate)
        {
            _mountTasks.RemoveAll(task => task.IsCompleted);
            foreach (var work in scheduled)
            {
                var token = _lifetime!.Token;
                _mountTasks.Add(Task.Run(() => MountAndObserveAsync(work, token)));
            }
        }
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private List<MountWork> PrepareMounts()
    {
        var scheduled = new List<MountWork>();
        if (_lifetime is null || _lifetime.IsCancellationRequested) return scheduled;
        var present = _inventories.Values.SelectMany(inventory => inventory.Devices)
            .Where(device => device.State != DeviceState.Unavailable)
            .ToDictionary(device => device.Key, device => device.MediaGeneration);
        _removalSuppressions.RemoveWhere(item => !present.TryGetValue(item.Device, out var generation)
            || generation != item.MediaGeneration);
        foreach (var device in _removalErrors.Keys.Where(device => !present.ContainsKey(device)).ToArray())
            _removalErrors.Remove(device);
        _mountAttempts.RemoveWhere(attempt => !present.TryGetValue(attempt.Volume.Device, out var generation)
            || generation != attempt.MediaGeneration);
        foreach (var attempt in _mountErrors.Keys.Where(attempt => !_mountAttempts.Contains(attempt)).ToArray())
            _mountErrors.Remove(attempt);

        foreach (var provider in _providers)
        {
            if (!_inventories.TryGetValue(provider.ProviderId, out var observed)) continue;
            foreach (var device in observed.Devices)
            {
                if (_activeMounts.ContainsKey(device.Key) || _activeRemovals.ContainsKey(device.Key)
                    || _removalErrors.ContainsKey(device.Key)
                    || _removalSuppressions.Contains(new RemovalSuppression(device.Key, device.MediaGeneration))
                    || (device.Capabilities & DeviceCapabilities.Mount) == 0
                    || (device.Capabilities & (DeviceCapabilities.OpticalSource | DeviceCapabilities.RemovableSource
                        | DeviceCapabilities.PspDestination)) == 0
                    || device.State is DeviceState.Detecting or DeviceState.Removing or DeviceState.Unavailable) continue;
                var volume = device.Volumes.FirstOrDefault(volume => volume.State == DeviceState.Unmounted
                    && string.IsNullOrWhiteSpace(volume.Root) && volume.Error is null
                    && !_mountAttempts.Contains(new MountAttempt(volume.Key, device.MediaGeneration)));
                if (volume is null) continue;
                var attempt = new MountAttempt(volume.Key, device.MediaGeneration);
                var work = new MountWork(provider, new DeviceMountRequest(volume.Key, observed.Revision, device.MediaGeneration), attempt);
                _mountAttempts.Add(attempt);
                _activeMounts.Add(device.Key, work);
                scheduled.Add(work);
            }
        }
        return scheduled;
    }

    private DeviceSnapshot WithMountState(DeviceSnapshot device)
    {
        if (device.State is DeviceState.Detecting or DeviceState.Removing or DeviceState.Unavailable) return device;
        if (_activeRemovals.ContainsKey(device.Key))
            return device with { State = DeviceState.Removing,
                Volumes = device.Volumes.Select(volume => volume with { State = DeviceState.Removing }).ToImmutableArray() };
        _activeMounts.TryGetValue(device.Key, out var active);
        var volumes = device.Volumes.Select(volume =>
        {
            if (volume.State == DeviceState.Ready && !string.IsNullOrWhiteSpace(volume.Root)) return volume;
            if (active is not null && active.Request.MediaGeneration == device.MediaGeneration && active.Request.Volume == volume.Key)
                return volume with { State = DeviceState.Mounting, Root = null };
            return _mountErrors.TryGetValue(new MountAttempt(volume.Key, device.MediaGeneration), out var error)
                ? volume with { State = DeviceState.Error, Root = null, Error = error } : volume;
        }).ToImmutableArray();
        var pending = active is not null && active.Request.MediaGeneration == device.MediaGeneration;
        if (_removalErrors.TryGetValue(device.Key, out var removalError)
            && removalError.Volume is { } errorVolume)
        {
            volumes = volumes.Select(volume => volume.Key == errorVolume
                ? volume with { State = DeviceState.Error, Error = removalError } : volume).ToImmutableArray();
        }
        return device with { Volumes = volumes,
            State = _removalErrors.ContainsKey(device.Key) ? DeviceState.Error
                : pending ? DeviceState.Mounting : volumes.Any(volume => volume.Error is not null)
                    ? DeviceState.Error : device.State };
    }

    private async Task MountAndObserveAsync(MountWork work, CancellationToken token)
    {
        DeviceOperationResult result;
        try
        {
            await _authorizationGate.WaitAsync(token).ConfigureAwait(false);
            try { result = await work.Provider.MountAsync(work.Request, token).ConfigureAwait(false); }
            finally { _authorizationGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            result = new DeviceOperationResult(DeviceOperationStatus.Failed,
                new DeviceOperationError("MountFailed", exception.Message, work.Request.Volume));
        }
        if (token.IsCancellationRequested) return;
        DeviceInventorySnapshot? inventory = null;
        DeviceOperationError? error = null;
        try { inventory = await work.Provider.GetSnapshotAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception exception) { error = new DeviceOperationError("DeviceObservationFailed", exception.Message); }
        if (token.IsCancellationRequested) return;
        await _updates.Writer.WriteAsync(new SessionUpdate(work.Provider.ProviderId, inventory, error,
            CompletedMount: work, MountResult: result), token).ConfigureAwait(false);
    }

    private async Task RemoveAndObserveAsync(RemovalWork work, CancellationToken token)
    {
        DeviceOperationResult result;
        try
        {
            await _authorizationGate.WaitAsync(token).ConfigureAwait(false);
            try { result = await work.Provider.RemoveAsync(work.Request, token).ConfigureAwait(false); }
            finally { _authorizationGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            result = new DeviceOperationResult(DeviceOperationStatus.Failed,
                new DeviceOperationError("RemovalFailed", exception.Message));
        }
        if (token.IsCancellationRequested) return;
        DeviceInventorySnapshot? inventory = null;
        DeviceOperationError? error = null;
        try { inventory = await work.Provider.GetSnapshotAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception exception) { error = new DeviceOperationError("DeviceObservationFailed", exception.Message); }
        if (token.IsCancellationRequested) return;
        await _updates.Writer.WriteAsync(new SessionUpdate(work.Provider.ProviderId, inventory, error,
            CompletedRemoval: work, RemovalResult: result), token).ConfigureAwait(false);
    }

    private static DeviceInventorySnapshot Normalize(string providerId, DeviceInventorySnapshot inventory)
    {
        var devices = inventory.Devices.IsDefault ? ImmutableArray<DeviceSnapshot>.Empty : inventory.Devices;
        if (devices.Any(device => device.Key.ProviderId != providerId
            || (!device.Volumes.IsDefault && device.Volumes.Any(volume => volume.Key.Device != device.Key))))
        {
            throw new InvalidDataException("Device inventory contains an identity belonging to another device or provider.");
        }
        return inventory with { Devices = devices.DistinctBy(device => device.Key).Select(device => device with
        {
            Volumes = (device.Volumes.IsDefault ? ImmutableArray<VolumeSnapshot>.Empty : device.Volumes)
                .DistinctBy(volume => volume.Key).Select(volume =>
                    device.State is DeviceState.Ready or DeviceState.Mounting or DeviceState.Error
                    && volume.State == DeviceState.Ready && !string.IsNullOrWhiteSpace(volume.Root)
                        ? volume : volume with { Root = null }).ToImmutableArray()
        }).ToImmutableArray() };
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _lifetime?.Cancel();
        await Task.WhenAll(_workers).ConfigureAwait(false);
        if (_lifetime is not null)
        {
            // Drain observations already queued before cancellation so no mount task can be added
            // after we capture the task list. The processor remains alive for completions.
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _updates.Writer.WriteAsync(new SessionUpdate(Applied: drained)).ConfigureAwait(false);
            await drained.Task.ConfigureAwait(false);
            Task[] mounts;
            Task[] removals;
            lock (_mountTaskGate)
            {
                mounts = _mountTasks.ToArray();
                removals = _removalTasks.ToArray();
            }
            await Task.WhenAll(mounts).ConfigureAwait(false);
            await Task.WhenAll(removals).ConfigureAwait(false);
        }
        _updates.Writer.TryComplete();
        await _processor.ConfigureAwait(false);
        foreach (var provider in _providers) await provider.DisposeAsync().ConfigureAwait(false);
        _lifetime?.Dispose();
    }

    private sealed record SessionUpdate(string? ProviderId = null, DeviceInventorySnapshot? Inventory = null,
        DeviceOperationError? Error = null, bool Terminal = false, DeviceSessionState? Associations = null,
        TaskCompletionSource? Applied = null, MountWork? CompletedMount = null, DeviceOperationResult? MountResult = null,
        VolumeKey? RetryVolume = null, DeviceKey? RemoveDevice = null, RemovalWork? CompletedRemoval = null,
        DeviceOperationResult? RemovalResult = null);

    private sealed record MountAttempt(VolumeKey Volume, string? MediaGeneration);
    private sealed record MountWork(IDeviceProvider Provider, DeviceMountRequest Request, MountAttempt Attempt);
    private sealed record RemovalSuppression(DeviceKey Device, string? MediaGeneration);
    private sealed record RemovalWork(IDeviceProvider Provider, DeviceRemovalRequest Request, RemovalSuppression Suppression);
}
