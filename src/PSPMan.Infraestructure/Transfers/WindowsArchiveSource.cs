using System.Text.Json;
using PSPMan.Core.Transfers;
using static PSPMan.Infraestructure.Transfers.WindowsCopyFileAccess;

namespace PSPMan.Infraestructure.Transfers;

internal sealed class WindowsArchiveSource(BoundRoot root, string[] path) : IGameArchiveSource
{
    public string ArchiveName => path[^1];
    private string? _captured;

    private CopyFileEvidence DescribePart(string[] part, CancellationToken token)
    {
        var (parent, parents) = root.Parent(part, false, token);
        if (parent is null) throw Failure(GameCopyErrorCode.MissingArchive, "The archive directory does not exist.");
        var handle = WindowsCopyNative.OpenLeaf(parent, part[^1], WindowsCopyOpenMode.Metadata, out var status);
        if (handle is null)
        {
            if (WindowsCopyNative.IsMissing(status)) throw new FileNotFoundException("The archive part does not exist.", part[^1]);
            ThrowOpen(status);
        }
        try
        {
            var stat = root.Describe(handle!);
            if (stat.IsDirectory) throw Failure(GameCopyErrorCode.ReadError, "The archive part is not a regular file.");
            root.PinFile(handle!, stat.Identity); handle = null;
            var evidence = root.FileEvidence(stat, parents);
            return evidence;
        }
        finally { handle?.Dispose(); }
    }

    public ValueTask<string> CaptureEvidenceAsync(CancellationToken cancellationToken) => root.RunAsync(token =>
    {
        try
        {
            var current = JsonSerializer.Serialize(DescribePart(path, token));
            if (_captured is not null && current != _captured) throw Failure(GameCopyErrorCode.SourceChanged, "The selected archive changed.");
            _captured = current;
            return ValueTask.FromResult(current);
        }
        catch (FileNotFoundException cause) { throw Failure(GameCopyErrorCode.MissingArchive, cause.Message, cause); }
        catch (UnsafeTraversalException) { throw Failure(GameCopyErrorCode.ReadError, "The source archive traverses a reparse point."); }
    }, cancellationToken);

    public async ValueTask ValidateAsync(string expectedEvidence, CancellationToken cancellationToken)
    {
        if (await CaptureEvidenceAsync(cancellationToken).ConfigureAwait(false) != expectedEvidence)
            throw Failure(GameCopyErrorCode.SourceChanged, "The selected source evidence no longer matches.");
    }

    public ValueTask<Stream> OpenPartAsync(string relativeName, CancellationToken cancellationToken) => root.RunAsync<Stream>(token =>
    {
        if (!WindowsCopyDestination.SafeNames([relativeName])) throw Failure(GameCopyErrorCode.ReadError, "Archive parts must be confined leaf names.");
        var part = (string[])path.Clone(); part[^1] = relativeName;
        try
        {
            var before = DescribePart(part, token);
            var (parent, parents) = root.Parent(part, false, token);
            if (parent is null) throw Failure(GameCopyErrorCode.SourceChanged, "The archive parent changed.");
            var handle = WindowsCopyNative.OpenLeaf(parent, relativeName, WindowsCopyOpenMode.Read, out var status);
            if (handle is null)
            {
                if (WindowsCopyNative.IsMissing(status)) throw new FileNotFoundException("The archive part does not exist.", relativeName);
                ThrowOpen(status);
            }
            try
            {
                var stat = root.Describe(handle!);
                if (stat.IsDirectory || root.FileEvidence(stat, parents) != before)
                    throw Failure(GameCopyErrorCode.SourceChanged, "The archive part changed while opening.");
                Stream stream = new FileStream(handle!, FileAccess.Read, 1, isAsync: false);
                handle = null;
                try { return ValueTask.FromResult<Stream>(root.Own(stream, part, stat, false)); }
                catch { stream.Dispose(); throw; }
            }
            finally { handle?.Dispose(); }
        }
        catch (UnsafeTraversalException) { throw Failure(GameCopyErrorCode.ReadError, "Archive part traversal is unsafe."); }
    }, cancellationToken);

    public ValueTask DisposeAsync() => root.DisposeAsync();
}
