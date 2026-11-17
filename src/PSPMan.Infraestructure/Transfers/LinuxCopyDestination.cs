using System.Collections.Immutable;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PSPMan.Core.Transfers;
using static PSPMan.Infraestructure.Transfers.LinuxCopyFileAccess;

namespace PSPMan.Infraestructure.Transfers;

internal sealed class LinuxCopyDestination(BoundRoot root) : IGameCopyDestination
{
    internal string RootIdentity => root.Identity.Identity;
    internal CopyVolumeEvidence VolumeEvidence => root.Evidence;
    internal ValueTask<long?> SampleSettledCapacityAsync(CancellationToken token) => root.RunAsync(_ =>
    {
        LinuxCopyNative.Synchronize(root.Handle);
        return ValueTask.FromResult(root.Hooks.AvailableBytes is { } sample ? sample() : LinuxCopyNative.AvailableBytes(root.Handle));
    }, token);

    internal sealed record Target(string Identity, bool Created, string? InitialChange);

    // Called only under the resource owner's short namespace guard, after size admission.
    // Exclusively create a missing leaf and register its inode before exposing an output.
    // This also resolves case/short aliases on the mounted filesystem without a string comparer.
    internal ValueTask<Target> ClaimTargetAsync(ImmutableArray<string> segments, bool directory, CancellationToken token) =>
        root.RunAsync(ct =>
        {
            if (!IsSafe(segments, allowEmpty: directory))
                return ValueTask.FromResult(new Target("unsafe:" + Guid.NewGuid(), false, null));
            try
            {
                var full = Full(segments);
                var (parent, _) = root.Parent(full, true, ct);
                using (parent)
                {
                    var relative = string.Join('/', full);
                    var handle = root.Open(relative, LinuxCopyNative.PathOnly, out var error);
                    var created = false;
                    if (handle is null && error == LinuxCopyNative.Missing)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (directory)
                        {
                            var (createdDirectory, _) = root.Parent(full.Append("unused-child").ToArray(), true, ct);
                            createdDirectory?.Dispose();
                        }
                        else
                        {
                            root.Hooks.BeforeMutation?.Invoke("claim", relative);
                            root.ValidateCoreAsync(ct).AsTask().GetAwaiter().GetResult();
                            handle = root.Open(relative, LinuxCopyNative.ReadWrite | LinuxCopyNative.Create | LinuxCopyNative.Exclusive, out error);
                            created = handle is not null;
                            if (handle is null && error != LinuxCopyNative.Exists) root.ThrowOpen(error);
                        }
                        handle ??= root.Open(relative, LinuxCopyNative.PathOnly, out error);
                    }
                    if (handle is null) root.ThrowOpen(error);
                    try
                    {
                        var stat = LinuxCopyNative.Describe(handle!);
                        var result = new Target(stat.Identity, created, created ? stat.Change : null);
                        root.Pin(handle!, stat.Identity); handle = null;
                        return ValueTask.FromResult(result);
                    }
                    finally { handle?.Dispose(); }
                }
            }
            catch (UnsafeTraversalException) { return ValueTask.FromResult(new Target("unsafe:" + Guid.NewGuid(), false, null)); }
        }, token);

    public string DestinationPath => Path.Combine(root.RootPath, "ISO");
    public async ValueTask ValidateAsync(CancellationToken cancellationToken) =>
        _ = await root.RunAsync(_ => ValueTask.FromResult(true), cancellationToken).ConfigureAwait(false);
    public ValueTask<long?> GetAvailableBytesAsync(CancellationToken cancellationToken) =>
        root.RunAsync(_ =>
        {
            try { return ValueTask.FromResult(root.Hooks.AvailableBytes is { } available ? available() : LinuxCopyNative.AvailableBytes(root.Handle)); }
            catch (IOException error) { throw Failure(GameCopyErrorCode.UnknownFreeSpace, error.Message, error); }
        }, cancellationToken);

    internal static bool IsSafe(ImmutableArray<string> segments, bool allowEmpty = false)
    {
        if (segments.IsDefaultOrEmpty) return allowEmpty;
        if (segments[0] is { Length: >= 2 } first && char.IsAsciiLetter(first[0]) && first[1] == ':') return false;
        foreach (var segment in segments)
        {
            if (segment is null || segment.Length == 0 || segment is "." or ".."
                || segment.Contains('/') || segment.Contains('\\')) return false;
            if (segment.Contains('\0')) throw Failure(GameCopyErrorCode.InvalidName, "A filename contains a null character.");
            try { _ = new UTF8Encoding(false, true).GetByteCount(segment); }
            catch (EncoderFallbackException error) { throw Failure(GameCopyErrorCode.InvalidName, "A filename contains invalid Unicode.", error); }
        }
        return true;
    }

    private static string[] Full(ImmutableArray<string> segments) => segments.IsDefaultOrEmpty ? ["ISO"] : ["ISO", .. segments];

    private CopyTargetSnapshot InspectCore(string[] segments, CancellationToken token)
    {
        var (parent, parents) = root.Parent(segments, false, token);
        using (parent)
        {
            if (parent is null) return new(CopyTargetKind.Missing, null);
            var handle = root.Open(string.Join('/', segments), LinuxCopyNative.PathOnly, out var error);
            if (handle is null)
            {
                if (error == LinuxCopyNative.Missing) return new(CopyTargetKind.Missing, null);
                root.ThrowOpen(error);
            }
            try
            {
                var stat = LinuxCopyNative.Describe(handle!);
                if (stat.IsLink || stat.Links > 1 && stat.IsRegular) return new(CopyTargetKind.File, null, IsConfined: false);
                if (stat.IsDirectory) return new(CopyTargetKind.Directory, null);
                if (!stat.IsRegular) throw Failure(GameCopyErrorCode.FileDirectoryConflict, "The destination is not a regular file or directory.");
                using var readable = root.Open(string.Join('/', segments), LinuxCopyNative.NonBlock, out error);
                if (readable is null) root.ThrowOpen(error);
                var openedStat = LinuxCopyNative.Describe(readable!);
                if (openedStat.Identity != stat.Identity || openedStat.Change != stat.Change || openedStat.Length != stat.Length)
                    throw Failure(root.Changed, "The destination changed while opening overwrite evidence.");
                var evidence = root.FileEvidence(stat, parents, root.HashContent(readable!, stat, token));
                root.Pin(handle!, evidence.Identity); handle = null;
                return new(CopyTargetKind.File, evidence);
            }
            finally { handle?.Dispose(); }
        }
    }

    public ValueTask<CopyTargetSnapshot> InspectAsync(ImmutableArray<string> segments, CancellationToken cancellationToken) =>
        root.RunAsync(token =>
        {
            if (!IsSafe(segments, allowEmpty: true)) return ValueTask.FromResult(new CopyTargetSnapshot(CopyTargetKind.Missing, null, IsConfined: false));
            try { return ValueTask.FromResult(InspectCore(Full(segments), token)); }
            catch (UnsafeTraversalException) { return ValueTask.FromResult(new CopyTargetSnapshot(CopyTargetKind.Missing, null, IsConfined: false)); }
        }, cancellationToken);

    public ValueTask<GameCopySkipReason?> EnsureDirectoryAsync(ImmutableArray<string> segments, CancellationToken cancellationToken) =>
        root.RunAsync<GameCopySkipReason?>(token =>
        {
            if (!IsSafe(segments, allowEmpty: true)) return ValueTask.FromResult<GameCopySkipReason?>(GameCopySkipReason.UnsafePath);
            try
            {
                // Treat the directory as a parent, so even empty entries go through identical no-follow traversal.
                var (parent, _) = root.Parent(Full(segments).Append("unused-child").ToArray(), true, token);
                parent?.Dispose();
                return ValueTask.FromResult<GameCopySkipReason?>(null);
            }
            catch (UnsafeTraversalException) { return ValueTask.FromResult<GameCopySkipReason?>(GameCopySkipReason.Link); }
        }, cancellationToken);

    public ValueTask<CopyOutputOpenResult> OpenOutputAsync(ImmutableArray<string> segments,
        CopyFileEvidence? consent, CancellationToken cancellationToken) => root.RunAsync(async token =>
    {
        if (!IsSafe(segments)) return new CopyOutputOpenResult(null, SkipReason: GameCopySkipReason.UnsafePath);
        var full = Full(segments);
        var relative = string.Join('/', full);
        SafeFileHandle? opened = null;
        try
        {
            var snapshot = InspectCore(full, token);
            if (!snapshot.IsConfined) return new(null, SkipReason: GameCopySkipReason.Link);
            if (snapshot.Kind == CopyTargetKind.Directory) throw Failure(GameCopyErrorCode.FileDirectoryConflict, "A file entry conflicts with an existing directory.");
            if (snapshot.Evidence != consent) return new(null, TargetChanged: true);
            var (parent, parentEvidence) = root.Parent(full, true, token);
            using (parent)
            {
                root.Hooks.BeforeMutation?.Invoke("open", relative);
                await root.ValidateCoreAsync(token).ConfigureAwait(false);
                // The actual file open resolves the whole path below the pinned root atomically.
                opened = root.Open(relative, LinuxCopyNative.ReadWrite | LinuxCopyNative.NonBlock
                    | (consent is null ? LinuxCopyNative.Create | LinuxCopyNative.Exclusive : 0), out var error);
                if (opened is null)
                {
                    if (error == LinuxCopyNative.Exists || error == LinuxCopyNative.Missing && consent is not null) return new(null, TargetChanged: true);
                    root.ThrowOpen(error);
                }
                var actual = LinuxCopyNative.Describe(opened!);
                if (actual.IsLink || actual.IsRegular && actual.Links > 1) return new(null, SkipReason: GameCopySkipReason.Link);
                if (!actual.IsRegular) throw Failure(GameCopyErrorCode.FileDirectoryConflict, "The opened destination is not a regular file.");
                if (consent is not null)
                {
                    root.Hooks.BeforeMutation?.Invoke("truncate", relative);
                    await root.ValidateCoreAsync(token).ConfigureAwait(false);
                    // Compare both held file and current namespace/parents immediately before truncation.
                    var current = InspectCore(full, token);
                    if (current.Kind == CopyTargetKind.Directory)
                        throw Failure(GameCopyErrorCode.FileDirectoryConflict, "The destination became a directory before truncation.");
                    var heldStat = LinuxCopyNative.Describe(opened!);
                    var held = root.FileEvidence(heldStat, parentEvidence, root.HashContent(opened!, heldStat, token));
                    if (!current.IsConfined) return new(null, SkipReason: GameCopySkipReason.Link);
                    if (held != consent || current.Evidence != consent) return new(null, TargetChanged: true);
                    token.ThrowIfCancellationRequested();
                    LinuxCopyNative.Truncate(opened!);
                }
                Stream stream = new FileStream(opened!, FileAccess.Write, 1, isAsync: false);
                opened = null; // FileStream owns the descriptor from here.
                try
                {
                    if (root.Hooks.WrapOutput is { } wrap) stream = wrap(stream);
                    var owned = root.Own(stream, relative, actual, true);
                    return new CopyOutputOpenResult(new OutputFile(owned));
                }
                catch { await stream.DisposeAsync().ConfigureAwait(false); throw; }
            }
        }
        catch (UnsafeTraversalException) { return new(null, SkipReason: GameCopySkipReason.Link); }
        finally { opened?.Dispose(); }
    }, cancellationToken);

    public ValueTask DisposeAsync() => root.DisposeAsync();
    private sealed class OutputFile(CopyAccessStream stream) : IGameCopyOutputFile
    {
        public Stream Output => stream;
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
