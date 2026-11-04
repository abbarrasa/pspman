using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using PSPMan.Core.Games;
using PSPMan.Desktop.Services;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;
using PSPMan.Infraestructure.Games;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public class GameCatalogImportFlowTests
{
    [Theory]
    [InlineData("new")]
    [InlineData("replace")]
    [InlineData("decline")]
    public async Task TemplateControlsSaveOrDeclineReplacementWithoutChangingCatalog(string action)
    {
        using var data = new ImportData(Header);
        var destination = Path.Combine(data.Folder, "pspman-import-template.csv");
        byte[] original = [4, 2, 0];
        if (action != "new") File.WriteAllBytes(destination, original);
        var before = File.ReadAllBytes(data.CatalogPath);
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await session.Dispatch(() =>
        {
            var window = data.OpenWindow();
            try
            {
                Click(window, "GetCsvTemplateButton");
                Assert.Equal("Save CSV import template", Control<TextBlock>(window, "ExportFormTitleText").Text);
                Assert.Equal("Save template", Control<Button>(window, "ExportFileButton").Content);
                Assert.Equal("pspman-import-template.csv", Control<TextBox>(window, "ExportFileNameInput").Text);
                Click(window, "ChooseExportFolderButton");
                Click(window, "ExportFileButton");
                if (action != "new")
                {
                    Assert.Equal(original, File.ReadAllBytes(destination));
                    Assert.Equal("The template file already exists. Replace it?", Control<TextBlock>(window, "ExportReplacementPromptText").Text);
                    Click(window, action == "replace" ? "ReplaceExportButton" : "DeclineExportButton");
                }
                if (action == "decline")
                {
                    Assert.Equal(original, File.ReadAllBytes(destination));
                    Assert.False(Control<StackPanel>(window, "ExportSuccessPanel").IsVisible);
                    Click(window, "CancelExportButton");
                }
                else
                {
                    Assert.Equal(GameCatalogCsvTemplate.Generate(), File.ReadAllBytes(destination));
                    Assert.Equal("CSV template saved to:", Control<TextBlock>(window, "ExportSuccessHeadingText").Text);
                    Assert.Equal(destination, Control<TextBlock>(window, "ExportSuccessPathText").Text);
                }
                Assert.Equal(before, File.ReadAllBytes(data.CatalogPath));
                Click(window, "ExportCatalogButton");
                Assert.Equal("Export catalog to CSV", Control<TextBlock>(window, "ExportFormTitleText").Text);
                Assert.Equal("Export", Control<Button>(window, "ExportFileButton").Content);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class TemplateFolderPicker(string folder) : IGameExportFolderPicker
    {
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(folder);
    }

    private const string Header = "Title;Medium;DvdNumber;Location;Format;SizeBytes;Notes\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisibleControlsPreviewThenCancelOrConfirmWithoutPrematureCatalogChanges(bool confirm)
    {
        using var data = new ImportData(Header + "Game;Usb;;/game.rar;ISO;1024;\nInvalid;Usb;;/bad.rar;ISO;bad;\n");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await session.Dispatch(async () =>
        {
            var window = data.OpenWindow();
            try
            {
                var vm = (MainViewModel)window.DataContext!;
                var before = File.ReadAllBytes(data.CatalogPath);
                Click(window, "ImportCatalogButton");
                Assert.True(Control<ScrollViewer>(window, "ImportFormPanel").IsVisible);
                Click(window, "ChooseImportFileButton");
                await vm.ChooseImportFileCommand.ExecutionTask!;
                Assert.Equal(data.CsvPath, Control<TextBlock>(window, "ImportSourcePathText").Text);
                Click(window, "PreviewImportButton");
                await vm.PreviewImportCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.RunJobs();

                Assert.Contains("1 new games, 0 modified games, 1 erroneous rows", Control<TextBlock>(window, "ImportStatusText").Text!);
                Assert.Single(Control<ItemsControl>(window, "ImportRowErrorsList").Items);
                Assert.True(Control<StackPanel>(window, "ImportConfirmationPanel").IsVisible);
                Assert.False(Control<Button>(window, "AddGameButton").IsEnabled);
                Assert.Empty(vm.Games);
                Assert.Equal(before, File.ReadAllBytes(data.CatalogPath));

                Click(window, confirm ? "ConfirmImportButton" : "CancelImportButton");
                if (confirm) await vm.ConfirmImportCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.RunJobs();
                if (confirm)
                {
                    Assert.Equal("Game", Assert.Single(Control<ListBox>(window, "GamesList").Items.Cast<GameRecord>()).Title);
                    Assert.Contains("1 created, 0 updated, 0 failed", Control<TextBlock>(window, "ImportStatusText").Text!);
                    Assert.False(Control<StackPanel>(window, "ImportConfirmationPanel").IsVisible);
                    Assert.Single(new SqliteGameRecordRepository(data.CatalogPath).List());
                }
                else
                {
                    Assert.False(Control<ScrollViewer>(window, "ImportFormPanel").IsVisible);
                    Assert.Empty(vm.Games);
                    Assert.Equal(before, File.ReadAllBytes(data.CatalogPath));
                }
            }
            finally { window.Close(); }
            return 0;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("malformed")]
    [InlineData("changed")]
    public async Task VisibleFeedbackDistinguishesEmptyPreviewAndSourceFailures(string scenario)
    {
        using var data = new ImportData(scenario == "empty" ? Header : scenario == "malformed"
            ? Header + "\"unterminated" : Header + "Game;Usb;;/game.rar;ISO;1;\n");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await session.Dispatch(async () =>
        {
            var window = data.OpenWindow();
            try
            {
                var vm = (MainViewModel)window.DataContext!;
                var before = File.ReadAllBytes(data.CatalogPath);
                Click(window, "ImportCatalogButton");
                Click(window, "ChooseImportFileButton");
                await vm.ChooseImportFileCommand.ExecutionTask!;
                Click(window, "PreviewImportButton");
                await vm.PreviewImportCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(10));
                if (scenario == "changed")
                {
                    File.AppendAllText(data.CsvPath, "Other;Usb;;/other.rar;ISO;1;\n");
                    Click(window, "ConfirmImportButton");
                    await vm.ConfirmImportCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(10));
                }
                Dispatcher.UIThread.RunJobs();
                if (scenario == "empty")
                {
                    Assert.Contains("0 new games, 0 modified games, 0 erroneous rows", Control<TextBlock>(window, "ImportStatusText").Text!);
                    Assert.False(Control<TextBlock>(window, "ImportErrorText").IsVisible);
                }
                else
                {
                    Assert.True(Control<TextBlock>(window, "ImportErrorText").IsVisible);
                    Assert.NotEmpty(Control<TextBlock>(window, "ImportErrorText").Text!);
                    Assert.False(Control<Button>(window, "ConfirmImportButton").IsEnabled);
                    if (scenario == "changed") Assert.Contains("changed", Control<TextBlock>(window, "ImportErrorText").Text!);
                }
                Assert.Empty(vm.Games);
                Assert.Equal(before, File.ReadAllBytes(data.CatalogPath));
            }
            finally { window.Close(); }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BusyIndicatorAndCancelControlFollowBackgroundEvaluation()
    {
        using var data = new ImportData(Header);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new BlockingSource(entered, release);
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await session.Dispatch(async () =>
        {
            var repository = new SqliteGameRecordRepository(data.CatalogPath);
            var vm = new MainViewModel(new GameCatalogService(repository), null,
                importFilePicker: new Picker(data.CsvPath), importService: new GameCatalogImportService(repository, source));
            var window = new MainWindow { DataContext = vm };
            window.Show();
            try
            {
                Click(window, "ImportCatalogButton");
                Click(window, "ChooseImportFileButton");
                await vm.ChooseImportFileCommand.ExecutionTask!;
                Click(window, "PreviewImportButton");
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.RunJobs();
                Assert.True(Control<ProgressBar>(window, "ImportProgressBar").IsVisible);
                Assert.True(Control<ProgressBar>(window, "ImportProgressBar").IsIndeterminate);
                Assert.False(Control<Button>(window, "ChooseImportFileButton").IsEnabled);
                Assert.False(Control<Button>(window, "PreviewImportButton").IsEnabled);
                Click(window, "CancelImportButton");
                release.Set();
                await vm.PreviewImportCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.RunJobs();
                Assert.False(Control<ProgressBar>(window, "ImportProgressBar").IsVisible);
                Assert.False(Control<ScrollViewer>(window, "ImportFormPanel").IsVisible);
                Assert.Empty(vm.Games);
            }
            finally { release.Set(); window.Close(); }
            return 0;
        }, CancellationToken.None);
    }

    private static T Control<T>(MainWindow window, string name) where T : Control =>
        Assert.IsType<T>(window.FindControl<T>(name));

    private static void Click(MainWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var button = Control<Button>(window, name);
        button.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEnabled);
        button.Focus();
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Picker(string path) : IGameImportFilePicker
    {
        public Task<string?> PickFileAsync() => Task.FromResult<string?>(path);
    }

    private sealed class BlockingSource(TaskCompletionSource<bool> entered, ManualResetEventSlim release) : IGameCatalogImportSource
    {
        public IGameCatalogImportReader Open(string path)
        {
            entered.SetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return new FileGameCatalogImportSource().Open(path);
        }
    }

    private sealed class ImportData : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "PSPMan.Import.UI.Tests", Guid.NewGuid().ToString("N"));
        public string CsvPath => Path.Combine(Folder, "games.csv");
        public string CatalogPath => Path.Combine(Folder, "PSPMan", "catalog.db");
        public ImportData(string content)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(CsvPath, content);
            CatalogStartup.CreateMainViewModel(Folder);
        }
        public MainWindow OpenWindow()
        {
            var window = new MainWindow { DataContext = CatalogStartup.CreateMainViewModel(Folder, new TemplateFolderPicker(Folder), new Picker(CsvPath)) };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }
        public void Dispose() => Directory.Delete(Folder, true);
    }
}
