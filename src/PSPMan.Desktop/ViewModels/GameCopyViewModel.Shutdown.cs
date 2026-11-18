using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PSPMan.Desktop.ViewModels;

public sealed partial class GameCopyViewModel
{
    private TaskCompletionSource<bool>? _closeDecision;
    private Guid? _coordinatorCloseDecision;
    private bool _respondingToClose;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloseConfirmation))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanSubmitCopies))]
    private CopyCloseConfirmationViewModel? _closeConfirmation;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanStart))] [NotifyPropertyChangedFor(nameof(CanSubmitCopies))] private bool _isClosing;
    public bool CanSubmitCopies => !_disposed && !IsClosing && !HasCloseConfirmation && _coordinator is { IsAdmissionOpen: true };
    public bool HasCloseConfirmation => CloseConfirmation is not null;
    public bool CanStart => !_disposed && !IsBusy && !HasCloseConfirmation && !IsClosing;

    /// <summary>Close admission before consulting owners: queued UI projections are not a lifetime barrier.</summary>
    internal Task<bool> ConfirmCloseAsync()
    {
        if (_closeDecision is { } pending) return pending.Task;
        if (_disposed || IsClosing) return Task.FromResult(true);
        _coordinatorCloseDecision = _coordinator?.BeginClose();
        if (!(_coordinator?.IsBusy ?? IsBusy))
        {
            IsClosing = true;
            return Task.FromResult(true);
        }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _closeDecision = completion;
        _respondingToClose = false;
        var destinations = _coordinator is null ? Destination : string.Join(Environment.NewLine,
            _coordinator.Operations.Where(operation => !CopyOperationViewModel.IsTerminal(operation.Snapshot.State))
                .Select(operation => operation.Request.Destination.Root).Distinct());
        // Reserve before publication: UI observers may answer/dispose reentrantly.
        CloseConfirmation = new(destinations, RespondClose);
        return completion.Task;
    }

    private void RespondClose(CopyCloseConfirmationViewModel confirmation, bool accept)
    {
        if (_disposed || _respondingToClose || !ReferenceEquals(CloseConfirmation, confirmation)) return;
        _respondingToClose = true;
        var completion = _closeDecision!;
        if (accept) { IsClosing = true; StopRows(); }
        // Never hold the presentation gate across coordinator cancellation callbacks.
        _ = CompleteCloseDecisionAsync(completion, accept);
    }

    private async Task CompleteCloseDecisionAsync(TaskCompletionSource<bool> completion, bool accept)
    {
        try
        {
            if (_coordinator is not null && _coordinatorCloseDecision is { } id)
                await _coordinator.ResolveCloseAsync(id, accept);
            if (ReferenceEquals(_closeDecision, completion))
            {
                _closeDecision = null;
                CloseConfirmation = null;
            }
            completion.TrySetResult(accept);
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    internal async Task StopForShutdownAsync()
    {
        IsClosing = true;
        StopRows();
        if (_coordinator is not null)
            await _coordinator.ResolveCloseAsync(_coordinatorCloseDecision ?? _coordinator.BeginClose(), true);
    }

    // Called under the presentation gate by unconditional programmatic disposal.
    private void RetireCloseConfirmation()
    {
        var decision = _closeDecision;
        _closeDecision = null;
        IsClosing = true;
        CloseConfirmation = null;
        decision?.TrySetResult(false);
    }
}
