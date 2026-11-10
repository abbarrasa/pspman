using System.Collections.Immutable;

namespace PSPMan.Infraestructure.Devices.Linux;

public enum LinuxDeviceKind
{
    Usb,
    Optical,
    MemoryCard
}

public sealed class LinuxDeviceInventory(IEnumerable<LinuxPhysicalDevice> devices)
{
    public ImmutableArray<LinuxPhysicalDevice> Devices { get; } =
        devices?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(devices));
}

public sealed class LinuxPhysicalDevice
{
    public required string ObjectPath { get; init; }
    public required LinuxDeviceKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsSourceEligible { get; init; }
    public required bool IsEjectable { get; init; }
    public required ImmutableArray<LinuxVolume> Volumes { get; init; }
}

public sealed class LinuxVolume
{
    public required string ObjectPath { get; init; }
    public required string DevicePath { get; init; }
    public required string? Label { get; init; }
    public required ImmutableArray<string> MountPoints { get; init; }
}

/// <summary>Normalizes typed UDisks2 objects into removable-device inventory data.</summary>
public sealed class LinuxDeviceProvider(IUDisksClient client) : IAsyncDisposable
{
    public async ValueTask<LinuxDeviceInventory> GetInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var snapshot = await client.GetManagedObjectsAsync(cancellationToken).ConfigureAwait(false);
        var drives = snapshot.Objects
            .Select(TryReadDrive)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToDictionary(item => item.ObjectPath, StringComparer.Ordinal);

        foreach (var block in snapshot.Objects)
        {
            if (!TryReadBlock(block, out var drivePath, out var volume))
                continue;
            if (drives.TryGetValue(drivePath, out var drive))
                drive.VolumesBuilder.Add(volume);
        }

        return new LinuxDeviceInventory(drives.Values.Select(item => item.Build()));
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();

    private static DriveBuilder? TryReadDrive(UDisksManagedObject item)
    {
        var drive = item.Interfaces.FirstOrDefault(
            @interface => @interface.Name == "org.freedesktop.UDisks2.Drive");
        if (drive is null || !TryBoolean(drive, "Removable", out var removable) || !removable)
            return null;

        var compatibility = TryStrings(drive, "MediaCompatibility");
        var bus = TryString(drive, "ConnectionBus");
        var kind = compatibility.Any(IsOpticalCompatibility)
            ? LinuxDeviceKind.Optical
            : string.Equals(bus, "usb", StringComparison.OrdinalIgnoreCase)
                ? LinuxDeviceKind.Usb
                : LinuxDeviceKind.MemoryCard;
        var vendor = TryString(drive, "Vendor");
        var model = TryString(drive, "Model");
        var displayName = string.Join(" ", new[] { vendor, model }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        return new DriveBuilder
        {
            ObjectPath = item.ObjectPath,
            Kind = kind,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Removable device" : displayName,
            IsEjectable = TryBoolean(drive, "Ejectable", out var ejectable) && ejectable
        };
    }

    private static bool TryReadBlock(
        UDisksManagedObject item, out string drivePath, out LinuxVolume volume)
    {
        drivePath = string.Empty;
        volume = null!;
        var block = item.Interfaces.FirstOrDefault(
            @interface => @interface.Name == "org.freedesktop.UDisks2.Block");
        if (block is null || !TryObjectPath(block, "Drive", out drivePath)
            || !TryBytes(block, "Device", out var devicePath))
            return false;

        var filesystem = item.Interfaces.FirstOrDefault(
            @interface => @interface.Name == "org.freedesktop.UDisks2.Filesystem");
        volume = new LinuxVolume
        {
            ObjectPath = item.ObjectPath,
            DevicePath = devicePath,
            Label = TryString(block, "IdLabel"),
            MountPoints = filesystem is null
                ? ImmutableArray<string>.Empty
                : TryStrings(filesystem, "MountPoints").ToImmutableArray()
        };
        return true;
    }

    private static bool IsOpticalCompatibility(string value) =>
        value.Contains("optical", StringComparison.OrdinalIgnoreCase)
        || value.Contains("cd", StringComparison.OrdinalIgnoreCase)
        || value.Contains("dvd", StringComparison.OrdinalIgnoreCase);

    private static string? TryString(UDisksInterfaceSnapshot item, string name) =>
        item.Properties.TryGetValue(name, out var value) && value.Type == UDisksPropertyType.String
            ? value.AsString()
            : null;

    private static bool TryObjectPath(UDisksInterfaceSnapshot item, string name, out string value)
    {
        if (item.Properties.TryGetValue(name, out var property)
            && property.Type == UDisksPropertyType.ObjectPath)
        {
            value = property.AsObjectPath();
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryBytes(UDisksInterfaceSnapshot item, string name, out string value)
    {
        if (item.Properties.TryGetValue(name, out var property)
            && property.Type == UDisksPropertyType.Bytes)
        {
            value = property.AsBytes();
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryBoolean(UDisksInterfaceSnapshot item, string name, out bool value)
    {
        if (item.Properties.TryGetValue(name, out var property)
            && property.Type == UDisksPropertyType.Boolean)
        {
            value = property.AsBoolean();
            return true;
        }
        value = false;
        return false;
    }

    private static ImmutableArray<string> TryStrings(UDisksInterfaceSnapshot item, string name) =>
        item.Properties.TryGetValue(name, out var property)
            && property.Type == UDisksPropertyType.StringArray
            ? property.AsStringArray()
            : ImmutableArray<string>.Empty;

    private sealed class DriveBuilder
    {
        public required string ObjectPath { get; init; }
        public required LinuxDeviceKind Kind { get; init; }
        public required string DisplayName { get; init; }
        public required bool IsEjectable { get; init; }
        public List<LinuxVolume> VolumesBuilder { get; } = [];

        public LinuxPhysicalDevice Build() => new()
        {
            ObjectPath = ObjectPath,
            Kind = Kind,
            DisplayName = DisplayName,
            IsSourceEligible = true,
            IsEjectable = IsEjectable,
            Volumes = VolumesBuilder.ToImmutableArray()
        };
    }
}
