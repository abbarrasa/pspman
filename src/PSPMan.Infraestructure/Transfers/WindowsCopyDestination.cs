using System.Collections.Immutable;
using Microsoft.Win32.SafeHandles;
using PSPMan.Core.Transfers;
using static PSPMan.Infraestructure.Transfers.WindowsCopyFileAccess;

namespace PSPMan.Infraestructure.Transfers;

internal sealed class WindowsCopyDestination(BoundRoot root) : ICopyResourceDestination
{
    public string RootIdentity => root.Stat.Identity;
    public CopyVolumeEvidence VolumeEvidence => root.Evidence;
    // Every owned output has been drained/flushed before this query. No privileged volume flush.
    public ValueTask<long?> SampleSettledCapacityAsync(CancellationToken token) => GetAvailableBytesAsync(token);
    public ValueTask<CopyResourceTarget> ClaimTargetAsync(ImmutableArray<string> segments, bool directory, CancellationToken token) => root.RunAsync(ct =>
    {
        if (!SafeNames(segments, directory)) return ValueTask.FromResult(new CopyResourceTarget("unsafe:" + Guid.NewGuid(), false, null));
        try
        {
            var path = Full(segments);
            var (parent, parents) = root.Parent(path, true, ct);
            if (parent is null) throw Failure(root.Changed, "The destination parent is unavailable.");
            var handle = WindowsCopyNative.OpenLeaf(parent, path[^1], WindowsCopyOpenMode.Metadata, out var status);
            bool created = false;
            if (handle is null && WindowsCopyNative.IsMissing(status))
            {
                root.Hooks.BeforeMutation?.Invoke("claim", string.Join('/', path));
                root.ValidateCoreAsync(ct).AsTask().GetAwaiter().GetResult();
                _ = root.Describe(parent);
                ct.ThrowIfCancellationRequested();
                using (var creation = WindowsCopyNative.OpenLeaf(parent, path[^1], directory ? WindowsCopyOpenMode.CreateDirectory : WindowsCopyOpenMode.CreateFile, out status))
                {
                    if (creation is null && status != WindowsCopyNative.Collision) ThrowOpen(status);
                    if (creation is not null)
                    {
                        var initial = root.Describe(creation);
                        handle = WindowsCopyNative.OpenLeaf(parent, path[^1], WindowsCopyOpenMode.Metadata, out status);
                        if (handle is null) ThrowOpen(status);
                        try
                        {
                            if (root.Describe(handle!).Identity != initial.Identity)
                                throw Failure(root.Changed, "The newly created target changed before pinning.");
                        }
                        catch { handle?.Dispose(); handle = null; throw; }
                        created = !directory;
                    }
                }
                // Retaining write access here would prevent the next owner's non-sharing writer.
                handle ??= WindowsCopyNative.OpenLeaf(parent, path[^1], WindowsCopyOpenMode.Metadata, out status);
            }
            if (handle is null) ThrowOpen(status);
            try
            {
                var stat = root.Describe(handle!);
                root.PinFile(handle!, stat.Identity); handle = null;
                return ValueTask.FromResult(new CopyResourceTarget(stat.Identity, created, created ? stat.Change : null,
                    root.FileEvidence(stat, parents).Identity));
            }
            finally { handle?.Dispose(); }
        }
        catch (UnsafeTraversalException) { return ValueTask.FromResult(new CopyResourceTarget("unsafe:" + Guid.NewGuid(), false, null)); }
    }, token);

    public string DestinationPath => Path.Combine(root.RootPath, "ISO");
    public async ValueTask ValidateAsync(CancellationToken cancellationToken) =>
        _ = await root.RunAsync(_ => ValueTask.FromResult(true), cancellationToken).ConfigureAwait(false);
    public ValueTask<long?> GetAvailableBytesAsync(CancellationToken cancellationToken) => root.RunAsync(_ =>
    {
        try { return ValueTask.FromResult(root.Hooks.AvailableBytes is { } available ? available() : WindowsCopyNative.AvailableBytes(root.Handle)); }
        catch (IOException error) { throw Failure(GameCopyErrorCode.UnknownFreeSpace, error.Message, error); }
    }, cancellationToken);

    internal static bool SafeNames(ImmutableArray<string> segments, bool allowEmpty = false)
    {
        if (!WindowsCopyPathRules.IsSafe(segments, allowEmpty)) return false;
        if (!segments.IsDefaultOrEmpty && segments.Any(segment => !WindowsCopyPathRules.IsCompatibleName(segment)))
            throw Failure(GameCopyErrorCode.InvalidName, "An entry contains an incompatible Windows filename.");
        return true;
    }
    private static string[] Full(ImmutableArray<string> segments) => segments.IsDefaultOrEmpty ? ["ISO"] : ["ISO", .. segments];

    private CopyTargetSnapshot InspectCore(string[] path, CancellationToken token)
    {
        var (parent, parents) = root.Parent(path, false, token);
        if (parent is null) return new(CopyTargetKind.Missing, null);
        var metadata = WindowsCopyNative.OpenLeaf(parent, path[^1], WindowsCopyOpenMode.Metadata, out var status);
        if (metadata is null) { if (WindowsCopyNative.IsMissing(status)) return new(CopyTargetKind.Missing, null); ThrowOpen(status); }
        try
        {
            var stat = root.Describe(metadata!);
            if (stat.IsDirectory) return new(CopyTargetKind.Directory, null);
            if (stat.Links > 1) throw new UnsafeTraversalException();
            using var readable = WindowsCopyNative.OpenLeaf(parent, path[^1], WindowsCopyOpenMode.Read, out status);
            if (readable is null) ThrowOpen(status);
            var readStat = root.Describe(readable!);
            if (readStat.Identity != stat.Identity || readStat.Change != stat.Change || readStat.Length != stat.Length)
                throw Failure(root.Changed, "The destination changed while opening overwrite evidence.");
            var hash = root.HashContent(readable!, stat, token);
            root.PinFile(metadata!, stat.Identity); metadata = null;
            var evidence = root.FileEvidence(stat, parents, hash);
            return new(CopyTargetKind.File, evidence);
        }
        finally { metadata?.Dispose(); }
    }

    public ValueTask<CopyTargetSnapshot> InspectAsync(ImmutableArray<string> segments, CancellationToken cancellationToken) => root.RunAsync(token =>
    {
        if (!SafeNames(segments, true)) return ValueTask.FromResult(new CopyTargetSnapshot(CopyTargetKind.Missing, null, IsConfined: false));
        try { return ValueTask.FromResult(InspectCore(Full(segments), token)); }
        catch (UnsafeTraversalException) { return ValueTask.FromResult(new CopyTargetSnapshot(CopyTargetKind.Missing, null, IsConfined: false)); }
    }, cancellationToken);

    public ValueTask<GameCopySkipReason?> EnsureDirectoryAsync(ImmutableArray<string> segments, CancellationToken cancellationToken) => root.RunAsync<GameCopySkipReason?>(token =>
    {
        if (!SafeNames(segments, true)) return ValueTask.FromResult<GameCopySkipReason?>(GameCopySkipReason.UnsafePath);
        try
        {
            _ = root.Parent([.. Full(segments), "unused-child"], true, token);
            return ValueTask.FromResult<GameCopySkipReason?>(null);
        }
        catch (UnsafeTraversalException) { return ValueTask.FromResult<GameCopySkipReason?>(GameCopySkipReason.Link); }
    }, cancellationToken);

    public ValueTask<CopyOutputOpenResult> OpenOutputAsync(ImmutableArray<string> segments, CopyFileEvidence? consent,
        CancellationToken cancellationToken) => root.RunAsync(async token =>
    {
        if (!SafeNames(segments)) return new CopyOutputOpenResult(null, SkipReason: GameCopySkipReason.UnsafePath);
        var path = Full(segments);
        SafeFileHandle? opened = null;
        try
        {
            var snapshot = InspectCore(path, token);
            if (snapshot.Kind == CopyTargetKind.Directory) throw Failure(GameCopyErrorCode.FileDirectoryConflict, "A file entry conflicts with a directory.");
            if (snapshot.Evidence != consent) return new(null, TargetChanged: true);
            var (parent, parents) = root.Parent(path, true, token);
            if (parent is null) throw Failure(root.Changed, "The destination parent could not be created.");
            root.Hooks.BeforeMutation?.Invoke("open", string.Join('/', path));
            await root.ValidateCoreAsync(token).ConfigureAwait(false);
            _ = root.Describe(parent);
            opened = WindowsCopyNative.OpenLeaf(parent, path[^1], consent is null ? WindowsCopyOpenMode.CreateFile : WindowsCopyOpenMode.Write, out var status);
            if (opened is null)
            {
                if (status == WindowsCopyNative.Collision || consent is not null && WindowsCopyNative.IsMissing(status)) return new(null, TargetChanged: true);
                if (WindowsCopyNative.ClassifiedError(status) == GameCopyErrorCode.FileDirectoryConflict)
                {
                    using var changed = WindowsCopyNative.OpenLeaf(parent, path[^1], WindowsCopyOpenMode.Metadata, out _);
                    if (changed is not null) _ = root.Describe(changed);
                }
                ThrowOpen(status);
            }
            var stat = root.Describe(opened!);
            if (stat.IsDirectory) throw Failure(GameCopyErrorCode.FileDirectoryConflict, "The opened output is a directory.");
            if (stat.Links > 1) return new(null, SkipReason: GameCopySkipReason.Link);
            if (consent is not null)
            {
                root.Hooks.BeforeMutation?.Invoke("truncate", string.Join('/', path));
                await root.ValidateCoreAsync(token).ConfigureAwait(false);
                // The non-sharing write handle prevents replacement/writers after acquisition;
                // still compare the namespace and held bytes before the only truncation call.
                var current = InspectCore(path, token);
                if (current.Kind == CopyTargetKind.Directory) throw Failure(GameCopyErrorCode.FileDirectoryConflict, "The destination became a directory.");
                var heldStat = root.Describe(opened!);
                var held = root.FileEvidence(heldStat, parents, root.HashContent(opened!, heldStat, token));
                if (current.Evidence != consent || held != consent) return new(null, TargetChanged: true);
                token.ThrowIfCancellationRequested();
                WindowsCopyNative.Truncate(opened!);
            }
            var outputHandle = opened!;
            Stream stream = new FileStream(outputHandle, FileAccess.Write, 1, isAsync: false);
            opened = null;
            try
            {
                if (root.Hooks.WrapOutput is { } wrap) stream = wrap(stream);
                return new CopyOutputOpenResult(new OutputFile(root.Own(stream, path, stat, true, outputHandle)));
            }
            catch { await stream.DisposeAsync().ConfigureAwait(false); throw; }
        }
        catch (UnsafeTraversalException) { return new(null, SkipReason: GameCopySkipReason.Link); }
        finally { opened?.Dispose(); }
    }, cancellationToken);

    public ValueTask DisposeAsync() => root.DisposeAsync();
    private sealed class OutputFile(CopyAccessStream stream) : IGameCopyOutputFile
    { public Stream Output => stream; public ValueTask DisposeAsync() => stream.DisposeAsync(); }
}
