using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PSPMan.Desktop.ViewModels;

public sealed partial class GameCopyViewModel
{
    private TaskCompletionSource<bool>? _closeDecision;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloseConfirmation))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private CopyCloseConfirmationViewModel? _closeConfirmation;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanStart))] private bool _isClosing;
    public bool HasCloseConfirmation => CloseConfirmation is not null;
    public bool CanStart => !_disposed && !IsBusy && !HasCloseConfirmation && !IsClosing;

    /// <summary>UI-context decision only. Pending copy input/I/O continues until acceptance.</summary>
    internal Task<bool> ConfirmCloseAsync()
    {
        lock (_gate)
        {
            if (_closeDecision is { } pending) return pending.Task;
            if (_disposed || IsClosing || !IsBusy) return Task.FromResult(true);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeDecision = completion;
            // Publish after storing completion: UI observers may respond reentrantly.
            CloseConfirmation = new(Destination, RespondClose);
            return completion.Task;
        }
    }

    private void RespondClose(CopyCloseConfirmationViewModel confirmation, bool accept)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(CloseConfirmation, confirmation)) return;
            var completion = _closeDecision;
            _closeDecision = null;
            if (accept) IsClosing = true;
            CloseConfirmation = null;
            completion?.TrySetResult(accept);
        }
    }

    internal async Task StopForShutdownAsync()
    {
        IsClosing = true;
        if (IsBusy) await CancelAsync();
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
