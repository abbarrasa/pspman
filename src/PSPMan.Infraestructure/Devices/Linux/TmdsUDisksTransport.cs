using Tmds.DBus.Protocol;

namespace PSPMan.Infraestructure.Devices.Linux;

/// <summary>Minimal typed ObjectManager transport for the UDisks2 system bus.</summary>
public sealed class TmdsUDisksTransport : IUDisksTransport
{
    private const string Destination = "org.freedesktop.UDisks2";
    private const string ObjectManagerPath = "/org/freedesktop/UDisks2";
    private const string ObjectManagerInterface = "org.freedesktop.DBus.ObjectManager";
    private readonly DBusConnection _connection;

    private TmdsUDisksTransport(DBusConnection connection) => _connection = connection;

    public static async ValueTask<TmdsUDisksTransport> ConnectSystemAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DBusAddress.System is not { } address)
            throw new UDisksTransportException("ServiceUnavailable", "The system D-Bus address is unavailable.");
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new TmdsUDisksTransport(connection);
        }
        catch (OperationCanceledException)
        {
            connection.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            connection.Dispose();
            throw new UDisksTransportException("ServiceUnavailable", "The UDisks2 service is unavailable.", exception);
        }
    }

    public async ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var message = CreateGetManagedObjectsMessage();
            var snapshot = await _connection.CallMethodAsync(message, static (reply, _) =>
                ReadManagedObjects(reply.GetBodyReader())).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new UDisksTransportException("DbusError", "The UDisks2 ObjectManager call failed.", exception);
        }

        MessageBuffer CreateGetManagedObjectsMessage()
        {
            using var writer = _connection.GetMessageWriter();
            writer.WriteMethodCallHeader(Destination, ObjectManagerPath, ObjectManagerInterface, "",
                "GetManagedObjects");
            return writer.CreateMessage();
        }
    }

    public ValueTask DisposeAsync()
    {
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }

    private static UDisksObjectManagerSnapshot ReadManagedObjects(Reader reader)
    {
        var objects = new List<UDisksManagedObject>();
        var objectEnd = reader.ReadDictionaryStart();
        while (reader.HasNext(objectEnd))
        {
            var objectPath = reader.ReadObjectPath().ToString();
            var interfaces = new List<UDisksInterfaceSnapshot>();
            var interfaceEnd = reader.ReadDictionaryStart();
            while (reader.HasNext(interfaceEnd))
            {
                var interfaceName = reader.ReadString();
                var properties = new Dictionary<string, UDisksPropertyValue>(StringComparer.Ordinal);
                var propertyEnd = reader.ReadDictionaryStart();
                while (reader.HasNext(propertyEnd))
                {
                    var propertyName = reader.ReadString();
                    properties[propertyName] = ReadProperty(propertyName, reader.ReadVariantValue());
                }
                interfaces.Add(new UDisksInterfaceSnapshot(interfaceName, properties));
            }
            objects.Add(new UDisksManagedObject(objectPath, interfaces));
        }
        return new UDisksObjectManagerSnapshot(objects);
    }

    private static UDisksPropertyValue ReadProperty(string name, VariantValue value)
    {
        if (value.Type == VariantValueType.Bool)
            return UDisksPropertyValue.Boolean(value.GetBool());
        if (value.Type == VariantValueType.String)
            return UDisksPropertyValue.String(value.GetString());
        if (value.Type == VariantValueType.ObjectPath)
            return UDisksPropertyValue.ObjectPath(value.GetObjectPath().ToString());
        if (value.Type == VariantValueType.Array && value.ItemType == VariantValueType.Byte)
            return UDisksPropertyValue.Bytes(System.Text.Encoding.UTF8.GetString(value.GetArray<byte>()));
        if (value.Type == VariantValueType.Array && value.ItemType == VariantValueType.Array
            && value.ValueType == VariantValueType.Byte)
        {
            return UDisksPropertyValue.StringArray(value.GetArray<byte[]>()
                .Select(bytes => System.Text.Encoding.UTF8.GetString(bytes)).ToArray());
        }
        return UDisksPropertyValue.Unsupported(name);
    }
}
