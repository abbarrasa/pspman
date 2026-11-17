using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using PSPMan.Core.Devices;
using PSPMan.Core.Transfers;

namespace PSPMan.Infraestructure.Transfers;

internal static class WindowsCopyFileAccess
{
    internal static GameCopyException Failure(GameCopyErrorCode code, string message, Exception? cause = null) => new(new(code, message), cause);
    internal sealed class UnsafeTraversalException : Exception;

    internal static async ValueTask<IGameCopyDestination> OpenDestinationAsync(
        Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> inventory, CopyVolumeEvidence evidence,
        CopyFileAccessHooks hooks, CancellationToken token) =>
        new WindowsCopyDestination(await BoundRoot.OpenAsync(inventory, evidence, true, hooks, token).ConfigureAwait(false));

    internal static async ValueTask<IGameArchiveSource> OpenSourceAsync(
        Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> inventory, CopyVolumeEvidence evidence,
        string location, CopyFileAccessHooks hooks, CancellationToken token)
    {
        if (evidence.Syntax != MountedPathSyntax.Windows || MountedGamePathRules.Resolve(evidence.Root, location, evidence.Syntax).Path is null)
            throw Failure(GameCopyErrorCode.MissingArchive, "The selected archive requires a valid medium-rooted Windows location.");
        var root = await BoundRoot.OpenAsync(inventory, evidence, false, hooks, token).ConfigureAwait(false);
        try
        {
            var source = new WindowsArchiveSource(root, location[1..].Split('/'));
            await source.CaptureEvidenceAsync(token).ConfigureAwait(false);
            return source;
        }
        catch { await root.DisposeAsync().ConfigureAwait(false); throw; }
    }

    internal sealed class RootHandles : IDisposable
    {
        internal readonly List<SafeFileHandle> Handles = [];
        internal SafeFileHandle Root => Handles[^1];
        internal WindowsCopyNative.FileStat Stat = null!;
        public void Dispose() { for (int i = Handles.Count - 1; i >= 0; i--) Handles[i].Dispose(); }
    }

    private static RootHandles OpenRoot(string rootPath, string selectedVolume, CancellationToken token)
    {
        var result = new RootHandles();
        try
        {
            if (!WindowsCopyPathRules.TryVolumeGuidRoot(selectedVolume, out var expected))
                throw new IOException("The selected Windows observation has no canonical volume GUID.");
            var mapping = WindowsCopyNative.ResolveMount(rootPath);
            if (!WindowsCopyPathRules.TryVolumeGuidRoot(mapping.VolumePath, out var mapped) || mapped != expected)
                throw new IOException("The mounted path belongs to a different Windows GUID volume.");
            var normalized = rootPath.EndsWith('\\') ? rootPath : rootPath + "\\";
            if (!normalized.StartsWith(mapping.MountPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The observed root cannot be resolved below its selected mount point.");
            if (mapping.MountPath.Length > 3)
                result.Handles.Add(WindowsCopyNative.OpenSelectedMount(mapping.MountPath, expected));
            result.Handles.Add(WindowsCopyNative.OpenGuidVolumeRoot(selectedVolume));
            var drive = WindowsCopyNative.Describe(result.Root);
            if (drive.IsReparse || !drive.IsDirectory || !drive.Volume.StartsWith(expected.ToString("D") + ":", StringComparison.Ordinal))
                throw new UnsafeTraversalException();
            result.Stat = drive;
            foreach (var segment in normalized[mapping.MountPath.Length..].TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                token.ThrowIfCancellationRequested();
                var handle = WindowsCopyNative.OpenLeaf(result.Root, segment, WindowsCopyOpenMode.Directory, out var status);
                if (handle is null) ThrowOpen(status);
                result.Handles.Add(handle!);
                var stat = WindowsCopyNative.Describe(handle!);
                if (stat.IsReparse || stat.Volume != drive.Volume) throw new UnsafeTraversalException();
                if (!stat.IsDirectory) throw Failure(GameCopyErrorCode.FileDirectoryConflict, "A mounted root component is not a directory.");
                result.Stat = stat;
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal static void ThrowOpen(int status)
    {
        if (status is unchecked((int)0xc000050b) or unchecked((int)0x8000002d)) throw new UnsafeTraversalException();
        if (WindowsCopyNative.ClassifiedError(status) is { } code)
            throw Failure(code, code == GameCopyErrorCode.InvalidName ? "The filesystem rejects the Windows name." : "A file/directory conflict prevents access.",
                WindowsCopyNative.NtError("Open relative entry", status));
        throw WindowsCopyNative.NtError("Open relative entry", status);
    }

    internal sealed class BoundRoot : IAsyncDisposable
    {
        private readonly Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> _inventory;
        private readonly CopyVolumeEvidence _evidence;
        private readonly RootHandles _root;
        private readonly bool _destination;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<string, SafeFileHandle> _directories = [];
        private sealed record FilePin(SafeFileHandle Handle, Guid Token);
        private readonly Dictionary<string, FilePin> _files = [];
        private readonly List<SafeFileHandle> _retiredFiles = [];
        private readonly HashSet<CopyAccessStream> _streams = [];
        private readonly object _streamGate = new(), _disposeGate = new();
        private Task? _disposal;
        private bool _disposed;
        internal CopyFileAccessHooks Hooks { get; }
        internal string RootPath => _evidence.Root;
        internal SafeFileHandle Handle => _root.Root;
        internal WindowsCopyNative.FileStat Stat => _root.Stat;
        internal GameCopyErrorCode Changed => _destination ? GameCopyErrorCode.DestinationChanged : GameCopyErrorCode.SourceChanged;

        private BoundRoot(Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> inventory, CopyVolumeEvidence evidence,
            bool destination, CopyFileAccessHooks hooks, RootHandles root)
        { _inventory = inventory; _evidence = evidence; _destination = destination; Hooks = hooks; _root = root; }

        internal static async ValueTask<BoundRoot> OpenAsync(Func<CancellationToken, ValueTask<DeviceInventorySnapshot>> inventory,
            CopyVolumeEvidence evidence, bool destination, CopyFileAccessHooks hooks, CancellationToken token)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native Windows copy access requires Windows.");
            if (evidence.Syntax != MountedPathSyntax.Windows || MountedGamePathRules.Resolve(evidence.Root, "/probe", evidence.Syntax).Path is null)
                throw Failure(destination ? GameCopyErrorCode.DestinationUnavailable : GameCopyErrorCode.SourceUnavailable, "A valid Windows drive-rooted mount path is required.");
            await ValidateObservationAsync(inventory, evidence, destination, token).ConfigureAwait(false);
            RootHandles? handles = null;
            try
            {
                handles = OpenRoot(evidence.Root, evidence.Volume.VolumeId, token);
                var root = new BoundRoot(inventory, evidence, destination, hooks, handles);
                await root.ValidateCoreAsync(token).ConfigureAwait(false);
                return root;
            }
            catch (UnsafeTraversalException)
            {
                handles?.Dispose();
                throw Failure(destination ? GameCopyErrorCode.DestinationUnavailable : GameCopyErrorCode.SourceUnavailable,
                    "The selected root traverses a reparse point or a different volume.");
            }
            catch { handles?.Dispose(); throw; }
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
            try
            {
                using var current = OpenRoot(RootPath, _evidence.Volume.VolumeId, token);
                if (current.Stat.Identity != Stat.Identity || WindowsCopyNative.Describe(Handle).Identity != Stat.Identity)
                    throw Failure(Changed, "The mounted root no longer identifies the captured Windows volume and directory.");
            }
            catch (UnsafeTraversalException) { throw Failure(Changed, "The mounted root now traverses a reparse point or different volume."); }
            catch (IOException cause) { throw Failure(Changed, "The captured Windows mount is unavailable or changed: " + cause.Message, cause); }
        }

        internal ValueTask<T> RunAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken token) => new(Task.Run(async () =>
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try { await ValidateCoreAsync(token).ConfigureAwait(false); return await action(token).ConfigureAwait(false); }
            catch (IOException error) when (error is not FileNotFoundException)
            { throw Failure(_destination ? GameCopyErrorCode.WriteError : GameCopyErrorCode.ReadError, error.Message, error); }
            catch (UnauthorizedAccessException error)
            { throw Failure(_destination ? GameCopyErrorCode.WriteError : GameCopyErrorCode.ReadError, error.Message, error); }
            finally { _gate.Release(); }
        }, token));

        internal WindowsCopyNative.FileStat Describe(SafeFileHandle handle)
        {
            var stat = WindowsCopyNative.Describe(handle);
            if (stat.IsReparse) throw new UnsafeTraversalException();
            if (stat.Volume != Stat.Volume) throw Failure(Changed, "The opened entry belongs to a different Windows volume.");
            return stat;
        }

        internal (SafeFileHandle? Handle, string Evidence) Parent(string[] segments, bool create, CancellationToken token)
        {
            var parent = Handle;
            var identities = Stat.Identity;
            for (int count = 0; count < segments.Length - 1; count++)
            {
                token.ThrowIfCancellationRequested();
                // Inspect the leaf without a directory-only constraint first: a file symlink
                // used as a parent is still a link skip, not a plain-file structural failure.
                using (var metadata = WindowsCopyNative.OpenLeaf(parent, segments[count], WindowsCopyOpenMode.Metadata, out var probeStatus))
                {
                    if (metadata is not null && !Describe(metadata).IsDirectory)
                        throw Failure(GameCopyErrorCode.FileDirectoryConflict, "A parent entry is a file.");
                    if (metadata is null && !WindowsCopyNative.IsMissing(probeStatus)) ThrowOpen(probeStatus);
                }
                var opened = WindowsCopyNative.OpenLeaf(parent, segments[count], WindowsCopyOpenMode.Directory, out var status);
                if (opened is null && WindowsCopyNative.IsMissing(status) && create)
                {
                    Hooks.BeforeMutation?.Invoke("directory", string.Join('/', segments.Take(count + 1)));
                    ValidateCoreAsync(token).AsTask().GetAwaiter().GetResult();
                    _ = Describe(parent); // A held parent is never followed through a new reparse tag.
                    opened = WindowsCopyNative.OpenLeaf(parent, segments[count], WindowsCopyOpenMode.CreateDirectory, out status);
                    if (opened is null && status == WindowsCopyNative.Collision)
                        opened = WindowsCopyNative.OpenLeaf(parent, segments[count], WindowsCopyOpenMode.Directory, out status);
                }
                if (opened is null)
                {
                    if (WindowsCopyNative.IsMissing(status)) return (null, identities);
                    if (WindowsCopyNative.ClassifiedError(status) == GameCopyErrorCode.FileDirectoryConflict)
                    {
                        using var changed = WindowsCopyNative.OpenLeaf(parent, segments[count], WindowsCopyOpenMode.Metadata, out _);
                        if (changed is not null) _ = Describe(changed); // classify a late file link as a skip
                    }
                    ThrowOpen(status);
                }
                try
                {
                    var stat = Describe(opened!);
                    if (!stat.IsDirectory) throw Failure(GameCopyErrorCode.FileDirectoryConflict, "A destination parent is a file.");
                    if (_directories.TryGetValue(stat.Identity, out var retained)) { opened!.Dispose(); parent = retained; }
                    else { parent = opened!; _directories.Add(stat.Identity, opened!); }
                    opened = null;
                    identities += "/" + stat.Identity;
                }
                finally { opened?.Dispose(); }
            }
            return (parent, identities);
        }

        internal void PinFile(SafeFileHandle handle, string identity)
        {
            if (_files.TryGetValue(identity, out var retained))
            {
                bool stillSame;
                try { stillSame = WindowsCopyNative.Describe(retained.Handle).Identity == identity; }
                catch (IOException) { stillSame = false; }
                if (stillSame) { handle.Dispose(); return; }
                // FAT IDs can change on rename. Never let reuse of that directory-entry ID
                // authorize an old consent, even if bytes and coarse timestamps coincide.
                _retiredFiles.Add(retained.Handle);
            }
            _files[identity] = new(handle, Guid.NewGuid());
        }

        internal CopyFileEvidence FileEvidence(WindowsCopyNative.FileStat stat, string parents, string? hash = null)
        {
            if (!_files.TryGetValue(stat.Identity, out var pin))
                throw Failure(Changed, "The opened Windows file no longer has its observed identity.");
            return new(parents + "/" + stat.Identity + ":" + pin.Token.ToString("N"),
                stat.Length, stat.Change + (hash is null ? "" : ":" + hash));
        }

        internal string HashContent(SafeFileHandle handle, WindowsCopyNative.FileStat before, CancellationToken token)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long offset = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var read = RandomAccess.Read(handle, buffer, offset);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read); offset = checked(offset + read);
            }
            var after = Describe(handle);
            if (after.Identity != before.Identity || after.Change != before.Change || after.Length != before.Length)
                throw Failure(Changed, "The file changed while collecting Windows overwrite evidence.");
            token.ThrowIfCancellationRequested();
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        internal CopyAccessStream Own(Stream stream, string[] path, WindowsCopyNative.FileStat expected, bool writing)
        {
            var owned = new CopyAccessStream(stream, async token =>
            {
                await RunAsync(ct =>
                {
                    try
                    {
                        var (parent, _) = Parent(path, false, ct);
                        if (parent is null) throw Failure(Changed, "An open file parent is unavailable.");
                        using var current = WindowsCopyNative.OpenLeaf(parent, path[^1], WindowsCopyOpenMode.Metadata, out var status);
                        if (current is null) throw Failure(Changed, "The open file is unavailable.", WindowsCopyNative.NtError("Revalidate open file", status));
                        var stat = Describe(current);
                        if (stat.Identity != expected.Identity || !writing && (stat.Length != expected.Length || stat.Change != expected.Change))
                            throw Failure(Changed, "The open Windows file has changed.");
                    }
                    catch (UnsafeTraversalException) { throw Failure(Changed, "The open file path now traverses a reparse point."); }
                    return ValueTask.FromResult(true);
                }, token).ConfigureAwait(false);
            }, writing, released => { lock (_streamGate) _streams.Remove(released); });
            lock (_streamGate) _streams.Add(owned);
            return owned;
        }

        public ValueTask DisposeAsync() { lock (_disposeGate) return new(_disposal ??= DisposeCoreAsync()); }
        private async Task DisposeCoreAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { _disposed = true; } finally { _gate.Release(); }
            CopyAccessStream[] streams;
            lock (_streamGate) streams = _streams.ToArray();
            Exception? failure = null;
            foreach (var stream in streams)
                try { await stream.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure ??= error; }
            foreach (var file in _files.Values) file.Handle.Dispose();
            foreach (var file in _retiredFiles) file.Dispose();
            foreach (var directory in _directories.Values.Reverse()) directory.Dispose();
            _root.Dispose();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
