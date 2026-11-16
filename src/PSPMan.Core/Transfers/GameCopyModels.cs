using System.Collections.Immutable;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;

namespace PSPMan.Core.Transfers;

public enum GameCopyState
{
    Preparing, AwaitingPassword, AwaitingOverwrite, Transferring,
    AwaitingPasswordDecision, Stopping, Completed, CompletedWithSkippedFiles, Cancelled, Failed
}
public enum GameCopyErrorCode
{
    SourceUnavailable, DestinationUnavailable, SourceChanged, DestinationChanged,
    MissingArchive, MissingPart, EmptyArchive, CorruptArchive, UnsupportedArchive,
    PasswordRequired, IncorrectPassword, IndeterminateEncryptedError,
    UnknownActualSize, InvalidActualSize, SizeOverflow, UnknownFreeSpace,
    InsufficientSpace, InvalidName, FileDirectoryConflict, ReadError, WriteError, CloseError, Cancelled
}
public enum GameCopyStopReason { None, Cancelled, Failed }
public enum GameCopySkipReason { UnsafePath, Link, OverwriteDeclined }
public enum CopyTargetKind { Missing, File, Directory }
public enum CopyOverwriteDecision { Overwrite, Skip, Cancel }
public enum CopyPasswordDecision { Retry, Cancel }

public sealed record GameCopyError(GameCopyErrorCode Code, string Message, int? EntryIndex = null);
public sealed class GameCopyException(GameCopyError error, Exception? innerException = null)
    : Exception(error.Message, innerException)
{
    public GameCopyError Error { get; } = error;
}

/// <summary>Captured evidence, not a promise that a mount root remains available.</summary>
public sealed record CopyVolumeEvidence(
    DeviceKey Device, DeviceIdentity DeviceIdentity, string? MediaGeneration,
    VolumeKey Volume, DeviceIdentity VolumeIdentity, string Root, MountedPathSyntax Syntax);
public sealed record GameCopyRequest(Guid OperationId, GameRecord Game,
    CopyVolumeEvidence Source, CopyVolumeEvidence Destination);

/// <summary>Index and archive order identify entries, including repeated paths.</summary>
public sealed record GameArchiveEntry(int Index, string Path, long? SizeBytes,
    bool IsDirectory = false, bool IsLink = false, bool IsEncrypted = false);
public sealed record GameArchiveInspection(ImmutableArray<GameArchiveEntry> Entries, string SourceEvidence);
public sealed record CopyFileEvidence(string Identity, long Length, string ChangeEvidence);
public sealed record CopyTargetSnapshot(CopyTargetKind Kind, CopyFileEvidence? Evidence,
    bool IsNameCompatible = true, bool IsConfined = true);
public sealed record CopyTargetDecision(GameCopySkipReason? SkipReason = null, GameCopyError? Error = null);
public sealed record CopyEntryPath(ImmutableArray<string> Segments, GameCopySkipReason? SkipReason = null)
{
    public bool IsSafe => SkipReason is null;
}
public sealed record CopySizeResult(long? Bytes, int FileCount, GameCopyError? Error = null);
/// <summary>Resolved work can include skips/replay; decoded/written counters contain actual I/O only.</summary>
public sealed record GameCopyWork(long TotalBytes, long DecompressedBytes, long WrittenBytes,
    long ResolvedDecompressionBytes, long ResolvedOutputBytes);
public sealed record GameCopyProgress(GameCopyWork Work, decimal Fraction);
public sealed record GameCopyPrompt(Guid OperationId, Guid PromptId);
public sealed record CopyOverwritePrompt(GameCopyPrompt Identity, int EntryIndex,
    string DestinationPath, CopyFileEvidence Evidence, string RetainedOutputWarning);
public sealed record CopyPasswordPrompt(GameCopyPrompt Identity, GameCopyErrorCode Reason);
/// <summary>Transient response: never publish in snapshots, progress, history or logs.</summary>
public sealed record CopyPasswordResponse(GameCopyPrompt Identity, CopyPasswordDecision Decision,
    string? Password = null);
public sealed record CopyOverwriteResponse(GameCopyPrompt Identity, CopyOverwriteDecision Decision);
public sealed record GameCopySnapshot(Guid OperationId, string Title, string Destination,
    GameCopyState State, GameCopyProgress? Progress = null, GameCopyPrompt? Prompt = null,
    GameCopyError? Error = null, int SkippedEntries = 0);
public sealed record GameCopyResult(Guid OperationId, GameCopyState State, string Destination,
    int SkippedEntries, GameCopyError? Error = null);
