using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Owns read-only registrations. Callbacks must only enqueue a reconciliation signal.</summary>
public interface IWindowsDeviceNotificationApi
{
    IDisposable RegisterInterface(Guid classGuid, Action changed);
    IDisposable RegisterVolume(string volumeGuidPath, Action changed);
}

/// <summary>Single-session, event-driven Windows observation, independent of UI and mount operations.</summary>
public sealed class WindowsDeviceObserver(
    Func<CancellationToken, ValueTask<WindowsDeviceInventory>> readInventory,
    IWindowsDeviceNotificationApi notifications) : IAsyncDisposable
{
    private readonly Func<CancellationToken, ValueTask<WindowsDeviceInventory>> _readInventory =
        readInventory ?? throw new ArgumentNullException(nameof(readInventory));
    private readonly IWindowsDeviceNotificationApi _notifications =
        notifications ?? throw new ArgumentNullException(nameof(notifications));
    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        AllowSynchronousContinuations = false,
        SingleReader = true
    });
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<IDisposable> _interfaces = [];
    private readonly Dictionary<string, IDisposable> _volumes = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private int _started;

    public async IAsyncEnumerable<WindowsDeviceInventory> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("This observer supports one observation session.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        try
        {
            await Task.Run(() =>
            {
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var suffix in new[] { "07", "08", "0d" })
                        _interfaces.Add(_notifications.RegisterInterface(
                            new Guid($"53f563{suffix}-b6bf-11d0-94f2-00a0c91efb8b"), Signal));
                }
            }, token).ConfigureAwait(false);
            string? previous = null;
            do
            {
                // Even injected synchronous implementations cannot block the caller's UI context.
                var inventory = await Task.Run(async () =>
                    await _readInventory(token).ConfigureAwait(false), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                await Task.Run(() => ReconcileWatches(inventory, token), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var fingerprint = Fingerprint(inventory);
                if (fingerprint != previous)
                {
                    previous = fingerprint;
                    yield return inventory;
                }
            } while (await _changes.Reader.WaitToReadAsync(token).ConfigureAwait(false) && ConsumeSignal());
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    private bool ConsumeSignal() => _changes.Reader.TryRead(out _);
    private void Signal() => _changes.Writer.TryWrite(true);

    private void ReconcileWatches(WindowsDeviceInventory inventory, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            var paths = inventory.Devices.SelectMany(device => device.Volumes)
                .Select(volume => volume.VolumeGuidPath).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in _volumes.Keys.Where(path => !paths.Contains(path)).ToArray())
            {
                _volumes[path].Dispose();
                _volumes.Remove(path);
            }
            foreach (var path in paths.Where(path => !_volumes.ContainsKey(path)))
            {
                _volumes.Add(path, _notifications.RegisterVolume(path, Signal));
                // Close the gap between reading this volume and registering its target notifications.
                Signal();
            }
        }
    }

    private static string Fingerprint(WindowsDeviceInventory inventory) => JsonSerializer.Serialize(inventory with
    {
        Devices = inventory.Devices.OrderBy(device => device.InstanceId, StringComparer.Ordinal).Select(device => device with
        {
            Volumes = device.Volumes.OrderBy(volume => volume.VolumeGuidPath ?? volume.InterfacePath, StringComparer.Ordinal)
                .Select(volume => volume with
                {
                    MountPoints = volume.MountPoints.Order(StringComparer.Ordinal).ToImmutableArray()
                }).ToImmutableArray()
        }).ToImmutableArray(),
        Errors = inventory.Errors.OrderBy(error => JsonSerializer.Serialize(error), StringComparer.Ordinal).ToImmutableArray()
    });

    public async ValueTask DisposeAsync()
    {
        // Never unregister from the native callback: CM_Unregister_Notification waits for callbacks.
        await Task.Run(() =>
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _lifetime.Cancel();
                _changes.Writer.TryComplete();
                List<Exception> failures = [];
                try
                {
                    foreach (var registration in _volumes.Values.Concat(_interfaces))
                    {
                        try { registration.Dispose(); }
                        catch (Exception error) { failures.Add(error); }
                    }
                }
                finally
                {
                    _volumes.Clear();
                    _interfaces.Clear();
                }
                if (failures.Count != 0) throw new AggregateException("Notification cleanup failed.", failures);
            }
        }).ConfigureAwait(false);
    }
}
