using System;
using System.Collections.Generic;
using System.IO;
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

    public GameCatalogImportService? ImportService { get; }

    public bool IsImportFormOpen
    {
        get => _isImportFormOpen;
        private set
        {
            if (SetProperty(ref _isImportFormOpen, value))
            {
                OnPropertyChanged(nameof(ImportStatusText));
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
                OnPropertyChanged(nameof(HasImportPreview));
                OnPropertyChanged(nameof(ImportNewCount));
                OnPropertyChanged(nameof(ImportModifiedCount));
                OnPropertyChanged(nameof(ImportErrorCount));
                OnPropertyChanged(nameof(ImportRowErrors));
                OnPropertyChanged(nameof(ImportStatusText));
            }
        }
    }

    public bool HasImportPreview => ImportPreview is not null;
    public int ImportNewCount => ImportPreview?.NewCount ?? 0;
    public int ImportModifiedCount => ImportPreview?.ModifiedCount ?? 0;
    public int ImportErrorCount => ImportPreview?.ErrorCount ?? 0;
    public IReadOnlyList<GameCatalogImportRowError> ImportRowErrors => ImportPreview?.RowErrors ?? [];
    public string? ImportStatusText => !IsImportFormOpen ? null : IsImportBusy ? "Evaluating CSV..."
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

    [RelayCommand(CanExecute = nameof(IsImportFormOpen))]
    private void CancelImport()
    {
        _importPreviewCancellation?.Cancel();
        _importSelectionVersion++;
        ImportPreview = null;
        IsImportFormOpen = false;
        ImportSourcePath = null;
        ImportError = null;
    }
}
