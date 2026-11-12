using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using PSPMan.Core.Devices;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public sealed partial class DeviceMountingFlowTests
{
    [Theory]
    [InlineData(MountedPathSyntax.Linux, DeviceOperationStatus.AuthorizationDenied)]
    [InlineData(MountedPathSyntax.Windows, DeviceOperationStatus.Cancelled)]
    [InlineData(MountedPathSyntax.Linux, DeviceOperationStatus.Cancelled)]
    [InlineData(MountedPathSyntax.Windows, DeviceOperationStatus.AuthorizationDenied)]
    public async Task PendingAuthorizationKeepsKeyboardBrowsingAndFailureRequiresExplicitRetry(
        MountedPathSyntax syntax, DeviceOperationStatus status)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var source = Unmounted(Device("usb", DeviceMedium.Usb, syntax));
            await using var test = await Fixture.CreateControlled(syntax, [source]);
            var pending = await Next(test.Operations.MountRequests);
            await Until(() => test.Devices.Devices[0].State == DeviceState.Mounting);
            BrowseWithKeyboard(test);
            Assert.Null(test.Devices.ConstructedSourcePath);
            pending.SetResult(new(status, new("Authorization", "Authorization not granted", source.Volumes[0].Key)));
            await Until(() => test.Devices.Devices[0].Volumes[0].CanRetryMount);
            Assert.Contains("Authorization not granted", string.Join(" ", Texts(test.Window)));
            await test.Publish(source);
            Assert.Equal(1, test.Operations.Mounts);
            var retry = Buttons(test.Window).Single(button => button.Name == "RetryMountButton" && button.IsEnabled);
            await Activate(test.Window, retry);
            var second = await Next(test.Operations.MountRequests);
            Assert.Equal(2, test.Operations.Mounts);
            test.Operations.SetInventory([Device("usb", DeviceMedium.Usb, syntax)]);
            second.SetResult(new(DeviceOperationStatus.Succeeded));
            await Until(() => test.Devices.Devices[0].State == DeviceState.Ready);
            SelectGame(test, test.UsbGame); AssertPath(test, "usb", test.UsbGame);
            Assert.Equal(0, test.Operations.Removals);
        });
    }

    [Theory]
    [InlineData(MountedPathSyntax.Linux)]
    [InlineData(MountedPathSyntax.Windows)]
    public async Task BusyRemovalKeepsBrowsingShowsErrorAndAllowsExplicitRetry(MountedPathSyntax syntax)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var source = Device("usb", DeviceMedium.Usb, syntax);
            await using var test = await Fixture.CreateControlled(syntax, [source]);
            await Activate(test.Window, RemoveButton(test, "usb"));
            var pending = await Next(test.Operations.RemovalRequests);
            await Until(() => test.Devices.Devices[0].State == DeviceState.Removing);
            BrowseWithKeyboard(test);
            Assert.Null(test.Devices.ConstructedSourcePath);
            pending.SetResult(new(DeviceOperationStatus.Failed, new("Busy", "Device still in use")));
            await Until(() => test.Devices.Devices[0].CanRemove);
            Assert.Contains("Device still in use", string.Join(" ", Texts(test.Window)));
            AssertPath(test, "usb", test.CardGame);
            await test.Publish(source);
            Assert.Equal(1, test.Operations.Removals);
            await Activate(test.Window, RemoveButton(test, "usb"));
            var retry = await Next(test.Operations.RemovalRequests);
            test.Operations.SetInventory([Unmounted(source)]);
            retry.SetResult(new(DeviceOperationStatus.Succeeded));
            await Until(() => test.Devices.Devices[0].State == DeviceState.Unmounted);
            await test.Publish(Unmounted(source));
            Assert.Equal(0, test.Operations.Mounts);
            Assert.Equal(2, test.Operations.Removals);
            Assert.Null(test.Devices.ConstructedSourcePath);
            await test.Publish();
            await test.Publish(Unmounted(source));
            var remount = await Next(test.Operations.MountRequests);
            test.Operations.SetInventory([source]);
            remount.SetResult(new(DeviceOperationStatus.Succeeded));
            await Until(() => test.Devices.ConstructedSourcePath is not null);
            AssertPath(test, "usb", test.CardGame);
            Assert.Equal(1, test.Operations.Mounts);
        });
    }

    [Theory]
    [InlineData(MountedPathSyntax.Linux)]
    [InlineData(MountedPathSyntax.Windows)]
    public async Task ReliableReconnectUsesFreshRootsWithoutFallbackAndRestartClearsChoices(MountedPathSyntax syntax)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var source = Reliable(Device("usb", DeviceMedium.Usb, syntax));
            var psp = Reliable(Device("psp-one", DeviceMedium.Psp, syntax));
            var alternate = Device("card", DeviceMedium.MemoryCard, syntax);
            var otherPsp = Device("psp-two", DeviceMedium.Psp, syntax);
            var freshSource = Reconnect(source, Root("card-two", syntax));
            var freshPsp = Reconnect(psp, Root("disc-two", syntax));
            PSPMan.Core.Games.GameRecord[] records;
            await using (var test = await Fixture.CreateControlled(syntax, [source, alternate, psp, otherPsp]))
            {
                records = test.Records;
                SelectGame(test, test.UsbGame);
                await Activate(test.Window, SourceButton(test.Window, "usb"));
                await Activate(test.Window, PspButton(test.Window, "psp-one"));
                await Until(() => test.Devices.GameDestination.PreferredDevice == psp.Key);
                AssertPath(test, "usb", test.UsbGame); AssertPsp(test, "psp-one");
                await test.Publish(alternate, otherPsp);
                await Until(() => test.Devices.GameDestination.Status == DestinationResolutionStatus.Unavailable);
                Assert.Equal(SourceResolutionStatus.Unavailable, test.Devices.RemovableSource.Status);
                Assert.Null(test.Devices.ConstructedSourcePath);
                Assert.Null(test.Devices.GameDestination.SelectedDevice);
                Assert.Empty(test.Devices.GameDestination.Volumes);
                Assert.Equal(source.Key, test.Devices.RemovableSource.PreferredDevice);
                Assert.Equal(psp.Key, test.Devices.GameDestination.PreferredDevice);
                Assert.Contains("Source unavailable.", Texts(test.Window));
                Assert.Contains("The selected PSP destination is unavailable. Choose another PSP explicitly.", Texts(test.Window));
                await test.Publish(alternate, otherPsp, freshSource, freshPsp);
                await Until(() => test.Devices.GameDestination.SelectedDevice == freshPsp.Key
                    && test.Devices.RemovableSource.SelectedDevice == freshSource.Key);
                Assert.Equal(freshPsp.Volumes[0].Root, Assert.Single(test.Devices.GameDestination.Volumes).Root);
                var path = syntax == MountedPathSyntax.Windows
                    ? freshSource.Volumes[0].Root + test.UsbGame.Location[1..].Replace('/', '\\')
                    : freshSource.Volumes[0].Root + test.UsbGame.Location;
                Assert.Equal(path, test.Devices.ConstructedSourcePath);
                Assert.Contains(path!, Texts(test.Window));
                Assert.DoesNotContain(Root("usb", syntax), Texts(test.Window));
                Assert.DoesNotContain(Root("psp-one", syntax), Texts(test.Window));
                await Activate(test.Window, RemoveButton(test, freshSource.Key.DeviceId));
                var removal = await Next(test.Operations.RemovalRequests);
                test.Operations.SetInventory([Unmounted(freshSource), alternate, freshPsp, otherPsp]);
                removal.SetResult(new(DeviceOperationStatus.Succeeded));
                await Until(() => test.Devices.Devices.Single(row => row.Key == freshSource.Key).State == DeviceState.Unmounted);
                Assert.Equal(0, test.Operations.Mounts);
            }
            await using var restarted = await Fixture.CreateControlled(syntax, [Unmounted(freshSource), alternate, freshPsp, otherPsp], records);
            var mount = await Next(restarted.Operations.MountRequests);
            Assert.Equal(records.Select(game => game.Id), restarted.Main.Games.Select(game => game.Id));
            SelectGame(restarted, restarted.UsbGame);
            Assert.Null(restarted.Devices.RemovableSource.PreferredDevice);
            Assert.Null(restarted.Devices.GameDestination.PreferredDevice);
            Assert.Equal(DestinationResolutionStatus.ChooseDevice, restarted.Devices.GameDestination.Status);
            restarted.Operations.SetInventory([freshSource, alternate, freshPsp, otherPsp]);
            mount.SetResult(new(DeviceOperationStatus.Succeeded));
            await Until(() => restarted.Devices.Devices.Single(row => row.Key == freshSource.Key).State == DeviceState.Ready);
            Assert.Equal(SourceResolutionStatus.ChooseDevice, restarted.Devices.RemovableSource.Status);
            Assert.Equal(1, restarted.Operations.Mounts);
        });
    }

    private static void BrowseWithKeyboard(Fixture test)
    {
        SelectGame(test, test.UsbGame);
        var list = Control<ListBox>(test.Window, "GamesList");
        Assert.True(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0)).Focus());
        test.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        test.Window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Pump(); Assert.Same(test.CardGame, test.Main.SelectedGame);
        Assert.Equal(test.CardGame.Title, Control<TextBlock>(test.Window, "SelectedTitleText").Text);
    }
    private static Button RemoveButton(Fixture test, string id) => Buttons(test.Window).Single(button =>
        button.Name == "RemoveDeviceButton" && button.DataContext is DeviceItemViewModel row && row.Key.DeviceId == id);
    private static async Task<TaskCompletionSource<DeviceOperationResult>> Next(
        System.Threading.Channels.Channel<TaskCompletionSource<DeviceOperationResult>> requests) =>
        await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    private static DeviceSnapshot Unmounted(DeviceSnapshot device) => device with
    { State = DeviceState.Unmounted, Volumes = [device.Volumes[0] with { State = DeviceState.Unmounted, Root = null }] };
    private static DeviceSnapshot Reliable(DeviceSnapshot device) => device with
    { Identity = new(device.Key.DeviceId, DeviceIdentityConfidence.Persistent),
        Volumes = [device.Volumes[0] with { Identity = new(device.Key.DeviceId + "-volume", DeviceIdentityConfidence.Persistent) }] };
    private static DeviceSnapshot Reconnect(DeviceSnapshot device, string root)
    {
        var key = new DeviceKey("flow", device.Key.DeviceId + "-reconnected");
        return device with { Key = key, Volumes = [device.Volumes[0] with { Key = new(key, "fresh-volume"), Root = root }] };
    }
}
