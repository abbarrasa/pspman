using System.Collections.Immutable;
using PSPMan.Core.Devices;

namespace PSPMan.Core.Transfers;

/// <summary>Executes one operation without owning device selection, UI, or catalogue storage.</summary>
public sealed class GameCopyService(IGameCopyFileSystem fileSystem, IGameArchiveReader archiveReader,
    IGameCopyInteraction interaction)
{
    public Task<GameCopyResult> CopyAsync(GameCopyRequest request,
        IProgress<GameCopySnapshot>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OperationId == Guid.Empty) throw new ArgumentException("An operation identity is required.", nameof(request));
        return new Operation(fileSystem, archiveReader, interaction, request, progress, cancellationToken).RunAsync();
    }

    private sealed class Operation(IGameCopyFileSystem fileSystem, IGameArchiveReader reader,
        IGameCopyInteraction interaction, GameCopyRequest request, IProgress<GameCopySnapshot>? observer,
        CancellationToken token) : IGameArchiveExtractionTarget, IProgress<ArchiveByteProgress>
    {
        private const string RetentionWarning = "Incomplete or incorrect files remain on the PSP. Overwritten files are not restored.";
        private readonly GameCopyTerminalGate terminal = new();
        private readonly HashSet<int> completed = [];
        private readonly HashSet<int> skipped = [];
        private readonly Dictionary<int, long> decoded = [], written = [];
        private IGameArchiveSource? source;
        private IGameCopyDestination? destination;
        private IGameCopyOutputFile? output;
        private int? outputIndex;
        private ImmutableArray<GameArchiveEntry> entries;
        private string? sourceEvidence;
        private long totalBytes;
        private GameCopyProgress? progress;
        private GameCopyError? failure;
        private GameCopyState state = GameCopyState.Preparing;
        private GameCopyPrompt? prompt;
        private string Separator => request.Destination.Syntax == MountedPathSyntax.Windows ? "\\" : "/";
        private string Destination => destination?.DestinationPath ?? request.Destination.Root.TrimEnd('/', '\\') + Separator + "ISO";

        public async Task<GameCopyResult> RunAsync()
        {
            using var cancellation = token.Register(() => terminal.RequestStop(GameCopyStopReason.Cancelled));
            try
            {
                Publish(GameCopyState.Preparing);
                token.ThrowIfCancellationRequested();
                source = await fileSystem.OpenSourceAsync(request.Source, request.Game.Location, token).ConfigureAwait(false);
                sourceEvidence = await source.CaptureEvidenceAsync(token).ConfigureAwait(false);
                destination = await fileSystem.OpenDestinationAsync(request.Destination, token).ConfigureAwait(false);
                await TransferAsync().ConfigureAwait(false);
                await ValidateAsync().ConfigureAwait(false);
            }
            catch (Exception error) { RecordFailure(error); Publish(GameCopyState.Stopping); }
            finally
            {
                await CloseOutputAsync().ConfigureAwait(false);
                await ReleaseAsync(destination).ConfigureAwait(false);
                await ReleaseAsync(source).ConfigureAwait(false);
            }
            // The cancellation registration remains live through release and atomic terminal arbitration.
            var outcome = terminal.TryFinish(skipped.Count, true)!.Value;
            prompt = null;
            if (outcome is GameCopyState.Completed or GameCopyState.CompletedWithSkippedFiles)
                UpdateProgress(true);
            if (outcome == GameCopyState.Cancelled)
                failure = new(GameCopyErrorCode.Cancelled, "Copy cancelled. " + RetentionWarning);
            Publish(outcome);
            return new(request.OperationId, outcome, Destination, skipped.Count, failure);
        }

        private async Task TransferAsync()
        {
            string? password = null;
            try
            {
                while (true)
                {
                    IGameArchiveSession? archive = null;
                    Exception? error = null;
                    try
                    {
                        await ValidateAsync().ConfigureAwait(false);
                        Publish(GameCopyState.Preparing);
                        archive = await reader.OpenAsync(source!, password, token).ConfigureAwait(false);
                        password = null;
                        var inspection = await archive.InspectAsync(token).ConfigureAwait(false);
                        if (entries.IsDefault)
                        {
                            var size = GameCopyRules.SumActualBytes(inspection.Entries);
                            if (size.Error is not null) throw new GameCopyException(size.Error);
                            if (inspection.Entries.Select(e => e.Index).Distinct().Count() != inspection.Entries.Length
                                || inspection.Entries.Any(e => e.Index < 0))
                                throw new GameCopyException(new(GameCopyErrorCode.CorruptArchive, "Archive entry identities are invalid."));
                            entries = inspection.Entries;
                            totalBytes = size.Bytes!.Value;
                            UpdateProgress();
                        }
                        else if (!entries.SequenceEqual(inspection.Entries))
                            throw new GameCopyException(new(GameCopyErrorCode.SourceChanged, "Archive entries changed during retry."));
                        await ValidateAsync().ConfigureAwait(false);
                        var remaining = entries.Where(e => !completed.Contains(e.Index) && !e.IsDirectory).Sum(e => e.SizeBytes!.Value);
                        var capacity = GameCopyRules.CheckCapacity(remaining, await destination!.GetAvailableBytesAsync(token).ConfigureAwait(false));
                        if (capacity is not null) throw new GameCopyException(capacity);
                        Publish(GameCopyState.Transferring);
                        await archive.ExtractAsync(entries.Where(e => !completed.Contains(e.Index)).Select(e => e.Index).ToImmutableArray(),
                            this, this, token).ConfigureAwait(false);
                        if (completed.Count != entries.Length)
                            throw new GameCopyException(new(GameCopyErrorCode.ReadError, "Extraction did not complete every requested entry."));
                    }
                    catch (Exception caught) { error = caught; }
                    finally
                    {
                        password = null;
                        await CloseOutputAsync().ConfigureAwait(false);
                        await ReleaseAsync(archive).ConfigureAwait(false);
                    }
                    if (failure is not null) throw new GameCopyException(failure);
                    token.ThrowIfCancellationRequested();
                    if (error is null) return;
                    if (error is not GameCopyException archiveError || archiveError.Error.Code is not
                        (GameCopyErrorCode.PasswordRequired or GameCopyErrorCode.IncorrectPassword or GameCopyErrorCode.IndeterminateEncryptedError))
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                    var reason = ((GameCopyException)error).Error.Code;
                    password = await AskPasswordAsync(reason).ConfigureAwait(false);
                }
            }
            finally { password = null; }
        }

        private async ValueTask ValidateAsync()
        {
            token.ThrowIfCancellationRequested();
            await source!.ValidateAsync(sourceEvidence!, token).ConfigureAwait(false);
            await destination!.ValidateAsync(token).ConfigureAwait(false);
        }

        private async Task<string?> AskPasswordAsync(GameCopyErrorCode reason)
        {
            while (true)
            {
                await ValidateAsync().ConfigureAwait(false);
                prompt = new(request.OperationId, Guid.NewGuid());
                Publish(reason == GameCopyErrorCode.IndeterminateEncryptedError ? GameCopyState.AwaitingPasswordDecision : GameCopyState.AwaitingPassword,
                    new(reason, reason == GameCopyErrorCode.IndeterminateEncryptedError ? "Incorrect password or damaged archive" : "Archive password required."));
                var response = await interaction.RequestPasswordAsync(new(prompt, reason), token).AsTask().WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (response.Identity != prompt) continue;
                prompt = null;
                if (response.Decision == CopyPasswordDecision.Cancel) throw new OperationCanceledException(token);
                if (response.Decision != CopyPasswordDecision.Retry) continue;
                return response.Password;
            }
        }

        public async ValueTask<ArchiveEntryTarget> PrepareEntryAsync(GameArchiveEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ValidateAsync().ConfigureAwait(false);
            if (output is not null || !entries.Contains(entry) || completed.Contains(entry.Index))
                throw new GameCopyException(new(GameCopyErrorCode.ReadError, "Unexpected archive entry callback."));
            var path = GameCopyRules.ClassifyEntry(entry.Path, entry.IsLink, entry.IsDirectory);
            if (!path.IsSafe) return Skip(entry.Index, path.SkipReason!.Value);
            while (true)
            {
                await ValidateAsync().ConfigureAwait(false);
                var target = await destination!.InspectAsync(path.Segments, token).ConfigureAwait(false);
                var decision = GameCopyRules.ValidateTarget(entry, target);
                if (decision.Error is not null) throw new GameCopyException(decision.Error);
                if (decision.SkipReason is { } skip) return Skip(entry.Index, skip);
                if (entry.IsDirectory)
                {
                    var directorySkip = await destination.EnsureDirectoryAsync(path.Segments, token).ConfigureAwait(false);
                    return directorySkip is { } reason ? Skip(entry.Index, reason) : new(null, null);
                }
                CopyFileEvidence? consent = null;
                if (target.Kind == CopyTargetKind.File)
                {
                    if (target.Evidence is null) throw new GameCopyException(new(GameCopyErrorCode.WriteError, "Overwrite evidence is unavailable."));
                    prompt = new(request.OperationId, Guid.NewGuid());
                    Publish(GameCopyState.AwaitingOverwrite);
                    var answer = await interaction.ConfirmOverwriteAsync(new(prompt, entry.Index,
                        Destination.TrimEnd('/', '\\') + Separator + string.Join(Separator, path.Segments), target.Evidence, RetentionWarning), token).AsTask().WaitAsync(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (answer.Identity != prompt) continue;
                    prompt = null;
                    if (answer.Decision == CopyOverwriteDecision.Cancel) throw new OperationCanceledException(token);
                    if (answer.Decision == CopyOverwriteDecision.Skip) return Skip(entry.Index, GameCopySkipReason.OverwriteDeclined);
                    if (answer.Decision != CopyOverwriteDecision.Overwrite) continue;
                    consent = target.Evidence;
                }
                await ValidateAsync().ConfigureAwait(false);
                var opened = await destination.OpenOutputAsync(path.Segments, consent, token).ConfigureAwait(false);
                if (opened.TargetChanged) continue;
                if (opened.SkipReason is { } openSkip) return Skip(entry.Index, openSkip);
                output = opened.File ?? throw new GameCopyException(new(GameCopyErrorCode.WriteError, "Destination output is unavailable."));
                outputIndex = entry.Index;
                Publish(GameCopyState.Transferring);
                return new(output.Output, null);
            }
        }

        private ArchiveEntryTarget Skip(int index, GameCopySkipReason reason)
        {
            skipped.Add(index);
            return new(null, reason);
        }

        public async ValueTask CompleteEntryAsync(int entryIndex, GameCopyError? error)
        {
            if (error is not null) Publish(GameCopyState.Stopping, error);
            if (outputIndex == entryIndex) await CloseOutputAsync().ConfigureAwait(false);
            if (failure is not null) throw new GameCopyException(failure);
            if (error is null && !token.IsCancellationRequested)
            {
                completed.Add(entryIndex);
                UpdateProgress();
            }
        }

        public void Report(ArchiveByteProgress value)
        {
            // Archive callbacks are serialized and synchronous; no queued progress may outlive this attempt.
            if (outputIndex != value.EntryIndex || token.IsCancellationRequested) return;
            var length = entries.Single(e => e.Index == value.EntryIndex).SizeBytes!.Value;
            if (value.DecompressedBytes < 0 || value.WrittenBytes < 0 || value.DecompressedBytes > length || value.WrittenBytes > length)
                throw new GameCopyException(new(GameCopyErrorCode.InvalidActualSize, "Decoded entry size differs from its inspected size.", value.EntryIndex));
            decoded[value.EntryIndex] = Math.Max(decoded.GetValueOrDefault(value.EntryIndex), value.DecompressedBytes);
            written[value.EntryIndex] = Math.Max(written.GetValueOrDefault(value.EntryIndex), value.WrittenBytes);
            UpdateProgress();
        }

        private void UpdateProgress(bool closed = false)
        {
            if (entries.IsDefault) return;
            long resolvedDecoded = 0, resolvedWritten = 0;
            foreach (var entry in entries)
            {
                if (entry.IsDirectory) continue;
                resolvedDecoded += completed.Contains(entry.Index) ? entry.SizeBytes!.Value : decoded.GetValueOrDefault(entry.Index);
                resolvedWritten += completed.Contains(entry.Index) ? entry.SizeBytes!.Value : written.GetValueOrDefault(entry.Index);
            }
            progress = GameCopyRules.AdvanceProgress(new(totalBytes, decoded.Values.Sum(), written.Values.Sum(), resolvedDecoded, resolvedWritten),
                progress?.Fraction ?? 0, closed);
            if (!closed) Publish(state);
        }

        private async ValueTask CloseOutputAsync()
        {
            var owned = output;
            output = null; outputIndex = null;
            await ReleaseAsync(owned).ConfigureAwait(false);
        }

        private async ValueTask ReleaseAsync(IAsyncDisposable? owned)
        {
            if (owned is null) return;
            try { await owned.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { RecordFailure(new GameCopyException(new(GameCopyErrorCode.CloseError, "Copy files could not be closed. " + RetentionWarning), error)); }
        }

        private void RecordFailure(Exception error)
        {
            if (error is OperationCanceledException || error is GameCopyException { Error.Code: GameCopyErrorCode.Cancelled })
            {
                terminal.RequestStop(GameCopyStopReason.Cancelled);
                return;
            }
            var detail = error is GameCopyException copyError ? copyError.Error
                : new GameCopyError(GameCopyErrorCode.ReadError, "Copy failed while processing the archive or destination.");
            if (failure is null || detail.Code == GameCopyErrorCode.CloseError)
                failure = detail with { Message = detail.Message + " " + RetentionWarning };
            terminal.RequestStop(GameCopyStopReason.Failed);
        }

        private void Publish(GameCopyState next, GameCopyError? detail = null)
        {
            state = next;
            observer?.Report(new(request.OperationId, request.Game.Title, Destination, state, progress, prompt, detail ?? failure, skipped.Count));
        }
    }
}
