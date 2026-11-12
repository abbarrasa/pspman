using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;
using PSPMan.Infraestructure.Devices;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class DeviceMountingFlowTests
{
    [Theory]
    [InlineData(MountedPathSyntax.Linux, false)]
    [InlineData(MountedPathSyntax.Linux, true)]
    [InlineData(MountedPathSyntax.Windows, false)]
    [InlineData(MountedPathSyntax.Windows, true)]
    public async Task VisibleSelectionsKeepSourceGroupsVolumesAndPerGamePspChoicesIndependent(
        MountedPathSyntax syntax, bool missingLabels)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var card = Device("card", DeviceMedium.MemoryCard, syntax);
            card = card with { Volumes = [card.Volumes[0] with { Label = missingLabels ? null : "Games" },
                card.Volumes[0] with { Key = new(card.Key, "second"), Root = Root("card-two", syntax), Label = missingLabels ? " " : "Games" }] };
            await using var test = await Fixture.Create(syntax, Device("usb", DeviceMedium.Usb, syntax), card,
                Device("disc-one", DeviceMedium.Optical, syntax), Device("disc-two", DeviceMedium.Optical, syntax),
                Device("psp-one", DeviceMedium.Psp, syntax), Device("psp-two", DeviceMedium.Psp, syntax));

            Assert.Same(test.Devices, Control<DevicesView>(test.Window, "DevicePanel").DataContext);
            Assert.Equal(4, Control<ItemsControl>(test.Window, "SourceDevices").Items.Count);
            Assert.Equal(2, Control<ItemsControl>(test.Window, "PspDevices").Items.Count);
            Assert.Equal(new[] { "card", "usb" }, test.Devices.RemovableSource.Candidates.Select(choice => choice.Device.DeviceId).OrderBy(id => id));
            Assert.Equal(SourceResolutionStatus.ChooseDevice, test.Devices.OpticalSource.Status);
            Assert.Equal(SourceResolutionStatus.ChooseDevice, test.Devices.RemovableSource.Status);
            Assert.Null(test.Devices.RemovableSource.PreferredDevice);
            Assert.Null(test.Devices.OpticalSource.PreferredDevice);

            SelectGame(test, test.UsbGame);
            Assert.Null(test.Devices.ConstructedSourcePath);
            Assert.Contains("Choose a source device.", Texts(test.Window));
            Assert.Equal(DestinationResolutionStatus.ChooseDevice, test.Devices.GameDestination.Status);
            Assert.Contains("Choose a PSP destination for this game.", Texts(test.Window));
            Assert.Null(test.Devices.GameDestination.PreferredDevice);

            var cardButton = SourceButton(test.Window, "card");
            AssertOptionDetails(cardButton, "card", Root("card", syntax), Root("card-two", syntax));
            await Activate(test.Window, cardButton);
            await Until(() => test.Devices.RemovableSource.Status == SourceResolutionStatus.ChooseVolume);
            Assert.Equal(card.Key, test.Devices.RemovableSource.PreferredDevice);
            Assert.Null(test.Devices.RemovableSource.Root);
            Assert.Null(test.Devices.ConstructedSourcePath);
            Assert.Contains("Choose a source volume.", Texts(test.Window));
            Assert.Equal(2, test.Devices.RemovableSource.Volumes.Length);
            Assert.All(test.Devices.RemovableSource.Volumes,
                volume => Assert.Equal(missingLabels ? "Unlabelled volume" : "Games", volume.Label));
            AssertOptionDetails(VolumeButton(test.Window, "volume"), missingLabels ? "Unlabelled volume" : "Games", Root("card", syntax), "volume");
            var secondVolume = VolumeButton(test.Window, "second");
            AssertOptionDetails(secondVolume, missingLabels ? "Unlabelled volume" : "Games", Root("card-two", syntax), "second");
            await Activate(test.Window, secondVolume);
            await Until(() => test.Devices.RemovableSource.SelectedVolume?.VolumeId == "second");
            AssertPath(test, "card-two", test.UsbGame);
            Assert.Contains("Selected volume", Texts(test.Window));

            var firstPsp = PspButton(test.Window, "psp-one");
            AssertOptionDetails(firstPsp, "psp-one", Root("psp-one", syntax));
            await Activate(test.Window, firstPsp);
            await Until(() => test.Devices.GameDestination.PreferredDevice?.DeviceId == "psp-one");
            AssertPsp(test, "psp-one");

            var firstDisc = SourceButton(test.Window, "disc-one");
            AssertOptionDetails(firstDisc, "disc-one", Root("disc-one", syntax));
            await Activate(test.Window, firstDisc);
            await Until(() => test.Devices.OpticalSource.PreferredDevice?.DeviceId == "disc-one");
            AssertPath(test, "card-two", test.UsbGame);
            AssertPsp(test, "psp-one");

            SelectGame(test, test.CardGame);
            AssertPath(test, "card-two", test.CardGame);
            Assert.Equal(DestinationResolutionStatus.ChooseDevice, test.Devices.GameDestination.Status);
            Assert.Null(test.Devices.GameDestination.PreferredDevice);
            var secondPsp = PspButton(test.Window, "psp-two");
            AssertOptionDetails(secondPsp, "psp-two", Root("psp-two", syntax));
            await Activate(test.Window, secondPsp);
            await Until(() => test.Devices.GameDestination.PreferredDevice?.DeviceId == "psp-two");
            AssertPsp(test, "psp-two");
            SelectGame(test, test.UsbGame);
            AssertPath(test, "card-two", test.UsbGame);
            AssertPsp(test, "psp-one");

            // One shared USB/card preference replaces its own volume association only.
            var usb = SourceButton(test.Window, "usb");
            AssertOptionDetails(usb, "usb", Root("usb", syntax));
            await Activate(test.Window, usb);
            await Until(() => test.Devices.RemovableSource.PreferredDevice?.DeviceId == "usb");
            Assert.Equal("volume", test.Devices.RemovableSource.SelectedVolume!.Value.VolumeId);
            AssertPath(test, "usb", test.UsbGame);
            Assert.Equal("disc-one", test.Devices.OpticalSource.PreferredDevice!.Value.DeviceId);
            AssertPsp(test, "psp-one");
            SelectGame(test, test.CardGame);
            AssertPath(test, "usb", test.CardGame);
            AssertPsp(test, "psp-two");

            SelectGame(test, test.DvdGame);
            AssertPath(test, "disc-one", test.DvdGame);
            Assert.Equal(2, test.Devices.OpticalSource.Candidates.Length);
            var secondDisc = SourceButton(test.Window, "disc-two");
            AssertOptionDetails(secondDisc, "disc-two", Root("disc-two", syntax));
            await Activate(test.Window, secondDisc);
            await Until(() => test.Devices.OpticalSource.PreferredDevice?.DeviceId == "disc-two");
            AssertPath(test, "disc-two", test.DvdGame);
            Assert.Equal("usb", test.Devices.RemovableSource.PreferredDevice!.Value.DeviceId);
            SelectGame(test, test.OtherDvdGame);
            Assert.NotEqual(test.DvdGame.DvdNumber, test.OtherDvdGame.DvdNumber);
            Assert.Equal(2, test.Devices.OpticalSource.Candidates.Length);
            AssertPath(test, "disc-two", test.OtherDvdGame);

            SelectGame(test, test.UsbGame); AssertPath(test, "usb", test.UsbGame); AssertPsp(test, "psp-one");
            SelectGame(test, test.CardGame); AssertPath(test, "usb", test.CardGame); AssertPsp(test, "psp-two");
            Assert.Equal("disc-two", test.Devices.OpticalSource.PreferredDevice!.Value.DeviceId);
            test.AssertReadOnly();
        });
    }

    [Theory]
    [InlineData(MountedPathSyntax.Linux, false)]
    [InlineData(MountedPathSyntax.Linux, true)]
    [InlineData(MountedPathSyntax.Windows, false)]
    [InlineData(MountedPathSyntax.Windows, true)]
    public async Task EmptyOrUniqueSourcesAndDestinationsDoNotInventAChoice(MountedPathSyntax syntax, bool connected)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var test = await Fixture.Create(syntax, connected
                ? [Device("usb", DeviceMedium.Usb, syntax), Device("disc-one", DeviceMedium.Optical, syntax), Device("psp-one", DeviceMedium.Psp, syntax)] : []);
            SelectGame(test, test.UsbGame);
            Assert.Null(test.Devices.RemovableSource.PreferredDevice);
            Assert.Null(test.Devices.OpticalSource.PreferredDevice);
            Assert.Null(test.Devices.GameDestination.PreferredDevice);
            Assert.DoesNotContain(Buttons(test.Window), button => button.Name == "ChoosePspButton" && button.IsEffectivelyVisible);
            if (connected)
            {
                Assert.Equal(SourceResolutionStatus.Selected, test.Devices.RemovableSource.Status);
                Assert.Equal(DestinationResolutionStatus.Selected, test.Devices.GameDestination.Status);
                AssertPath(test, "usb", test.UsbGame); AssertPsp(test, "psp-one");
                Assert.Contains("PSP destination available; not proof that a future copy will succeed.", Texts(test.Window));
                SelectGame(test, test.CardGame); AssertPath(test, "usb", test.CardGame);
                SelectGame(test, test.DvdGame); AssertPath(test, "disc-one", test.DvdGame);
            }
            else
            {
                Assert.Equal(SourceResolutionStatus.Unavailable, test.Devices.RemovableSource.Status);
                Assert.Equal(SourceResolutionStatus.Unavailable, test.Devices.OpticalSource.Status);
                Assert.Equal(DestinationResolutionStatus.Unavailable, test.Devices.GameDestination.Status);
                Assert.Null(test.Devices.ConstructedSourcePath);
                Assert.Contains("No devices detected.", Texts(test.Window));
                Assert.Contains("Source unavailable.", Texts(test.Window));
                Assert.Contains("PSP destination unavailable.", Texts(test.Window));
                Assert.DoesNotContain(Buttons(test.Window), button => button.Name is "ChooseSourceButton" or "ChooseVolumeButton");
            }
            test.AssertReadOnly();
        });
    }

    private static string Root(string id, MountedPathSyntax syntax) => syntax == MountedPathSyntax.Linux ? "/virtual/" + id : id switch
    {
        "usb" => @"E:\", "card" => @"F:\", "card-two" => @"G:\", "disc-one" => @"H:\",
        "disc-two" => @"I:\", "psp-one" => @"J:\", "psp-two" => @"K:\", _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    private static DeviceSnapshot Device(string id, DeviceMedium medium, MountedPathSyntax syntax)
    {
        var key = new DeviceKey("flow", id);
        var identity = new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly);
        var role = medium switch
        {
            DeviceMedium.Psp => DeviceCapabilities.PspDestination,
            DeviceMedium.Optical => DeviceCapabilities.OpticalSource,
            _ => DeviceCapabilities.RemovableSource
        };
        return new(key, id, medium, role | DeviceCapabilities.Mount | DeviceCapabilities.SafeRemoval,
            identity, "media", DeviceState.Ready, [new(new(key, "volume"), "Games", Root(id, syntax), identity, DeviceState.Ready)]);
    }

    private static void SelectGame(Fixture test, GameRecord game)
    {
        Control<ListBox>(test.Window, "GamesList").SelectedItem = game; Pump();
        Assert.Same(game, test.Main.SelectedGame);
        Assert.Equal(game.Id, test.Devices.GameDestination.GameId);
        Assert.Equal(game.Title, Control<TextBlock>(test.Window, "SelectedTitleText").Text);
    }

    private static void AssertPath(Fixture test, string device, GameRecord game)
    {
        var root = Root(device, test.Syntax);
        var expected = test.Syntax == MountedPathSyntax.Windows ? root + game.Location[1..].Replace('/', '\\') : root + game.Location;
        Assert.Null(test.Devices.SourcePathError);
        Assert.Equal(expected, test.Devices.ConstructedSourcePath);
        Assert.Contains(expected, Texts(test.Window));
        Assert.Contains("Constructed path only; not proof that the game file exists or can be accessed.", Texts(test.Window));
    }

    private static void AssertPsp(Fixture test, string id)
    {
        Assert.Equal(id, test.Devices.GameDestination.SelectedDevice!.Value.DeviceId);
        Assert.Equal(id, Control<TextBlock>(test.Window, "SelectedPspNameText").Text);
        Assert.Equal(Root(id, test.Syntax), Assert.Single(test.Devices.GameDestination.Volumes).Root);
    }

    private static void AssertOptionDetails(Button button, params string[] expected)
    {
        var parent = Assert.IsType<StackPanel>(button.GetVisualParent());
        var texts = parent.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
        foreach (var text in expected) Assert.Contains(text, texts);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
    }

    private static T Control<T>(MainWindow window, string name) where T : Control
    {
        Pump();
        // DevicesView owns a separate name scope; its controls are reached through the visual tree.
        return Assert.IsType<T>(window.FindControl<T>(name)
            ?? window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name));
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static string[] Texts(MainWindow window)
    { Pump(); return window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? "").ToArray(); }
    private static Button[] Buttons(MainWindow window)
    { Pump(); return window.GetVisualDescendants().OfType<Button>().ToArray(); }
    private static Button SourceButton(MainWindow window, string id) => Buttons(window).Single(button => button.Name == "ChooseSourceButton"
        && button.DataContext is SourceChoice choice && choice.Device.DeviceId == id);
    private static Button VolumeButton(MainWindow window, string id) => Buttons(window).Single(button => button.Name == "ChooseVolumeButton"
        && button.DataContext is SourceVolumeChoice choice && choice.Volume.VolumeId == id);
    private static Button PspButton(MainWindow window, string id) => Buttons(window).Single(button => button.Name == "ChoosePspButton"
        && button.DataContext is PspDestinationOptionViewModel option && option.Choice.Device.DeviceId == id);

    private static async Task Activate(MainWindow window, Button button)
    {
        button.BringIntoView(); Pump(); Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEnabled); Assert.True(button.Focus());
        var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command);
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.NotNull(command.ExecutionTask);
        await command.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(10));
        Pump();
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) { Pump(); if (condition()) return; await Task.Delay(10, timeout.Token); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public MountedPathSyntax Syntax { get; }
        public GameRecord UsbGame { get; } = Game("USB game", GameMedium.Usb, "/GAMES/Usb.rar");
        public GameRecord CardGame { get; } = Game("Card game", GameMedium.MemoryCard, "/GAMES/Card.rar");
        public GameRecord DvdGame { get; } = Game("Disc game", GameMedium.Dvd, "/GAMES/Disc.rar", 42);
        public GameRecord OtherDvdGame { get; } = Game("Other disc number", GameMedium.Dvd, "/GAMES/Disc.rar", 99);
        public MainViewModel Main { get; }
        public MainWindow Window { get; }
        public DevicesViewModel Devices => Main.Devices!;
        private readonly Repository repository;
        private readonly Provider provider;
        private readonly ApplicationDeviceLifetime lifetime;
        private readonly GameRecord[] original;
        private int? exitCode;

        private Fixture(MountedPathSyntax syntax, DeviceSnapshot[] devices)
        {
            Syntax = syntax;
            original = [UsbGame, CardGame, DvdGame, OtherDvdGame];
            repository = new(original);
            Main = new(new GameCatalogService(repository), null, mountedPathSyntax: syntax);
            Window = new MainWindow { DataContext = Main, Width = 900, Height = 450 };
            provider = new(devices);
            lifetime = new(Window, Main, code => { exitCode = code; Window.Close(); }, _ =>
                ValueTask.FromResult(new DeviceProviderFactoryResult([provider])));
        }

        public static async Task<Fixture> Create(MountedPathSyntax syntax, params DeviceSnapshot[] devices)
        {
            var test = new Fixture(syntax, devices);
            try
            {
                test.Window.Show(); await test.lifetime.Initialization.WaitAsync(TimeSpan.FromSeconds(10));
                await Until(() => test.Devices.Devices.Length == devices.Length && !test.Devices.IsDetecting);
                return test;
            }
            catch { await test.DisposeAsync(); throw; }
        }

        public void AssertReadOnly()
        {
            Assert.Equal(original, Main.Games);
            Assert.Equal(original, repository.List());
            Assert.Equal(0, repository.Writes);
            Assert.Equal(0, provider.Mounts);
            Assert.Equal(0, provider.Removals);
        }

        public async ValueTask DisposeAsync()
        {
            Window.Close(); await lifetime.ShutdownCompletion.WaitAsync(TimeSpan.FromSeconds(10));
            await lifetime.DisposeAsync();
            Assert.Equal(0, exitCode);
            Assert.Equal(1, provider.Watches);
            Assert.Equal(1, provider.Unsubscriptions);
            Assert.Equal(1, provider.Disposals);
            AssertReadOnly();
        }

        private static GameRecord Game(string title, GameMedium medium, string location, int? disc = null) =>
            new(Guid.NewGuid(), title, medium, disc, location, GameFormat.Iso, 1024, null);
    }

    private sealed class Repository(params GameRecord[] records) : IGameRecordRepository
    {
        public int Writes;
        public IReadOnlyList<GameRecord> List() => records.ToArray();
        public GameRecord? GetById(Guid id) => records.SingleOrDefault(record => record.Id == id);
        public void Create(GameRecord record) { Writes++; throw new InvalidOperationException("Device selection must not create catalog records."); }
        public void Update(GameRecord record) { Writes++; throw new InvalidOperationException("Device selection must not change catalog records."); }
        public void Delete(Guid id) { Writes++; throw new InvalidOperationException("Device selection must not delete catalog records."); }
    }

    // Inventory-only fake: roots describe observations, never real files or native devices.
    private sealed class Provider(params DeviceSnapshot[] devices) : IDeviceProvider
    {
        public string ProviderId => "flow";
        private readonly DeviceInventorySnapshot inventory = new(1, devices.ToImmutableArray());
        public int Mounts, Removals, Watches, Unsubscriptions, Disposals;
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(inventory); }
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            Interlocked.Increment(ref Watches);
            try { yield return inventory; await Task.Delay(Timeout.Infinite, token); }
            finally { Interlocked.Increment(ref Unsubscriptions); }
        }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default)
        { Interlocked.Increment(ref Mounts); throw new InvalidOperationException("Ready volumes must not be mounted again."); }
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default)
        { Interlocked.Increment(ref Removals); throw new InvalidOperationException("This selection flow must not remove devices."); }
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
    }
}
