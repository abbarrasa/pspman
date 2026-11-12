using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Desktop.Services;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class MainWindowDeviceIntegrationTests
{
    [Fact]
    public async Task MinimumWindowKeepsTheCatalogEditorCsvActionsAndBoundedDevicePanelAccessible()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await RunUi(ui, async () =>
        {
            await using var test = await Fixture.Create(Device("usb"), Device("psp", psp: true));
            var panel = Control<DevicesView>(test.Window, "DevicePanel");
            Assert.Same(test.Devices, panel.DataContext);
            AssertInside(test.Window, panel);
            Assert.InRange(panel.Bounds.Width, 180, 320);
            foreach (var name in new[] { "ImportCatalogButton", "ExportCatalogButton", "GetCsvTemplateButton" })
            {
                var button = Control<Button>(test.Window, name);
                Assert.True(button.IsEnabled); AssertInside(test.Window, button);
            }
            AssertInside(test.Window, Control<ListBox>(test.Window, "GamesList"));
            SelectGame(test, test.First);
            Control<TextBox>(test.Window, "TitleInput").BringIntoView(); Pump();
            AssertInside(test.Window, Control<TextBox>(test.Window, "TitleInput"));
            AssertInside(test.Window, Control<TextBlock>(test.Window, "SelectedTitleText"));
            Assert.Equal(test.First, test.Main.SelectedGame);
            Assert.Equal(0, test.Repository.Writes);
        });
    }

    [Fact]
    public async Task KeyboardPspChoicesBelongToEachSelectedGame()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await RunUi(ui, async () =>
        {
            await using var test = await Fixture.Create(Device("first", true), Device("second", true));
            SelectGame(test, test.First);
            Assert.Contains("Choose a PSP destination for this game.", Texts(test.Window));
            Assert.Contains("/media/first", Texts(test.Window));
            await Activate(test.Window, PspButton(test.Window, "first"));
            SelectGame(test, test.Second);
            Assert.Null(test.Devices.GameDestination.SelectedDevice);
            await Activate(test.Window, PspButton(test.Window, "second"));
            SelectGame(test, test.First);
            Assert.Equal("first", test.Devices.GameDestination.SelectedDevice!.Value.DeviceId);
            Assert.Equal("first", Control<TextBlock>(test.Window, "SelectedPspNameText").Text);
            Assert.Equal("second", test.Session.Snapshot.Associations.PspDestinations[test.Second.Id].DeviceId);
            Assert.Equal(0, test.Repository.Writes);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task NoAndSinglePspStatesDoNotCreateAFalseChoice(int count)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await RunUi(ui, async () =>
        {
            await using var test = await Fixture.Create(count == 0 ? [] : [Device("only", true)]);
            SelectGame(test, test.First);
            Assert.Equal(count == 0 ? DestinationResolutionStatus.Unavailable : DestinationResolutionStatus.Selected,
                test.Devices.GameDestination.Status);
            Assert.DoesNotContain(Buttons(test.Window), button => button.Name == "ChoosePspButton" && button.IsEffectivelyVisible);
            if (count == 1)
            {
                Assert.Equal("only", Control<TextBlock>(test.Window, "SelectedPspNameText").Text);
                Assert.Contains("/media/only", Texts(test.Window));
                Assert.Contains("PSP destination available; not proof that a future copy will succeed.", Texts(test.Window));
            }
            else Assert.Contains("PSP destination unavailable.", Texts(test.Window));
            Assert.Empty(test.Session.Snapshot.Associations.PspDestinations);
        });
    }

    [Fact]
    public async Task DisconnectedChoiceShowsUnavailableAndPermitsExplicitReplacement()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await RunUi(ui, async () =>
        {
            await using var test = await Fixture.Create(Device("first", true), Device("second", true));
            SelectGame(test, test.First);
            await Activate(test.Window, PspButton(test.Window, "first"));
            await test.Publish(Device("second", true));
            Assert.Null(test.Devices.GameDestination.SelectedDevice);
            Assert.Contains("The selected PSP destination is unavailable. Choose another PSP explicitly.", Texts(test.Window));
            Assert.Equal("first", test.Session.Snapshot.Associations.PspDestinations[test.First.Id].DeviceId);
            var replacement = PspButton(test.Window, "second");
            Assert.True(replacement.IsEffectivelyVisible);
            await Activate(test.Window, replacement);
            Assert.Equal("second", Control<TextBlock>(test.Window, "SelectedPspNameText").Text);
        });
    }

    [Fact]
    public async Task PendingDeviceOperationsLeaveKeyboardCatalogAndCsvInteractionAvailable()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await RunUi(ui, async () =>
        {
            await using var test = await Fixture.Create(Device("usb"), Device("other"));
            var source = Buttons(test.Window).Single(button => button.Name == "ChooseSourceButton"
                && button.DataContext is SourceChoice choice && choice.Device.DeviceId == "usb");
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(source)));
            await Activate(test.Window, source);
            var removal = Buttons(test.Window).Single(button => button.Name == "RemoveDeviceButton"
                && button.DataContext is DeviceItemViewModel row && row.Key.DeviceId == "usb");
            await Activate(test.Window, removal);
            await Until(() => test.Devices.Devices.Any(row => row.Key.DeviceId == "usb" && row.State == DeviceState.Removing));
            var games = Control<ListBox>(test.Window, "GamesList");
            SelectGame(test, test.First);
            Assert.True(Assert.IsType<ListBoxItem>(games.ContainerFromIndex(0)).Focus());
            test.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            test.Window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); Pump();
            Assert.Equal(test.Second, test.Main.SelectedGame);
            await Activate(test.Window, Control<Button>(test.Window, "ExportCatalogButton"));
            Assert.True(test.Main.IsExportFormOpen);
            AssertInside(test.Window, games);
            AssertInside(test.Window, Control<DevicesView>(test.Window, "DevicePanel"));
            await Activate(test.Window, Control<Button>(test.Window, "CancelExportButton"));
            await Activate(test.Window, Control<Button>(test.Window, "ImportCatalogButton"));
            Assert.True(test.Main.IsImportFormOpen);
            AssertInside(test.Window, Control<Button>(test.Window, "ChooseImportFileButton"));
            AssertInside(test.Window, games);
            AssertInside(test.Window, Control<DevicesView>(test.Window, "DevicePanel"));
            Assert.Equal(0, test.Repository.Writes);
        });
    }

    [Fact]
    public async Task StalePspButtonsDisableAfterSwitchingGameAndTabDoesNotTrapFocus()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await RunUi(ui, async () =>
        {
            await using var test = await Fixture.Create(Device("first", true), Device("second", true));
            SelectGame(test, test.First);
            var old = PspButton(test.Window, "first");
            var oldAction = (IAsyncRelayCommand)old.Command!;
            old.BringIntoView(); Pump(); old.Focus();
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(old)));
            test.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); Pump();
            Assert.NotSame(old, test.Window.FocusManager!.GetFocusedElement());
            SelectGame(test, test.Second);
            // Item presenters may reuse the button for a new option. The old action, not a
            // recycled visual's current binding, must remain disabled and reject direct calls.
            Assert.False(oldAction.CanExecute(null));
            await oldAction.ExecuteAsync(null); Pump();
            Assert.Empty(test.Session.Snapshot.Associations.PspDestinations);
        });
    }

    [Fact]
    public async Task CatalogOnlyConsumersDoNotReserveADeviceColumn()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.Dispatch(() =>
        {
            var main = new MainViewModel(new GameCatalogService(new Repository()), null);
            var window = new MainWindow { DataContext = main, Width = 900, Height = 450 };
            window.Show(); Pump();
            try
            {
                Assert.False(Control<DevicesView>(window, "DevicePanel").IsEffectivelyVisible);
                var workspace = Control<Grid>(window, "CatalogWorkspace");
                Assert.Equal(0, workspace.ColumnDefinitions[3].ActualWidth);
                Assert.True(Control<TextBox>(window, "TitleInput").IsEffectivelyVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    // Explicit TResult selects the async overload; Dispatch(Func<Task>) otherwise returns Task<Task>.
    private static Task RunUi(HeadlessUnitTestSession ui, Func<Task> action) => ui.Dispatch<bool>(async () =>
    { await action(); return true; }, CancellationToken.None);
    private static T Control<T>(MainWindow window, string name) where T : Control
    { Pump(); return Assert.IsType<T>(window.FindControl<T>(name)); }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static Button[] Buttons(MainWindow window) { Pump(); return window.GetVisualDescendants().OfType<Button>().ToArray(); }
    private static string[] Texts(MainWindow window) { Pump(); return window.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? "").ToArray(); }
    private static Button PspButton(MainWindow window, string id) => Buttons(window).Single(button => button.Name == "ChoosePspButton"
        && AutomationProperties.GetName(button) == "Use PSP " + id);
    private static void SelectGame(Fixture test, GameRecord game) { Control<ListBox>(test.Window, "GamesList").SelectedItem = game; Pump(); }
    private static async Task Activate(MainWindow window, Button button)
    {
        button.BringIntoView(); Pump(); Assert.True(button.IsEnabled); Assert.True(button.Focus());
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        if (button.Command is IAsyncRelayCommand command && command.ExecutionTask is { } task)
            await task.WaitAsync(TimeSpan.FromSeconds(10));
        Pump();
    }
    private static void AssertInside(MainWindow window, Control control)
    {
        Pump(); Assert.True(control.IsEffectivelyVisible); Assert.True(control.Bounds.Width > 0); Assert.True(control.Bounds.Height > 0);
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.InRange(point.X, -1, window.ClientSize.Width);
        Assert.InRange(point.Y, -1, window.ClientSize.Height);
        Assert.True(point.X + control.Bounds.Width <= window.ClientSize.Width + 1);
        Assert.True(point.Y + control.Bounds.Height <= window.ClientSize.Height + 1);
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) { Pump(); if (predicate()) return; await Task.Delay(10, timeout.Token); }
    }
    private static DeviceSnapshot Device(string id, bool psp = false)
    {
        var key = new DeviceKey("test", id); var identity = new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly);
        return new(key, id, psp ? DeviceMedium.Psp : DeviceMedium.Usb,
            (psp ? DeviceCapabilities.PspDestination : DeviceCapabilities.RemovableSource) | DeviceCapabilities.SafeRemoval,
            identity, "media", DeviceState.Ready, [new(new(key, "volume"), "Storage", "/media/" + id, identity, DeviceState.Ready)]);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public Provider Provider { get; } = new();
        public GameRecord First { get; } = new(Guid.NewGuid(), "First game", GameMedium.Usb, null, "/first.iso", GameFormat.Iso, 1, null);
        public GameRecord Second { get; } = new(Guid.NewGuid(), "Second game", GameMedium.Usb, null, "/second.iso", GameFormat.Iso, 2, null);
        public Repository Repository { get; }
        public DeviceSessionService Session { get; }
        public DevicesViewModel Devices { get; }
        public MainViewModel Main { get; }
        public MainWindow Window { get; }
        private Fixture(DeviceSnapshot[] devices)
        {
            Provider.Inventory = new(1, devices.ToImmutableArray());
            Session = new([Provider]); Devices = new(Session); Repository = new(First, Second);
            Main = new(new GameCatalogService(Repository), null, new FolderPicker(), new ExportWriter(), "/catalog/catalog.db",
                new ImportPicker(), new GameCatalogImportService(Repository, new ImportSource()), Devices);
            Window = new MainWindow { DataContext = Main, Width = 900, Height = 450 };
            Window.Show(); Pump();
        }
        public static async Task<Fixture> Create(params DeviceSnapshot[] devices)
        { var test = new Fixture(devices); await test.Session.StartAsync(); Pump(); return test; }
        public async Task Publish(params DeviceSnapshot[] devices)
        {
            Provider.Inventory = new(Provider.Inventory.Revision + 1, devices.ToImmutableArray());
            Provider.Changes.Writer.TryWrite(Provider.Inventory);
            await Until(() => Session.Snapshot.ProviderRevisions.GetValueOrDefault("test") == Provider.Inventory.Revision);
            // Core publishes its snapshot before the UI subscriber has projected it.
            await Until(() => Devices.Devices.Select(row => row.Key).SequenceEqual(devices.Select(device => device.Key)));
        }
        public async ValueTask DisposeAsync() { Window.Close(); Devices.Dispose(); await Session.DisposeAsync(); }
    }
    private sealed class Repository(params GameRecord[] games) : IGameRecordRepository
    {
        public int Writes;
        public IReadOnlyList<GameRecord> List() => games;
        public GameRecord? GetById(Guid id) => games.FirstOrDefault(game => game.Id == id);
        public void Create(GameRecord record) { ++Writes; throw new InvalidOperationException(); }
        public void Update(GameRecord record) { ++Writes; throw new InvalidOperationException(); }
        public void Delete(Guid id) { ++Writes; throw new InvalidOperationException(); }
    }
    private sealed class FolderPicker : IGameExportFolderPicker { public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null); }
    private sealed class ImportPicker : IGameImportFilePicker { public Task<string?> PickFileAsync() => Task.FromResult<string?>(null); }
    private sealed class ImportSource : IGameCatalogImportSource { public IGameCatalogImportReader Open(string path) => throw new InvalidOperationException(); }
    private sealed class ExportWriter : IGameCatalogExportWriter
    {
        public ExportDestinationSnapshot Capture(string path) => throw new InvalidOperationException();
        public void Publish(string path, ReadOnlyMemory<byte> content, ExportDestinationSnapshot snapshot, bool overwriteApproved) => throw new InvalidOperationException();
    }
    private sealed class Provider : IDeviceProvider
    {
        public string ProviderId => "test";
        public DeviceInventorySnapshot Inventory = new(0, []);
        public readonly Channel<DeviceInventorySnapshot> Changes = Channel.CreateUnbounded<DeviceInventorySnapshot>();
        public readonly TaskCompletionSource<DeviceOperationResult> Removal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default) => new(Inventory);
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        { yield return Inventory; await foreach (var snapshot in Changes.Reader.ReadAllAsync(token)) yield return snapshot; }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public async ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default) => await Removal.Task.WaitAsync(token);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
