using System.Collections.Immutable;

namespace PSPMan.Core.Transfers;

/// <summary>Adapters bind handles to captured volume evidence; paths alone never authorize access.</summary>
public interface IGameCopyFileSystem
{
    ValueTask<IGameArchiveSource> OpenSourceAsync(CopyVolumeEvidence source, string location,
        CancellationToken cancellationToken);
    /// <summary>Opening a destination session does not create ISO or mutate any destination.</summary>
    ValueTask<IGameCopyDestination> OpenDestinationAsync(CopyVolumeEvidence destination,
        CancellationToken cancellationToken);
}
public sealed record CopyOutputOpenResult(IGameCopyOutputFile? File, bool TargetChanged = false,
    GameCopySkipReason? SkipReason = null);

public interface IGameCopyDestination : IAsyncDisposable
{
    string DestinationPath { get; }
    ValueTask ValidateAsync(CancellationToken cancellationToken);
    ValueTask<long?> GetAvailableBytesAsync(CancellationToken cancellationToken);
    /// <summary>Inspect without mutation using native case/name/containment semantics and pinned handles.</summary>
    ValueTask<CopyTargetSnapshot> InspectAsync(ImmutableArray<string> segments, CancellationToken cancellationToken);
    /// <summary>Called only after actual-size/free-space checks; merges directories or fails on a file conflict.
    /// Returns a skip reason if unsafe/link traversal is detected at mutation time.</summary>
    ValueTask<GameCopySkipReason?> EnsureDirectoryAsync(ImmutableArray<string> segments, CancellationToken cancellationToken);
    /// <summary>
    /// Null consent means exclusive create. Non-null consent authorizes only that exact observed file.
    /// Validate volume/parent/target evidence before truncation; return TargetChanged without mutation
    /// if evidence differs. Return SkipReason for unsafe/link traversal. No blind overwrite is allowed.
    /// The returned file owns its stream and pinned output handles until async disposal.
    /// </summary>
    ValueTask<CopyOutputOpenResult> OpenOutputAsync(ImmutableArray<string> segments,
        CopyFileEvidence? consent, CancellationToken cancellationToken);
}

public interface IGameCopyOutputFile : IAsyncDisposable
{
    Stream Output { get; }
}
