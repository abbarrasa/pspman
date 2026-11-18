using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PSPMan.Core.Transfers;

namespace PSPMan.Desktop.ViewModels;

public sealed partial class GameCopyViewModel
{
    private readonly Dictionary<Guid, CopyOperationViewModel> _rows = [];
    private readonly ObservableCollection<CopyOperationViewModel> _operations = [];
    private ReadOnlyObservableCollection<CopyOperationViewModel>? _readOnlyOperations;
    private Guid? _legacyInputOperation;
    public ReadOnlyObservableCollection<CopyOperationViewModel> Operations => _readOnlyOperations ??= new(_operations);

    // Explicit immutable submissions used by production and functional tests.
    internal GameCopyAdmission Submit(IEnumerable<GameCopySubmission> submissions, CancellationToken token = default)
    {
        if (_disposed || IsClosing || HasCloseConfirmation) throw new OperationCanceledException("The application is closing.");
        if (_coordinator is null) throw new InvalidOperationException("Copy services are not available.");
        var result = _coordinator.Submit(submissions, new OperationProgress(Observe), token);
        foreach (var request in result.Accepted)
        {
            GetRow(request.OperationId, request);
            Observe(_coordinator.Operations.Single(s => s.Request.OperationId == request.OperationId));
        }
        _post(() => { if (!_disposed) { IsOpen = true; RefreshBusy(); } });
        return result;
    }
    internal Task<GameCopyResult> GetCompletion(Guid id) => _coordinator?.GetCompletion(id)
        ?? throw new InvalidOperationException("Copy services are not available.");

    private void Observe(GameCopyOperationSnapshot snapshot)
    {
        try { GetRow(snapshot.Request.OperationId, snapshot.Request).Receive(snapshot); }
        catch (ObjectDisposedException) when (_disposed) { /* A late observer cannot fault a draining operation. */ }
    }
    private void ObserveLegacy(GameCopySnapshot snapshot)
    {
        lock (_gate) { if (_disposed) return; _legacyInputOperation = snapshot.OperationId; }
        if (_coordinator?.Operations.FirstOrDefault(s => s.Request.OperationId == snapshot.OperationId) is { } value) Observe(value);
    }
    private CopyOperationViewModel GetRow(Guid id, GameCopyRequest? request = null)
    {
        CopyOperationViewModel row;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_rows.TryGetValue(id, out row!)) return row;
            request ??= _coordinator?.Operations.FirstOrDefault(s => s.Request.OperationId == id)?.Request;
            if (_coordinator is not null && request is null) throw new OperationCanceledException("Unknown copy operation.");
            row = new(id, request, _post, key => _coordinator?.CancelAsync(key) ?? Task.CompletedTask, () => IsClosing || _disposed);
            row.PropertyChanged += OnRowChanged;
            _rows.Add(id, row);
        }
        if (IsClosing) row.BeginStopping();
        _post(() =>
        {
            if (_disposed) return;
            _operations.Add(row);
            if (_legacyInputOperation == id) Input = row.Input;
            RefreshBusy();
        });
        return row;
    }
    private void OnRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed || sender is not CopyOperationViewModel row) return;
        if (args.PropertyName == nameof(CopyOperationViewModel.IsBusy)) RefreshBusy();
        if (_legacyInputOperation == row.OperationId && args.PropertyName == nameof(CopyOperationViewModel.Input)) Input = row.Input;
    }
    private void RefreshBusy() => IsBusy = _operations.Any(row => row.IsBusy);

    private Task<(string Decision, string Password)> RequestRowInputAsync(GameCopyPrompt identity, bool password,
        string message, string path, string warning, CancellationToken token)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsClosing) throw new OperationCanceledException("The application is closing.");
            // Historical single-operation harnesses retain aliases; production prompts belong to rows.
            if (_coordinator is null || _coordinator.Snapshot?.OperationId == identity.OperationId)
                _legacyInputOperation = identity.OperationId;
        }
        return GetRow(identity.OperationId).RequestAsync(identity, password, message, path, warning, token);
    }
    private void DisposeRows()
    {
        foreach (var row in _rows.Values.ToArray()) { row.PropertyChanged -= OnRowChanged; row.Dispose(); }
        _post(() => Input = null);
    }
    private void StopRows()
    {
        CopyOperationViewModel[] rows;
        lock (_gate) rows = _rows.Values.ToArray();
        foreach (var row in rows) row.BeginStopping();
    }
    private sealed class OperationProgress(Action<GameCopyOperationSnapshot> report) : IProgress<GameCopyOperationSnapshot>
    { public void Report(GameCopyOperationSnapshot value) => report(value); }
}
