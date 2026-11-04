using System;
using System.IO;
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

    public GameCatalogImportService? ImportService { get; }

    public bool IsImportFormOpen
    {
        get => _isImportFormOpen;
        private set
        {
            if (SetProperty(ref _isImportFormOpen, value))
            {
                NotifyImportAvailability();
                CancelImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ImportSourcePath
    {
        get => _importSourcePath;
        private set => SetProperty(ref _importSourcePath, value);
    }

    public string? ImportError
    {
        get => _importError;
        private set
        {
            if (SetProperty(ref _importError, value)) OnPropertyChanged(nameof(HasImportError));
        }
    }

    public bool HasImportError => !string.IsNullOrWhiteSpace(ImportError);

    private bool IsImportAvailable => IsEditorAvailable && ImportService is not null && _importFilePicker is not null;

    public bool CanOpenImport => IsImportAvailable && !IsImportFormOpen;

    public bool CanChooseImportFile => IsImportAvailable && IsImportFormOpen;

    private void NotifyImportAvailability()
    {
        OnPropertyChanged(nameof(CanOpenImport));
        OnPropertyChanged(nameof(CanChooseImportFile));
        OpenImportCommand.NotifyCanExecuteChanged();
        ChooseImportFileCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenImport))]
    private void OpenImport()
    {
        if (!CanOpenImport) return;
        _importSelectionVersion++;
        ImportSourcePath = null;
        ImportError = null;
        IsImportFormOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanChooseImportFile))]
    private async Task ChooseImportFileAsync()
    {
        if (!CanChooseImportFile) return;
        var version = _importSelectionVersion;
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

    [RelayCommand(CanExecute = nameof(IsImportFormOpen))]
    private void CancelImport()
    {
        _importSelectionVersion++;
        IsImportFormOpen = false;
        ImportSourcePath = null;
        ImportError = null;
    }
}
