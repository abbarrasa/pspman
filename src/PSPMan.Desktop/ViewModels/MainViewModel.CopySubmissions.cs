using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<GameRecord> SelectedGames { get; } = [];
    public ObservableCollection<CopySubmissionViewModel> CopyDrafts { get; } = [];
    private bool _copyPreparationEnabled;
    public bool IsCopyPreparationEnabled => _copyPreparationEnabled;
    public bool HasCopyDrafts => CopyDrafts.Count != 0;
    public bool ShowCopyDrafts => HasCopyDrafts && Copy is { HasCloseConfirmation: false, IsClosing: false };
    public bool CanPrepareSelectedCopies => IsCopyPreparationEnabled && SelectedGames.Count != 0 && Copy is { CanSubmitCopies: true };
    public bool CanSendPreparedCopies => IsCopyPreparationEnabled && HasCopyDrafts && Copy is { CanSubmitCopies: true };
    [ObservableProperty] private string _copyPreparationSummary = "";
    [ObservableProperty] private GameCopyAdmission? _lastCopyAdmission;

    private static DeviceSessionSnapshot EmptyCopyObservation() => new(new(0, []), new(),
        ImmutableDictionary<string, long>.Empty, ImmutableDictionary<string, DeviceOperationError>.Empty);
    private void InitializeCopySelection()
    {
        SelectedGames.CollectionChanged += (_, _) => UpdatePreparationCommands();
        CopyDrafts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCopyDrafts)); OnPropertyChanged(nameof(ShowCopyDrafts)); UpdatePreparationCommands();
        };
        if (Devices is not null) Devices.PropertyChanged += OnSubmissionDevicesChanged;
    }
    // Enabled on copy-service attachment, including unavailable-service presentation.
    internal void EnableCopyPreparation()
    {
        _copyPreparationEnabled = true;
        OnPropertyChanged(nameof(IsCopyPreparationEnabled));
        UpdatePreparationCommands();
    }
    private void OnSubmissionDevicesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DevicesViewModel.SubmissionObservation)) RefreshCopyDrafts();
    }
    private void RefreshCopyDrafts()
    {
        var observation = Devices?.SubmissionObservation ?? EmptyCopyObservation();
        foreach (var draft in CopyDrafts.ToArray()) draft.Refresh(observation);
    }
    private void UpdatePreparationCommands()
    {
        OnPropertyChanged(nameof(CanPrepareSelectedCopies)); OnPropertyChanged(nameof(CanSendPreparedCopies));
        PrepareSelectedCopiesCommand.NotifyCanExecuteChanged(); SendPreparedCopiesCommand.NotifyCanExecuteChanged();
    }
    private void ReconcileCopySelection(IReadOnlyList<GameRecord> selectedBeforeRefresh)
    {
        var current = Games.ToDictionary(game => game.Id);
        var selected = selectedBeforeRefresh.Where(game => current.ContainsKey(game.Id)).Select(game => current[game.Id]).ToArray();
        if (!SelectedGames.SequenceEqual(selected))
        {
            SelectedGames.Clear();
            foreach (var game in selected) SelectedGames.Add(game);
        }
        foreach (var draft in CopyDrafts.ToArray())
        {
            if (!current.TryGetValue(draft.Game.Id, out var game)) CopyDrafts.Remove(draft);
            else draft.UpdateGame(game);
        }
    }
    [RelayCommand(CanExecute = nameof(CanPrepareSelectedCopies))]
    private void PrepareSelectedCopies()
    {
        if (!CanPrepareSelectedCopies) return;
        var selected = SelectedGames.Select(game => game.Id).ToHashSet();
        foreach (var draft in CopyDrafts.Where(draft => !selected.Contains(draft.Game.Id)).ToArray()) CopyDrafts.Remove(draft);
        var observation = Devices?.SubmissionObservation ?? EmptyCopyObservation();
        foreach (var game in SelectedGames.DistinctBy(game => game.Id).ToArray())
        {
            if (CopyDrafts.All(draft => draft.Game.Id != game.Id)) CopyDrafts.Add(new(game, observation, _mountedPathSyntax));
        }
        CopyPreparationSummary = "Choose the source and PSP for each game, then send explicitly.";
    }
    [RelayCommand(CanExecute = nameof(CanSendPreparedCopies))]
    private void SendPreparedCopies()
    {
        if (!CanSendPreparedCopies) return;
        var drafts = CopyDrafts.ToArray();
        var ready = new List<GameCopySubmission>();
        var readyIndices = new List<int>();
        var incidents = new List<GameCopySubmissionIncident>();
        var observation = Devices?.SubmissionObservation ?? EmptyCopyObservation();
        for (var index = 0; index < drafts.Length; index++)
        {
            drafts[index].Refresh(observation);
            var submission = drafts[index].Prepare(Guid.NewGuid(), out var error);
            if (submission is null) incidents.Add(new(index, drafts[index].Game.Id, error!));
            else { ready.Add(submission); readyIndices.Add(index); }
        }
        GameCopyAdmission admission;
        try { admission = ready.Count == 0 ? new([], []) : Copy!.Submit(ready); }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or GameCopyException)
        {
            var incident = error is GameCopyException copyError ? copyError.Error
                : new GameCopyError(GameCopyErrorCode.CopyInProgress, "Copy admission is closed. Retry explicitly after reopening.");
            admission = new([], ready.Select((submission, index) => new GameCopySubmissionIncident(index, submission.Game.Id, incident)).ToImmutableArray());
        }
        incidents.AddRange(admission.Incidents.Select(incident => incident with { SubmissionIndex = readyIndices[incident.SubmissionIndex] }));
        foreach (var incident in incidents) drafts[incident.SubmissionIndex].ReportIncident(incident.Error);
        var accepted = admission.Accepted.Select(request => request.Game.Id).ToHashSet();
        foreach (var draft in drafts.Where(draft => accepted.Contains(draft.Game.Id))) CopyDrafts.Remove(draft);
        LastCopyAdmission = new(admission.Accepted, incidents.OrderBy(incident => incident.SubmissionIndex).ToImmutableArray());
        CopyPreparationSummary = $"Added {admission.Accepted.Length} copies. {incidents.Count} games need attention. Files are retained if a copy stops.";
    }
}
