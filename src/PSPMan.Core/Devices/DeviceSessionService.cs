using System.Collections.Immutable;
using System.Threading.Channels;

namespace PSPMan.Core.Devices;

public sealed record DeviceSessionSnapshot(
    DeviceInventorySnapshot Inventory,
    DeviceSessionState Associations,
    ImmutableDictionary<string, long> ProviderRevisions,
    ImmutableDictionary<string, DeviceOperationError> ProviderErrors);

/// <summary>Owns provider observation and disposal. No mounting or UI work is performed here.</summary>
public sealed class DeviceSessionService : IAsyncDisposable
{
    private readonly IDeviceProvider[] _providers;
    private readonly object _lifecycle = new();
    private readonly Channel<SessionUpdate> _updates = Channel.CreateUnbounded<SessionUpdate>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly Dictionary<string, DeviceInventorySnapshot> _inventories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedObservers = new(StringComparer.Ordinal);
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
                    || (revisions.TryGetValue(providerId, out var previous) && inventory.Revision <= previous)) return;
                try { _inventories[providerId] = Normalize(providerId, inventory); }
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

    private void Publish(DeviceSessionSnapshot previous, ImmutableDictionary<string, DeviceOperationError> errors,
        ImmutableDictionary<string, long> revisions, DeviceSessionState? associations)
    {
        var inventory = new DeviceInventorySnapshot(previous.Inventory.Revision + 1,
            _providers.Where(provider => _inventories.ContainsKey(provider.ProviderId))
                .SelectMany(provider => _inventories[provider.ProviderId].Devices).ToImmutableArray());
        var snapshot = new DeviceSessionSnapshot(inventory,
            DeviceAssociationRules.ReconcileAssociations(associations ?? previous.Associations, inventory), revisions, errors);
        Volatile.Write(ref _snapshot, snapshot);
        SnapshotChanged?.Invoke(this, snapshot);
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
        _updates.Writer.TryComplete();
        await _processor.ConfigureAwait(false);
        foreach (var provider in _providers) await provider.DisposeAsync().ConfigureAwait(false);
        _lifetime?.Dispose();
    }

    private sealed record SessionUpdate(string? ProviderId = null, DeviceInventorySnapshot? Inventory = null,
        DeviceOperationError? Error = null, bool Terminal = false, DeviceSessionState? Associations = null,
        TaskCompletionSource? Applied = null);
}
