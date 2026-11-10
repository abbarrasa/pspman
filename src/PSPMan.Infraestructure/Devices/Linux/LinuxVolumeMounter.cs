namespace PSPMan.Infraestructure.Devices.Linux;

/// <summary>Mounts an unmounted Linux volume through UDisks2 and native authorization.</summary>
public sealed class LinuxVolumeMounter(IUDisksClient client)
{
    public async ValueTask<string> MountAsync(
        LinuxVolume volume,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volume);
        cancellationToken.ThrowIfCancellationRequested();

        var existingRoot = volume.MountPoints.FirstOrDefault(
            root => !string.IsNullOrWhiteSpace(root));
        if (existingRoot is not null)
            return existingRoot;

        var root = await client.MountAsync(volume.ObjectPath, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(root))
            throw new UDisksClientException("MalformedReply",
                "UDisks2 returned an empty mount root.");
        return root;
    }
}
