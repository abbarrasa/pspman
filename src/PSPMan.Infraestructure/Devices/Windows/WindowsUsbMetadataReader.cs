using System.Collections.Immutable;
using System.Globalization;
using PSPMan.Core.Devices;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Native PnP evidence ordered from storage devnode toward its USB parent.</summary>
public sealed record WindowsPnpNode(string InstanceId, ImmutableArray<string> HardwareIds,
    ImmutableArray<string> CompatibleIds, bool HasUniqueId);

public sealed record WindowsUsbMetadata(string UsbDeviceInstanceId, ushort VendorId, ushort ProductId,
    string? Serial, ImmutableArray<UsbInterfaceDescriptor> Interfaces);

/// <summary>Decodes OS-provided USB IDs, never product labels, volume names or directory hints.</summary>
public static class WindowsUsbMetadataReader
{
    public static WindowsUsbMetadata? Read(ImmutableArray<WindowsPnpNode> ancestry)
    {
        var interfaces = new List<(ushort Vendor, ushort Product, UsbInterfaceDescriptor Interface)>();
        foreach (var node in ancestry)
        {
            var instance = ParseDeviceId(string.Join("\\", node.InstanceId.Split('\\').Take(2)));
            var parsed = node.HardwareIds.Select(ParseDeviceId).FirstOrDefault(value => value is not null) ?? instance;
            if (parsed is not null && instance is not null && parsed != instance)
                return null;
            if (parsed is { } descriptor)
            {
                foreach (var id in node.CompatibleIds)
                    if (ParseInterfaceId(id) is { } usbInterface)
                        interfaces.Add((descriptor.Vendor, descriptor.Product, usbInterface));
                if (!descriptor.IsFunction)
                {
                    var parts = node.InstanceId.Split('\\');
                    // UNIQUEID distinguishes a bus-supplied unique suffix from a port/location-generated one.
                    var serial = node.HasUniqueId && parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2])
                        && !parts[2].Contains('&') ? parts[2] : null;
                    return new WindowsUsbMetadata(node.InstanceId, descriptor.Vendor, descriptor.Product, serial,
                        interfaces.Where(item => item.Vendor == descriptor.Vendor && item.Product == descriptor.Product)
                            .Select(item => item.Interface).Distinct().ToImmutableArray());
                }
            }
            // Do not walk past an unreadable/malformed USB device and borrow its hub's descriptors.
            if (IsPhysicalUsbNode(node))
                return null;
        }
        return null;
    }

    internal static bool IsPhysicalUsbNode(WindowsPnpNode node) =>
        node.HardwareIds.Append(node.InstanceId).Any(id => id.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase)
            && !id.Split('\\')[1].Contains("&MI_", StringComparison.OrdinalIgnoreCase));

    private sealed record UsbId(ushort Vendor, ushort Product, bool IsFunction);

    private static UsbId? ParseDeviceId(string id)
    {
        var parts = id.Split('\\');
        if (parts.Length != 2 || !parts[0].Equals("USB", StringComparison.OrdinalIgnoreCase))
            return null;
        var fields = parts[1].Split('&');
        if (fields.Length < 2 || fields[0].Length != 8 || fields[1].Length != 8
            || !fields[0].StartsWith("VID_", StringComparison.OrdinalIgnoreCase)
            || !fields[1].StartsWith("PID_", StringComparison.OrdinalIgnoreCase)
            || !ushort.TryParse(fields[0].AsSpan(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vendor)
            || !ushort.TryParse(fields[1].AsSpan(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var product))
            return null;
        return new UsbId(vendor, product, fields.Any(field => field.StartsWith("MI_", StringComparison.OrdinalIgnoreCase)));
    }

    private static UsbInterfaceDescriptor? ParseInterfaceId(string id)
    {
        const string prefix = @"USB\Class_";
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var fields = id[4..].Split('&');
        if (fields.Length != 3 || fields[0].Length != 8 || fields[1].Length != 11 || fields[2].Length != 7
            || !fields[1].StartsWith("SubClass_", StringComparison.OrdinalIgnoreCase)
            || !fields[2].StartsWith("Prot_", StringComparison.OrdinalIgnoreCase)
            || !byte.TryParse(fields[0].AsSpan(6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var classCode)
            || !byte.TryParse(fields[1].AsSpan(9), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var subclass)
            || !byte.TryParse(fields[2].AsSpan(5), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var protocol))
            return null;
        return new UsbInterfaceDescriptor(classCode, subclass, protocol);
    }
}

/// <summary>Applies recognition and confidence to one refreshed inventory, tracking media generations only in memory.</summary>
internal sealed class WindowsUsbIdentityMapper
{
    private readonly Dictionary<string, (string Fingerprint, string Generation)> _media = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ambiguousIdentityIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public ImmutableArray<WindowsPhysicalDevice> Map(ImmutableArray<WindowsPhysicalDevice> devices, WindowsStorageDevice[] native)
    {
        var metadata = native.ToDictionary(device => device.InstanceId,
            device => WindowsUsbMetadataReader.Read(device.UsbAncestry), StringComparer.OrdinalIgnoreCase);
        var duplicateIds = metadata.Values.Where(item => item?.Serial is not null)
            .GroupBy(item => PersistentId(item!), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            // A known collision remains ambiguous for this session even after one duplicate disappears.
            _ambiguousIdentityIds.UnionWith(duplicateIds);
            var present = devices.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var missing in _media.Keys.Where(key => !present.Contains(key)).ToArray())
                _media.Remove(missing);
            return devices.Select(device =>
            {
                var usb = metadata[device.InstanceId];
                var fingerprint = string.Join("|", device.Volumes.OrderBy(volume => volume.VolumeGuidPath, StringComparer.OrdinalIgnoreCase)
                    .Select(volume => $"{volume.VolumeGuidPath?.ToUpperInvariant()}:{volume.FileSystemSerialNumber}:{volume.State}"));
                if (!_media.TryGetValue(device.InstanceId, out var previous) || previous.Fingerprint != fingerprint)
                    _media[device.InstanceId] = (fingerprint, Guid.NewGuid().ToString("N"));
                var identity = usb?.Serial is not null && !_ambiguousIdentityIds.Contains(PersistentId(usb))
                    ? new DeviceIdentity(PersistentId(usb), DeviceIdentityConfidence.Persistent)
                    : new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly);
                var recognition = usb is null ? null : PspUsbRecognition.Recognize(new PspUsbDescriptor(
                    usb.VendorId, usb.ProductId,
                    device.Volumes.Any(volume => volume.State == WindowsVolumeState.Available && volume.Error is null),
                    usb.Interfaces));
                var psp = recognition?.Status == PspUsbRecognitionStatus.Recognized;
                return device with
                {
                    UsbMetadata = usb, Identity = identity, PspRecognition = recognition,
                    Kind = psp ? WindowsDeviceKind.Psp : device.Kind,
                    IsSourceEligible = !psp && device.IsSourceEligible,
                    // A reader serial and a filesystem serial/GUID do not establish unique inserted-card identity.
                    MediaIdentity = new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly),
                    MediaGeneration = _media[device.InstanceId].Generation
                };
            }).ToImmutableArray();
        }
    }

    private static string PersistentId(WindowsUsbMetadata metadata) =>
        $"USB:{metadata.VendorId:X4}:{metadata.ProductId:X4}:{metadata.Serial!.ToUpperInvariant()}";
}
