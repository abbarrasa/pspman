using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public class GameCatalogFlowTests
{
    [Fact]
    public async Task EmptyCatalogDisplaysEmptyStateWithoutAVisibleList()
    {
        using var data = new TemporaryApplicationData();
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                Assert.True(Control<TextBlock>(window, "EmptyStateText").IsVisible);
                Assert.False(Control<ListBox>(window, "GamesList").IsVisible);
                Assert.Empty(((MainViewModel)window.DataContext!).Games);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectingAStoredGameShowsItsSavedDetails()
    {
        using var data = new TemporaryApplicationData();
        var first = CatalogStartup.CreateMainViewModel(data.Path);
        var saved = first.CatalogService!.Create(new GameRecordInput(
            "Key of Heaven", GameMedium.Dvd, 2, "/DevHook/GAMES/Key of Heaven.rar",
            GameFormat.Iso, 1_073_741_824, "Second disc"));
        Assert.True(saved.IsSuccess);
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                var list = Control<ListBox>(window, "GamesList");
                Assert.True(list.IsVisible);
                Assert.Equal(saved.Value, Assert.Single(list.ItemsSource!.Cast<GameRecord>()));

                list.SelectedItem = saved.Value;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("Key of Heaven", Control<TextBlock>(window, "SelectedTitleText").Text);
                Assert.Equal("DVD", Control<TextBlock>(window, "SelectedMediumText").Text);
                Assert.Equal("2", Control<TextBlock>(window, "SelectedDvdNumberText").Text);
                Assert.Equal("/DevHook/GAMES/Key of Heaven.rar",
                    Control<TextBlock>(window, "SelectedLocationText").Text);
                Assert.Equal("ISO", Control<TextBlock>(window, "SelectedFormatText").Text);
                Assert.Equal("1073741824", Control<TextBlock>(window, "SelectedSizeText").Text);
                Assert.Equal("1 GiB", Control<TextBlock>(window, "SelectedReadableSizeText").Text);
                Assert.Equal("Second disc", Control<TextBlock>(window, "SelectedNotesText").Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditorControlsCreateAGameThatAppearsInListAndPersists()
    {
        using var data = new TemporaryApplicationData();
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                var medium = Control<ComboBox>(window, "MediumInput");
                var format = Control<ComboBox>(window, "FormatInput");
                Assert.Equal(["DVD", "USB", "Memory Card"],
                    medium.ItemsSource!.Cast<GameMediumChoice>().Select(choice => choice.Label));
                Assert.Equal(["ISO", "CHD"],
                    format.ItemsSource!.Cast<GameFormatChoice>().Select(choice => choice.Label));

                medium.SelectedItem = medium.ItemsSource!.Cast<GameMediumChoice>().Single(choice =>
                    choice.Value == GameMedium.Dvd);
                Dispatcher.UIThread.RunJobs();
                Assert.True(Control<TextBox>(window, "DvdNumberInput").IsVisible);
                medium.SelectedItem = medium.ItemsSource!.Cast<GameMediumChoice>().Single(choice =>
                    choice.Value == GameMedium.Usb);
                Assert.False(Control<TextBox>(window, "DvdNumberInput").IsVisible);

                Control<TextBox>(window, "TitleInput").Text = "Offline Game";
                Control<TextBox>(window, "LocationInput").Text = "/GAMES/Offline.rar";
                format.SelectedItem = format.ItemsSource!.Cast<GameFormatChoice>().Single(choice =>
                    choice.Value == GameFormat.Chd);
                Control<TextBox>(window, "SizeInput").Text = "1048576";
                Control<TextBox>(window, "NotesInput").Text = "Stored offline";
                Dispatcher.UIThread.RunJobs();
                var add = Control<Button>(window, "AddGameButton");
                Assert.NotNull(add.Command);
                Assert.True(add.Command.CanExecute(null));
                add.Focus();
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();

                var viewModel = (MainViewModel)window.DataContext!;
                var created = Assert.Single(viewModel.Games);
                Assert.Equal("Offline Game", created.Title);
                Assert.Equal("/GAMES/Offline.rar", created.Location);
                Assert.Equal(created, Assert.Single(Control<ListBox>(window, "GamesList")
                    .ItemsSource!.Cast<GameRecord>()));
                Assert.Equal("Offline Game", Control<TextBlock>(window, "SelectedTitleText").Text);
                Assert.Null(viewModel.OperationError);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        var reopened = CatalogStartup.CreateMainViewModel(data.Path);
        Assert.Equal("Offline Game", Assert.Single(reopened.Games).Title);
    }

    private static MainWindow OpenWindow(string applicationDataRoot)
    {
        var window = new MainWindow
        {
            DataContext = CatalogStartup.CreateMainViewModel(applicationDataRoot)
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Control<T>(MainWindow window, string name) where T : Control =>
        Assert.IsType<T>(window.FindControl<T>(name));

    private sealed class TemporaryApplicationData : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "PSPMan.Headless.Tests", Guid.NewGuid().ToString());

        public TemporaryApplicationData() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
