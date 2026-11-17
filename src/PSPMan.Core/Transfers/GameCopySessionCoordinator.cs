using PSPMan.Core.Devices;
using PSPMan.Core.Games;

namespace PSPMan.Core.Transfers;

/// <summary>Observes a caller-owned session. Dispose this coordinator before disposing that session.</summary>
public sealed class GameCopySessionCoordinator : IDeviceRemovalInterceptor, IAsyncDisposable
{
    private const string RetentionWarning = "Incomplete or incorrect files remain on the PSP. Overwritten files are not restored.";
    private readonly object gate = new();
    private readonly DeviceSessionService session;
    private readonly GameCopyService engine;
    private readonly MountedPathSyntax syntax;
    private readonly Dictionary<DeviceKey, TaskCompletionSource> removals = [];
    private GameRecord? selectedGame;
    private ActiveCopy? active;
    private GameCopySnapshot? snapshot;
    private bool disposed;
    private Task? disposeTask;

    public GameCopySessionCoordinator(DeviceSessionService session, GameCopyService engine, MountedPathSyntax syntax)
    {
        this.session = session;
        this.engine = engine;
        this.syntax = syntax;
        session.SnapshotChanged += OnSessionChanged;
    }

    public bool IsBusy { get { lock (gate) return active is not null; } }
    public GameCopySnapshot? Snapshot { get { lock (gate) return snapshot; } }

    public void SetSelectedGame(GameRecord? game)
    {
        ActiveCopy? stop = null;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            selectedGame = game;
            if (active is { } operation && operation.Request.Game != game)
                stop = FailLocked(operation, new(GameCopyErrorCode.SourceChanged, "The selected game changed."));
        }
        Cancel(stop);
    }

    /// <summary>Rejects a second start rather than queuing it. Progress observers must return promptly.</summary>
    public Task<GameCopyResult> StartAsync(IProgress<GameCopySnapshot>? progress = null, CancellationToken cancellationToken = default)
    {
        ActiveCopy operation;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (active is not null) throw Error(GameCopyErrorCode.CopyInProgress, "A copy is already active or stopping.");
            var game = selectedGame ?? throw Error(GameCopyErrorCode.NoGameSelected, "Select a game to copy.");
            var observation = session.Snapshot;
            var request = Capture(observation, game);
            if (removals.ContainsKey(request.Source.Device) || removals.ContainsKey(request.Destination.Device))
                throw Error(GameCopyErrorCode.CopyInProgress, "A required device is being removed.");
            operation = new(request, observation.Associations, progress, cancellationToken);
            active = operation;
        }
        // Capture/reserve synchronously, then run potentially synchronous adapter work off the caller.
        _ = Task.Run(() => RunAsync(operation));
        return operation.Completion.Task;
    }

    public async Task CancelAsync()
    {
        ActiveCopy? operation;
        lock (gate)
        {
            operation = active;
            if (operation is not null) operation.CancelRequested = true;
        }
        Cancel(operation);
        if (operation is not null) await operation.Completion.Task.ConfigureAwait(false);
    }

    public async Task FailAndStopAsync(GameCopyError error)
    {
        ActiveCopy? operation;
        lock (gate) operation = active is { } current ? FailLocked(current, error) : null;
        Cancel(operation);
        if (operation is not null) await operation.Completion.Task.ConfigureAwait(false);
    }

    public async ValueTask RemoveDeviceAsync(DeviceKey device, DeviceInventorySnapshot observation,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource reservation;
        ActiveCopy? operation;
        DeviceSnapshot expected;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            expected = observation.Devices.SingleOrDefault(d => d.Key == device)
                ?? throw Error(GameCopyErrorCode.DestinationUnavailable, "The removal observation is unavailable.");
            ValidateRemoval(expected, session.Snapshot.Inventory);
            if (removals.ContainsKey(device)) throw Error(GameCopyErrorCode.CopyInProgress, "The device already has a pending removal request.");
            reservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            removals.Add(device, reservation);
            operation = active is { } current && (current.Request.Source.Device == device || current.Request.Destination.Device == device)
                ? FailLocked(current, new(GameCopyErrorCode.DestinationChanged, "Copy stopped because device removal was requested.")) : null;
        }
        try
        {
            Cancel(operation);
            // Keep the reservation until files are closed, even when the removal caller cancels.
            if (operation is not null) await operation.Completion.Task.ConfigureAwait(false);
            DeviceInventorySnapshot fresh;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                fresh = session.Snapshot.Inventory;
                ValidateRemoval(expected, fresh);
            }
            await session.RemoveDeviceAsync(device, cancellationToken, fresh).ConfigureAwait(false);
            // Session acceptance publishes Removing before returning; it then owns native completion.
        }
        finally
        {
            lock (gate) { removals.Remove(device); reservation.TrySetResult(); }
        }
    }

    private async Task RunAsync(ActiveCopy operation)
    {
        GameCopyResult result;
        try
        {
            lock (gate)
            {
                if (ValidateActive(operation, session.Snapshot) is { } changed) FailLocked(operation, changed);
            }
            if (operation.Failure is not null || operation.CancelRequested) Cancel(operation);
            result = await engine.CopyAsync(operation.Request, new InlineProgress(value => OnProgress(operation, value)), operation.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var detail = error is GameCopyException copy ? copy.Error : new GameCopyError(GameCopyErrorCode.ReadError, "Copy operation failed.");
            if (!detail.Message.Contains(RetentionWarning, StringComparison.Ordinal))
                detail = detail with { Message = detail.Message + " " + RetentionWarning };
            var cancelled = error is OperationCanceledException;
            result = new(operation.Request.OperationId, cancelled ? GameCopyState.Cancelled : GameCopyState.Failed,
                snapshot?.Destination ?? operation.Request.Destination.Root, 0,
                cancelled ? new(GameCopyErrorCode.Cancelled, "Copy cancelled. " + RetentionWarning) : detail);
        }
        GameCopySnapshot final;
        lock (gate)
        {
            if (operation.Failure is null && ValidateActive(operation, session.Snapshot) is { } changed)
                FailLocked(operation, changed);
            if (operation.Failure is { } failure && result.Error?.Code != GameCopyErrorCode.CloseError)
                result = result with { State = GameCopyState.Failed, Error = failure };
            else if ((operation.CancelRequested || operation.Cancellation.IsCancellationRequested) && result.State is GameCopyState.Completed or GameCopyState.CompletedWithSkippedFiles)
                result = result with { State = GameCopyState.Cancelled, Error = new(GameCopyErrorCode.Cancelled, "Copy cancelled. " + RetentionWarning) };
            var basis = operation.Terminal ?? (snapshot?.OperationId == operation.Request.OperationId ? snapshot : null);
            var progress = basis?.Progress;
            if (result.State is GameCopyState.Failed or GameCopyState.Cancelled && progress?.Fraction == 1)
                progress = progress with { Fraction = .999m };
            final = new(result.OperationId, operation.Request.Game.Title, result.Destination, result.State,
                progress, null, result.Error, result.SkippedEntries);
            snapshot = final;
            active = null;
            operation.Completion.TrySetResult(result);
        }
        operation.Cancellation.Dispose();
        // A failed presentation consumer cannot strand ownership or prevent removal after release.
        try { Notify(operation, final); } catch { /* The authoritative terminal snapshot remains available. */ }
    }

    private void OnProgress(ActiveCopy operation, GameCopySnapshot value)
    {
        lock (gate)
        {
            if (disposed || active != operation || value.OperationId != operation.Request.OperationId) return;
            if (GameCopyRules.IsTerminal(value.State)) { operation.Terminal = value; return; }
            if (operation.Failure is { } failure) value = value with { State = GameCopyState.Stopping, Prompt = null, Error = failure };
            snapshot = value;
        }
        Notify(operation, value);
    }

    private void Notify(ActiveCopy operation, GameCopySnapshot value)
    {
        lock (gate) { if (disposed || snapshot?.OperationId != value.OperationId) return; }
        operation.Observer?.Report(value);
    }

    private void OnSessionChanged(object? sender, DeviceSessionSnapshot observation)
    {
        ActiveCopy? stop = null;
        lock (gate)
        {
            if (!disposed && active is { } operation && ValidateActive(operation, observation) is { } error)
                stop = FailLocked(operation, error);
        }
        Cancel(stop);
    }

    private GameCopyError? ValidateActive(ActiveCopy operation, DeviceSessionSnapshot observation)
    {
        if (selectedGame != operation.Request.Game) return new(GameCopyErrorCode.SourceChanged, "The selected game changed.");
        var optical = operation.Request.Game.Medium == GameMedium.Dvd;
        var before = operation.Associations;
        var now = observation.Associations;
        if ((optical ? before.OpticalSource != now.OpticalSource || before.OpticalVolume != now.OpticalVolume
                : before.RemovableSource != now.RemovableSource || before.RemovableVolume != now.RemovableVolume)
            || before.PspDestinations.GetValueOrDefault(operation.Request.Game.Id) != now.PspDestinations.GetValueOrDefault(operation.Request.Game.Id))
            return new(GameCopyErrorCode.SourceChanged, "The selected source or PSP changed.");
        var sourceCapability = optical ? DeviceCapabilities.OpticalSource : DeviceCapabilities.RemovableSource;
        if (!Matches(operation.Request.Source, observation, sourceCapability)) return new(GameCopyErrorCode.SourceChanged, "The source device or mounted volume changed or became unavailable.");
        if (!Matches(operation.Request.Destination, observation, DeviceCapabilities.PspDestination)) return new(GameCopyErrorCode.DestinationChanged, "The PSP or mounted volume changed or became unavailable.");
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

    private static ActiveCopy FailLocked(ActiveCopy operation, GameCopyError error)
    {
        operation.Failure ??= error with { Message = error.Message + " " + RetentionWarning };
        return operation;
    }
    private static void Cancel(ActiveCopy? operation)
    {
        try { operation?.Cancellation.Cancel(); } catch (ObjectDisposedException) { } // Completion won the race.
    }
    private static GameCopyException Error(GameCopyErrorCode code, string message) => new(new(code, message));

    public ValueTask DisposeAsync()
    {
        ActiveCopy? operation;
        Task[] waiting;
        TaskCompletionSource completion;
        lock (gate)
        {
            if (disposeTask is not null) return new(disposeTask);
            disposed = true;
            session.SnapshotChanged -= OnSessionChanged;
            operation = active;
            if (operation is not null) operation.CancelRequested = true;
            waiting = removals.Values.Select(r => r.Task).ToArray();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            disposeTask = completion.Task;
        }
        // Mark the stop under the gate, then cancel immediately outside it before returning.
        Cancel(operation);
        _ = DrainAsync(operation, waiting, completion);
        return new(completion.Task);
    }

    private static async Task DrainAsync(ActiveCopy? operation, Task[] waiting, TaskCompletionSource completion)
    {
        try
        {
            if (operation is not null) await operation.Completion.Task.ConfigureAwait(false);
            await Task.WhenAll(waiting).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private sealed class ActiveCopy(GameCopyRequest request, DeviceSessionState associations,
        IProgress<GameCopySnapshot>? observer, CancellationToken token)
    {
        internal GameCopyRequest Request { get; } = request;
        internal DeviceSessionState Associations { get; } = associations;
        internal IProgress<GameCopySnapshot>? Observer { get; } = observer;
        internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        internal TaskCompletionSource<GameCopyResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal GameCopyError? Failure;
        internal GameCopySnapshot? Terminal;
        internal bool CancelRequested;
    }
    private sealed class InlineProgress(Action<GameCopySnapshot> report) : IProgress<GameCopySnapshot>
    { public void Report(GameCopySnapshot value) => report(value); }
}
