using System.Collections.Immutable;

namespace PSPMan.Core.Transfers;

public sealed record GameCopyResourceWait(Guid OperationId, GameCopyWaitReason Reason, string DestinationPath);

/// <summary>
/// Shared by the session's engine calls. Implementations coordinate native capacity/alias evidence;
/// no mount string or host string comparer alone authorizes a budget or target claim.
/// </summary>
public interface IGameCopyResources
{
    /// <summary>
    /// Bind one caller-owned native destination to this operation. The returned adapter accounts
    /// output chunks automatically; callers must not debit progress or wrap its writes again.
    /// Dispose entry/output, then budget, then the original destination. Binding never owns that root.
    /// </summary>
    IGameCopyDestination BindDestination(Guid operationId, CopyVolumeEvidence volume, IGameCopyDestination destination);

    /// <summary>
    /// Reserve actual pending output using consistent held-volume free-space evidence. Insufficient/unknown
    /// space fails this operation before new writes; it does not wait for space. Retry replaces its claim.
    /// </summary>
    ValueTask<IGameCopyBudgetLease> ReserveAsync(Guid operationId, CopyVolumeEvidence volume,
        IGameCopyDestination destination, long pendingBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Acquire before target inspection/consent. Publish only this operation's cancellable file wait.
    /// Native-equivalent targets share exclusion; compatible directory creation uses short namespace guards.
    /// Revalidate after waiting. Hold one entry at a time through consent/open/flush/output close; release
    /// on skip/failure/cancel. A lease supplies no overwrite consent and cannot bypass conditional open.
    /// </summary>
    ValueTask<IGameCopyEntryLease> AcquireEntryAsync(Guid operationId, CopyVolumeEvidence volume,
        IGameCopyDestination destination, ImmutableArray<string> segments, bool isDirectory,
        IProgress<GameCopyResourceWait>? waiting, CancellationToken cancellationToken);
}

/// <summary>Attempt-level allocation accounting, independent of monotonic UI progress and source decoding.</summary>
public interface IGameCopyBudgetLease : IAsyncDisposable
{
    Guid OperationId { get; }
    CopyVolumeKey Volume { get; }
    /// <summary>Retry rechecks full still-pending replay output; completed entries are excluded.</summary>
    ValueTask RevalidateAsync(long pendingBytes, CancellationToken cancellationToken);
    ValueTask SkipPendingAsync(long bytes, CancellationToken cancellationToken);
    /// <summary>
    /// Enters one output chunk's allocation epoch; admission fences await existing epochs, not whole files.
    /// Cancellation before acquisition makes no debit. End the epoch before awaiting more source/input.
    /// </summary>
    ValueTask<IGameCopyWriteLease> BeginWriteAsync(long requestedBytes, CancellationToken cancellationToken);
    // Async disposal follows output/native return and reconciles settlement before releasing unused claims.
    // It never deletes output or refunds written bytes. Close failure must remain visible to the caller.
}

public interface IGameCopyWriteLease : IAsyncDisposable
{
    /// <summary>
    /// Complete once with actual accepted bytes, including a known partial write. No cancellation token:
    /// bookkeeping must not be abandoned when the copy is cancelled. Uncertain/failed writes keep a
    /// conservative charge until the owner reconciles native allocation; disposing is not a space refund.
    /// </summary>
    ValueTask CompleteAsync(long writtenBytes);
}

public interface IGameCopyEntryLease : IAsyncDisposable
{
    Guid OperationId { get; }
    CopyVolumeKey Volume { get; }
    /// <summary>Adapter-owned target identity, never a writable path or native handle.</summary>
    string TargetIdentity { get; }
}
