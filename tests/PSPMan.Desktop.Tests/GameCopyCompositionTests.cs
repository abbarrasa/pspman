using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;
using PSPMan.Infraestructure.Devices;
using static PSPMan.Desktop.Tests.GameCopyTestContext;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class GameCopyCompositionTests
{
    [Fact]
    public async Task LifetimeAttachesCopyWithTheLatestGameAndDetachesItBeforeSessionDisposal()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var release = new TaskCompletionSource<DeviceProviderFactoryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var main = new MainViewModel();
            var window = new MainWindow { DataContext = main };
            await using var lifetime = new ApplicationDeviceLifetime(window, main, _ => { }, _ => new(release.Task));
            window.Show();
            Assert.Null(main.Copy);
            main.SelectedGame = new(Guid.NewGuid(), "Latest", GameMedium.Usb, null, "/game.zip", GameFormat.Iso, 3, null);
            var provider = new Provider { Inventory = new(1, [Device("source")]) };
            release.SetResult(new([provider]));
            await lifetime.Initialization;
            await Wait(() => main.Devices!.RemovableSource.Candidates.Length == 1);
            await main.Devices!.ChooseSourceCommand.ExecuteAsync(main.Devices.RemovableSource.Candidates[0]);
            await main.CopySelectedGameCommand.ExecuteAsync(null);
            Assert.Contains("No selected PSP", main.Copy!.Notification);
            Assert.Same(main.Copy, window.FindControl<GameCopyView>("CopyPanel")!.DataContext);
            var retired = main.Copy;
            await lifetime.DisposeAsync();
            Assert.Null(main.Copy); Assert.Null(main.Devices);
            await retired.StartAsync(); // Disposed presentation cannot restart or attach itself.
            Dispatcher.UIThread.RunJobs();
            Assert.Null(main.Copy);
            window.Close();
        });
    }

    [Fact]
    public async Task FailedDeviceFactoryStillExposesTheCopyPreparationError()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var main = new MainViewModel();
            var window = new MainWindow { DataContext = main };
            await using var lifetime = new ApplicationDeviceLifetime(window, main, _ => { }, _ => throw new IOException("Unavailable"));
            window.Show(); await lifetime.Initialization;
            Assert.NotNull(main.Copy);
            await main.CopySelectedGameCommand.ExecuteAsync(null);
            Assert.Contains("could not be initialized", main.Copy!.Notification);
            Assert.True(main.Copy.IsOpen);
            await lifetime.DisposeAsync(); window.Close();
        });
    }
}
