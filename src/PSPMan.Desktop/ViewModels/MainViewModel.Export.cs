using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Desktop.Services;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel
{
    private readonly IGameExportFolderPicker? _exportFolderPicker;
    private bool _isExportFormOpen;
    private string? _exportFolderPath;
    private string _exportFileName = "pspman-games.csv";
    private string? _exportError;

    public bool IsExportFormOpen
    {
        get => _isExportFormOpen;
        private set
        {
            if (SetProperty(ref _isExportFormOpen, value))
            {
                OnPropertyChanged(nameof(CanOpenExport));
                OpenExportCommand.NotifyCanExecuteChanged();
                ChooseExportFolderCommand.NotifyCanExecuteChanged();
                CancelExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ExportFolderPath
    {
        get => _exportFolderPath;
        private set => SetProperty(ref _exportFolderPath, value);
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

    public bool CanOpenExport => IsEditorAvailable && _exportFolderPicker is not null && !IsExportFormOpen;

    public bool CanChooseExportFolder => IsEditorAvailable && _exportFolderPicker is not null && IsExportFormOpen;

    [RelayCommand(CanExecute = nameof(CanOpenExport))]
    private void OpenExport()
    {
        ExportFileName = "pspman-games.csv";
        ExportFolderPath = null;
        ExportError = null;
        IsExportFormOpen = true;
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
