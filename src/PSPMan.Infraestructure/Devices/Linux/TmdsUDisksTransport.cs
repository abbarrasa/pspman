using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Tmds.DBus.Protocol;

namespace PSPMan.Infraestructure.Devices.Linux;

/// <summary>Minimal typed ObjectManager transport for the UDisks2 system bus.</summary>
public sealed class TmdsUDisksTransport : IUDisksTransport, IUDisksChangeSource
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

    public async ValueTask<string> MountAsync(
        string filesystemObjectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filesystemObjectPath);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var message = CreateMountMessage();
            var mountPoint = await _connection.CallMethodAsync(message,
                static (reply, _) => reply.GetBodyReader().ReadString()).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return mountPoint;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DBusErrorReplyException exception)
        {
            throw new UDisksTransportException(exception.ErrorName,
                exception.ErrorMessage, exception);
        }
        catch (Exception exception)
        {
            throw new UDisksTransportException("DbusError",
                "The UDisks2 mount call failed.", exception);
        }

        MessageBuffer CreateMountMessage()
        {
            using var writer = _connection.GetMessageWriter();
            writer.WriteMethodCallHeader(Destination, filesystemObjectPath,
                "org.freedesktop.UDisks2.Filesystem", "a{sv}", "Mount");
            var options = writer.WriteDictionaryStart();
            writer.WriteDictionaryEnd(options);
            return writer.CreateMessage();
        }
    }

    public async ValueTask UnmountAsync(
        string filesystemObjectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filesystemObjectPath);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var message = CreateUnmountMessage();
            await _connection.CallMethodAsync(message, static (_, _) => 0)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DBusErrorReplyException exception)
        {
            throw new UDisksTransportException(exception.ErrorName,
                exception.ErrorMessage, exception);
        }
        catch (Exception exception)
        {
            throw new UDisksTransportException("DbusError",
                "The UDisks2 unmount call failed.", exception);
        }

        MessageBuffer CreateUnmountMessage()
        {
            using var writer = _connection.GetMessageWriter();
            writer.WriteMethodCallHeader(Destination, filesystemObjectPath,
                "org.freedesktop.UDisks2.Filesystem", "a{sv}", "Unmount");
            var options = writer.WriteDictionaryStart();
            writer.WriteDictionaryEnd(options);
            return writer.CreateMessage();
        }
    }

    public ValueTask EjectAsync(
        string driveObjectPath,
        CancellationToken cancellationToken = default) =>
        InvokeDriveOperationAsync(driveObjectPath, "Eject", "eject", cancellationToken);

    public ValueTask PowerOffAsync(
        string driveObjectPath,
        CancellationToken cancellationToken = default) =>
        InvokeDriveOperationAsync(driveObjectPath, "PowerOff", "power-off", cancellationToken);

    private async ValueTask InvokeDriveOperationAsync(
        string driveObjectPath,
        string method,
        string operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driveObjectPath);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var message = CreateDriveOperationMessage();
            await _connection.CallMethodAsync(message, static (_, _) => 0).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DBusErrorReplyException exception)
        {
            throw new UDisksTransportException(exception.ErrorName,
                exception.ErrorMessage, exception);
        }
        catch (Exception exception)
        {
            throw new UDisksTransportException("DbusError",
                $"The UDisks2 {operation} call failed.", exception);
        }

        MessageBuffer CreateDriveOperationMessage()
        {
            using var writer = _connection.GetMessageWriter();
            writer.WriteMethodCallHeader(Destination, driveObjectPath,
                "org.freedesktop.UDisks2.Drive", "a{sv}", method);
            var options = writer.WriteDictionaryStart();
            writer.WriteDictionaryEnd(options);
            return writer.CreateMessage();
        }
    }

    public ValueTask DisposeAsync()
    {
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }

    public async ValueTask<IAsyncEnumerable<UDisksChange>> SubscribeChangesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var channel = Channel.CreateUnbounded<UDisksChange>();
        var observers = new List<IDisposable>();
        try
        {
            observers.Add(await _connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = ObjectManagerInterface,
                    Member = "InterfacesAdded"
                },
                static (message, _) => ReadObjectChange(message, UDisksChangeKind.InterfacesAdded),
                notification => Publish(channel, notification),
                emitOnCapturedContext: false).ConfigureAwait(false));
            observers.Add(await _connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = ObjectManagerInterface,
                    Member = "InterfacesRemoved"
                },
                static (message, _) => ReadObjectChange(message, UDisksChangeKind.InterfacesRemoved),
                notification => Publish(channel, notification),
                emitOnCapturedContext: false).ConfigureAwait(false));
            observers.Add(await _connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = "org.freedesktop.DBus.Properties",
                    Member = "PropertiesChanged"
                },
                static (message, _) => new UDisksChange(
                    UDisksChangeKind.PropertiesChanged, message.PathAsString ?? string.Empty),
                notification => Publish(channel, notification),
                emitOnCapturedContext: false).ConfigureAwait(false));
            cancellationToken.ThrowIfCancellationRequested();
            return ReadChangesAsync(channel, observers, cancellationToken);
        }
        catch
        {
            foreach (var observer in observers)
                observer.Dispose();
            channel.Writer.TryComplete();
            throw;
        }
    }

    private static void Publish(Channel<UDisksChange> channel, Notification<UDisksChange> notification)
    {
        if (notification.IsCompletion)
            channel.Writer.TryComplete(notification.Exception);
        else
            channel.Writer.TryWrite(notification.Value!);
    }

    private static UDisksChange ReadObjectChange(Message message, UDisksChangeKind kind)
    {
        var reader = message.GetBodyReader();
        return new UDisksChange(kind, reader.ReadObjectPath().ToString());
    }

    private static async IAsyncEnumerable<UDisksChange> ReadChangesAsync(
        Channel<UDisksChange> channel,
        IReadOnlyList<IDisposable> observers,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken))
                yield return change;
        }
        finally
        {
            foreach (var observer in observers)
                observer.Dispose();
            channel.Writer.TryComplete();
        }
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
