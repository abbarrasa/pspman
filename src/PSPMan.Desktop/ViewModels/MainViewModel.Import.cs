using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Games;
using PSPMan.Desktop.Services;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel
{
    private readonly IGameImportFilePicker? _importFilePicker;
    private bool _isImportFormOpen;
    private string? _importSourcePath;
    private string? _importError;
    private int _importSelectionVersion;
    private bool _isImportBusy;
    private GameCatalogImportPreview? _importPreview;
    private CancellationTokenSource? _importPreviewCancellation;
    private bool _isApplyingImport;
    private GameCatalogImportApplyResult? _importApplyResult;

    public GameCatalogImportService? ImportService { get; }

    public bool IsImportFormOpen
    {
        get => _isImportFormOpen;
        private set
        {
            if (SetProperty(ref _isImportFormOpen, value))
            {
                OnPropertyChanged(nameof(ImportStatusText));
                NotifyCatalogMutationAvailability();
                NotifyImportAvailability();
                CancelImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ImportSourcePath
    {
        get => _importSourcePath;
        private set
        {
            if (SetProperty(ref _importSourcePath, value))
            {
                ImportPreview = null;
                NotifyImportAvailability();
            }
        }
    }

    public string? ImportError
    {
        get => _importError;
        private set
        {
            if (SetProperty(ref _importError, value)) OnPropertyChanged(nameof(HasImportError));
        }
    }

    public bool IsImportBusy
    {
        get => _isImportBusy;
        private set
        {
            if (SetProperty(ref _isImportBusy, value))
            {
                OnPropertyChanged(nameof(ImportStatusText));
                NotifyCatalogMutationAvailability();
                NotifyImportAvailability();
            }
        }
    }

    public GameCatalogImportPreview? ImportPreview
    {
        get => _importPreview;
        private set
        {
            if (SetProperty(ref _importPreview, value))
            {
                NotifyCatalogMutationAvailability();
                NotifyImportAvailability();
                OnPropertyChanged(nameof(HasImportPreview));
                OnPropertyChanged(nameof(ImportNewCount));
                OnPropertyChanged(nameof(ImportModifiedCount));
                OnPropertyChanged(nameof(ImportErrorCount));
                OnPropertyChanged(nameof(ImportRowErrors));
                OnPropertyChanged(nameof(ImportStatusText));
            }
        }
    }

    public GameCatalogImportApplyResult? ImportApplyResult
    {
        get => _importApplyResult;
        private set
        {
            if (SetProperty(ref _importApplyResult, value))
            {
                OnPropertyChanged(nameof(HasImportResult));
                OnPropertyChanged(nameof(ImportCreatedCount));
                OnPropertyChanged(nameof(ImportUpdatedCount));
                OnPropertyChanged(nameof(ImportFailedCount));
                OnPropertyChanged(nameof(ImportSaveErrors));
                OnPropertyChanged(nameof(ImportStatusText));
            }
        }
    }

    public bool HasImportResult => ImportApplyResult is not null;
    public int ImportCreatedCount => ImportApplyResult?.CreatedCount ?? 0;
    public int ImportUpdatedCount => ImportApplyResult?.UpdatedCount ?? 0;
    public int ImportFailedCount => ImportApplyResult?.FailedCount ?? 0;
    public IReadOnlyList<GameCatalogImportRowError> ImportSaveErrors => ImportApplyResult?.SaveErrors ?? [];
    public bool CanConfirmImport => CanChooseImportFile && HasImportPreview;
    public bool CanCancelImport => IsImportFormOpen && !_isApplyingImport;
    private bool IsCatalogMutationBlocked => IsImportBusy || HasImportPreview;

    private void NotifyCatalogMutationAvailability()
    {
        OnPropertyChanged(nameof(IsAdding));
        OnPropertyChanged(nameof(CanStartEdit));
        OnPropertyChanged(nameof(CanStartDelete));
        OnPropertyChanged(nameof(CanConfirmDelete));
        OnPropertyChanged(nameof(CanSaveChanges));
        AddGameCommand.NotifyCanExecuteChanged();
        StartEditCommand.NotifyCanExecuteChanged();
        SaveChangesCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();
    }

    public bool HasImportPreview => ImportPreview is not null;
    public int ImportNewCount => ImportPreview?.NewCount ?? 0;
    public int ImportModifiedCount => ImportPreview?.ModifiedCount ?? 0;
    public int ImportErrorCount => ImportPreview?.ErrorCount ?? 0;
    public IReadOnlyList<GameCatalogImportRowError> ImportRowErrors => ImportPreview?.RowErrors ?? [];
    public string? ImportStatusText => !IsImportFormOpen ? null : IsImportBusy ? (_isApplyingImport ? "Importing CSV..." : "Evaluating CSV...")
        : HasImportResult ? $"{ImportCreatedCount} created, {ImportUpdatedCount} updated, {ImportFailedCount} failed."
        : HasImportPreview ? $"{ImportNewCount} new games, {ImportModifiedCount} modified games, {ImportErrorCount} erroneous rows."
        : null;

    public bool HasImportError => !string.IsNullOrWhiteSpace(ImportError);

    private bool IsImportAvailable => IsEditorAvailable && ImportService is not null && _importFilePicker is not null;

    public bool CanOpenImport => IsImportAvailable && !IsImportFormOpen && !IsImportBusy;

    public bool CanChooseImportFile => IsImportAvailable && IsImportFormOpen && !IsImportBusy;

    public bool CanPreviewImport => CanChooseImportFile && ImportSourcePath is not null;

    private void NotifyImportAvailability()
    {
        OnPropertyChanged(nameof(CanOpenImport));
        OnPropertyChanged(nameof(CanChooseImportFile));
        OnPropertyChanged(nameof(CanPreviewImport));
        OnPropertyChanged(nameof(CanConfirmImport));
        OnPropertyChanged(nameof(CanCancelImport));
        ConfirmImportCommand.NotifyCanExecuteChanged();
        CancelImportCommand.NotifyCanExecuteChanged();
        OpenImportCommand.NotifyCanExecuteChanged();
        ChooseImportFileCommand.NotifyCanExecuteChanged();
        PreviewImportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenImport))]
    private void OpenImport()
    {
        if (!CanOpenImport) return;
        _importSelectionVersion++;
        ImportSourcePath = null;
        ImportPreview = null;
        ImportError = null;
        ImportApplyResult = null;
        IsImportFormOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanChooseImportFile))]
    private async Task ChooseImportFileAsync()
    {
        if (!CanChooseImportFile) return;
        var version = _importSelectionVersion;
        ImportSourcePath = null;
        ImportPreview = null;
        ImportError = null;
        ImportApplyResult = null;
        try
        {
            var path = await _importFilePicker!.PickFileAsync();
            if (version != _importSelectionVersion || !CanChooseImportFile) return;
            if (path is null)
            {
                CancelImport();
                return;
            }
            ImportSourcePath = path;
            ImportError = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            if (version == _importSelectionVersion && CanChooseImportFile) ImportError = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewImport))]
    private async Task PreviewImportAsync()
    {
        if (!CanPreviewImport) return;
        var version = _importSelectionVersion;
        var path = ImportSourcePath!;
        using var cancellation = new CancellationTokenSource();
        _importPreviewCancellation = cancellation;
        ImportPreview = null;
        ImportError = null;
        ImportApplyResult = null;
        IsImportBusy = true;
        try
        {
            // Await resumes on the caller's UI context; only Core evaluation runs in the worker.
            var result = await Task.Run(() => ImportService!.Preview(path, cancellation.Token));
            if (cancellation.IsCancellationRequested || version != _importSelectionVersion || !IsImportFormOpen) return;
            if (result.IsSuccess)
            {
                ImportPreview = result.Value;
            }
            else
            {
                ReportCorruption(result);
                ImportError = result.Error;
            }
        }
        catch (Exception)
        {
            if (!cancellation.IsCancellationRequested && version == _importSelectionVersion && IsImportFormOpen)
                ImportError = "Unable to prepare the CSV import preview.";
        }
        finally
        {
            _importPreviewCancellation = null;
            IsImportBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirmImport))]
    private async Task ConfirmImportAsync()
    {
        if (!CanConfirmImport) return;
        var preview = ImportPreview!;
        _isApplyingImport = true;
        IsImportBusy = true;
        ImportPreview = null; // Each confirmation consumes its proposals, including failed attempts.
        ImportApplyResult = null;
        ImportError = null;
        try
        {
            var preparation = await Task.Run(() => ImportService!.PrepareConfirmedImport(preview));
            if (!preparation.IsSuccess)
            {
                ReportCorruption(preparation);
                ImportError = preparation.Error;
                return;
            }
            using var session = preparation.Value!;
            var result = await Task.Run(() => ImportService!.Apply(session));
            ImportApplyResult = result;
            ImportError = result.Error;
            if (result.IsCorrupt) CatalogError = result.Error;

            if (result.SavedRecords.Count > 0)
            {
                var selectedId = SelectedGame?.Id;
                var refresh = await Task.Run(() => CatalogService!.List());
                var confirmed = result.SavedRecords.ToDictionary(record => record.Id);
                var listed = refresh.IsSuccess ? refresh.Value ?? [] : Games;
                var listedIds = listed.Select(record => record.Id).ToHashSet();
                Games = listed.Select(record => confirmed.GetValueOrDefault(record.Id, record))
                    .Concat(result.SavedRecords.Where(saved => !listedIds.Contains(saved.Id))).ToArray();
                SelectedGame = Games.FirstOrDefault(record => record.Id == selectedId);
                if (!refresh.IsSuccess)
                {
                    ReportCorruption(refresh);
                    var refreshError = "Saved game records were retained, but the catalog could not be refreshed.";
                    ImportError = result.Error is null ? refreshError : $"{result.Error} {refreshError}";
                }
            }
        }
        catch (Exception)
        {
            ImportError = "Unable to complete the CSV import.";
        }
        finally
        {
            _isApplyingImport = false;
            IsImportBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelImport))]
    private void CancelImport()
    {
        if (!CanCancelImport) return;
        _importPreviewCancellation?.Cancel();
        _importSelectionVersion++;
        ImportPreview = null;
        IsImportFormOpen = false;
        ImportSourcePath = null;
        ImportError = null;
        ImportApplyResult = null;
    }
}
