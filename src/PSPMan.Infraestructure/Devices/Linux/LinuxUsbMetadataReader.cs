using System.Collections.Immutable;
using System.Globalization;

namespace PSPMan.Infraestructure.Devices.Linux;

public enum LinuxUsbIdentityConfidence
{
    None,
    Ambiguous,
    Reliable
}

public sealed record LinuxUsbInterfaceMetadata(byte ClassCode, byte SubclassCode, byte ProtocolCode);

public sealed record LinuxUsbMetadata(
    string StorageDevicePath,
    ushort? VendorId,
    ushort? ProductId,
    string? Serial,
    ImmutableArray<LinuxUsbInterfaceMetadata> Interfaces,
    string MediaGeneration,
    LinuxUsbIdentityConfidence IdentityConfidence,
    LinuxUsbIdentityConfidence MediaIdentityConfidence,
    string? ProductLabel = null,
    string? VolumeLabel = null);

/// <summary>Reads USB ancestry and persistent identity metadata from an injected sysfs root.</summary>
public sealed class LinuxUsbMetadataReader
{
    private readonly string _sysfsRoot;

    public LinuxUsbMetadataReader(string sysfsRoot)
    {
        if (string.IsNullOrWhiteSpace(sysfsRoot))
            throw new ArgumentException("A sysfs root is required.", nameof(sysfsRoot));
        _sysfsRoot = Path.GetFullPath(sysfsRoot);
    }

    public LinuxUsbMetadata Read(string storageDevicePath)
    {
        if (string.IsNullOrWhiteSpace(storageDevicePath)
            || !storageDevicePath.StartsWith("/dev/", StringComparison.Ordinal))
            throw new ArgumentException("The storage device path must be rooted below /dev.",
                nameof(storageDevicePath));

        var blockName = Path.GetFileName(storageDevicePath);
        var blockDirectory = Path.Combine(_sysfsRoot, "class", "block", blockName);
        var deviceDirectory = Path.Combine(blockDirectory, "device");
        if (!Directory.Exists(deviceDirectory))
            return Empty(storageDevicePath, deviceDirectory);

        var usbDevice = FindUsbDevice(deviceDirectory);
        var vendorId = ParseHex(ReadText(usbDevice, "idVendor"));
        var productId = ParseHex(ReadText(usbDevice, "idProduct"));
        var serial = ReadText(usbDevice, "serial");
        var interfaces = ReadInterfaces(deviceDirectory);
        var identityConfidence = vendorId.HasValue && productId.HasValue
            ? string.IsNullOrWhiteSpace(serial)
                ? LinuxUsbIdentityConfidence.Ambiguous
                : LinuxUsbIdentityConfidence.Reliable
            : LinuxUsbIdentityConfidence.None;

        return new LinuxUsbMetadata(
            storageDevicePath,
            vendorId,
            productId,
            serial,
            interfaces,
            Path.GetRelativePath(_sysfsRoot, deviceDirectory),
            identityConfidence,
            identityConfidence == LinuxUsbIdentityConfidence.None
                ? LinuxUsbIdentityConfidence.None
                : LinuxUsbIdentityConfidence.Ambiguous);
    }

    private LinuxUsbMetadata Empty(string storageDevicePath, string deviceDirectory) => new(
        storageDevicePath,
        null,
        null,
        null,
        ImmutableArray<LinuxUsbInterfaceMetadata>.Empty,
        Path.GetRelativePath(_sysfsRoot, deviceDirectory),
        LinuxUsbIdentityConfidence.None,
        LinuxUsbIdentityConfidence.None);

    private static string FindUsbDevice(string deviceDirectory)
    {
        var descendant = Directory.EnumerateDirectories(deviceDirectory, "*",
                SearchOption.AllDirectories)
            .FirstOrDefault(directory =>
                File.Exists(Path.Combine(directory, "idVendor"))
                || File.Exists(Path.Combine(directory, "idProduct")));
        if (descendant is not null)
            return descendant;

        var current = new DirectoryInfo(deviceDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "idVendor"))
                || File.Exists(Path.Combine(current.FullName, "idProduct")))
                return current.FullName;
            current = current.Parent;
        }
        return deviceDirectory;
    }

    private static ImmutableArray<LinuxUsbInterfaceMetadata> ReadInterfaces(string deviceDirectory)
    {
        var interfaces = new List<LinuxUsbInterfaceMetadata>();
        foreach (var directory in Directory.EnumerateDirectories(deviceDirectory, "*",
                     SearchOption.AllDirectories))
        {
            var classCode = ParseByte(ReadText(directory, "bInterfaceClass"));
            var subclassCode = ParseByte(ReadText(directory, "bInterfaceSubClass"));
            var protocolCode = ParseByte(ReadText(directory, "bInterfaceProtocol"));
            if (classCode.HasValue && subclassCode.HasValue && protocolCode.HasValue)
                interfaces.Add(new LinuxUsbInterfaceMetadata(classCode.Value, subclassCode.Value,
                    protocolCode.Value));
        }
        return interfaces.ToImmutableArray();
    }

    private static string? ReadText(string? directory, string fileName)
    {
        if (directory is null)
            return null;
        var path = Path.Combine(directory, fileName);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static ushort? ParseHex(string? value) =>
        ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static byte? ParseByte(string? value) =>
        byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
}
