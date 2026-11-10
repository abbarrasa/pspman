namespace PSPMan.Infraestructure.Devices.Linux;

/// <summary>
/// Validates typed UDisks ObjectManager data from the D-Bus transport. It performs no
/// localization, filesystem probing or physical-device normalization.
/// </summary>
public sealed class UDisksClient(IUDisksTransport transport) : IUDisksClient
{
    public async ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        try
        {
            var snapshot = await transport.GetManagedObjectsAsync(cancellationToken).ConfigureAwait(false);
            Validate(snapshot);
            return snapshot;
        }
        catch (UDisksClientException)
        {
            throw;
        }
        catch (UDisksTransportException exception)
        {
            throw new UDisksClientException(exception.Code, exception.Message, exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new UDisksClientException("MalformedReply", "The UDisks2 reply was malformed.", exception);
        }
    }

    public async ValueTask<string> MountAsync(
        string filesystemObjectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filesystemObjectPath);
        try
        {
            return await transport.MountAsync(filesystemObjectPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (UDisksClientException)
        {
            throw;
        }
        catch (UDisksTransportException exception)
        {
            throw new UDisksClientException(exception.Code, exception.Message, exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new UDisksClientException("MountFailed", "The UDisks2 mount call failed.", exception);
        }
    }

    public ValueTask DisposeAsync() => transport.DisposeAsync();

    private static void Validate(UDisksObjectManagerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var item in snapshot.Objects)
        {
            if (string.IsNullOrWhiteSpace(item.ObjectPath)
                || !item.ObjectPath.StartsWith("/org/freedesktop/UDisks2/", StringComparison.Ordinal))
                throw new UDisksClientException("MalformedReply", "The UDisks2 reply contains an invalid object path.");
            if (item.Interfaces.IsDefaultOrEmpty)
                throw new UDisksClientException("MalformedReply", "The UDisks2 object has no interfaces.");
            foreach (var @interface in item.Interfaces)
            {
                if (string.IsNullOrWhiteSpace(@interface.Name) || @interface.Properties is null)
                    throw new UDisksClientException("MalformedReply", "The UDisks2 interface is malformed.");
                foreach (var property in @interface.Properties)
                    if (property.Value is null)
                        throw new UDisksClientException("MalformedReply", "The UDisks2 property has no value.");
            }
        }
    }
}
