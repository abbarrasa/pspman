using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
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

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class DevicesViewTests
{
    [Fact]
    public async Task EmptyAndServiceFailureFeedbackIsVisibleInEnglish()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.Dispatch(() =>
        {
            using var model = new DevicesViewModel();
            var window = Open(model);
            try
            {
                Assert.Contains("No devices detected.", Texts(window));
                model.Update(Snapshot(1) with { ProviderErrors = ImmutableDictionary<string, DeviceOperationError>.Empty
                    .Add("test", new("ServiceUnavailable", "Native service unavailable")) });
                Pump();
                Assert.Contains("ServiceUnavailable: Native service unavailable", Texts(window));
                Assert.Contains("Device services are unavailable.", Texts(window));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TypedRowsShowGroupsRootsIdentifiersAndMissingOrDuplicateLabels()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.Dispatch(() =>
        {
            using var model = new DevicesViewModel();
            var usb = Device("reader") with { Medium = DeviceMedium.Unknown };
            usb = usb with { Volumes = [usb.Volumes[0], usb.Volumes[0] with { Key = new(usb.Key, "second"), Root = "/media/second" },
                usb.Volumes[0] with { Key = new(usb.Key, "third"), Label = null, Root = "/media/third" }] };
            var psp = Device("psp", DeviceMedium.Psp);
            model.Update(Snapshot(1, usb, psp));
            var window = Open(model);
            try
            {
                var text = Texts(window);
                foreach (var expected in new[] { "Game sources", "PSP destinations", "Memory Card", "reader", "psp",
                    "Games", "Unlabelled volume", "/media/reader", "/media/second", "/media/third", "second", "third" })
                    Assert.Contains(expected, text);
                var sourceRows = window.GetVisualDescendants().OfType<ItemsControl>().Single(control => control.Name == "SourceDevices");
                Assert.Single(sourceRows.Items);
                Assert.Single(window.GetVisualDescendants().OfType<ItemsControl>().Single(control => control.Name == "PspDevices").Items);
                Assert.Equal("Safely remove", RemovalButton(window, "reader").Content);
                Assert.False(SourceButton(window, "reader").IsEnabled);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(DeviceState.Detecting, "Detecting")]
    [InlineData(DeviceState.Unmounted, "Not mounted")]
    [InlineData(DeviceState.Mounting, "Mounting")]
    [InlineData(DeviceState.Ready, "Ready")]
    [InlineData(DeviceState.Removing, "Removing")]
    [InlineData(DeviceState.Unavailable, "Unavailable")]
    [InlineData(DeviceState.Error, "Error")]
    public async Task StatesAreRenderedAsTextWithoutInventingUsableRoots(DeviceState state, string text)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.Dispatch(() =>
        {
            using var model = new DevicesViewModel();
            model.Update(Snapshot(1, Device("one") with { State = state }));
            var window = Open(model);
            try
            {
                Assert.Contains(text, Texts(window));
                if (state is DeviceState.Detecting or DeviceState.Removing or DeviceState.Unavailable or DeviceState.Unmounted)
                    Assert.DoesNotContain("/media/one", Texts(window));
                Assert.False(RemovalButton(window, "one").IsEnabled);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisibleSourceAndVolumeActionsPreserveIndependentGroups(bool missingLabels)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var card = Device("card", DeviceMedium.MemoryCard);
            card = card with { Volumes = [card.Volumes[0] with { Label = missingLabels ? null : "Games" },
                card.Volumes[0] with { Key = new(card.Key, "second"), Label = missingLabels ? null : "Games", Root = "/media/second" }] };
            await using var test = await Fixture.Create(Device("usb"), card, Device("dvd", DeviceMedium.Optical));
            Assert.Contains("Choose a source device.", Texts(test.Window));
            await Click(test.Window, SourceButton(test.Window, "card"));
            Assert.Equal(SourceResolutionStatus.ChooseVolume, test.Model.RemovableSource.Status);
            Assert.Contains("Source volume", Texts(test.Window));
            await Click(test.Window, VolumeButton(test.Window, "second"));
            await Click(test.Window, SourceButton(test.Window, "dvd"));
            Assert.Equal(card.Key, test.Session.Snapshot.Associations.RemovableSource);
            Assert.Equal("dvd", test.Session.Snapshot.Associations.OpticalSource!.Value.DeviceId);
            Assert.Equal("second", test.Session.Snapshot.Associations.RemovableVolume!.Value.VolumeId);
            Assert.Contains("/media/second", Texts(test.Window));
            Assert.Contains("Selected source", Texts(test.Window));
            Assert.Contains("Selected volume", Texts(test.Window));
            if (missingLabels) Assert.Contains("Unlabelled volume", Texts(test.Window));
            Assert.Equal("Eject", RemovalButton(test.Window, "dvd").Content);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DisconnectedExplicitSourceShowsUnavailableInsteadOfSwitchingToAnother()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var test = await Fixture.Create(Device("one"), Device("two"));
            await Click(test.Window, SourceButton(test.Window, "one"));
            test.Model.SetGameSource(GameMedium.Usb, "/GAMES/file.iso", MountedPathSyntax.Linux); Pump();
            Assert.Contains("/media/one/GAMES/file.iso", Texts(test.Window));
            await test.Publish(Device("two"));
            Assert.Contains("The selected source is unavailable. Choose another source explicitly.", Texts(test.Window));
            Assert.DoesNotContain("/media/one/GAMES/file.iso", Texts(test.Window));
            Assert.Equal("one", test.Session.Snapshot.Associations.RemovableSource!.Value.DeviceId);
            Assert.Null(test.Model.ConstructedSourcePath);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RemovalDisablesOnlyItsDeviceAndNativeVetoAllowsExplicitRetry()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var test = await Fixture.Create(Device("one"), Device("two"));
            await Click(test.Window, RemovalButton(test.Window, "one"));
            await Until(() => test.Provider.Removals == 1);
            Assert.Contains("Removing", Texts(test.Window));
            Assert.False(RemovalButton(test.Window, "one").IsEnabled);
            Assert.True(RemovalButton(test.Window, "two").IsEnabled);
            Assert.True(SourceButton(test.Window, "two").IsEnabled);
            test.Provider.Removal.TrySetResult(new(DeviceOperationStatus.Failed, new("Busy", "Native veto: application in use")));
            await Until(() => Texts(test.Window).Contains("Busy: Native veto: application in use"));
            Assert.True(RemovalButton(test.Window, "one").IsEnabled);
            await Click(test.Window, RemovalButton(test.Window, "one"));
            await Until(() => test.Provider.Removals == 2);
            Assert.Equal("one", test.Provider.LastRemoval!.Device.DeviceId);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedMountHasVisibleNativeErrorAndAnEnabledRetryAction()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var device = Device("one");
            device = device with { Capabilities = device.Capabilities | DeviceCapabilities.Mount, State = DeviceState.Unmounted,
                Volumes = [device.Volumes[0] with { State = DeviceState.Unmounted, Root = null }] };
            await using var test = await Fixture.Create(device);
            await Until(() => Buttons(test.Window).Any(button => button.Name == "RetryMountButton" && button.IsEnabled));
            Assert.Contains("Denied: Native authorization denied", Texts(test.Window));
            Assert.DoesNotContain("/media/one", Texts(test.Window));
            var button = Buttons(test.Window).Single(button => button.Name == "RetryMountButton");
            Assert.Equal(((DeviceVolumeViewModel)button.DataContext!).CanRetryMount, button.IsEnabled);
            await Click(test.Window, button);
            await Until(() => test.Provider.Mounts == 2);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task UnclassifiedDeviceErrorsRemainVisibleOutsideTheSourceAndPspGroups()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.Dispatch(() =>
        {
            using var model = new DevicesViewModel();
            model.Update(Snapshot(1, Device("unknown") with { Capabilities = DeviceCapabilities.None,
                State = DeviceState.Error, Error = new("RecognitionFailed", "USB descriptors are inconclusive") }));
            var window = Open(model);
            try
            {
                Assert.Contains("Other devices", Texts(window));
                Assert.Contains("RecognitionFailed: USB descriptors are inconclusive", Texts(window));
                Assert.Single(model.OtherDevices);
                Assert.Empty(model.Sources);
                Assert.Empty(model.PspDestinations);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OldAndDisposedOptionsDisableTheirBoundActionsAndCannotApplySelections()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var test = await Fixture.Create(Device("one"), Device("two"));
            var oldButton = SourceButton(test.Window, "one");
            var old = (SourceChoice)oldButton.DataContext!;
            var oldCommand = oldButton.Command;
            var one = Device("one");
            await test.Publish(one with { Volumes = [one.Volumes[0] with { Root = "/media/new" }] }, Device("two"));
            Assert.False(old.CanSelect);
            Assert.False(old.SelectCommand.CanExecute(null));
            // The presenter may reuse this button for a fresh choice; validate the retired action.
            Assert.Same(old.SelectCommand, oldCommand);
            Assert.False(oldCommand!.CanExecute(null));
            await old.SelectCommand.ExecuteAsync(null); Pump();
            Assert.Null(test.Session.Snapshot.Associations.RemovableSource);
            var currentButton = SourceButton(test.Window, "one");
            Assert.True(currentButton.IsEnabled);
            var current = (SourceChoice)currentButton.DataContext!;
            test.Model.Dispose(); Pump();
            Assert.False(current.CanSelect);
            Assert.False(currentButton.IsEnabled);
            Assert.False(RemovalButton(test.Window, "one").IsEnabled);
            await current.SelectCommand.ExecuteAsync(null);
            await test.Session.SetAssociationsAsync(new());
            Assert.Null(test.Session.Snapshot.Associations.RemovableSource);
        }, CancellationToken.None);
    }

    private static DeviceSnapshot Device(string id, DeviceMedium medium = DeviceMedium.Usb)
    {
        var key = new DeviceKey("test", id); var identity = new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly);
        var capability = medium == DeviceMedium.Optical ? DeviceCapabilities.OpticalSource
            : medium == DeviceMedium.Psp ? DeviceCapabilities.PspDestination : DeviceCapabilities.RemovableSource;
        return new(key, id, medium, capability | DeviceCapabilities.SafeRemoval, identity, "media", DeviceState.Ready,
            [new(new(key, "volume"), "Games", "/media/" + id, identity, DeviceState.Ready)]);
    }
    private static DeviceSessionSnapshot Snapshot(long revision, params DeviceSnapshot[] devices) => new(new(revision, devices.ToImmutableArray()),
        new(), ImmutableDictionary<string, long>.Empty, ImmutableDictionary<string, DeviceOperationError>.Empty);
    private static Window Open(DevicesViewModel model)
    {
        var window = new Window { Width = 900, Height = 800, Content = new DevicesView { DataContext = model } };
        window.Show(); Pump(); return window;
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static string[] Texts(Window window) { Pump(); return window.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? "").ToArray(); }
    private static Button[] Buttons(Window window) { Pump(); return window.GetVisualDescendants().OfType<Button>().ToArray(); }
    private static Button SourceButton(Window window, string id) => Buttons(window).Single(button => button.Name == "ChooseSourceButton"
        && button.DataContext is SourceChoice choice && choice.Device.DeviceId == id);
    private static Button VolumeButton(Window window, string id) => Buttons(window).Single(button => button.Name == "ChooseVolumeButton"
        && button.DataContext is SourceVolumeChoice choice && choice.Volume.VolumeId == id);
    private static Button RemovalButton(Window window, string id) => Buttons(window).Single(button => button.Name == "RemoveDeviceButton"
        && button.DataContext is DeviceItemViewModel row && row.Key.DeviceId == id);
    private static async Task Click(Window window, Button button)
    {
        button.BringIntoView(); Pump(); Assert.True(button.IsEnabled); button.Focus();
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        if (button.Command is IAsyncRelayCommand command && command.ExecutionTask is { } task)
            await task.WaitAsync(TimeSpan.FromSeconds(10));
        Pump();
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) { Pump(); if (predicate()) return; await Task.Delay(10, timeout.Token); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public Provider Provider { get; } = new();
        public DeviceSessionService Session { get; }
        public DevicesViewModel Model { get; }
        public Window Window { get; }
        private Fixture(DeviceSnapshot[] devices)
        {
            Provider.Inventory = new(1, devices.ToImmutableArray());
            Session = new([Provider]); Model = new(Session); Window = Open(Model);
        }
        public static async Task<Fixture> Create(params DeviceSnapshot[] devices)
        { var test = new Fixture(devices); await test.Session.StartAsync(); Pump(); return test; }
        public async Task Publish(params DeviceSnapshot[] devices)
        {
            Provider.Inventory = new(Provider.Inventory.Revision + 1, devices.ToImmutableArray());
            Provider.Changes.Writer.TryWrite(Provider.Inventory);
            await Until(() => Session.Snapshot.ProviderRevisions.GetValueOrDefault("test") == Provider.Inventory.Revision);
            Pump();
        }
        public async ValueTask DisposeAsync() { Window.Close(); Model.Dispose(); await Session.DisposeAsync(); }
    }
    private sealed class Provider : IDeviceProvider
    {
        public string ProviderId => "test";
        public DeviceInventorySnapshot Inventory = new(0, []);
        public readonly Channel<DeviceInventorySnapshot> Changes = Channel.CreateUnbounded<DeviceInventorySnapshot>();
        public readonly TaskCompletionSource<DeviceOperationResult> Removal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Removals, Mounts;
        public DeviceRemovalRequest? LastRemoval;
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default) => new(Inventory);
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        { yield return Inventory; await foreach (var snapshot in Changes.Reader.ReadAllAsync(token)) yield return snapshot; }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default)
        { Interlocked.Increment(ref Mounts); return new(new DeviceOperationResult(DeviceOperationStatus.AuthorizationDenied, new("Denied", "Native authorization denied", request.Volume))); }
        public async ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default)
        { LastRemoval = request; Interlocked.Increment(ref Removals); return await Removal.Task.WaitAsync(token); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
