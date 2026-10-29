using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Games;
using PSPMan.Desktop.Services;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel
{
    private readonly IGameExportFolderPicker? _exportFolderPicker;
    private readonly IGameCatalogExportWriter? _exportWriter;
    private readonly string? _catalogPath;
    private bool _isExportFormOpen;
    private string? _exportFolderPath;
    private string _exportFileName = "pspman-games.csv";
    private string? _exportError;
    private string? _exportedPath;
    private PendingExportReplacement? _pendingExportReplacement;

    private sealed record PendingExportReplacement(
        string DestinationPath, byte[] Content, ExportDestinationSnapshot Snapshot);

    public bool IsExportFormOpen
    {
        get => _isExportFormOpen;
        private set
        {
            if (SetProperty(ref _isExportFormOpen, value))
            {
                OnPropertyChanged(nameof(CanOpenExport));
                OnPropertyChanged(nameof(CanExportCatalog));
                OnPropertyChanged(nameof(CanConfirmExportReplacement));
                OpenExportCommand.NotifyCanExecuteChanged();
                ChooseExportFolderCommand.NotifyCanExecuteChanged();
                CancelExportCommand.NotifyCanExecuteChanged();
                ExportCatalogCommand.NotifyCanExecuteChanged();
                ConfirmExportReplacementCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ExportFolderPath
    {
        get => _exportFolderPath;
        private set
        {
            if (SetProperty(ref _exportFolderPath, value))
            {
                PendingReplacement = null;
                OnPropertyChanged(nameof(CanExportCatalog));
                ExportCatalogCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ExportFileName
    {
        get => _exportFileName;
        set
        {
            if (SetProperty(ref _exportFileName, value))
            {
                PendingReplacement = null;
            }
        }
    }

    public string? ExportError
    {
        get => _exportError;
        private set
        {
            if (SetProperty(ref _exportError, value))
            {
                OnPropertyChanged(nameof(HasExportError));
            }
        }
    }

    public bool HasExportError => !string.IsNullOrWhiteSpace(ExportError);

    public string? ExportedPath
    {
        get => _exportedPath;
        private set
        {
            if (SetProperty(ref _exportedPath, value))
            {
                OnPropertyChanged(nameof(HasExportSuccess));
            }
        }
    }

    public bool HasExportSuccess => !string.IsNullOrWhiteSpace(ExportedPath);

    private PendingExportReplacement? PendingReplacement
    {
        get => _pendingExportReplacement;
        set
        {
            if (SetProperty(ref _pendingExportReplacement, value))
            {
                OnPropertyChanged(nameof(IsReplacementPending));
                OnPropertyChanged(nameof(PendingReplacementPath));
                OnPropertyChanged(nameof(CanExportCatalog));
                OnPropertyChanged(nameof(CanConfirmExportReplacement));
                ConfirmExportReplacementCommand.NotifyCanExecuteChanged();
                CancelExportReplacementCommand.NotifyCanExecuteChanged();
                ExportCatalogCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsReplacementPending => PendingReplacement is not null;

    public string? PendingReplacementPath => PendingReplacement?.DestinationPath;

    public bool CanOpenExport => IsEditorAvailable && _exportFolderPicker is not null && !IsExportFormOpen;

    public bool CanChooseExportFolder => IsEditorAvailable && _exportFolderPicker is not null && IsExportFormOpen;

    public bool CanExportCatalog => IsEditorAvailable && IsExportFormOpen
        && !IsReplacementPending && ExportFolderPath is not null
        && _exportWriter is not null && _catalogPath is not null;

    public bool CanConfirmExportReplacement => IsEditorAvailable && IsExportFormOpen && IsReplacementPending;

    [RelayCommand(CanExecute = nameof(CanOpenExport))]
    private void OpenExport()
    {
        ExportFileName = "pspman-games.csv";
        ExportFolderPath = null;
        ExportError = null;
        ExportedPath = null;
        PendingReplacement = null;
        IsExportFormOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanExportCatalog))]
    private void ExportCatalog()
    {
        ExportedPath = null;
        ExportError = null;
        PendingReplacement = null;

        if (!GameExportDestination.TryResolve(ExportFolderPath, ExportFileName, _catalogPath,
                out var destinationPath, out var error))
        {
            ExportError = error;
            return;
        }

        var result = CatalogService!.List();
        if (!result.IsSuccess)
        {
            ReportCorruption(result);
            ExportError = result.IsCorrupt
                ? result.Error
                : "Unable to read the game catalog for export. Try again.";
            return;
        }

        byte[] bytes;
        try
        {
            bytes = GameCatalogCsvSerializer.SerializeCatalog(result.Value ?? []);
        }
        catch (Exception)
        {
            ExportError = "Unable to prepare the catalog export.";
            return;
        }

        ExportDestinationSnapshot snapshot;
        try
        {
            snapshot = _exportWriter!.Capture(destinationPath!);
        }
        catch (Exception)
        {
            ExportError = "Unable to inspect the export destination. Check access and try again.";
            return;
        }

        if (snapshot.Exists)
        {
            PendingReplacement = new PendingExportReplacement(destinationPath!, bytes, snapshot);
            return;
        }

        PublishExport(destinationPath!, bytes, snapshot, overwriteApproved: false);
    }

    [RelayCommand(CanExecute = nameof(CanConfirmExportReplacement))]
    private void ConfirmExportReplacement()
    {
        if (PendingReplacement is not { } pending)
        {
            return;
        }

        PublishExport(pending.DestinationPath, pending.Content, pending.Snapshot,
            overwriteApproved: true);
    }

    private void PublishExport(string destinationPath, byte[] content,
        ExportDestinationSnapshot snapshot, bool overwriteApproved)
    {
        ExportError = null;
        ExportedPath = null;
        try
        {
            _exportWriter!.Publish(destinationPath, content, snapshot, overwriteApproved);
            PendingReplacement = null;
            ExportedPath = destinationPath;
            IsExportFormOpen = false;
        }
        catch (Exception)
        {
            PendingReplacement = null;
            ExportError = overwriteApproved
                ? "Unable to write the replacement export. The destination was left unchanged; try again."
                : "Unable to write the export file. Check the destination and try again.";
        }
    }

    [RelayCommand(CanExecute = nameof(IsReplacementPending))]
    private void CancelExportReplacement()
    {
        PendingReplacement = null;
    }

    [RelayCommand(CanExecute = nameof(CanChooseExportFolder))]
    private async Task ChooseExportFolderAsync()
    {
        try
        {
            var folderPath = await _exportFolderPicker!.PickFolderAsync();
            if (!IsExportFormOpen || !IsEditorAvailable)
            {
                return;
            }

            if (folderPath is null)
            {
                CancelExport();
                return;
            }

            ExportFolderPath = folderPath;
            ExportError = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ExportError = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsExportFormOpen))]
    private void CancelExport()
    {
        IsExportFormOpen = false;
        PendingReplacement = null;
        ExportFolderPath = null;
        ExportError = null;
    }
}
