using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Devices;
using PSPMan.Core.Transfers;

namespace PSPMan.Desktop.ViewModels;

/// <summary>One captured operation and its private, identity-bound input lease.</summary>
public sealed partial class CopyOperationViewModel : ViewModelBase
{
    private readonly object gate = new();
    private readonly Action<Action> post;
    private readonly Func<Guid, Task> cancel;
    private readonly Func<bool> isClosing;
    private readonly HashSet<Guid> prompts = [];
    private PendingInput? pending;
    private long receivedRevision = -1, appliedRevision = -1;
    private bool terminal, stopping, disposed;

    internal CopyOperationViewModel(Guid id, GameCopyRequest? request, Action<Action> post, Func<Guid, Task> cancel, Func<bool>? isClosing = null)
    { OperationId = id; Request = request; this.post = post; this.cancel = cancel; this.isClosing = isClosing ?? (() => false); }
    public Guid OperationId { get; }
    public GameCopyRequest? Request { get; }
    public string Title => Request?.Game.Title ?? "Copy input";
    public string Source => Request is { } request
        ? MountedGamePathRules.Resolve(request.Source.Root, request.Game.Location, request.Source.Syntax).Path ?? ""
        : "";
    public string Psp => Request?.Destination.Root ?? "";
    [ObservableProperty] private string destination = "";
    [ObservableProperty] private string status = "Preparing";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNotification))] private string notification = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasWarning))] private string warning = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasWork))] private string workText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasWait))] private string waitText = "";
    public bool HasNotification => Notification.Length > 0;
    public bool HasWarning => Warning.Length > 0;
    public bool HasWork => WorkText.Length > 0;
    public bool HasWait => WaitText.Length > 0;
    [ObservableProperty] private double progressPercentage;
    [ObservableProperty] private bool isIndeterminate = true;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CancelCommand))] private bool isBusy = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasInput))] private CopyInputViewModel? input;
    public bool HasInput => Input is not null;
    public long Revision => appliedRevision;

    internal void Receive(GameCopyOperationSnapshot value)
    {
        lock (gate)
        {
            if (disposed || value.Request.OperationId != OperationId || value.Snapshot.OperationId != OperationId ||
                value.Revision <= receivedRevision || terminal) return;
            receivedRevision = value.Revision;
            if (IsTerminal(value.Snapshot.State)) { terminal = true; ReleasePending(true); }
        }
        post(() => Project(value));
    }
    private void Project(GameCopyOperationSnapshot value)
    {
        lock (gate)
        {
            if (disposed || value.Revision <= appliedRevision || value.Revision < receivedRevision) return;
            appliedRevision = value.Revision;
            var snapshot = value.Snapshot;
            Destination = snapshot.Destination;
            Status = stopping && !IsTerminal(snapshot.State) ? "Stopping" : StateText(snapshot.State);
            WaitText = snapshot.WaitReason == GameCopyWaitReason.DestinationFile ? "Waiting for a destination file used by another copy." : "";
            IsBusy = !IsTerminal(snapshot.State);
            IsIndeterminate = IsBusy && snapshot.Progress is null;
            if (snapshot.Progress is { } progress)
            {
                ProgressPercentage = (double)(progress.Fraction * 100);
                WorkText = $"{progress.Work.TotalBytes:N0} bytes total · {progress.Work.DecompressedBytes:N0} decompressed · {progress.Work.WrittenBytes:N0} written";
            }
            if (!IsBusy)
            {
                Notification = snapshot.Error?.Message ?? Status;
                Warning = snapshot.State is GameCopyState.Failed or GameCopyState.Cancelled ? GameCopyViewModel.RetainedOutputWarning : "";
            }
            OnPropertyChanged(nameof(Revision));
        }
    }
    internal Task<(string Decision, string Password)> RequestAsync(GameCopyPrompt identity, bool password,
        string message, string path, string warning, CancellationToken token)
    {
        PendingInput owner;
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            if (disposed || terminal || stopping || isClosing() || identity.OperationId != OperationId || prompts.Contains(identity.PromptId))
                throw new OperationCanceledException("This copy input is no longer current.");
            if (pending is not null) throw new InvalidOperationException("An input is already pending for this operation.");
            prompts.Add(identity.PromptId);
            owner = new(new(password, message, path, warning, Respond));
            pending = owner;
        }
        post(() => { lock (gate) { if (!disposed && ReferenceEquals(pending, owner)) Input = owner.Input; } });
        return AwaitInputAsync(owner, token);
    }
    private async Task<(string Decision, string Password)> AwaitInputAsync(PendingInput owner, CancellationToken token)
    {
        using var registration = token.Register(() => Release(owner, true));
        try { return await owner.Completion.Task.ConfigureAwait(false); }
        finally { Release(owner, false); }
    }
    private void Respond(CopyInputViewModel input, string decision)
    {
        lock (gate)
        {
            if (disposed || terminal || stopping || isClosing() || pending is not { } owner || !ReferenceEquals(owner.Input, input)) return;
            if (decision != "cancel" && (input.IsPassword ? decision != "retry" : decision is not ("overwrite" or "skip"))) return;
            var secret = input.IsPassword && decision == "retry" ? input.Password : "";
            input.Password = "";
            owner.Completion.TrySetResult((decision, secret));
            Release(owner, false);
        }
    }
    private void Release(PendingInput owner, bool cancelled)
    {
        lock (gate)
        {
            if (!ReferenceEquals(pending, owner)) return;
            pending = null;
            if (cancelled) owner.Completion.TrySetCanceled();
        }
        post(() => { owner.Input.Password = ""; if (ReferenceEquals(Input, owner.Input)) Input = null; });
    }
    private void ReleasePending(bool cancelled) { if (pending is { } owner) Release(owner, cancelled); }
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private async Task CancelAsync()
    {
        lock (gate) { if (disposed || terminal || stopping) return; stopping = true; ReleasePending(true); }
        Status = "Stopping";
        await cancel(OperationId);
    }
    internal void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; ReleasePending(true); }
        post(() => { IsBusy = false; IsIndeterminate = false; CancelCommand.NotifyCanExecuteChanged(); });
    }
    internal static bool IsTerminal(GameCopyState state) => state is GameCopyState.Completed or GameCopyState.CompletedWithSkippedFiles or GameCopyState.Cancelled or GameCopyState.Failed;
    internal static string StateText(GameCopyState state) => state switch
    {
        GameCopyState.AwaitingPassword => "Password required",
        GameCopyState.AwaitingPasswordDecision => "Incorrect password or damaged archive",
        GameCopyState.AwaitingOverwrite => "Overwrite confirmation required",
        GameCopyState.CompletedWithSkippedFiles => "Completed with skipped files",
        GameCopyState.WaitingForResource => "Waiting for resource",
        _ => state.ToString()
    };
    private sealed class PendingInput(CopyInputViewModel input)
    {
        internal readonly CopyInputViewModel Input = input;
        internal readonly TaskCompletionSource<(string Decision, string Password)> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
