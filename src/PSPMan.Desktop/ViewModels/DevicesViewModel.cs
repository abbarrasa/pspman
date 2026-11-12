using System;
using System.Collections.Immutable;
using System.Linq;
using Avalonia.Threading;
using PSPMan.Core.Devices;

namespace PSPMan.Desktop.ViewModels;

public sealed record DeviceProviderErrorViewModel(string ProviderId, string ErrorText);

/// <summary>Observes a caller-owned session; never starts discovery or performs native operations.</summary>
public sealed partial class DevicesViewModel : ViewModelBase, IDisposable
{
    private readonly DeviceSessionService? _session;
    private readonly Action<Action> _postToUi;
    private readonly DeviceOperationError? _startupError;
    private readonly object _gate = new();
    private bool _disposed;
    private long _requestedRevision = -1;
    private long _version;
    private Presentation _presentation = new([], [], [], [], false);

    public DevicesViewModel(DeviceSessionService? session = null, Action<Action>? postToUi = null,
        DeviceOperationError? startupError = null)
    {
        _session = session;
        _postToUi = postToUi ?? (action => Dispatcher.UIThread.Post(action));
        _startupError = startupError;
        InitializeSourceCommands();
        if (session is not null) session.SnapshotChanged += OnSnapshotChanged;
        Update(session?.Snapshot ?? new(new(0, []), new DeviceSessionState(),
            ImmutableDictionary<string, long>.Empty, ImmutableDictionary<string, DeviceOperationError>.Empty));
    }

    public ImmutableArray<DeviceItemViewModel> Devices => _presentation.Devices;
    public ImmutableArray<DeviceItemViewModel> Sources => _presentation.Sources;
    public ImmutableArray<DeviceItemViewModel> PspDestinations => _presentation.Destinations;
    public ImmutableArray<DeviceProviderErrorViewModel> ProviderErrors => _presentation.Errors;
    public bool IsEmpty => Devices.IsEmpty;
    public bool IsDetecting => _presentation.Detecting;
    public bool HasErrors => !ProviderErrors.IsEmpty || Devices.Any(device => device.HasErrors);
    public string StatusText => IsDetecting ? "Detecting devices..."
        : IsEmpty ? HasErrors ? "Device services are unavailable." : "No devices detected."
        : HasErrors ? "Some devices or services reported errors." : "Devices detected.";

    private void OnSnapshotChanged(object? sender, DeviceSessionSnapshot snapshot) => Update(snapshot);

    /// <summary>Accepts immutable session observations from any thread; the injected poster must run on the UI context.</summary>
    public void Update(DeviceSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        long version;
        lock (_gate)
        {
            if (_disposed || snapshot.Inventory.Revision < _requestedRevision) return;
            _requestedRevision = snapshot.Inventory.Revision;
            version = ++_version;
        }
        _postToUi(() =>
        {
            lock (_gate)
            {
                if (_disposed || version != _version) return;
                var devices = (snapshot.Inventory.Devices.IsDefault ? [] : snapshot.Inventory.Devices)
                    .Select(device => new DeviceItemViewModel(device)).ToImmutableArray();
                var errors = snapshot.ProviderErrors.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new DeviceProviderErrorViewModel(pair.Key, DeviceItemViewModel.FormatError(pair.Value)!)).ToImmutableArray();
                if (_startupError is not null)
                    errors = errors.Add(new("Device services", DeviceItemViewModel.FormatError(_startupError)!));
                // Publish one complete state before raising any binding notifications.
                _presentation = new(devices, devices.Where(device => device.IsSource).ToImmutableArray(),
                    devices.Where(device => device.IsPspDestination).ToImmutableArray(), errors,
                    devices.Any(device => device.State == DeviceState.Detecting)
                        || (_session is not null && snapshot.Inventory.Revision == 0 && devices.IsEmpty && errors.IsEmpty));
                ProjectSources(snapshot);
                foreach (var property in new[] { nameof(Devices), nameof(Sources), nameof(PspDestinations), nameof(ProviderErrors),
                    nameof(IsEmpty), nameof(IsDetecting), nameof(HasErrors), nameof(StatusText) }) OnPropertyChanged(property);
            }
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ++_version;
        }
        if (_session is not null) _session.SnapshotChanged -= OnSnapshotChanged;
        DisposeSourceCommands();
    }

    private sealed record Presentation(ImmutableArray<DeviceItemViewModel> Devices,
        ImmutableArray<DeviceItemViewModel> Sources, ImmutableArray<DeviceItemViewModel> Destinations,
        ImmutableArray<DeviceProviderErrorViewModel> Errors, bool Detecting);
}
