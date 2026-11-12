using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PSPMan.Core.Devices;

namespace PSPMan.Infraestructure.Devices.Linux;

/// <summary>Session adapter over typed UDisks operations and USB evidence; no game-file probes.</summary>
internal sealed class LinuxSessionDeviceProvider : SessionProviderBridge<LinuxDeviceInventory>
{
    private readonly IUDisksClient _client;
    private readonly IUDisksChangeSource _changes;
    private readonly Func<string, LinuxUsbMetadata> _metadata;
    private readonly CachedClient _cached = new();
    private readonly LinuxDeviceProvider _inventory;
    private readonly Dictionary<string, (string Fingerprint, string Generation)> _media = [];
    private readonly HashSet<string> _ambiguous = [];
    private ImmutableArray<DeviceSnapshot> _projected = [];
    public override string ProviderId => "linux";

    internal LinuxSessionDeviceProvider(IUDisksClient client, IUDisksChangeSource changes, Func<string, LinuxUsbMetadata> metadata)
    { _client = client; _changes = changes; _metadata = metadata; _inventory = new(_cached); }

    internal static async ValueTask<IDeviceProvider> CreateAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var transport = await TmdsUDisksTransport.ConnectSystemAsync(token).ConfigureAwait(false);
        try { return new LinuxSessionDeviceProvider(new UDisksClient(transport), new BorrowedChanges(transport), new LinuxUsbMetadataReader("/sys").Read); }
        catch { await transport.DisposeAsync().ConfigureAwait(false); throw; }
    }

    protected override async ValueTask<LinuxDeviceInventory> ReadNativeAsync(CancellationToken token)
    {
        _cached.Snapshot = await _client.GetManagedObjectsAsync(token).ConfigureAwait(false);
        var inventory = await _inventory.GetInventoryAsync(token).ConfigureAwait(false);
        // Reconcile every observed native transition, even if a subsequent buffered signal causes
        // the common bridge to publish a fresher inventory instead of this one.
        _projected = MapInventory(inventory);
        return inventory;
    }

    protected override async IAsyncEnumerable<LinuxDeviceInventory> ObserveNativeAsync([EnumeratorCancellation] CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var stream = await _changes.SubscribeChangesAsync(lifetime.Token).ConfigureAwait(false);
        await using var changes = stream.GetAsyncEnumerator(lifetime.Token);
        // Enter the native stream before yielding: its finally owns already-acquired match rules.
        var next = changes.MoveNextAsync().AsTask();
        try
        {
            yield return await ReadForObserverAsync(lifetime.Token).ConfigureAwait(false);
            while (await next.ConfigureAwait(false))
            {
                next = changes.MoveNextAsync().AsTask();
                yield return await ReadForObserverAsync(lifetime.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            lifetime.Cancel();
            try { await next.ConfigureAwait(false); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
    }

    protected override ImmutableArray<DeviceSnapshot> Project(LinuxDeviceInventory inventory) => _projected;

    private ImmutableArray<DeviceSnapshot> MapInventory(LinuxDeviceInventory inventory)
    {
        var metadata = inventory.Devices.ToDictionary(device => device.ObjectPath, device =>
            device.Kind == LinuxDeviceKind.Usb && device.Volumes.FirstOrDefault() is { } volume ? _metadata(volume.DevicePath) : null);
        foreach (var group in metadata.Values.Where(value => !string.IsNullOrWhiteSpace(value?.Serial))
            .GroupBy(value => PersistentId(value!)).Where(group => group.Count() > 1)) _ambiguous.Add(group.Key);
        var present = inventory.Devices.Select(device => device.ObjectPath).ToHashSet();
        foreach (var missing in _media.Keys.Where(key => !present.Contains(key)).ToArray()) _media.Remove(missing);
        return inventory.Devices.Select(device =>
        {
            var usb = metadata[device.ObjectPath];
            var key = new DeviceKey(ProviderId, device.ObjectPath);
            // Labels and mount roots are not media observations. UUID is a stale-format hint only,
            // never persistent inserted-card identity. Observed disconnect creates a new generation.
            var signature = JsonSerializer.Serialize(new
            {
                MediaAvailable = Property(device.ObjectPath, "org.freedesktop.UDisks2.Drive", "MediaAvailable"),
                Volumes = device.Volumes.OrderBy(volume => volume.ObjectPath, StringComparer.Ordinal)
                    .Select(volume => new { volume.ObjectPath, volume.DevicePath, Filesystem = HasFilesystem(volume),
                        Uuid = Property(volume.ObjectPath, "org.freedesktop.UDisks2.Block", "IdUUID") })
            });
            if (!_media.TryGetValue(device.ObjectPath, out var previous) || previous.Fingerprint != signature)
                _media[device.ObjectPath] = (signature, Guid.NewGuid().ToString("N"));
            var identity = usb is { IdentityConfidence: LinuxUsbIdentityConfidence.Reliable, Serial: not null }
                && !_ambiguous.Contains(PersistentId(usb)) ? new DeviceIdentity(PersistentId(usb), DeviceIdentityConfidence.Persistent)
                : new(null, DeviceIdentityConfidence.SessionOnly);
            var recognition = usb is { VendorId: { } vendor, ProductId: { } product }
                ? PspUsbRecognition.Recognize(new(vendor, product, device.Volumes.Any(HasFilesystem),
                    usb.Interfaces.Select(value => new UsbInterfaceDescriptor(value.ClassCode, value.SubclassCode, value.ProtocolCode)).ToArray())) : null;
            var psp = recognition?.Status == PspUsbRecognitionStatus.Recognized;
            DeviceOperationError? error = usb?.VendorId == 0x054c && recognition?.Status == PspUsbRecognitionStatus.Inconclusive
                ? new("PspRecognitionFailed", "PSP USB recognition is inconclusive.") : null;
            var volumes = device.Volumes.OrderBy(volume => volume.ObjectPath, StringComparer.Ordinal).Select(volume =>
            {
                var root = volume.MountPoints.FirstOrDefault(root => !string.IsNullOrWhiteSpace(root));
                return new VolumeSnapshot(new(key, volume.ObjectPath), volume.Label, root,
                    new(null, DeviceIdentityConfidence.SessionOnly), root is not null ? DeviceState.Ready
                        : HasFilesystem(volume) ? DeviceState.Unmounted : DeviceState.Unavailable);
            }).ToImmutableArray();
            var capabilities = psp ? DeviceCapabilities.PspDestination : device.Kind == LinuxDeviceKind.Optical
                ? DeviceCapabilities.OpticalSource : DeviceCapabilities.RemovableSource;
            capabilities |= DeviceCapabilities.Mount;
            if (device.Kind == LinuxDeviceKind.Optical ? device.IsEjectable : device.IsPowerOffSupported) capabilities |= DeviceCapabilities.SafeRemoval;
            return new DeviceSnapshot(key, device.DisplayName, psp ? DeviceMedium.Psp : device.Kind switch
                { LinuxDeviceKind.Optical => DeviceMedium.Optical, LinuxDeviceKind.Usb => DeviceMedium.Usb, _ => DeviceMedium.MemoryCard },
                capabilities, identity, _media[device.ObjectPath].Generation,
                error is not null ? DeviceState.Error : volumes.Any(volume => volume.State == DeviceState.Ready)
                    ? DeviceState.Ready : volumes.IsEmpty ? DeviceState.Unavailable : DeviceState.Unmounted, volumes, error);
        }).ToImmutableArray();
    }

    private string? Property(string path, string name, string property) => _cached.Snapshot.Objects
        .FirstOrDefault(item => item.ObjectPath == path)?.Interfaces.FirstOrDefault(item => item.Name == name)
        ?.Properties.GetValueOrDefault(property) is { } value ? value.Type switch
        { UDisksPropertyType.String => value.AsString(), UDisksPropertyType.Boolean => value.AsBoolean() ? "true" : "false", _ => null } : null;
    private bool HasFilesystem(LinuxVolume volume) => _cached.Snapshot.Objects.Any(item => item.ObjectPath == volume.ObjectPath
        && item.Interfaces.Any(@interface => @interface.Name == "org.freedesktop.UDisks2.Filesystem"));
    private static string PersistentId(LinuxUsbMetadata metadata) => $"USB:{metadata.VendorId:X4}:{metadata.ProductId:X4}:{metadata.Serial}";

    protected override async ValueTask<DeviceOperationResult> MountNativeAsync(LinuxDeviceInventory inventory, DeviceMountRequest request, CancellationToken token)
    {
        var device = inventory.Devices.Single(item => item.ObjectPath == request.Volume.Device.DeviceId);
        var volume = device.Volumes.Single(item => item.ObjectPath == request.Volume.VolumeId);
        if (!HasFilesystem(volume)) return Failure("VolumeUnavailable", "The selected volume has no available filesystem.");
        try
        {
            await new LinuxVolumeMounter(_client).MountAsync(volume, token).ConfigureAwait(false);
            var refreshed = await ReadNativeAsync(token).ConfigureAwait(false);
            return refreshed.Devices.Where(item => item.ObjectPath == device.ObjectPath).SelectMany(item => item.Volumes)
                .Any(item => item.ObjectPath == volume.ObjectPath && !item.MountPoints.IsEmpty)
                ? new(DeviceOperationStatus.Succeeded) : Failure("MountNotObserved", "Mounting was not confirmed by UDisks2.");
        }
        catch (UDisksClientException error) { return Failure(error.Code, error.Message); }
    }

    protected override async ValueTask<DeviceOperationResult> RemoveNativeAsync(LinuxDeviceInventory inventory, DeviceRemovalRequest request, CancellationToken token)
    {
        var device = inventory.Devices.Single(item => item.ObjectPath == request.Device.DeviceId);
        var result = await new LinuxPhysicalDeviceRemovalCoordinator(_client, ReadNativeAsync).RemoveAsync(device, token).ConfigureAwait(false);
        return result.Succeeded ? new(DeviceOperationStatus.Succeeded)
            : Failure(result.Error?.Code ?? "RemovalFailed", result.Error?.Message ?? "Safe removal did not complete.");
    }
    private static DeviceOperationResult Failure(string code, string message) => new(
        code.Contains("NotAuthorized", StringComparison.Ordinal) ? DeviceOperationStatus.AuthorizationDenied : DeviceOperationStatus.Failed, new(code, message));
    protected override async ValueTask DisposeNativeAsync()
    {
        try { await _changes.DisposeAsync().ConfigureAwait(false); }
        finally { await _client.DisposeAsync().ConfigureAwait(false); }
    }
    private sealed class CachedClient : IUDisksClient
    {
        public UDisksObjectManagerSnapshot Snapshot = new([]);
        public ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(CancellationToken token = default) => new(Snapshot);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class BorrowedChanges(IUDisksChangeSource changes) : IUDisksChangeSource
    {
        public ValueTask<IAsyncEnumerable<UDisksChange>> SubscribeChangesAsync(CancellationToken token = default) => changes.SubscribeChangesAsync(token);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask; // The owning UDisksClient disposes the shared transport.
    }
}
