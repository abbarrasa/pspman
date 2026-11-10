using System.Collections.Immutable;

namespace PSPMan.Infraestructure.Devices.Linux;

public interface IUDisksClient : IAsyncDisposable
{
    ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<string> MountAsync(string filesystemObjectPath,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This D-Bus client does not support mounting.");
}

public interface IUDisksTransport : IAsyncDisposable
{
    ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<string> MountAsync(string filesystemObjectPath,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This D-Bus transport does not support mounting.");
}

public sealed class UDisksObjectManagerSnapshot
{
    public UDisksObjectManagerSnapshot(IEnumerable<UDisksManagedObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Objects = objects.ToImmutableArray();
    }

    public ImmutableArray<UDisksManagedObject> Objects { get; }
}

public sealed class UDisksManagedObject
{
    public UDisksManagedObject(string objectPath, IEnumerable<UDisksInterfaceSnapshot> interfaces)
    {
        ObjectPath = objectPath;
        ArgumentNullException.ThrowIfNull(interfaces);
        Interfaces = interfaces.ToImmutableArray();
    }

    public string ObjectPath { get; }
    public ImmutableArray<UDisksInterfaceSnapshot> Interfaces { get; }

    public UDisksInterfaceSnapshot Interface(string name) =>
        Interfaces.Single(item => item.Name == name);
}

public sealed class UDisksInterfaceSnapshot
{
    public UDisksInterfaceSnapshot(string name, IReadOnlyDictionary<string, UDisksPropertyValue> properties)
    {
        Name = name;
        Properties = properties;
    }

    public string Name { get; }
    public IReadOnlyDictionary<string, UDisksPropertyValue> Properties { get; }

    public UDisksPropertyValue Property(string name) => Properties[name];
}

public enum UDisksPropertyType
{
    Boolean,
    String,
    Bytes,
    StringArray,
    ObjectPath,
    Unsupported
}

public sealed record UDisksPropertyValue(UDisksPropertyType Type, object Value)
{
    public static UDisksPropertyValue Boolean(bool value) => new(UDisksPropertyType.Boolean, value);
    public static UDisksPropertyValue String(string value) => new(UDisksPropertyType.String, value);
    public static UDisksPropertyValue Bytes(string value) => new(UDisksPropertyType.Bytes, value);
    public static UDisksPropertyValue StringArray(params string[] values) =>
        new(UDisksPropertyType.StringArray, values);
    public static UDisksPropertyValue ObjectPath(string value) =>
        new(UDisksPropertyType.ObjectPath, value);
    public static UDisksPropertyValue Unsupported(object value) =>
        new(UDisksPropertyType.Unsupported, value);

    public bool AsBoolean() => Get<bool>(UDisksPropertyType.Boolean);
    public string AsString() => Get<string>(UDisksPropertyType.String);
    public string AsBytes() => Get<string>(UDisksPropertyType.Bytes);
    public string AsObjectPath() => Get<string>(UDisksPropertyType.ObjectPath);
    public ImmutableArray<string> AsStringArray() =>
        Get<string[]>(UDisksPropertyType.StringArray).ToImmutableArray();

    private T Get<T>(UDisksPropertyType expected)
    {
        if (Type != expected || Value is not T typed)
            throw new InvalidOperationException($"D-Bus property has type {Type}, expected {expected}.");
        return typed;
    }
}

public sealed class UDisksTransportException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}

public sealed class UDisksClientException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}
