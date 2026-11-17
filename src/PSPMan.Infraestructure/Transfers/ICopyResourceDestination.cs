using System.Collections.Immutable;
using PSPMan.Core.Transfers;

namespace PSPMan.Infraestructure.Transfers;

/// <summary>Confined native evidence only; the shared owner never derives equality from path spelling.</summary>
internal interface ICopyResourceDestination : IGameCopyDestination
{
    string RootIdentity { get; }
    CopyVolumeEvidence VolumeEvidence { get; }
    ValueTask<long?> SampleSettledCapacityAsync(CancellationToken token);
    ValueTask<CopyResourceTarget> ClaimTargetAsync(ImmutableArray<string> segments, bool directory, CancellationToken token);
}

internal sealed record CopyResourceTarget(string Identity, bool Created, string? InitialChange, string? EvidenceIdentity = null);
