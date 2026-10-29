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

    public bool IsExportFormOpen
    {
        get => _isExportFormOpen;
        private set
        {
            if (SetProperty(ref _isExportFormOpen, value))
            {
                OnPropertyChanged(nameof(CanOpenExport));
                OnPropertyChanged(nameof(CanExportCatalog));
                OpenExportCommand.NotifyCanExecuteChanged();
                ChooseExportFolderCommand.NotifyCanExecuteChanged();
                CancelExportCommand.NotifyCanExecuteChanged();
                ExportCatalogCommand.NotifyCanExecuteChanged();
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
                OnPropertyChanged(nameof(CanExportCatalog));
                ExportCatalogCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ExportFileName
    {
        get => _exportFileName;
        set => SetProperty(ref _exportFileName, value);
    }

    public string? ExportError
    {
        get => _exportError;
        private set => SetProperty(ref _exportError, value);
    }

    public string? ExportedPath
    {
        get => _exportedPath;
        private set => SetProperty(ref _exportedPath, value);
    }

    public bool CanOpenExport => IsEditorAvailable && _exportFolderPicker is not null && !IsExportFormOpen;

    public bool CanChooseExportFolder => IsEditorAvailable && _exportFolderPicker is not null && IsExportFormOpen;

    public bool CanExportCatalog => IsEditorAvailable && IsExportFormOpen
        && ExportFolderPath is not null && _exportWriter is not null && _catalogPath is not null;

    [RelayCommand(CanExecute = nameof(CanOpenExport))]
    private void OpenExport()
    {
        ExportFileName = "pspman-games.csv";
        ExportFolderPath = null;
        ExportError = null;
        ExportedPath = null;
        IsExportFormOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanExportCatalog))]
    private void ExportCatalog()
    {
        ExportedPath = null;
        ExportError = null;

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
            ExportError = result.Error;
            return;
        }

        try
        {
            var bytes = GameCatalogCsvSerializer.SerializeCatalog(result.Value ?? []);
            var snapshot = _exportWriter!.Capture(destinationPath!);
            if (snapshot.Exists)
            {
                ExportError = "Replacing an existing export requires confirmation.";
                return;
            }

            _exportWriter.Publish(destinationPath!, bytes, snapshot, overwriteApproved: false);
            ExportedPath = destinationPath;
            IsExportFormOpen = false;
        }
        catch (Exception)
        {
            ExportError = "Unable to save the catalog export.";
        }
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
        ExportFolderPath = null;
        ExportError = null;
    }
}
