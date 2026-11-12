using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PSPMan.Desktop.Services;
using PSPMan.Desktop.Views;

namespace PSPMan.Desktop;

public partial class App : Application
{
    private ApplicationDeviceLifetime? _devices;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var main = CatalogStartup.CreateMainViewModel(
                exportFolderPicker: new AvaloniaGameExportFolderPicker(window),
                importFilePicker: new AvaloniaGameImportFilePicker(window));
            window.DataContext = main;
            desktop.MainWindow = window;
            _devices = new ApplicationDeviceLifetime(window, main, exitCode =>
            {
                desktop.ShutdownRequested -= OnShutdownRequested;
                desktop.Shutdown(exitCode);
            });
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs args)
    {
        if (!args.Cancel && _devices?.RequestShutdown() == true) args.Cancel = true;
    }
}
