using System.Collections.Immutable;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using PSPMan.Core.Devices;
using PSPMan.Core.Transfers;

namespace PSPMan.Infraestructure.Transfers;

internal static class LinuxCopyFileAccess
{
    internal static async ValueTask<IGameCopyDestination> OpenDestinationAsync(
        Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> inventory, CopyVolumeEvidence evidence,
        CopyFileAccessHooks hooks, CancellationToken token)
    {
        var root = await BoundRoot.OpenAsync(inventory, evidence, true, hooks, token).ConfigureAwait(false);
        return new LinuxCopyDestination(root);
    }

    internal static async ValueTask<IGameArchiveSource> OpenSourceAsync(
        Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> inventory, CopyVolumeEvidence evidence,
        string location, CopyFileAccessHooks hooks, CancellationToken token)
    {
        var resolved = MountedGamePathRules.Resolve(evidence.Root, location, evidence.Syntax);
        if (resolved.Path is null) throw Failure(GameCopyErrorCode.MissingArchive, resolved.Error!);
        var root = await BoundRoot.OpenAsync(inventory, evidence, false, hooks, token).ConfigureAwait(false);
        try
        {
            var segments = location[1..].Split('/').ToImmutableArray();
            var source = new LinuxArchiveSource(root, segments);
            await source.CaptureEvidenceAsync(token).ConfigureAwait(false);
            return source;
        }
        catch { await root.DisposeAsync().ConfigureAwait(false); throw; }
    }

    internal static GameCopyException Failure(GameCopyErrorCode code, string message, Exception? cause = null) =>
        new(new(code, message), cause);

    internal sealed class UnsafeTraversalException : Exception;

    internal sealed class BoundRoot : IAsyncDisposable
    {
        private readonly Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> _inventory;
        private readonly CopyVolumeEvidence _evidence;
        private readonly bool _destination;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly List<SafeFileHandle> _pins = [];
        private readonly HashSet<string> _pinnedIds = [];
        private readonly HashSet<CopyAccessStream> _streams = [];
        private readonly object _streamGate = new();
        private readonly object _disposeGate = new();
        private Task? _disposal;
        private bool _disposed;
        internal SafeFileHandle Handle { get; }
        internal LinuxCopyNative.Stat Identity { get; }
        internal CopyFileAccessHooks Hooks { get; }
        internal string RootPath => _evidence.Root;
        internal CopyVolumeEvidence Evidence => _evidence;
        internal GameCopyErrorCode Changed => _destination ? GameCopyErrorCode.DestinationChanged : GameCopyErrorCode.SourceChanged;
        internal GameCopyErrorCode IoError => _destination ? GameCopyErrorCode.WriteError : GameCopyErrorCode.ReadError;

        private BoundRoot(Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> inventory,
            CopyVolumeEvidence evidence, bool destination, CopyFileAccessHooks hooks, SafeFileHandle handle)
        { _inventory = inventory; _evidence = evidence; _destination = destination; Hooks = hooks;
            Handle = handle; Identity = LinuxCopyNative.Describe(handle); }

        internal static async ValueTask<BoundRoot> OpenAsync(Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> inventory,
            CopyVolumeEvidence evidence, bool destination, CopyFileAccessHooks hooks, CancellationToken token)
        {
            if (evidence.Syntax != MountedPathSyntax.Linux || MountedGamePathRules.Resolve(evidence.Root, "/probe", evidence.Syntax).Path is null)
                throw Failure(destination ? GameCopyErrorCode.DestinationUnavailable : GameCopyErrorCode.SourceUnavailable, "A valid Linux mounted root is required.");
            await ValidateObservationAsync(inventory, evidence, destination, token).ConfigureAwait(false);
            var handle = LinuxCopyNative.TryOpen(-100, evidence.Root, LinuxCopyNative.Directory, out var error, root: true);
            if (handle is null) throw Failure(destination ? GameCopyErrorCode.DestinationUnavailable : GameCopyErrorCode.SourceUnavailable,
                "The selected mounted root cannot be opened safely.", LinuxCopyNative.Error("openat2", error));
            try
            {
                var root = new BoundRoot(inventory, evidence, destination, hooks, handle);
                await root.ValidateCoreAsync(token).ConfigureAwait(false);
                return root;
            }
            catch { handle.Dispose(); throw; }
        }

        private static async ValueTask ValidateObservationAsync(Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> inventory,
            CopyVolumeEvidence evidence, bool destination, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = await inventory(token).ConfigureAwait(false);
            var device = snapshot.Devices.SingleOrDefault(item => item.Key == evidence.Device);
            var volume = device?.Volumes.SingleOrDefault(item => item.Key == evidence.Volume);
            if (device is null || volume is null || evidence.Volume.Device != evidence.Device
                || device.Identity != evidence.DeviceIdentity || volume.Identity != evidence.VolumeIdentity
                || device.MediaGeneration != evidence.MediaGeneration || device.State != DeviceState.Ready
                || volume.State != DeviceState.Ready || volume.Root != evidence.Root)
                throw Failure(destination ? GameCopyErrorCode.DestinationChanged : GameCopyErrorCode.SourceChanged,
                    "The selected device, volume, media or mounted root has changed or is unavailable.");
            token.ThrowIfCancellationRequested();
        }

        internal async ValueTask ValidateCoreAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await ValidateObservationAsync(_inventory, _evidence, _destination, token).ConfigureAwait(false);
            using var current = LinuxCopyNative.TryOpen(-100, _evidence.Root, LinuxCopyNative.Directory, out var error, root: true);
            if (current is null || LinuxCopyNative.Describe(current).Identity != Identity.Identity
                || LinuxCopyNative.Describe(Handle).Identity != Identity.Identity)
                throw Failure(Changed, "The mounted root no longer identifies the captured volume.",
                    current is null ? LinuxCopyNative.Error("openat2", error) : null);
        }

        internal ValueTask<T> RunAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken token) =>
            new(Task.Run(async () =>
            {
                await _gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await ValidateCoreAsync(token).ConfigureAwait(false);
                    return await action(token).ConfigureAwait(false);
                }
                catch (IOException error) when (error is not FileNotFoundException) { throw Failure(IoError, error.Message, error); }
                catch (UnauthorizedAccessException error) { throw Failure(IoError, error.Message, error); }
                finally { _gate.Release(); }
            }, token));

        internal SafeFileHandle? Open(string relative, int flags, out int error) =>
            LinuxCopyNative.TryOpen(Handle.DangerousGetHandle().ToInt32(), relative, flags, out error);

        // Open each full prefix beneath the original root, never relative to a moved parent.
        internal (SafeFileHandle? Handle, string Evidence) Parent(string[] segments, bool create, CancellationToken token)
        {
            SafeFileHandle? parent = null;
            var identities = Identity.Identity;
            try
            {
                for (int count = 1; count < segments.Length; count++)
                {
                    token.ThrowIfCancellationRequested();
                    var relative = string.Join('/', segments.Take(count));
                    var next = Open(relative, LinuxCopyNative.PathOnly, out var error);
                    if (next is null && error == LinuxCopyNative.Missing && create)
                    {
                        Hooks.BeforeMutation?.Invoke("directory", relative);
                        ValidateCoreAsync(token).AsTask().GetAwaiter().GetResult();
                        // Reopen and compare the full parent chain after any wait/race hook.
                        using var fresh = Open(count == 1 ? "." : string.Join('/', segments.Take(count - 1)),
                            LinuxCopyNative.PathOnly, out error);
                        if (fresh is null) ThrowOpen(error);
                        var freshStat = LinuxCopyNative.Describe(fresh!);
                        if (freshStat.IsLink) throw new UnsafeTraversalException();
                        if (!freshStat.IsDirectory) throw Failure(GameCopyErrorCode.FileDirectoryConflict, "A destination parent became a file.");
                        if (freshStat.Identity != (parent is null ? Identity.Identity : LinuxCopyNative.Describe(parent).Identity))
                            throw Failure(Changed, "The destination parent changed before directory creation.");
                        token.ThrowIfCancellationRequested();
                        LinuxCopyNative.MakeDirectory(fresh!, segments[count - 1]);
                        next = Open(relative, LinuxCopyNative.PathOnly, out error);
                    }
                    if (next is null)
                    {
                        if (error == LinuxCopyNative.Missing) { parent?.Dispose(); return (null, identities); }
                        ThrowOpen(error);
                    }
                    var stat = LinuxCopyNative.Describe(next!);
                    if (stat.IsLink) { next!.Dispose(); throw new UnsafeTraversalException(); }
                    if (!stat.IsDirectory) { next!.Dispose(); throw Failure(GameCopyErrorCode.FileDirectoryConflict, "A parent entry is a file, not a directory."); }
                    parent?.Dispose(); parent = next;
                    identities += "/" + stat.Identity;
                }
                parent ??= Open(".", LinuxCopyNative.PathOnly | LinuxCopyNative.Directory, out _)
                    ?? throw Failure(Changed, "The captured root is unavailable.");
                return (parent, identities);
            }
            catch { parent?.Dispose(); throw; }
        }

        internal void ThrowOpen(int error)
        {
            if (error is LinuxCopyNative.Loop or LinuxCopyNative.CrossDevice) throw new UnsafeTraversalException();
            if (error == 36 || error == 22) throw Failure(GameCopyErrorCode.InvalidName, "The filesystem rejects the entry name.", LinuxCopyNative.Error("openat2", error));
            if (error is LinuxCopyNative.NotDirectory or 21) throw Failure(GameCopyErrorCode.FileDirectoryConflict, "A file/directory conflict prevents opening the entry.");
            throw LinuxCopyNative.Error("openat2", error);
        }

        internal CopyFileEvidence FileEvidence(LinuxCopyNative.Stat stat, string parents, string? hash = null) =>
            new(parents + "/" + stat.Identity, stat.Length, stat.Change + (hash is null ? "" : ":" + hash));

        internal string HashContent(SafeFileHandle handle, LinuxCopyNative.Stat expected, CancellationToken token)
        {
            // FAT timestamp granularity must not authorize an undetected same-size content change.
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long offset = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var read = RandomAccess.Read(handle, buffer, offset);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                offset = checked(offset + read);
            }
            var current = LinuxCopyNative.Describe(handle);
            if (current.Identity != expected.Identity || current.Change != expected.Change || current.Length != expected.Length)
                throw Failure(Changed, "The file changed while collecting overwrite evidence.");
            token.ThrowIfCancellationRequested();
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        internal void Pin(SafeFileHandle handle, string identity)
        {
            // Keep consent identities alive so inode reuse cannot manufacture a matching observation.
            if (_pinnedIds.Add(identity)) _pins.Add(handle);
            else handle.Dispose();
        }

        internal CopyAccessStream Own(Stream stream, string relative, LinuxCopyNative.Stat expected, bool writing)
        {
            var owned = new CopyAccessStream(stream, async token =>
            {
                await RunAsync(async ct =>
                {
                    using var current = Open(relative, LinuxCopyNative.PathOnly, out var error);
                    var actual = current is null ? (LinuxCopyNative.Stat?)null : LinuxCopyNative.Describe(current);
                    if (actual is null || actual.Value.Identity != expected.Identity
                        || !writing && (actual.Value.Length != expected.Length || actual.Value.Change != expected.Change))
                        throw Failure(Changed, "The open file path or its parent changed.", current is null ? LinuxCopyNative.Error("openat2", error) : null);
                    await ValueTask.CompletedTask;
                    return true;
                }, token).ConfigureAwait(false);
            }, writing, released => { lock (_streamGate) _streams.Remove(released); });
            lock (_streamGate) _streams.Add(owned);
            return owned;
        }

        public ValueTask DisposeAsync()
        { lock (_disposeGate) return new(_disposal ??= DisposeCoreAsync()); }
        private async Task DisposeCoreAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { _disposed = true; }
            finally { _gate.Release(); }
            CopyAccessStream[] streams;
            lock (_streamGate) streams = _streams.ToArray();
            Exception? failure = null;
            foreach (var stream in streams)
                try { await stream.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure ??= error; }
            foreach (var pin in _pins) pin.Dispose();
            Handle.Dispose();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
