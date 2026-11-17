using PSPMan.Core.Devices;
using PSPMan.Core.Transfers;

namespace PSPMan.Infraestructure.Transfers;

/// <summary>Uses fresh observations from the caller-owned device provider; never mounts devices.</summary>
public sealed class FileGameCopyFileSystem : IGameCopyFileSystem
{
    private readonly Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> _getInventory;
    private readonly CopyFileAccessHooks _hooks;
    public FileGameCopyFileSystem(Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> getInventory)
        : this(getInventory, new()) { }
    internal FileGameCopyFileSystem(Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> getInventory,
        CopyFileAccessHooks hooks)
    { _getInventory = getInventory ?? throw new ArgumentNullException(nameof(getInventory)); _hooks = hooks; }

    public ValueTask<IGameArchiveSource> OpenSourceAsync(CopyVolumeEvidence source, string location,
        CancellationToken cancellationToken) => OpenAsync(async () =>
        {
            if (OperatingSystem.IsWindows())
                return await WindowsCopyFileAccess.OpenSourceAsync(_getInventory, source, location, _hooks, cancellationToken).ConfigureAwait(false);
            return await LinuxCopyFileAccess.OpenSourceAsync(_getInventory, source, location, _hooks, cancellationToken).ConfigureAwait(false);
        }, GameCopyErrorCode.SourceUnavailable, cancellationToken);
    public ValueTask<IGameCopyDestination> OpenDestinationAsync(CopyVolumeEvidence destination,
        CancellationToken cancellationToken) => OpenAsync(async () =>
        {
            if (OperatingSystem.IsWindows())
                return await WindowsCopyFileAccess.OpenDestinationAsync(_getInventory, destination, _hooks, cancellationToken).ConfigureAwait(false);
            return await LinuxCopyFileAccess.OpenDestinationAsync(_getInventory, destination, _hooks, cancellationToken).ConfigureAwait(false);
        }, GameCopyErrorCode.DestinationUnavailable, cancellationToken);

    private static ValueTask<T> OpenAsync<T>(Func<ValueTask<T>> open, GameCopyErrorCode errorCode, CancellationToken token) =>
        new(Task.Run(async () =>
        {
            try
            {
                if (!OperatingSystem.IsWindows()) LinuxCopyNative.RequirePlatform();
                return await open().ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or PlatformNotSupportedException or EntryPointNotFoundException)
            { throw LinuxCopyFileAccess.Failure(errorCode, error.Message, error); }
        }, token));
}

// Narrow internal seams for deterministic native mutation races and I/O failures, never device simulation in production.
internal sealed class CopyFileAccessHooks
{
    internal Action<string, string>? BeforeMutation { get; init; }
    internal Func<Stream, Stream>? WrapOutput { get; init; }
    internal Func<long?>? AvailableBytes { get; init; }
}
