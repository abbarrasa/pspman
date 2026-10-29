using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PSPMan.Desktop.Services;
using PSPMan.Desktop.Views;

namespace PSPMan.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            window.DataContext = CatalogStartup.CreateMainViewModel(
                exportFolderPicker: new AvaloniaGameExportFolderPicker(window));
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
