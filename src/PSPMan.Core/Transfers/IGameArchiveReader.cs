using System.Collections.Immutable;

namespace PSPMan.Core.Transfers;

/// <summary>The operation owns this source and keeps it alive until archive session disposal.</summary>
public interface IGameArchiveSource : IAsyncDisposable
{
    string ArchiveName { get; }
    /// <summary>Capture the source adapter's opaque identity/change evidence without destination mutation.</summary>
    ValueTask<string> CaptureEvidenceAsync(CancellationToken cancellationToken);
    /// <summary>Return a read-only seekable stream confined to the selected source. Ownership transfers
    /// to the requesting adapter, which disposes it after the engine releases its part handle.</summary>
    ValueTask<Stream> OpenPartAsync(string relativeName, CancellationToken cancellationToken);
    /// <summary>Validate a token returned by CaptureEvidenceAsync; inspection fingerprints are separate.</summary>
    ValueTask ValidateAsync(string expectedEvidence, CancellationToken cancellationToken);
}

public interface IGameArchiveReader
{
    /// <summary>Passwords are transient; input/format failures use GameCopyException with a typed error.</summary>
    ValueTask<IGameArchiveSession> OpenAsync(IGameArchiveSource source, string? password,
        CancellationToken cancellationToken);
}

/// <summary>Sessions are operation-local. Serialize calls, do not reenter from their own callbacks,
/// and await native return before disposal. External disposal cancels and awaits an active call.</summary>
public interface IGameArchiveSession : IAsyncDisposable
{
    ValueTask<GameArchiveInspection> InspectAsync(CancellationToken cancellationToken);
    /// <summary>Indices are distinct and archive-ordered; replay must not produce earlier output callbacks.</summary>
    ValueTask ExtractAsync(ImmutableArray<int> indices, IGameArchiveExtractionTarget target,
        IProgress<ArchiveByteProgress>? progress, CancellationToken cancellationToken);
}
public sealed record ArchiveByteProgress(int EntryIndex, long DecompressedBytes, long WrittenBytes);
public sealed record ArchiveEntryTarget(Stream? Output, GameCopySkipReason? SkipReason);

/// <summary>
/// Prepare returns a controlled caller-owned stream or an explicit skip. The adapter must await
/// CompleteEntryAsync for every prepared entry, including failures, before ExtractAsync returns.
/// The target closes its current output in CompleteEntryAsync and retains bytes on every failure.
/// Neither archive session nor native engine receives a destination directory or owns device rules.
/// </summary>
public interface IGameArchiveExtractionTarget
{
    ValueTask<ArchiveEntryTarget> PrepareEntryAsync(GameArchiveEntry entry, CancellationToken cancellationToken);
    // Cleanup must run even when the operation's token is already cancelled.
    ValueTask CompleteEntryAsync(int entryIndex, GameCopyError? error);
}
