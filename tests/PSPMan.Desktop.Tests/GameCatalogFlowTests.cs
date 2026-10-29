using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using PSPMan.Core.Games;
using PSPMan.Desktop.Services;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;
using PSPMan.Infraestructure.Games;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public class GameCatalogFlowTests
{
    private const string ExportHeader =
        "Title;Medium;DVD Number;Location;Format;Decompressed Size (bytes);Approximate Size (binary units);Notes\r\n";

    [Fact]
    public async Task SqliteExportKeepsSpecialTextInSingleCellsAndProtectsFormulaLikeValues()
    {
        const string formulaTitle = " \u2003=SUM(1;2)";
        const string multilineNotes = "Éxito; commas, colons: \"quoted\"\r\nSecond line";
        const string formulaNotes = " \u2003+SUM(1;2) \"quoted\"";
        using var data = new TemporaryApplicationData();
        var service = CatalogStartup.CreateMainViewModel(data.Path).CatalogService!;
        var first = service.Create(new GameRecordInput(formulaTitle, GameMedium.Dvd, 1,
            "/GAMES/first.rar", GameFormat.Iso, 1_024, multilineNotes));
        var second = service.Create(new GameRecordInput("Pokémon, azul: edición", GameMedium.Usb, null,
            "/GAMES/second.rar", GameFormat.Chd, 2_048, formulaNotes));
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var catalogBefore = File.ReadAllBytes(data.CatalogPath);
        var destination = System.IO.Path.Combine(data.Path, "special-games.csv");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(data.Path));
            try
            {
                Click(window, "ExportCatalogButton");
                Click(window, "ChooseExportFolderButton");
                Control<TextBox>(window, "ExportFileNameInput").Text = "special-games.csv";
                Dispatcher.UIThread.RunJobs();
                Click(window, "ExportFileButton");
                Assert.Equal(destination, Control<TextBlock>(window, "ExportSuccessPathText").Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        var bytes = File.ReadAllBytes(destination);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var csv = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"\t \u2003=SUM(1;2)\"", csv);
        Assert.Contains("\"Éxito; commas, colons: \"\"quoted\"\"\r\nSecond line\"", csv);
        Assert.Contains("\"\t \u2003+SUM(1;2) \"\"quoted\"\"\"", csv);

        using var reader = new StringReader(csv[1..]);
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(";");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            rows.Add(parser.ReadFields()!);
        }

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal(8, row.Length));
        Assert.Equal($"\t{formulaTitle}", rows[1][0]);
        Assert.Equal(multilineNotes, rows[1][7]);
        Assert.Equal("Pokémon, azul: edición", rows[2][0]);
        Assert.Equal($"\t{formulaNotes}", rows[2][7]);
        Assert.Equal(catalogBefore, File.ReadAllBytes(data.CatalogPath));
        Assert.Equal([first.Value!, second.Value!], new SqliteGameRecordRepository(data.CatalogPath).List());
    }

    [Fact]
    public async Task SavedSqliteCatalogExportsEveryOfflineRecordInSavedOrderWithoutChangingSources()
    {
        using var data = new TemporaryApplicationData();
        var service = CatalogStartup.CreateMainViewModel(data.Path).CatalogService!;
        var dvd = service.Create(new GameRecordInput(
            "First disc", GameMedium.Dvd, 2, "/GAMES/disc.rar", GameFormat.Iso, 1_024, "Disc 2"));
        var usb = service.Create(new GameRecordInput(
            "Second USB", GameMedium.Usb, null, "/GAMES/usb.rar", GameFormat.Chd, 2_048, null));
        var card = service.Create(new GameRecordInput(
            "Third card", GameMedium.MemoryCard, null, "/GAMES/card.rar", GameFormat.Iso, 3_072, "Stored"));
        Assert.True(dvd.IsSuccess);
        Assert.True(usb.IsSuccess);
        Assert.True(card.IsSuccess);

        var gameFile = System.IO.Path.Combine(data.Path, "media", "GAMES", "disc.rar");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(gameFile)!);
        File.WriteAllBytes(gameFile, [0, 1, 2, 255]);
        var gameFileBefore = File.ReadAllBytes(gameFile);
        var catalogBefore = File.ReadAllBytes(data.CatalogPath);
        var destination = System.IO.Path.Combine(data.Path, "saved-games.csv");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(data.Path));
            try
            {
                var list = Control<ListBox>(window, "GamesList");
                Assert.Equal([dvd.Value, usb.Value, card.Value], list.ItemsSource!.Cast<GameRecord>());
                list.SelectedItem = usb.Value;
                Dispatcher.UIThread.RunJobs();
                Click(window, "ExportCatalogButton");
                Click(window, "ChooseExportFolderButton");
                Control<TextBox>(window, "ExportFileNameInput").Text = "saved-games.csv";
                Dispatcher.UIThread.RunJobs();
                Click(window, "ExportFileButton");
                Assert.Equal(destination, Control<TextBlock>(window, "ExportSuccessPathText").Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        var expected = ExportHeader
            + "First disc;DVD;2;/GAMES/disc.rar;ISO;1024;1 KiB;Disc 2\r\n"
            + "Second USB;USB;;/GAMES/usb.rar;CHD;2048;2 KiB;\r\n"
            + "Third card;Memory Card;;/GAMES/card.rar;ISO;3072;3 KiB;Stored\r\n";
        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(expected)], File.ReadAllBytes(destination));
        Assert.Equal(catalogBefore, File.ReadAllBytes(data.CatalogPath));
        Assert.Equal(gameFileBefore, File.ReadAllBytes(gameFile));
        Assert.False(File.Exists(System.IO.Path.Combine(data.Path, "media", "GAMES", "usb.rar")));
    }

    [Fact]
    public async Task EmptySqliteCatalogExportsOnlyHeaderWithoutSelectionOrSourceChanges()
    {
        using var data = new TemporaryApplicationData();
        Assert.Empty(CatalogStartup.CreateMainViewModel(data.Path).Games);
        var catalogBefore = File.ReadAllBytes(data.CatalogPath);
        var destination = System.IO.Path.Combine(data.Path, "empty-saved-games.csv");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(data.Path));
            try
            {
                var viewModel = (MainViewModel)window.DataContext!;
                Assert.Empty(viewModel.Games);
                Assert.Null(viewModel.SelectedGame);
                Click(window, "ExportCatalogButton");
                Click(window, "ChooseExportFolderButton");
                Control<TextBox>(window, "ExportFileNameInput").Text = "empty-saved-games.csv";
                Dispatcher.UIThread.RunJobs();
                Click(window, "ExportFileButton");
                Assert.Equal(destination, Control<TextBlock>(window, "ExportSuccessPathText").Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(ExportHeader)], File.ReadAllBytes(destination));
        Assert.Equal(catalogBefore, File.ReadAllBytes(data.CatalogPath));
    }

    [Fact]
    public async Task CancelledFolderSelectionLeavesCatalogAndDestinationUnchangedWithoutSuccess()
    {
        using var data = new TemporaryApplicationData();
        Assert.Empty(CatalogStartup.CreateMainViewModel(data.Path).Games);
        var catalogBefore = File.ReadAllBytes(data.CatalogPath);
        var destination = System.IO.Path.Combine(data.Path, "pspman-games.csv");
        var destinationBefore = new byte[] { 0, 1, 2, 255 };
        File.WriteAllBytes(destination, destinationBefore);
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(null));
            try
            {
                Click(window, "ExportCatalogButton");
                Click(window, "ChooseExportFolderButton");

                Assert.False(Control<StackPanel>(window, "ExportFormPanel").IsVisible);
                Assert.False(Control<StackPanel>(window, "ExportSuccessPanel").IsVisible);
                Assert.Null(((MainViewModel)window.DataContext!).ExportedPath);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal(destinationBefore, File.ReadAllBytes(destination));
        Assert.Equal(catalogBefore, File.ReadAllBytes(data.CatalogPath));
    }

    [Fact]
    public async Task ExistingExportShowsConfirmationAndDeclinePreservesFileWithoutSuccess()
    {
        using var data = new TemporaryApplicationData();
        Assert.Empty(CatalogStartup.CreateMainViewModel(data.Path).Games);
        var catalogBefore = File.ReadAllBytes(data.CatalogPath);
        var destination = System.IO.Path.Combine(data.Path, "pspman-games.csv");
        var original = new byte[] { 0, 1, 2, 255 };
        File.WriteAllBytes(destination, original);
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(data.Path));
            try
            {
                Click(window, "ExportCatalogButton");
                Click(window, "ChooseExportFolderButton");
                Click(window, "ExportFileButton");

                Assert.True(Control<StackPanel>(window, "ExportReplacementPanel").IsVisible);
                Assert.Equal(destination, Control<TextBlock>(window, "PendingExportPathText").Text);
                Assert.True(Control<Button>(window, "ReplaceExportButton").IsEnabled);
                Assert.True(Control<Button>(window, "DeclineExportButton").IsEnabled);
                Assert.False(Control<StackPanel>(window, "ExportSuccessPanel").IsVisible);

                Click(window, "DeclineExportButton");

                Assert.False(Control<StackPanel>(window, "ExportReplacementPanel").IsVisible);
                Assert.True(Control<StackPanel>(window, "ExportFormPanel").IsVisible);
                Assert.False(Control<StackPanel>(window, "ExportSuccessPanel").IsVisible);
                Assert.Null(((MainViewModel)window.DataContext!).ExportedPath);
                Assert.Equal(original, File.ReadAllBytes(destination));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal(catalogBefore, File.ReadAllBytes(data.CatalogPath));
    }

    [Fact]
    public async Task CorruptCatalogReadReportsErrorAndPreservesExistingDestination()
    {
        using var data = new TemporaryApplicationData();
        Assert.Empty(CatalogStartup.CreateMainViewModel(data.Path).Games);
        var destination = System.IO.Path.Combine(data.Path, "pspman-games.csv");
        var destinationBefore = new byte[] { 5, 6, 7, 8 };
        File.WriteAllBytes(destination, destinationBefore);
        var corruptCatalog = new byte[] { 0, 1, 2, 3, 4, 5 };
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(data.Path));
            try
            {
                Click(window, "ExportCatalogButton");
                Click(window, "ChooseExportFolderButton");
                File.WriteAllBytes(data.CatalogPath, corruptCatalog);
                Click(window, "ExportFileButton");

                var error = Control<TextBlock>(window, "ExportErrorText");
                Assert.True(error.IsVisible);
                Assert.Contains("corrupt", error.Text, StringComparison.OrdinalIgnoreCase);
                Assert.False(Control<StackPanel>(window, "ExportSuccessPanel").IsVisible);
                Assert.False(Control<StackPanel>(window, "ExportReplacementPanel").IsVisible);
                Assert.False(Control<Button>(window, "ExportCatalogButton").IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal(destinationBefore, File.ReadAllBytes(destination));
        Assert.Equal(corruptCatalog, File.ReadAllBytes(data.CatalogPath));
    }

    [Fact]
    public async Task ApprovedReplacementShowsSavedPathAfterPublication()
    {
        using var data = new TemporaryApplicationData();
        var destination = System.IO.Path.Combine(data.Path, "pspman-games.csv");
        File.WriteAllText(destination, "previous export");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(data.Path));
            try
            {
                Click(window, "ExportCatalogButton");
                Click(window, "ChooseExportFolderButton");
                Click(window, "ExportFileButton");
                Assert.False(Control<StackPanel>(window, "ExportSuccessPanel").IsVisible);

                Click(window, "ReplaceExportButton");

                Assert.Equal(GameCatalogCsvSerializer.SerializeEmptyCatalog(), File.ReadAllBytes(destination));
                Assert.True(Control<StackPanel>(window, "ExportSuccessPanel").IsVisible);
                Assert.Equal(destination, Control<TextBlock>(window, "ExportSuccessPathText").Text);
                Assert.False(Control<TextBlock>(window, "ExportErrorText").IsVisible);
                Assert.False(Control<StackPanel>(window, "ExportReplacementPanel").IsVisible);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedWriteShowsVisibleErrorWithoutSuccess()
    {
        using var data = new TemporaryApplicationData();
        var missingFolder = System.IO.Path.Combine(data.Path, "missing-export-folder");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(missingFolder));
            try
            {
                Click(window, "ExportCatalogButton");
                Click(window, "ChooseExportFolderButton");
                Click(window, "ExportFileButton");

                var error = Control<TextBlock>(window, "ExportErrorText");
                Assert.True(error.IsVisible);
                Assert.Contains("write", error.Text, StringComparison.OrdinalIgnoreCase);
                Assert.False(Control<StackPanel>(window, "ExportSuccessPanel").IsVisible);
                Assert.False(Directory.Exists(missingFolder));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedReplacementWriteShowsErrorAndPreservesExistingBytes()
    {
        using var data = new TemporaryApplicationData();
        Assert.Empty(CatalogStartup.CreateMainViewModel(data.Path).Games);
        var catalogBefore = File.ReadAllBytes(data.CatalogPath);
        var destination = System.IO.Path.Combine(data.Path, "pspman-games.csv");
        var destinationBefore = new byte[] { 9, 8, 7, 6 };
        File.WriteAllBytes(destination, destinationBefore);
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var viewModel = new MainViewModel(
                new GameCatalogService(new SqliteGameRecordRepository(data.CatalogPath)), null,
                new FixedExportFolderPicker(data.Path), new FailingExportWriter(), data.CatalogPath);
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Click(window, "ExportCatalogButton");
                Click(window, "ChooseExportFolderButton");
                Click(window, "ExportFileButton");
                Assert.True(Control<StackPanel>(window, "ExportReplacementPanel").IsVisible);
                Click(window, "ReplaceExportButton");

                var error = Control<TextBlock>(window, "ExportErrorText");
                Assert.True(error.IsVisible);
                Assert.Contains("write", error.Text, StringComparison.OrdinalIgnoreCase);
                Assert.False(Control<StackPanel>(window, "ExportSuccessPanel").IsVisible);
                Assert.False(Control<StackPanel>(window, "ExportReplacementPanel").IsVisible);
                Assert.Null(viewModel.ExportedPath);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal(destinationBefore, File.ReadAllBytes(destination));
        Assert.Equal(catalogBefore, File.ReadAllBytes(data.CatalogPath));
    }

    [Fact]
    public async Task ExportFormCanChooseFolderRenameAndCancelWithoutSelection()
    {
        using var data = new TemporaryApplicationData();
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(data.Path));
            try
            {
                Assert.Null(((MainViewModel)window.DataContext!).SelectedGame);
                Assert.False(Control<StackPanel>(window, "ExportFormPanel").IsVisible);
                Click(window, "ExportCatalogButton");

                Assert.True(Control<StackPanel>(window, "ExportFormPanel").IsVisible);
                Assert.Equal("pspman-games.csv", Control<TextBox>(window, "ExportFileNameInput").Text);
                Click(window, "ChooseExportFolderButton");
                Assert.Equal(data.Path, Control<TextBlock>(window, "ExportFolderPathText").Text);

                Control<TextBox>(window, "ExportFileNameInput").Text = "my-games.csv";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("my-games.csv", ((MainViewModel)window.DataContext!).ExportFileName);
                Click(window, "CancelExportButton");

                Assert.False(Control<StackPanel>(window, "ExportFormPanel").IsVisible);
                Assert.False(File.Exists(System.IO.Path.Combine(data.Path, "my-games.csv")));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EmptyCatalogCanExportFromWindowWithoutSelection()
    {
        using var data = new TemporaryApplicationData();
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(data.Path));
            try
            {
                Assert.True(Control<TextBlock>(window, "EmptyStateText").IsVisible);
                Assert.Null(((MainViewModel)window.DataContext!).SelectedGame);
                Click(window, "ExportCatalogButton");
                Click(window, "ChooseExportFolderButton");
                Control<TextBox>(window, "ExportFileNameInput").Text = "empty-games.csv";
                Dispatcher.UIThread.RunJobs();
                Click(window, "ExportFileButton");

                var destination = System.IO.Path.Combine(data.Path, "empty-games.csv");
                Assert.Equal(GameCatalogCsvSerializer.SerializeEmptyCatalog(), File.ReadAllBytes(destination));
                Assert.Equal(destination, ((MainViewModel)window.DataContext!).ExportedPath);
                Assert.False(Control<StackPanel>(window, "ExportFormPanel").IsVisible);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

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
    public async Task MemoryCardRecordDisplaysSavedMediumWithoutConnectedMedia()
    {
        using var data = new TemporaryApplicationData();
        var saved = CatalogStartup.CreateMainViewModel(data.Path).CatalogService!.Create(
            new GameRecordInput("Portable", GameMedium.MemoryCard, null,
                "/GAMES/Portable.rar", GameFormat.Chd, 2_048, null));
        Assert.True(saved.IsSuccess);
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                Control<ListBox>(window, "GamesList").SelectedItem = saved.Value;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("Memory Card", Control<TextBlock>(window, "SelectedMediumText").Text);
                Assert.Equal("Not applicable", Control<TextBlock>(window, "SelectedDvdNumberText").Text);
                Assert.Equal("CHD", Control<TextBlock>(window, "SelectedFormatText").Text);
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

    [Fact]
    public async Task InvalidFieldsShowErrorsAndDoNotPersist()
    {
        using var data = new TemporaryApplicationData();
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                Control<TextBox>(window, "TitleInput").Text = " \t ";
                SelectMedium(window, GameMedium.Dvd);
                Control<TextBox>(window, "DvdNumberInput").Text = "0";
                Control<TextBox>(window, "LocationInput").Text = "/GAMES/../Broken.rar";
                SelectFormat(window, GameFormat.Iso);
                Control<TextBox>(window, "SizeInput").Text = "0";
                Control<TextBox>(window, "NotesInput").Text = new string('x', 2_501);
                Click(window, "AddGameButton");

                Assert.Equal("Title is required.", Control<TextBlock>(window, "TitleErrorText").Text);
                Assert.True(Control<TextBlock>(window, "TitleErrorText").IsVisible);
                Assert.Contains("DVD number", Control<TextBlock>(window, "DvdNumberErrorText").Text);
                Assert.Contains("Location", Control<TextBlock>(window, "LocationErrorText").Text);
                Assert.Contains("Size", Control<TextBlock>(window, "SizeErrorText").Text);
                Assert.Contains("2,500", Control<TextBlock>(window, "NotesErrorText").Text);
                Assert.Equal("Correct the fields above.", Control<TextBlock>(window, "OperationErrorText").Text);
                Assert.Empty(((MainViewModel)window.DataContext!).Games);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Empty(CatalogStartup.CreateMainViewModel(data.Path).Games);
    }

    [Fact]
    public async Task DuplicateCreateShowsConflictAndLeavesSavedRecordUnchanged()
    {
        using var data = new TemporaryApplicationData();
        var original = Seed(data.Path, "Original", "/GAMES/Same.rar");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                FillUsbEditor(window, "Different title", original.Location);
                Click(window, "AddGameButton");

                Assert.Contains("same medium", Control<TextBlock>(window, "OperationErrorText").Text);
                Assert.Equal(original, Assert.Single(((MainViewModel)window.DataContext!).Games));
                Assert.Equal("Different title", Control<TextBox>(window, "TitleInput").Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal(original, Assert.Single(CatalogStartup.CreateMainViewModel(data.Path).Games));
    }

    [Fact]
    public async Task OfflineGameCanBeEditedThroughControlsAndReopened()
    {
        using var data = new TemporaryApplicationData();
        var original = Seed(data.Path, "Original", "/DISCONNECTED/GAMES/Original.rar");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                Control<ListBox>(window, "GamesList").SelectedItem = original;
                Click(window, "EditSelectedButton");
                Assert.Equal("Original", Control<TextBox>(window, "TitleInput").Text);
                Assert.Equal(original.Location, Control<TextBox>(window, "LocationInput").Text);

                Control<TextBox>(window, "TitleInput").Text = "Corrected";
                Control<TextBox>(window, "LocationInput").Text = "/DISCONNECTED/GAMES/Corrected.rar";
                Control<TextBox>(window, "SizeInput").Text = "4096";
                Click(window, "SaveChangesButton");

                var saved = Assert.Single(((MainViewModel)window.DataContext!).Games);
                Assert.Equal(original.Id, saved.Id);
                Assert.Equal("Corrected", saved.Title);
                Assert.Equal("Corrected", Control<TextBlock>(window, "SelectedTitleText").Text);
                Assert.Equal("/DISCONNECTED/GAMES/Corrected.rar",
                    Control<TextBlock>(window, "SelectedLocationText").Text);
                Assert.Equal("4096", Control<TextBlock>(window, "SelectedSizeText").Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal("Corrected", Assert.Single(CatalogStartup.CreateMainViewModel(data.Path).Games).Title);
    }

    [Fact]
    public async Task DuplicateEditShowsConflictAndLeavesBothRecordsUnchanged()
    {
        using var data = new TemporaryApplicationData();
        var first = Seed(data.Path, "First", "/GAMES/First.rar");
        var second = Seed(data.Path, "Second", "/GAMES/Second.rar");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                Control<ListBox>(window, "GamesList").SelectedItem = second;
                Click(window, "EditSelectedButton");
                Control<TextBox>(window, "LocationInput").Text = first.Location;
                Click(window, "SaveChangesButton");

                Assert.Contains("same medium", Control<TextBlock>(window, "OperationErrorText").Text);
                Assert.Equal("Second", Control<TextBlock>(window, "SelectedTitleText").Text);
                Assert.Equal(first.Location, Control<TextBox>(window, "LocationInput").Text);
                Assert.Equal([first, second], ((MainViewModel)window.DataContext!).Games);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal([first, second], CatalogStartup.CreateMainViewModel(data.Path).Games);
    }

    [Fact]
    public async Task FailedCreateShowsErrorAndDoesNotClaimItWasSaved()
    {
        using var data = new TemporaryApplicationData();
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                FillUsbEditor(window, "Draft", "/GAMES/Draft.rar");
                File.Move(data.CatalogPath, data.MovedCatalogPath);
                Click(window, "AddGameButton");

                Assert.Contains("Unable to save", Control<TextBlock>(window, "OperationErrorText").Text);
                Assert.Empty(((MainViewModel)window.DataContext!).Games);
                Assert.Equal("Draft", Control<TextBox>(window, "TitleInput").Text);
                Assert.False(File.Exists(data.CatalogPath));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Empty(new SqliteGameRecordRepository(data.MovedCatalogPath).List());
    }

    [Fact]
    public async Task FailedEditShowsErrorAndKeepsPersistedRecordAndDraft()
    {
        using var data = new TemporaryApplicationData();
        var original = Seed(data.Path, "Original", "/GAMES/Original.rar");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                Control<ListBox>(window, "GamesList").SelectedItem = original;
                Click(window, "EditSelectedButton");
                Control<TextBox>(window, "TitleInput").Text = "Unsaved draft";
                File.Move(data.CatalogPath, data.MovedCatalogPath);
                Click(window, "SaveChangesButton");

                Assert.Contains("Unable to update", Control<TextBlock>(window, "OperationErrorText").Text);
                Assert.Equal(original, Assert.Single(((MainViewModel)window.DataContext!).Games));
                Assert.Equal("Original", Control<TextBlock>(window, "SelectedTitleText").Text);
                Assert.Equal("Unsaved draft", Control<TextBox>(window, "TitleInput").Text);
                Assert.False(File.Exists(data.CatalogPath));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal(original, Assert.Single(new SqliteGameRecordRepository(data.MovedCatalogPath).List()));
    }

    [Fact]
    public async Task CancelDeleteLeavesCatalogRecordAndGameFileUnchanged()
    {
        using var data = new TemporaryApplicationData();
        var record = Seed(data.Path, "First", "/GAMES/First.rar");
        var gameFile = System.IO.Path.Combine(data.Path, "GAMES", "First.rar");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(gameFile)!);
        var bytes = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(gameFile, bytes);
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                Control<ListBox>(window, "GamesList").SelectedItem = record;
                Click(window, "DeleteSelectedButton");

                Assert.True(Control<StackPanel>(window, "DeleteConfirmationPanel").IsVisible);
                Assert.Equal("First", Control<TextBlock>(window, "PendingDeleteTitleText").Text);
                Assert.True(Control<Button>(window, "ConfirmDeleteButton").IsEnabled);
                Assert.Equal(record, Assert.Single(((MainViewModel)window.DataContext!).Games));

                Click(window, "CancelDeleteButton");

                Assert.False(Control<StackPanel>(window, "DeleteConfirmationPanel").IsVisible);
                Assert.Equal(record, Assert.Single(((MainViewModel)window.DataContext!).Games));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal(record, Assert.Single(CatalogStartup.CreateMainViewModel(data.Path).Games));
        Assert.Equal(bytes, File.ReadAllBytes(gameFile));
    }

    [Fact]
    public async Task ConfirmDeleteRemovesOnlyRequestedRecordAndKeepsGameFile()
    {
        using var data = new TemporaryApplicationData();
        var first = Seed(data.Path, "First", "/GAMES/First.rar");
        var second = Seed(data.Path, "Second", "/GAMES/Second.rar");
        var gameFile = System.IO.Path.Combine(data.Path, "GAMES", "First.rar");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(gameFile)!);
        var bytes = new byte[] { 5, 6, 7, 8 };
        File.WriteAllBytes(gameFile, bytes);
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                var list = Control<ListBox>(window, "GamesList");
                list.SelectedItem = first;
                Click(window, "DeleteSelectedButton");
                list.SelectedItem = second;
                Dispatcher.UIThread.RunJobs();

                Assert.True(Control<StackPanel>(window, "DeleteConfirmationPanel").IsVisible);
                Assert.Equal("First", Control<TextBlock>(window, "PendingDeleteTitleText").Text);
                Click(window, "ConfirmDeleteButton");

                Assert.False(Control<StackPanel>(window, "DeleteConfirmationPanel").IsVisible);
                Assert.Equal(second, Assert.Single(((MainViewModel)window.DataContext!).Games));
                Assert.Equal(second, Assert.Single(list.ItemsSource!.Cast<GameRecord>()));
                Assert.Null(Control<TextBlock>(window, "OperationErrorText").Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal(second, Assert.Single(CatalogStartup.CreateMainViewModel(data.Path).Games));
        Assert.Equal(bytes, File.ReadAllBytes(gameFile));
    }

    [Fact]
    public async Task FailedDeleteLeavesRecordVisibleAndShowsAnError()
    {
        using var data = new TemporaryApplicationData();
        var record = Seed(data.Path, "First", "/GAMES/First.rar");
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path);
            try
            {
                Control<ListBox>(window, "GamesList").SelectedItem = record;
                Click(window, "DeleteSelectedButton");
                File.Move(data.CatalogPath, data.MovedCatalogPath);
                Click(window, "ConfirmDeleteButton");

                Assert.True(Control<StackPanel>(window, "DeleteConfirmationPanel").IsVisible);
                Assert.Contains("Unable to delete", Control<TextBlock>(window, "OperationErrorText").Text);
                Assert.True(Control<TextBlock>(window, "OperationErrorText").IsVisible);
                Assert.Equal(record, Assert.Single(((MainViewModel)window.DataContext!).Games));
                Assert.Equal(record, Control<ListBox>(window, "GamesList").SelectedItem);
                Assert.False(File.Exists(data.CatalogPath));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal(record, Assert.Single(new SqliteGameRecordRepository(data.MovedCatalogPath).List()));
    }

    [Fact]
    public async Task CorruptCatalogStartupShowsErrorWithoutOverwritingData()
    {
        using var data = new TemporaryApplicationData();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(data.CatalogPath)!);
        var bytes = new byte[] { 0, 1, 2, 3, 4, 5 };
        File.WriteAllBytes(data.CatalogPath, bytes);
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));

        await session.Dispatch(() =>
        {
            var window = OpenWindow(data.Path, new FixedExportFolderPicker(data.Path));
            try
            {
                var error = Control<TextBlock>(window, "CatalogErrorText");
                Assert.True(error.IsVisible);
                Assert.Contains("corrupt", error.Text, StringComparison.OrdinalIgnoreCase);
                Assert.False(Control<Button>(window, "AddGameButton").IsVisible);
                Assert.False(Control<ListBox>(window, "GamesList").IsVisible);
                Assert.False(Control<TextBlock>(window, "EmptyStateText").IsVisible);
                Assert.False(Control<Button>(window, "ExportCatalogButton").IsEnabled);
                Assert.False(((MainViewModel)window.DataContext!).AddGameCommand.CanExecute(null));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        Assert.Equal(bytes, File.ReadAllBytes(data.CatalogPath));
    }

    private static GameRecord Seed(string root, string title, string location)
    {
        var viewModel = CatalogStartup.CreateMainViewModel(root);
        var result = viewModel.CatalogService!.Create(new GameRecordInput(
            title, GameMedium.Usb, null, location, GameFormat.Iso, 2_048, null));
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    private static void FillUsbEditor(MainWindow window, string title, string location)
    {
        Control<TextBox>(window, "TitleInput").Text = title;
        SelectMedium(window, GameMedium.Usb);
        Control<TextBox>(window, "LocationInput").Text = location;
        SelectFormat(window, GameFormat.Iso);
        Control<TextBox>(window, "SizeInput").Text = "2048";
    }

    private static void SelectMedium(MainWindow window, GameMedium value)
    {
        var combo = Control<ComboBox>(window, "MediumInput");
        combo.SelectedItem = combo.ItemsSource!.Cast<GameMediumChoice>().Single(choice => choice.Value == value);
    }

    private static void SelectFormat(MainWindow window, GameFormat value)
    {
        var combo = Control<ComboBox>(window, "FormatInput");
        combo.SelectedItem = combo.ItemsSource!.Cast<GameFormatChoice>().Single(choice => choice.Value == value);
    }

    private static void Click(MainWindow window, string buttonName)
    {
        Dispatcher.UIThread.RunJobs();
        var button = Control<Button>(window, buttonName);
        button.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsEnabled);
        button.Focus();
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static MainWindow OpenWindow(string applicationDataRoot,
        IGameExportFolderPicker? exportFolderPicker = null)
    {
        var window = new MainWindow
        {
            DataContext = CatalogStartup.CreateMainViewModel(applicationDataRoot, exportFolderPicker)
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Control<T>(MainWindow window, string name) where T : Control =>
        Assert.IsType<T>(window.FindControl<T>(name));

    private sealed class FixedExportFolderPicker(string? folderPath) : IGameExportFolderPicker
    {
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(folderPath);
    }

    private sealed class FailingExportWriter : IGameCatalogExportWriter
    {
        private readonly AtomicCsvExportWriter _writer = new();

        public ExportDestinationSnapshot Capture(string destinationPath) => _writer.Capture(destinationPath);

        public void Publish(string destinationPath, ReadOnlyMemory<byte> content,
            ExportDestinationSnapshot inspectedDestination, bool overwriteApproved)
        {
            Assert.True(overwriteApproved);
            throw new IOException("Simulated replacement write failure.");
        }
    }

    private sealed class TemporaryApplicationData : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "PSPMan.Headless.Tests", Guid.NewGuid().ToString());

        public TemporaryApplicationData() => Directory.CreateDirectory(Path);

        public string CatalogPath => System.IO.Path.Combine(Path, "PSPMan", "catalog.db");

        public string MovedCatalogPath => System.IO.Path.Combine(Path, "PSPMan", "moved.db");

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
