using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;

namespace PSPMan.Desktop.ViewModels;

/// <summary>UI projection and transient interaction bridge; the coordinator owns copy policy.</summary>
public sealed partial class GameCopyViewModel : ViewModelBase, IGameCopyInteraction, IAsyncDisposable
{
    public const string RetainedOutputWarning = "Incomplete or incorrect files remain on the PSP. Overwritten files are not restored.";
    private readonly object _gate = new();
    private readonly Action<Action> _post;
    private readonly string? _startupError;
    private GameCopySessionCoordinator? _coordinator;
    private CopyInputViewModel? _pendingInput;
    private Action<string, string>? _respond;
    private Action? _cancelInput;
    private long _version;
    private bool _disposed, _terminal;
    private Task? _disposeTask;
    private CancellationTokenSource? _operationCancellation;

    public GameCopyViewModel(Action<Action>? postToUi = null, string? startupError = null)
    { _post = postToUi ?? (action => Dispatcher.UIThread.Post(action)); _startupError = startupError; }

    internal void AttachCoordinator(GameCopySessionCoordinator coordinator) => _coordinator = coordinator;
    public void SetSelectedGame(GameRecord? game) { if (!_disposed) _coordinator?.SetSelectedGame(game); }
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(CanStart))] private bool _isBusy;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _destination = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _notification = "";
    [ObservableProperty] private string _warning = "";
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private string _workText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasInput))] private CopyInputViewModel? _input;
    public bool HasInput => Input is not null;

    public async Task StartAsync()
    {
        long version;
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (!CanStart) return;
            version = ++_version; _terminal = false;
            cancellation = _operationCancellation = new();
        }
        IsOpen = true; IsBusy = true; Title = ""; Destination = "";
        Status = "Preparing"; Notification = ""; Warning = "";
        WorkText = ""; ProgressPercentage = 0; IsIndeterminate = true;
        try
        {
            if (_coordinator is null) throw new GameCopyException(new(GameCopyErrorCode.DestinationUnavailable,
                _startupError ?? "Device services are not available."));
            var result = await _coordinator.StartAsync(new SnapshotProgress(snapshot =>
                _post(() => Apply(version, snapshot))), cancellation.Token);
            lock (_gate)
            {
                if (_disposed || version != _version) return;
                // Final projection is authoritative even if UI progress was still queued.
                if (_coordinator.Snapshot is { } final) Apply(version, final);
                _terminal = true;
                Status = StateText(result.State);
                Notification = result.Error?.Message ?? Status;
                Destination = result.Destination;
                Warning = result.State is GameCopyState.Failed or GameCopyState.Cancelled ? RetainedOutputWarning : "";
                IsIndeterminate = false;
                IsBusy = false;
            }
        }
        catch (OperationCanceledException)
        {
            FinishError(version, "The copy was cancelled.");
            if (!_disposed && version == _version) { Status = "Cancelled"; Warning = RetainedOutputWarning; }
        }
        catch (GameCopyException error) { FinishError(version, error.Error.Message); }
        catch (Exception) { FinishError(version, "The copy could not be started."); }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_operationCancellation, cancellation)) _operationCancellation = null; }
            cancellation.Dispose();
        }
    }

    private void FinishError(long version, string message)
    {
        lock (_gate)
        {
            if (_disposed || version != _version) return;
            _terminal = true; Status = "Failed"; Notification = message;
            IsBusy = false; IsIndeterminate = false;
        }
    }

    private void Apply(long version, GameCopySnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed || version != _version || _terminal) return;
            Title = snapshot.Title; Destination = snapshot.Destination; Status = StateText(snapshot.State);
            IsIndeterminate = snapshot.Progress is null;
            if (snapshot.Progress is { } progress)
            {
                ProgressPercentage = (double)(progress.Fraction * 100);
                WorkText = $"{progress.Work.TotalBytes:N0} bytes total · {progress.Work.DecompressedBytes:N0} decompressed · {progress.Work.WrittenBytes:N0} written";
            }
        }
    }

    private static string StateText(GameCopyState state) => state switch
    {
        GameCopyState.AwaitingPassword => "Password required",
        GameCopyState.AwaitingPasswordDecision => "Incorrect password or damaged archive",
        GameCopyState.AwaitingOverwrite => "Overwrite confirmation required",
        GameCopyState.CompletedWithSkippedFiles => "Completed with skipped files",
        _ => state.ToString()
    };

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private async Task CancelAsync()
    {
        Status = "Stopping";
        _operationCancellation?.Cancel();
        if (_coordinator is not null) await _coordinator.CancelAsync();
    }

    public async ValueTask<CopyPasswordResponse> RequestPasswordAsync(CopyPasswordPrompt prompt, CancellationToken cancellationToken)
    {
        var reply = await RequestInputAsync(true, prompt.Reason switch
        {
            GameCopyErrorCode.IndeterminateEncryptedError => "Incorrect password or damaged archive",
            GameCopyErrorCode.IncorrectPassword => "Incorrect password. Try again or cancel.",
            _ => "Enter the archive password."
        }, "", RetainedOutputWarning, cancellationToken).ConfigureAwait(false);
        return new(prompt.Identity, reply.Decision == "retry" ? CopyPasswordDecision.Retry : CopyPasswordDecision.Cancel,
            reply.Decision == "retry" ? reply.Password : null);
    }

    public async ValueTask<CopyOverwriteResponse> ConfirmOverwriteAsync(CopyOverwritePrompt prompt, CancellationToken cancellationToken)
    {
        var reply = await RequestInputAsync(false, "Overwrite this file?", prompt.DestinationPath,
            prompt.RetainedOutputWarning, cancellationToken).ConfigureAwait(false);
        return new(prompt.Identity, reply.Decision switch
        {
            "overwrite" => CopyOverwriteDecision.Overwrite,
            "skip" => CopyOverwriteDecision.Skip,
            _ => CopyOverwriteDecision.Cancel
        });
    }

    private async Task<(string Decision, string Password)> RequestInputAsync(bool password, string message,
        string path, string warning, CancellationToken token)
    {
        var completion = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        CopyInputViewModel input;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsClosing) throw new OperationCanceledException("The application is closing.");
            token.ThrowIfCancellationRequested();
            if (_pendingInput is not null) throw new InvalidOperationException("A copy input is already pending.");
            input = new(password, message, path, warning, Respond);
            _pendingInput = input;
            _respond = (decision, secret) => completion.TrySetResult((decision, secret));
            _cancelInput = () => completion.TrySetCanceled();
            _post(() => { lock (_gate) { if (!_disposed && ReferenceEquals(_pendingInput, input)) Input = input; } });
        }
        using var registration = token.Register(() => ReleaseInput(input, cancel: true));
        try { return await completion.Task.ConfigureAwait(false); }
        finally { ReleaseInput(input, cancel: false); }
    }

    private void Respond(CopyInputViewModel input, string decision)
    {
        lock (_gate)
        {
            if (_disposed || IsClosing || !ReferenceEquals(_pendingInput, input)) return;
            var secret = input.Password;
            input.Password = "";
            _respond?.Invoke(decision, secret);
            ReleaseInput(input, cancel: false);
        }
    }

    private void ReleaseInput(CopyInputViewModel input, bool cancel)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_pendingInput, input)) return;
            if (cancel) _cancelInput?.Invoke();
            _pendingInput = null; _respond = null; _cancelInput = null;
            _post(() =>
            {
                input.Password = "";
                if (ReferenceEquals(Input, input)) Input = null;
            });
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposeTask is not null) return new(_disposeTask);
            _disposed = true; ++_version;
            RetireCloseConfirmation();
            if (_pendingInput is { } input) ReleaseInput(input, cancel: true);
            cancellation = _operationCancellation;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }
        // Do not acquire coordinator/native locks while holding the presentation gate.
        _ = DisposeCoreAsync(completion, cancellation);
        return new(completion.Task);
    }
    private async Task DisposeCoreAsync(TaskCompletionSource completion, CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
            if (_coordinator is not null) await _coordinator.DisposeAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
    private sealed class SnapshotProgress(Action<GameCopySnapshot> report) : IProgress<GameCopySnapshot>
    { public void Report(GameCopySnapshot value) => report(value); }
}
