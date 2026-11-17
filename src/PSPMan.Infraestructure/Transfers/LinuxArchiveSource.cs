using System.Collections.Immutable;
using System.Text.Json;
using PSPMan.Core.Transfers;
using static PSPMan.Infraestructure.Transfers.LinuxCopyFileAccess;

namespace PSPMan.Infraestructure.Transfers;

internal sealed class LinuxArchiveSource(BoundRoot root, ImmutableArray<string> segments) : IGameArchiveSource
{
    public string ArchiveName => segments[^1];
    private string? _captured;

    private CopyFileEvidence DescribePart(string[] path, CancellationToken token)
    {
        var (parent, parents) = root.Parent(path, false, token);
        using (parent)
        {
            if (parent is null) throw Failure(GameCopyErrorCode.MissingArchive, "The archive directory does not exist.");
            var handle = root.Open(string.Join('/', path), LinuxCopyNative.PathOnly, out var error);
            if (handle is null)
            {
                if (error == LinuxCopyNative.Missing) throw new FileNotFoundException("The archive part does not exist.", path[^1]);
                root.ThrowOpen(error);
            }
            try
            {
                var stat = LinuxCopyNative.Describe(handle!);
                if (!stat.IsRegular) throw Failure(GameCopyErrorCode.ReadError, "The archive part is not a regular file; links are not followed.");
                var evidence = root.FileEvidence(stat, parents);
                root.Pin(handle!, evidence.Identity); handle = null;
                return evidence;
            }
            finally { handle?.Dispose(); }
        }
    }

    public ValueTask<string> CaptureEvidenceAsync(CancellationToken cancellationToken) => root.RunAsync(token =>
    {
        try
        {
            var current = JsonSerializer.Serialize(DescribePart(segments.ToArray(), token));
            if (_captured is not null && _captured != current) throw Failure(GameCopyErrorCode.SourceChanged, "The selected archive changed.");
            _captured = current;
            return ValueTask.FromResult(current);
        }
        catch (FileNotFoundException error) { throw Failure(GameCopyErrorCode.MissingArchive, error.Message, error); }
        catch (UnsafeTraversalException) { throw Failure(GameCopyErrorCode.ReadError, "The source archive path traverses a link or another mount."); }
    }, cancellationToken);

    public async ValueTask ValidateAsync(string expectedEvidence, CancellationToken cancellationToken)
    {
        if (await CaptureEvidenceAsync(cancellationToken).ConfigureAwait(false) != expectedEvidence)
            throw Failure(GameCopyErrorCode.SourceChanged, "Source identity or change evidence no longer matches.");
    }

    public ValueTask<Stream> OpenPartAsync(string relativeName, CancellationToken cancellationToken) => root.RunAsync<Stream>(token =>
    {
        if (!LinuxCopyDestination.IsSafe([relativeName])) throw Failure(GameCopyErrorCode.ReadError, "Archive parts must be confined leaf names.");
        var path = segments.SetItem(segments.Length - 1, relativeName).ToArray();
        try
        {
            var before = DescribePart(path, token);
            var relative = string.Join('/', path);
            var handle = root.Open(relative, LinuxCopyNative.NonBlock, out var error);
            if (handle is null)
            {
                if (error == LinuxCopyNative.Missing) throw new FileNotFoundException("The archive part does not exist.", relativeName);
                root.ThrowOpen(error);
            }
            try
            {
                var stat = LinuxCopyNative.Describe(handle!);
                var (parent, parents) = root.Parent(path, false, token);
                using (parent)
                    if (parent is null || !stat.IsRegular || root.FileEvidence(stat, parents) != before)
                        throw Failure(GameCopyErrorCode.SourceChanged, "The archive part changed while opening it.");
                Stream stream = new FileStream(handle!, FileAccess.Read, 1, isAsync: false);
                handle = null;
                try { return ValueTask.FromResult<Stream>(root.Own(stream, relative, stat, false)); }
                catch { stream.Dispose(); throw; }
            }
            finally { handle?.Dispose(); }
        }
        catch (UnsafeTraversalException) { throw Failure(GameCopyErrorCode.ReadError, "Archive part traversal is unsafe."); }
    }, cancellationToken);

    public ValueTask DisposeAsync() => root.DisposeAsync();
}
