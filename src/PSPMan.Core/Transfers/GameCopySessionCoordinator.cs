using System.Collections.Immutable;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;

namespace PSPMan.Core.Transfers;

/// <summary>Session-only operation ownership. Dispose before its caller-owned device session.</summary>
public sealed class GameCopySessionCoordinator : IDeviceRemovalInterceptor, IAsyncDisposable
{
    private const string RetentionWarning = "Incomplete or incorrect files remain on the PSP. Overwritten files are not restored.";
    private readonly object gate = new();
    private readonly DeviceSessionService session;
    private readonly GameCopyService engine;
    private readonly MountedPathSyntax syntax;
    private readonly Dictionary<Guid, CopyOperation> operations = [], active = [];
    private readonly Dictionary<DeviceKey, HashSet<CopyOperation>> dependencies = [];
    private readonly Dictionary<DeviceKey, TaskCompletionSource> removals = [];
    private GameRecord? selectedGame;
    private Guid? legacyOperation, closeDecision;
    private bool disposed;
    private Task? disposeTask;
    private Task<bool>? closeDrain;

    public GameCopySessionCoordinator(DeviceSessionService session, GameCopyService engine, MountedPathSyntax syntax)
    {
        this.session = session;
        this.engine = engine;
        this.syntax = syntax;
        session.SnapshotChanged += OnSessionChanged;
    }

    public bool IsBusy { get { lock (gate) return active.Count != 0; } }
    public bool IsAdmissionOpen { get { lock (gate) return !disposed && closeDecision is null; } }
    public ImmutableArray<GameCopyOperationSnapshot> Operations
    { get { lock (gate) return operations.Values.Select(o => o.Snapshot).ToImmutableArray(); } }

    /// <summary>Compatibility projection for the existing single-row screen, removed when T19 activates batches.</summary>
    public GameCopySnapshot? Snapshot
    { get { lock (gate) return legacyOperation is { } id ? operations[id].Snapshot.Snapshot : null; } }

    public void SetSelectedGame(GameRecord? game)
    { lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); selectedGame = game; } }

    /// <summary>Capture/register the entire ready subset against one observation before scheduling any runner.</summary>
    public GameCopyAdmission Submit(IEnumerable<GameCopySubmission> submissions,
        IProgress<GameCopyOperationSnapshot>? observer = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        // Enumerators are caller code and must never run under the registry gate.
        var items = submissions.ToImmutableArray();
        var runners = new List<CopyOperation>();
        GameCopyAdmission result;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var observation = session.Snapshot;
            var capture = GameCopySubmissionRules.Capture(items, observation.Inventory, observation.Associations, syntax);
            var requests = capture.Accepted.ToDictionary(r => r.OperationId);
            var incidents = capture.Incidents.ToDictionary(i => i.SubmissionIndex);
            var accepted = ImmutableArray.CreateBuilder<GameCopyRequest>();
            for (var index = 0; index < items.Length; index++)
            {
                if (incidents.ContainsKey(index)) continue;
                var request = requests[items[index].OperationId];
                var error = closeDecision is not null ? new GameCopyError(GameCopyErrorCode.CopyInProgress, "Copy admission is closed.")
                    : operations.ContainsKey(request.OperationId) ? new(GameCopyErrorCode.InvalidSubmission, "This operation identity has already been used.")
                    : removals.ContainsKey(request.Source.Device) || removals.ContainsKey(request.Destination.Device)
                        ? new(GameCopyErrorCode.CopyInProgress, "A required device is being removed.")
                        : ValidateCaptured(request, observation);
                if (error is not null) { incidents.Add(index, new(index, request.Game.Id, error)); continue; }
                var owner = Register(request, observer, null, cancellationToken);
                runners.Add(owner); accepted.Add(request);
            }
            result = new(accepted.ToImmutable(), incidents.Values.OrderBy(i => i.SubmissionIndex).ToImmutableArray());
        }
        // All owners/completions are reserved before a reentrant observer can stop/dispose the batch.
        foreach (var owner in runners) _ = Task.Run(() => RunAsync(owner));
        return result;
    }

    public Task<GameCopyResult> GetCompletion(Guid operationId)
    { lock (gate) return operations.TryGetValue(operationId, out var owner) ? owner.Completion.Task : throw new KeyNotFoundException("Unknown copy operation."); }

    /// <summary>Single-copy compatibility entry point. Public multi-submission composition remains unwired until T19.</summary>
    public Task<GameCopyResult> StartAsync(IProgress<GameCopySnapshot>? progress = null, CancellationToken cancellationToken = default)
    {
        CopyOperation owner;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (active.Count != 0 || closeDecision is not null) throw Error(GameCopyErrorCode.CopyInProgress, "A copy is already active or admission is closed.");
            var game = selectedGame ?? throw Error(GameCopyErrorCode.NoGameSelected, "Select a game to copy.");
            var request = Capture(session.Snapshot, game);
            if (removals.ContainsKey(request.Source.Device) || removals.ContainsKey(request.Destination.Device))
                throw Error(GameCopyErrorCode.CopyInProgress, "A required device is being removed.");
            owner = Register(request, null, progress, cancellationToken);
            legacyOperation = request.OperationId;
        }
        _ = Task.Run(() => RunAsync(owner));
        return owner.Completion.Task;
    }

    private CopyOperation Register(GameCopyRequest request, IProgress<GameCopyOperationSnapshot>? observer,
        IProgress<GameCopySnapshot>? legacyObserver, CancellationToken token)
    {
        var owner = new CopyOperation(request, observer, legacyObserver, token);
        operations.Add(request.OperationId, owner); active.Add(request.OperationId, owner);
        foreach (var device in new[] { request.Source.Device, request.Destination.Device }.Distinct())
        {
            if (!dependencies.TryGetValue(device, out var owners)) dependencies.Add(device, owners = []);
            owners.Add(owner);
        }
        return owner;
    }

    public Task CancelAsync()
    { Guid? id; lock (gate) id = legacyOperation; return id is { } value ? CancelAsync(value) : Task.CompletedTask; }

    public Task CancelAsync(Guid operationId) => StopAsync(operationId, null);
    public Task FailAndStopAsync(Guid operationId, GameCopyError error)
    { ArgumentNullException.ThrowIfNull(error); return StopAsync(operationId, error); }
    public Task FailAndStopAsync(GameCopyError error)
    { Guid? id; lock (gate) id = legacyOperation; return id is { } value ? FailAndStopAsync(value, error) : Task.CompletedTask; }

    private Task StopAsync(Guid operationId, GameCopyError? error)
    {
        CopyOperation owner;
        lock (gate)
        {
            if (!active.TryGetValue(operationId, out owner!)) return Task.CompletedTask;
            if (error is null) owner.CancelRequested = true; else FailLocked(owner, error);
        }
        Signal(owner);
        return owner.Completion.Task;
    }

    /// <summary>Stops the current set. Call BeginClose first when additions must also be excluded.</summary>
    public Task CancelAllAsync()
    {
        CopyOperation[] owners;
        lock (gate) { owners = active.Values.ToArray(); foreach (var owner in owners) owner.CancelRequested = true; }
        foreach (var owner in owners) Signal(owner);
        return Task.WhenAll(owners.Select(o => o.Completion.Task));
    }

    /// <summary>One identity-bound close decision; completion cannot reopen admission.</summary>
    public Guid BeginClose()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return closeDecision ??= Guid.NewGuid();
        }
    }

    public Task<bool> ResolveCloseAsync(Guid decisionId, bool accept)
    {
        CopyOperation[] owners; Task[] waiting; TaskCompletionSource<bool> completion;
        lock (gate)
        {
            if (disposed || closeDecision != decisionId) return Task.FromResult(false);
            if (closeDrain is not null) return closeDrain;
            if (!accept) { closeDecision = null; return Task.FromResult(true); }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            closeDrain = completion.Task; // Reserve before cancellation callbacks can reenter.
            owners = active.Values.ToArray(); foreach (var owner in owners) owner.CancelRequested = true;
            waiting = removals.Values.Select(r => r.Task).ToArray();
        }
        foreach (var owner in owners) Signal(owner);
        _ = FinishCloseAsync(owners, waiting, completion);
        return completion.Task;
    }

    private static async Task FinishCloseAsync(CopyOperation[] owners, Task[] waiting, TaskCompletionSource<bool> completion)
    {
        try { await DrainAsync(owners, waiting).ConfigureAwait(false); completion.TrySetResult(true); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    public async ValueTask RemoveDeviceAsync(DeviceKey device, DeviceInventorySnapshot observation,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource reservation; CopyOperation[] owners; DeviceSnapshot expected;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            expected = observation.Devices.SingleOrDefault(d => d.Key == device)
                ?? throw Error(GameCopyErrorCode.DestinationUnavailable, "The removal observation is unavailable.");
            ValidateRemoval(expected, session.Snapshot.Inventory);
            if (removals.ContainsKey(device)) throw Error(GameCopyErrorCode.CopyInProgress, "The device already has a pending removal request.");
            reservation = new(TaskCreationOptions.RunContinuationsAsynchronously); removals.Add(device, reservation);
            owners = dependencies.TryGetValue(device, out var dependants) ? dependants.ToArray() : [];
            foreach (var owner in owners) FailLocked(owner, new(owner.Request.Source.Device == device ? GameCopyErrorCode.SourceChanged : GameCopyErrorCode.DestinationChanged,
                "Copy stopped because device removal was requested."));
        }
        try
        {
            foreach (var owner in owners) Signal(owner);
            // Caller cancellation cannot abandon owned file/resource release.
            await Task.WhenAll(owners.Select(o => o.Completion.Task)).ConfigureAwait(false);
            DeviceInventorySnapshot fresh;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                fresh = session.Snapshot.Inventory; ValidateRemoval(expected, fresh);
            }
            await session.RemoveDeviceAsync(device, cancellationToken, fresh).ConfigureAwait(false);
        }
        finally { lock (gate) { removals.Remove(device); reservation.TrySetResult(); } }
    }

    private async Task RunAsync(CopyOperation owner)
    {
        GameCopyResult result;
        try
        {
            bool stop;
            lock (gate)
            {
                if (ValidateCaptured(owner.Request, session.Snapshot) is { } changed) FailLocked(owner, changed);
                stop = owner.Failure is not null || owner.CancelRequested;
            }
            if (stop) Signal(owner);
            result = await engine.CopyAsync(owner.Request, new InlineProgress(value => OnProgress(owner, value)), owner.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var detail = error is GameCopyException copy ? copy.Error : new GameCopyError(GameCopyErrorCode.ReadError, "Copy operation failed.");
            if (!detail.Message.Contains(RetentionWarning, StringComparison.Ordinal)) detail = detail with { Message = detail.Message + " " + RetentionWarning };
            var cancelled = error is OperationCanceledException;
            result = new(owner.Request.OperationId, cancelled ? GameCopyState.Cancelled : GameCopyState.Failed,
                owner.Snapshot.Snapshot.Destination, 0, cancelled ? new(GameCopyErrorCode.Cancelled, "Copy cancelled. " + RetentionWarning) : detail);
        }
        GameCopyOperationSnapshot final;
        lock (gate)
        {
            if (owner.Failure is null && ValidateCaptured(owner.Request, session.Snapshot) is { } changed) FailLocked(owner, changed);
            if (owner.Failure is { } failure && result.Error?.Code != GameCopyErrorCode.CloseError)
                result = result with { State = GameCopyState.Failed, Error = failure };
            else if ((owner.CancelRequested || owner.Cancellation.IsCancellationRequested) && result.State is GameCopyState.Completed or GameCopyState.CompletedWithSkippedFiles)
                result = result with { State = GameCopyState.Cancelled, Error = new(GameCopyErrorCode.Cancelled, "Copy cancelled. " + RetentionWarning) };
            var progress = (owner.Terminal ?? owner.Snapshot.Snapshot).Progress;
            if (result.State is GameCopyState.Failed or GameCopyState.Cancelled && progress?.Fraction == 1) progress = progress with { Fraction = .999m };
            final = Update(owner, new(result.OperationId, owner.Request.Game.Title, result.Destination, result.State, progress, null, result.Error, result.SkippedEntries));
            active.Remove(owner.Request.OperationId);
            foreach (var device in new[] { owner.Request.Source.Device, owner.Request.Destination.Device }.Distinct())
                if (dependencies.TryGetValue(device, out var dependants)) { dependants.Remove(owner); if (dependants.Count == 0) dependencies.Remove(device); }
            owner.Completion.TrySetResult(result);
        }
        owner.Cancellation.Dispose();
        try { Notify(owner, final); } catch { /* Authoritative result/ownership must survive a failed terminal consumer. */ }
        finally { lock (gate) { owner.Observer = null; owner.LegacyObserver = null; } }
    }

    private void OnProgress(CopyOperation owner, GameCopySnapshot value)
    {
        GameCopyOperationSnapshot publication;
        lock (gate)
        {
            if (disposed || !active.TryGetValue(owner.Request.OperationId, out var current) || current != owner || value.OperationId != owner.Request.OperationId) return;
            if (GameCopyRules.IsTerminal(value.State)) { owner.Terminal = value; return; }
            if (owner.Failure is { } failure) value = value with { State = GameCopyState.Stopping, Prompt = null, Error = failure, WaitReason = null };
            publication = Update(owner, value);
        }
        Notify(owner, publication);
    }

    private static GameCopyOperationSnapshot Update(CopyOperation owner, GameCopySnapshot value)
        => owner.Snapshot = new(owner.Request, checked(owner.Snapshot.Revision + 1), value);

    private void Notify(CopyOperation owner, GameCopyOperationSnapshot value)
    {
        IProgress<GameCopyOperationSnapshot>? observer; IProgress<GameCopySnapshot>? legacy;
        lock (gate)
        {
            if (disposed || owner.Snapshot.Revision != value.Revision) return;
            observer = owner.Observer; legacy = owner.LegacyObserver;
        }
        observer?.Report(value); legacy?.Report(value.Snapshot);
    }

    private void OnSessionChanged(object? sender, DeviceSessionSnapshot observation)
    {
        CopyOperation[] affected;
        lock (gate)
        {
            if (disposed) return;
            affected = dependencies.Values.SelectMany(owners => owners).Distinct()
                .Where(owner => ValidateCaptured(owner.Request, observation) is not null).ToArray();
            foreach (var owner in affected) FailLocked(owner, ValidateCaptured(owner.Request, observation)!);
        }
        foreach (var owner in affected) Signal(owner);
    }

    private static GameCopyError? ValidateCaptured(GameCopyRequest request, DeviceSessionSnapshot observation)
    {
        var required = request.Game.Medium == GameMedium.Dvd ? DeviceCapabilities.OpticalSource : DeviceCapabilities.RemovableSource;
        if (!Matches(request.Source, observation, required)) return new(GameCopyErrorCode.SourceChanged, "The source device or mounted volume changed or became unavailable.");
        if (!Matches(request.Destination, observation, DeviceCapabilities.PspDestination)) return new(GameCopyErrorCode.DestinationChanged, "The PSP or mounted volume changed or became unavailable.");
        return null;
    }

    private GameCopyRequest Capture(DeviceSessionSnapshot observation, GameRecord game)
    {
        var source = DeviceAssociationRules.ResolveSourceVolume(game.Medium, observation.Inventory, observation.Associations);
        if (source.Status != SourceResolutionStatus.Selected || source.SelectedVolume is null)
            throw Error(GameCopyErrorCode.SourceUnavailable, "Select an available source device and mounted volume.");
        var destination = DeviceAssociationRules.ResolveDestination(game.Id, observation.Inventory, observation.Associations);
        if (destination.Status != DestinationResolutionStatus.Selected)
            throw Error(GameCopyErrorCode.DestinationUnavailable, destination.Status == DestinationResolutionStatus.ChooseDevice
                ? "Select the PSP to receive this game." : "No selected PSP is connected and available.");
        var sourceDevice = observation.Inventory.Devices.Single(d => d.Key == source.SelectedDevice);
        var psp = observation.Inventory.Devices.Single(d => d.Key == destination.SelectedDevice);
        var usable = psp.Volumes.Where(v => v.Key.Device == psp.Key && v.State == DeviceState.Ready && !string.IsNullOrWhiteSpace(v.Root)).ToArray();
        if (usable.Length != 1) throw Error(GameCopyErrorCode.DestinationUnavailable, "The PSP must have one usable mounted volume.");
        var request = new GameCopyRequest(Guid.NewGuid(), game, Evidence(sourceDevice, source.SelectedVolume), Evidence(psp, usable[0]));
        if (!Matches(request.Source, observation)) throw Error(GameCopyErrorCode.SourceUnavailable, "The selected source is not ready.");
        if (!Matches(request.Destination, observation)) throw Error(GameCopyErrorCode.DestinationUnavailable, "The selected PSP is not ready.");
        if (MountedGamePathRules.Resolve(request.Source.Root, game.Location, syntax).Path is null
            || MountedGamePathRules.Resolve(request.Destination.Root, "/ISO", syntax).Path is null)
            throw Error(GameCopyErrorCode.SourceUnavailable, "A selected mounted path is invalid.");
        return request;
    }

    private CopyVolumeEvidence Evidence(DeviceSnapshot device, VolumeSnapshot volume) => new(device.Key, device.Identity,
        device.MediaGeneration, volume.Key, volume.Identity, volume.Root!, syntax);

    private static bool Matches(CopyVolumeEvidence evidence, DeviceSessionSnapshot observation, DeviceCapabilities required = DeviceCapabilities.None)
    {
        if (observation.ProviderErrors.ContainsKey(evidence.Device.ProviderId)) return false;
        var device = observation.Inventory.Devices.FirstOrDefault(d => d.Key == evidence.Device);
        var volume = device?.Volumes.FirstOrDefault(v => v.Key == evidence.Volume);
        return device is { State: DeviceState.Ready } && device.Identity == evidence.DeviceIdentity && device.MediaGeneration == evidence.MediaGeneration
            && (device.Capabilities & required) == required
            && (required != DeviceCapabilities.PspDestination || device.Volumes.Count(v => v.State == DeviceState.Ready && !string.IsNullOrWhiteSpace(v.Root)) == 1)
            && (required is not (DeviceCapabilities.RemovableSource or DeviceCapabilities.OpticalSource)
                || device.Medium != DeviceMedium.Psp && (device.Capabilities & DeviceCapabilities.PspDestination) == 0)
            && volume is { State: DeviceState.Ready } && volume.Identity == evidence.VolumeIdentity && volume.Root == evidence.Root;
    }

    private static void ValidateRemoval(DeviceSnapshot expected, DeviceInventorySnapshot inventory)
    {
        var current = inventory.Devices.FirstOrDefault(d => d.Key == expected.Key);
        if (current is null || current.Identity != expected.Identity || current.MediaGeneration != expected.MediaGeneration
            || current.State != expected.State || current.Capabilities != expected.Capabilities
            || current.State is DeviceState.Unavailable or DeviceState.Detecting or DeviceState.Mounting or DeviceState.Removing
            || (current.Capabilities & DeviceCapabilities.SafeRemoval) == 0
            || current.Volumes.Length != expected.Volumes.Length || expected.Volumes.Any(v => !current.Volumes.Any(c =>
                c.Key == v.Key && c.Identity == v.Identity && c.Root == v.Root && c.State == v.State)))
            throw Error(GameCopyErrorCode.DestinationChanged, "The requested device changed or is no longer available for removal.");
    }

    private static void FailLocked(CopyOperation owner, GameCopyError error)
    { owner.Failure ??= error with { Message = error.Message + " " + RetentionWarning }; }

    private void Signal(CopyOperation owner)
    {
        try { owner.Cancellation.Cancel(); }
        catch (ObjectDisposedException) { } // Completion won the race.
        catch (AggregateException)
        {
            lock (gate)
                if (active.TryGetValue(owner.Request.OperationId, out var current) && current == owner)
                    FailLocked(owner, new(GameCopyErrorCode.CloseError, "A cancellation callback failed."));
        }
    }
    private static GameCopyException Error(GameCopyErrorCode code, string message) => new(new(code, message));

    public ValueTask DisposeAsync()
    {
        CopyOperation[] owners; Task[] waiting; TaskCompletionSource completion;
        lock (gate)
        {
            if (disposeTask is not null) return new(disposeTask);
            disposed = true; session.SnapshotChanged -= OnSessionChanged;
            owners = active.Values.ToArray(); foreach (var owner in owners) owner.CancelRequested = true;
            waiting = removals.Values.Select(r => r.Task).ToArray();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); disposeTask = completion.Task;
        }
        foreach (var owner in owners) Signal(owner);
        _ = FinishDisposalAsync(owners, waiting, completion);
        return new(completion.Task);
    }

    private static async Task DrainAsync(CopyOperation[] owners, Task[] waiting)
    {
        await Task.WhenAll(owners.Select(o => o.Completion.Task)).ConfigureAwait(false);
        await Task.WhenAll(waiting).ConfigureAwait(false);
    }
    private static async Task FinishDisposalAsync(CopyOperation[] owners, Task[] waiting, TaskCompletionSource completion)
    {
        try { await DrainAsync(owners, waiting).ConfigureAwait(false); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private sealed class CopyOperation
    {
        internal CopyOperation(GameCopyRequest request, IProgress<GameCopyOperationSnapshot>? observer,
            IProgress<GameCopySnapshot>? legacyObserver, CancellationToken token)
        {
            Request = request; Observer = observer; LegacyObserver = legacyObserver;
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var separator = request.Destination.Syntax == MountedPathSyntax.Windows ? "\\" : "/";
            Snapshot = new(request, 0, new(request.OperationId, request.Game.Title,
                request.Destination.Root.TrimEnd('/', '\\') + separator + "ISO", GameCopyState.Preparing));
        }
        internal GameCopyRequest Request { get; }
        internal IProgress<GameCopyOperationSnapshot>? Observer;
        internal IProgress<GameCopySnapshot>? LegacyObserver;
        internal GameCopyOperationSnapshot Snapshot;
        internal CancellationTokenSource Cancellation { get; }
        internal TaskCompletionSource<GameCopyResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal GameCopyError? Failure;
        internal GameCopySnapshot? Terminal;
        internal bool CancelRequested;
    }
    private sealed class InlineProgress(Action<GameCopySnapshot> report) : IProgress<GameCopySnapshot>
    { public void Report(GameCopySnapshot value) => report(value); }
}
