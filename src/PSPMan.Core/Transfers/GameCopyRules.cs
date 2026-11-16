using System.Collections.Immutable;

namespace PSPMan.Core.Transfers;

public static class GameCopyRules
{
    public static CopyEntryPath ClassifyEntry(string? path, bool isLink = false, bool isDirectory = false)
    {
        if (isLink) return new([], GameCopySkipReason.Link);
        if (string.IsNullOrEmpty(path) || path[0] is '/' or '\\' || path.Contains('\0')
            || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'))
            return new([], GameCopySkipReason.UnsafePath);
        var relative = path.Replace('\\', '/');
        if (isDirectory && relative.EndsWith('/')) relative = relative[..^1];
        var segments = relative.Split('/');
        return segments.Any(segment => segment.Length == 0 || segment is "." or "..")
            ? new([], GameCopySkipReason.UnsafePath) : new(segments.ToImmutableArray());
    }

    public static CopySizeResult SumActualBytes(IEnumerable<GameArchiveEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        long total = 0;
        var count = 0;
        var any = false;
        foreach (var entry in entries)
        {
            any = true;
            if (entry.IsDirectory) continue;
            count++;
            if (entry.SizeBytes is null)
                return new(null, count, new(GameCopyErrorCode.UnknownActualSize, "Actual decompressed size is unknown.", entry.Index));
            if (entry.SizeBytes < 0)
                return new(null, count, new(GameCopyErrorCode.InvalidActualSize, "Actual decompressed size is invalid.", entry.Index));
            try { total = checked(total + entry.SizeBytes.Value); }
            catch (OverflowException)
            {
                return new(null, count, new(GameCopyErrorCode.SizeOverflow, "Actual decompressed size exceeds the supported byte range.", entry.Index));
            }
        }
        return any ? new(total, count) : new(null, 0, new(GameCopyErrorCode.EmptyArchive, "The archive is empty."));
    }

    public static GameCopyError? CheckCapacity(long actualBytes, long? availableBytes)
    {
        if (actualBytes < 0) return new(GameCopyErrorCode.InvalidActualSize, "Actual decompressed size is invalid.");
        if (availableBytes is null or < 0) return new(GameCopyErrorCode.UnknownFreeSpace, "Available destination space could not be determined.");
        return availableBytes < actualBytes ? new(GameCopyErrorCode.InsufficientSpace, "The destination does not have enough free space.") : null;
    }

    public static CopyTargetDecision ValidateTarget(GameArchiveEntry entry, CopyTargetSnapshot target)
    {
        var path = ClassifyEntry(entry.Path, entry.IsLink, entry.IsDirectory);
        if (!path.IsSafe) return new(path.SkipReason);
        if (!target.IsConfined) return new(GameCopySkipReason.UnsafePath);
        if (!target.IsNameCompatible)
            return new(Error: new(GameCopyErrorCode.InvalidName, "The destination filename is incompatible.", entry.Index));
        if ((entry.IsDirectory && target.Kind == CopyTargetKind.File) || (!entry.IsDirectory && target.Kind == CopyTargetKind.Directory))
            return new(Error: new(GameCopyErrorCode.FileDirectoryConflict, "A destination file conflicts with a directory.", entry.Index));
        return new();
    }

    public static bool NeedsOverwriteConsent(CopyTargetSnapshot target, CopyFileEvidence? consent)
        => target.Kind == CopyTargetKind.File && (target.Evidence is null || consent is null || target.Evidence != consent);

    public static GameCopyProgress AdvanceProgress(GameCopyWork work, decimal previous, bool successfullyClosed = false)
    {
        if (previous is < 0 or > 1 || (!successfullyClosed && previous == 1)
            || work.TotalBytes < 0 || work.DecompressedBytes < 0 || work.WrittenBytes < 0
            || work.ResolvedDecompressionBytes < work.DecompressedBytes || work.ResolvedOutputBytes < work.WrittenBytes
            || work.ResolvedDecompressionBytes > work.TotalBytes || work.ResolvedOutputBytes > work.TotalBytes)
            throw new ArgumentOutOfRangeException(nameof(work), "Progress counters must describe bounded, nonnegative work.");
        if (successfullyClosed && (work.ResolvedDecompressionBytes != work.TotalBytes || work.ResolvedOutputBytes != work.TotalBytes))
            throw new ArgumentException("Unresolved work cannot be reported as completed.", nameof(work));
        var fraction = work.TotalBytes == 0 ? 0 :
            ((decimal)work.ResolvedDecompressionBytes + work.ResolvedOutputBytes) / (2m * work.TotalBytes);
        // A closed successful result alone authorizes 100%; skips resolve work without claiming writes.
        return new(work, successfullyClosed ? 1 : Math.Max(previous, Math.Min(.999m, fraction)));
    }

    public static GameCopyState? ReduceOutcome(GameCopyStopReason stop, int skippedCount, bool allStreamsClosed)
    {
        if (skippedCount < 0) throw new ArgumentOutOfRangeException(nameof(skippedCount));
        if (!Enum.IsDefined(stop)) throw new ArgumentOutOfRangeException(nameof(stop));
        if (!allStreamsClosed) return null;
        return stop switch
        {
            GameCopyStopReason.Cancelled => GameCopyState.Cancelled,
            GameCopyStopReason.Failed => GameCopyState.Failed,
            _ => skippedCount > 0 ? GameCopyState.CompletedWithSkippedFiles : GameCopyState.Completed
        };
    }

    public static bool MatchesPrompt(GameCopySnapshot snapshot, GameCopyPrompt response)
        => response.OperationId != Guid.Empty && response.PromptId != Guid.Empty
           && snapshot.OperationId == response.OperationId && snapshot.Prompt == response
           && snapshot.State is GameCopyState.AwaitingPassword or GameCopyState.AwaitingOverwrite or GameCopyState.AwaitingPasswordDecision;

    public static GameCopyState StateAfterArchiveError(GameCopyErrorCode error, bool outputsClosed)
    {
        if (!Enum.IsDefined(error)) throw new ArgumentOutOfRangeException(nameof(error));
        if (!outputsClosed) return GameCopyState.Stopping;
        return error switch
        {
            GameCopyErrorCode.Cancelled => GameCopyState.Cancelled,
            GameCopyErrorCode.IndeterminateEncryptedError => GameCopyState.AwaitingPasswordDecision,
            GameCopyErrorCode.PasswordRequired or GameCopyErrorCode.IncorrectPassword => GameCopyState.AwaitingPassword,
            _ => GameCopyState.Failed
        };
    }

    public static bool IsTerminal(GameCopyState state)
        => state is GameCopyState.Completed or GameCopyState.CompletedWithSkippedFiles or GameCopyState.Cancelled or GameCopyState.Failed;
}

/// <summary>Per-operation atomic stop/completion arbitration; no I/O or coordinator ownership.</summary>
public sealed class GameCopyTerminalGate
{
    private readonly object gate = new();
    private GameCopyStopReason stop;
    private bool terminal;

    public bool RequestStop(GameCopyStopReason reason)
    {
        if (reason is not (GameCopyStopReason.Cancelled or GameCopyStopReason.Failed))
            throw new ArgumentOutOfRangeException(nameof(reason));
        lock (gate)
        {
            if (terminal) return false;
            // A close/write failure before publication must not be reported as a clean cancellation.
            if (reason == GameCopyStopReason.Failed && stop == GameCopyStopReason.Cancelled)
            {
                stop = reason;
                return true;
            }
            if (stop != GameCopyStopReason.None) return false;
            stop = reason;
            return true;
        }
    }

    public GameCopyState? TryFinish(int skippedCount, bool allStreamsClosed)
    {
        lock (gate)
        {
            if (terminal) return null;
            var result = GameCopyRules.ReduceOutcome(stop, skippedCount, allStreamsClosed);
            if (result is not null) terminal = true;
            return result;
        }
    }
}
