using System.Collections.Immutable;
using PSPMan.Core.Transfers;
using static PSPMan.Infraestructure.Transfers.LinuxCopyFileAccess;

namespace PSPMan.Infraestructure.Transfers;

/// <summary>
/// Session-owned destination coordination. Bindings are an opt-in adapter for engine integration;
/// ordinary single-copy composition is unchanged. Destinations remain caller-owned.
/// </summary>
public sealed class CopyDestinationResources : IGameCopyResources
{
    private readonly object gate = new();
    private readonly Dictionary<CopyVolumeKey, Volume> volumes = [];
    private readonly Dictionary<Guid, Destination> operations = [];

    public IGameCopyDestination BindDestination(Guid operationId, CopyVolumeEvidence evidence, IGameCopyDestination destination)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        if (destination is not LinuxCopyDestination native || native.VolumeEvidence != evidence)
            throw new ArgumentException("A matching confined Linux destination is required.", nameof(destination));
        lock (gate)
        {
            var key = CopyVolumeKey.From(evidence);
            if (!volumes.TryGetValue(key, out var volume)) volumes.Add(key, volume = new(native.RootIdentity));
            if (volume.NativeIdentity != native.RootIdentity)
                throw Failure(GameCopyErrorCode.DestinationChanged, "One captured volume resolved to different native roots.");
            return new Destination(this, volume, operationId, evidence, native);
        }
    }

    private Destination Bound(Guid id, CopyVolumeEvidence evidence, IGameCopyDestination destination)
    {
        if (destination is not Destination bound || bound.Owner != this || bound.Id != id || bound.Evidence != evidence)
            throw new ArgumentException("Use this owner's matching operation binding.", nameof(destination));
        return bound;
    }

    public async ValueTask<IGameCopyBudgetLease> ReserveAsync(Guid operationId, CopyVolumeEvidence volume,
        IGameCopyDestination destination, long pendingBytes, CancellationToken cancellationToken)
    {
        var bound = Bound(operationId, volume, destination);
        await bound.State.Fence.WaitAsync(cancellationToken).ConfigureAwait(false);
        var registered = false;
        try
        {
            lock (bound.State.Sync)
                if (bound.Budget is not null || bound.State.Budgets.ContainsKey(operationId))
                    throw new InvalidOperationException("An operation already has a budget; revalidate its lease instead.");
            lock (gate)
            {
                if (operations.ContainsKey(operationId)) throw new InvalidOperationException("The operation already has an active reservation.");
                if (operations.Values.Any(other => other.State != bound.State && other.Native.RootIdentity == bound.Native.RootIdentity))
                    throw Failure(GameCopyErrorCode.DestinationChanged, "Different captured volumes identify the same native root.");
                operations.Add(operationId, bound); registered = true;
            }
            await SampleAsync(bound, cancellationToken).ConfigureAwait(false);
            lock (bound.State.Sync)
            {
                var result = bound.State.Ledger.Reserve(operationId, volume, pendingBytes, bound.State.FreeBytes);
                if (result.Error is { } error) throw new GameCopyException(error);
                bound.State.Ledger = result.State;
                var budget = new Budget(bound, result.Reservation!);
                bound.Budget = budget; bound.State.Budgets.Add(operationId, budget);
                return budget;
            }
        }
        finally
        {
            if (registered && bound.Budget is null) lock (gate) operations.Remove(operationId);
            bound.State.Fence.Release();
        }
    }

    // Fence blocks new chunks, but completion never needs it: started chunks can always drain.
    private static async Task SampleAsync(Destination destination, CancellationToken token)
    {
        var state = destination.State;
        Task drained;
        lock (state.Sync) drained = state.ActiveWrites == 0 ? Task.CompletedTask : state.Drained.Task;
        await drained.WaitAsync(token).ConfigureAwait(false);
        ResourceOutput[] outputs;
        lock (state.Sync) outputs = state.Budgets.Values.SelectMany(b => b.Outputs).ToArray();
        foreach (var output in outputs)
        {
            try { await output.FlushForSampleAsync(token).ConfigureAwait(false); }
            catch (Exception error) when (error is GameCopyException or IOException or ObjectDisposedException)
            {
                // A failed peer buffer is retired before capacity reconciliation. Attribute the
                // fault to its owner, never to an unrelated admitting operation. Close precedes
                // the native sample so no late buffered allocation can escape its charge.
                var budget = output.OwnerBudget;
                lock (state.Sync) budget.Failure ??= error as GameCopyException ?? Failure(GameCopyErrorCode.CloseError, error.Message, error);
                budget.Lifetime.Cancel();
                try { await output.DisposeAsync().ConfigureAwait(false); }
                catch (Exception closeError)
                { lock (state.Sync) budget.Failure = closeError as GameCopyException ?? Failure(GameCopyErrorCode.CloseError, closeError.Message, closeError); }
            }
        }
        long? free;
        try { free = await destination.Native.SampleSettledCapacityAsync(token).ConfigureAwait(false); }
        catch (GameCopyException error) when (error.Error.Code == GameCopyErrorCode.WriteError)
        { throw Failure(GameCopyErrorCode.UnknownFreeSpace, "Destination allocation could not be reconciled.", error); }
        if (free is null or < 0) throw Failure(GameCopyErrorCode.UnknownFreeSpace, "Available destination space could not be determined.");
        token.ThrowIfCancellationRequested();
        lock (state.Sync)
        {
            // Flush + syncfs + held-root fstatvfs represents completed chunks, including uncertain
            // partial writes. A cancelled/failed sample never clears conservative charges.
            state.FreeBytes = free;
            foreach (var budget in state.Budgets.Values.ToArray())
            {
                var update = state.Ledger.SettleWritten(budget.Claim, budget.Claim.UnsettledBytes);
                state.Ledger = update.State; budget.Claim = update.Reservation;
                if (budget.Closed) ReleaseBudget(budget);
            }
        }
    }

    private static void ReleaseBudget(Budget budget)
    {
        var state = budget.Binding.State;
        state.Ledger = state.Ledger.Release(budget.Claim, filesClosed: true);
        state.Budgets.Remove(budget.OperationId);
        lock (budget.Binding.Owner.gate) budget.Binding.Owner.operations.Remove(budget.OperationId);
    }

    public async ValueTask<IGameCopyEntryLease> AcquireEntryAsync(Guid operationId, CopyVolumeEvidence volume,
        IGameCopyDestination destination, ImmutableArray<string> segments, bool isDirectory,
        IProgress<GameCopyResourceWait>? waiting, CancellationToken cancellationToken)
    {
        var binding = Bound(operationId, volume, destination);
        TaskCompletionSource acquired;
        Budget budget;
        lock (binding.State.Sync)
        {
            budget = binding.RequireBudget();
            if (binding.EntryPending) throw new InvalidOperationException("Only one entry may be owned or awaited per operation.");
            binding.EntryPending = true;
            binding.PendingEntry = acquired = Signal();
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Lifetime.Token);
        var entry = new Entry(binding, segments, isDirectory, waiting);
        try
        {
            await RefreshAsync(entry, linked.Token).ConfigureAwait(false);
            lock (binding.State.Sync) { linked.Token.ThrowIfCancellationRequested(); binding.Entry = entry; }
            return entry;
        }
        catch { await entry.DisposeAsync().ConfigureAwait(false); throw; }
        finally { acquired.TrySetResult(); }
    }

    private static async Task RefreshAsync(Entry entry, CancellationToken token)
    {
        var binding = entry.Binding; var state = binding.State;
        // A path replacement during I/O is a native validation failure, not permission
        // to release the old inode while its output/flush is still owned.
        lock (state.Sync) if (binding.Budget!.Outputs.Count != 0) return;
        while (true)
        {
            Task? wait = null;
            await state.Namespace.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var target = await binding.Native.ClaimTargetAsync(entry.Segments, entry.Directory, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (target.Identity.StartsWith("unsafe:", StringComparison.Ordinal))
                {
                    DropTarget(entry); entry.Target = target; return;
                }
                if (entry.Target?.Identity == target.Identity) return;
                DropTarget(entry);
                if (state.Targets.TryGetValue(target.Identity, out var holder) && holder != entry)
                    wait = state.Changed.Task;
                else { state.Targets.Add(target.Identity, entry); entry.Target = target; return; }
            }
            finally { state.Namespace.Release(); }
            entry.Waiting?.Report(new(binding.Id, GameCopyWaitReason.DestinationFile, string.Join('/', entry.Segments)));
            await wait!.WaitAsync(token).ConfigureAwait(false);
        }
    }

    // Namespace semaphore is held. Native inode pins prevent identity reuse throughout a binding.
    private static void DropTarget(Entry entry)
    {
        var state = entry.Binding.State;
        if (entry.Target is { } target && state.Targets.TryGetValue(target.Identity, out var holder) && holder == entry)
        {
            state.Targets.Remove(target.Identity);
            var changed = state.Changed; state.Changed = Signal(); changed.TrySetResult();
        }
        entry.Target = null;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Volume(string identity)
    {
        internal readonly string NativeIdentity = identity;
        internal readonly object Sync = new();
        internal readonly SemaphoreSlim Fence = new(1, 1), Namespace = new(1, 1);
        internal readonly Dictionary<Guid, Budget> Budgets = [];
        internal readonly Dictionary<string, Entry> Targets = [];
        internal GameCopySpaceReservations Ledger = GameCopySpaceReservations.Empty;
        internal long? FreeBytes;
        internal int ActiveWrites;
        internal TaskCompletionSource Drained = Signal(), Changed = Signal();
    }

    private sealed class Budget(Destination binding, CopySpaceReservation claim) : IGameCopyBudgetLease
    {
        internal readonly Destination Binding = binding;
        internal CopySpaceReservation Claim = claim;
        internal readonly HashSet<ResourceOutput> Outputs = [];
        internal bool Closing, Closed;
        internal GameCopyException? Failure;
        internal readonly CancellationTokenSource Lifetime = new();
        internal long InFlightBytes;
        private Task? disposal;
        public Guid OperationId => Binding.Id;
        public CopyVolumeKey Volume => Claim.Volume;
        public async ValueTask RevalidateAsync(long pendingBytes, CancellationToken token)
        {
            var state = Binding.State;
            await state.Fence.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (state.Sync) Binding.RequireBudget();
                await SampleAsync(Binding, token).ConfigureAwait(false);
                lock (state.Sync)
                {
                    Binding.RequireBudget();
                    var result = state.Ledger.Reserve(OperationId, Binding.Evidence, pendingBytes, state.FreeBytes);
                    if (result.Error is { } error) throw new GameCopyException(error);
                    state.Ledger = result.State; Claim = result.Reservation!;
                }
            }
            finally { state.Fence.Release(); }
        }
        public ValueTask SkipPendingAsync(long bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (Binding.State.Sync)
            {
                Binding.RequireBudget();
                if (bytes < 0 || bytes > Claim.PendingBytes - InFlightBytes) throw new ArgumentOutOfRangeException(nameof(bytes));
                var update = Binding.State.Ledger.SkipPending(Claim, bytes);
                Binding.State.Ledger = update.State; Claim = update.Reservation;
            }
            return ValueTask.CompletedTask;
        }
        public async ValueTask<IGameCopyWriteLease> BeginWriteAsync(long requestedBytes, CancellationToken token)
        {
            var state = Binding.State;
            await state.Fence.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await Binding.Native.ValidateAsync(token).ConfigureAwait(false);
                lock (state.Sync)
                {
                    Binding.RequireBudget();
                    if (requestedBytes < 0 || requestedBytes > Claim.PendingBytes - InFlightBytes)
                        throw new ArgumentOutOfRangeException(nameof(requestedBytes));
                    if (state.ActiveWrites++ == 0) state.Drained = Signal();
                    InFlightBytes = checked(InFlightBytes + requestedBytes);
                    return new Write(this, requestedBytes);
                }
            }
            finally { state.Fence.Release(); }
        }
        public ValueTask DisposeAsync()
        { lock (Binding.State.Sync) { Closing = true; return new(disposal ??= Task.Run(CloseAsync)); } }
        private async Task CloseAsync()
        {
            Lifetime.Cancel();
            Task pending;
            lock (Binding.State.Sync) pending = Binding.PendingEntry?.Task ?? Task.CompletedTask;
            await pending.ConfigureAwait(false);
            Exception? failure = Failure;
            Entry? entry;
            lock (Binding.State.Sync) entry = Binding.Entry;
            try { if (entry is not null) await entry.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failure = error; }
            var state = Binding.State;
            await state.Fence.WaitAsync().ConfigureAwait(false);
            try
            {
                // Manual write leases are also owned until their completion, independently of files.
                await SampleAsync(Binding, default).ConfigureAwait(false);
                lock (state.Sync) { Closed = true; ReleaseBudget(this); }
            }
            catch (Exception error) { lock (state.Sync) Closed = true; failure ??= error; }
            finally { state.Fence.Release(); }
            lock (state.Sync) failure ??= Failure;
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class Write(Budget budget, long requested) : IGameCopyWriteLease
    {
        private bool complete;
        public ValueTask CompleteAsync(long writtenBytes)
        {
            if (writtenBytes < 0 || writtenBytes > requested) throw new ArgumentOutOfRangeException(nameof(writtenBytes));
            Finish(writtenBytes, duplicateIsError: true); return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() { Finish(requested, duplicateIsError: false); return ValueTask.CompletedTask; }
        private void Finish(long bytes, bool duplicateIsError)
        {
            var state = budget.Binding.State;
            lock (state.Sync)
            {
                if (complete) { if (duplicateIsError) throw new InvalidOperationException("The write epoch has completed."); return; }
                var update = state.Ledger.RecordWritten(budget.Claim, bytes);
                state.Ledger = update.State; budget.Claim = update.Reservation;
                complete = true; budget.InFlightBytes -= requested;
                if (--state.ActiveWrites == 0) state.Drained.TrySetResult();
            }
        }
    }

    private sealed class Entry(Destination binding, ImmutableArray<string> segments, bool directory,
        IProgress<GameCopyResourceWait>? waiting) : IGameCopyEntryLease
    {
        internal readonly Destination Binding = binding;
        internal readonly ImmutableArray<string> Segments = segments;
        internal readonly bool Directory = directory;
        internal readonly IProgress<GameCopyResourceWait>? Waiting = waiting;
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal LinuxCopyDestination.Target? Target;
        internal bool Closed;
        public Guid OperationId => Binding.Id;
        public CopyVolumeKey Volume => CopyVolumeKey.From(Binding.Evidence);
        public string TargetIdentity => Target?.Identity ?? "released";
        public async ValueTask DisposeAsync()
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Closed) return;
                Closed = true;
                Exception? failure = null; ResourceOutput[] outputs;
                lock (Binding.State.Sync) outputs = Binding.Budget!.Outputs.ToArray();
                foreach (var output in outputs)
                    try { await output.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure ??= error; }
                await Binding.State.Namespace.WaitAsync().ConfigureAwait(false);
                try { DropTarget(this); }
                finally { Binding.State.Namespace.Release(); }
                lock (Binding.State.Sync) { Binding.Entry = null; Binding.EntryPending = false; }
                if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            finally { Gate.Release(); }
        }
    }

    private sealed class Destination(CopyDestinationResources owner, Volume state, Guid id,
        CopyVolumeEvidence evidence, LinuxCopyDestination native) : IGameCopyDestination
    {
        internal readonly CopyDestinationResources Owner = owner;
        internal readonly Volume State = state;
        internal readonly Guid Id = id;
        internal readonly CopyVolumeEvidence Evidence = evidence;
        internal readonly LinuxCopyDestination Native = native;
        internal Budget? Budget;
        internal Entry? Entry;
        internal bool EntryPending;
        internal TaskCompletionSource? PendingEntry;
        public string DestinationPath => Native.DestinationPath;
        internal Budget RequireBudget()
        {
            if (Budget?.Failure is { } failure) throw failure;
            if (Budget is null || Budget.Closing) throw new InvalidOperationException("An active actual-size reservation is required.");
            return Budget;
        }
        private Entry RequireEntry(ImmutableArray<string> segments)
        {
            lock (State.Sync)
            {
                RequireBudget();
                if (Entry is null || Entry.Closed || !segments.SequenceEqual(Entry.Segments))
                    throw new InvalidOperationException("Acquire this entry before inspecting or mutating it.");
                return Entry;
            }
        }
        public ValueTask ValidateAsync(CancellationToken token) => Native.ValidateAsync(token);
        public ValueTask<long?> GetAvailableBytesAsync(CancellationToken token) => Native.GetAvailableBytesAsync(token);
        private static bool IsOwnEmpty(Entry entry, CopyTargetSnapshot snapshot) => entry.Target is { Created: true } target
            && snapshot.Evidence is { Length: 0 } file && file.Identity.EndsWith("/" + target.Identity, StringComparison.Ordinal)
            && file.ChangeEvidence.StartsWith(target.InitialChange + ":", StringComparison.Ordinal);
        public async ValueTask<CopyTargetSnapshot> InspectAsync(ImmutableArray<string> segments, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, Budget?.Lifetime.Token ?? default);
            token = linked.Token;
            var entry = RequireEntry(segments); await entry.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(entry.Closed, entry);
                await RefreshAsync(entry, token).ConfigureAwait(false);
                var snapshot = await Native.InspectAsync(segments, token).ConfigureAwait(false);
                return IsOwnEmpty(entry, snapshot) ? new(CopyTargetKind.Missing, null) : snapshot;
            }
            finally { entry.Gate.Release(); }
        }
        public async ValueTask<GameCopySkipReason?> EnsureDirectoryAsync(ImmutableArray<string> segments, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, Budget?.Lifetime.Token ?? default);
            token = linked.Token;
            var entry = RequireEntry(segments); await entry.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(entry.Closed, entry);
                await RefreshAsync(entry, token).ConfigureAwait(false);
                return await Native.EnsureDirectoryAsync(segments, token).ConfigureAwait(false);
            }
            finally { entry.Gate.Release(); }
        }
        public async ValueTask<CopyOutputOpenResult> OpenOutputAsync(ImmutableArray<string> segments, CopyFileEvidence? consent, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, Budget?.Lifetime.Token ?? default);
            token = linked.Token;
            var entry = RequireEntry(segments); await entry.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(entry.Closed, entry);
                lock (State.Sync)
                    if (Budget!.Outputs.Count != 0) throw new InvalidOperationException("Close the current output before reopening an entry.");
                await RefreshAsync(entry, token).ConfigureAwait(false);
                var snapshot = await Native.InspectAsync(segments, token).ConfigureAwait(false);
                if (snapshot.IsConfined && snapshot.Kind == CopyTargetKind.Missing)
                    return new(null, TargetChanged: true); // Reacquire/create/register under the namespace guard.
                if (consent is null && IsOwnEmpty(entry, snapshot)) consent = snapshot.Evidence;
                var result = await Native.OpenOutputAsync(segments, consent, token).ConfigureAwait(false);
                if (result.File is null) return result;
                entry.Target = entry.Target! with { Created = false };
                var output = new ResourceOutput(Budget!, result.File);
                lock (State.Sync) Budget!.Outputs.Add(output);
                return result with { File = output };
            }
            finally { entry.Gate.Release(); }
        }
        // The wrapped native session belongs to the caller; this binding owns no root handle.
        public ValueTask DisposeAsync() => Budget is null ? ValueTask.CompletedTask : Budget.DisposeAsync();
    }

    private sealed class ResourceOutput : IGameCopyOutputFile
    {
        private readonly Budget budget;
        private readonly IGameCopyOutputFile file;
        private readonly AccountedStream stream;
        private readonly SemaphoreSlim gate = new(1, 1);
        private volatile bool closed;
        internal ResourceOutput(Budget budget, IGameCopyOutputFile file)
        { this.budget = budget; this.file = file; stream = new(this); }
        public Stream Output => stream;
        internal Budget OwnerBudget => budget;
        internal async Task FlushForSampleAsync(CancellationToken token)
        {
            // No stream writer gate: a writer may be waiting at the admission fence. Its
            // already-started native chunks have drained, and CopyAccessStream serializes flush.
            lock (budget.Binding.State.Sync) { if (closed) return; }
            try { await file.Output.FlushAsync(token).ConfigureAwait(false); }
            catch (ObjectDisposedException) when (closed) { }
        }
        public async ValueTask DisposeAsync()
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (closed) return;
                try { await file.DisposeAsync().ConfigureAwait(false); }
                finally { lock (budget.Binding.State.Sync) { closed = true; budget.Outputs.Remove(this); } }
            }
            finally { gate.Release(); }
        }
        private sealed class AccountedStream(ResourceOutput owner) : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => !owner.closed;
            public override long Length => owner.file.Output.Length;
            public override long Position { get => owner.file.Output.Position; set => throw new NotSupportedException(); }
            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
            {
                lock (owner.budget.Binding.State.Sync) owner.budget.Binding.RequireBudget();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, owner.budget.Lifetime.Token);
                token = linked.Token;
                await owner.gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    ObjectDisposedException.ThrowIf(owner.closed, this);
                    while (!buffer.IsEmpty)
                    {
                        var chunk = buffer[..Math.Min(buffer.Length, 65536)];
                        await using var write = await owner.budget.BeginWriteAsync(chunk.Length, token).ConfigureAwait(false);
                        long? before = PositionOrUnknown();
                        try
                        {
                            await owner.file.Output.WriteAsync(chunk, token).ConfigureAwait(false);
                        }
                        catch
                        {
                            var after = PositionOrUnknown();
                            if (before is not null && after >= before && after - before <= chunk.Length)
                                await write.CompleteAsync(after.Value - before.Value).ConfigureAwait(false);
                            throw;
                        }
                        await write.CompleteAsync(chunk.Length).ConfigureAwait(false);
                        buffer = buffer[chunk.Length..];
                    }
                }
                finally { owner.gate.Release(); }
            }
            private long? PositionOrUnknown() { try { return owner.file.Output.Position; } catch (NotSupportedException) { return null; } }
            public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
            public override void Flush() => FlushAsync(default).GetAwaiter().GetResult();
            public override Task FlushAsync(CancellationToken token) => owner.FlushForSampleAsync(token);
            public override ValueTask DisposeAsync() => owner.DisposeAsync();
            protected override void Dispose(bool disposing) { if (disposing) owner.DisposeAsync().AsTask().GetAwaiter().GetResult(); base.Dispose(disposing); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
