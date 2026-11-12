using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;
using PSPMan.Infraestructure.Devices;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class ApplicationDeviceLifetimeTests
{
    [Fact]
    public async Task CompositionStartsOnlyAfterWindowOpensAndDoesNotReplaceCatalogState()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            using var data = new CatalogData();
            using var release = new ManualResetEventSlim();
            var entered = Signal();
            var provider = new Provider(Device("source"));
            var main = data.Open();
            main.SelectedGame = main.Games[0];
            main.StartEditCommand.Execute(null);
            main.Editor.Title = "Unsaved editor text";
            var window = new MainWindow { DataContext = main };
            var desktop = new Desktop(window);
            await using var lifetime = new ApplicationDeviceLifetime(window, main, desktop.Shutdown, _ =>
            {
                Assert.False(Dispatcher.UIThread.CheckAccess());
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                return ValueTask.FromResult(new DeviceProviderFactoryResult([provider]));
            });
            try
            {
                Pump();
                Assert.False(entered.Task.IsCompleted);
                Assert.True(main.Devices!.IsDetecting);
                window.Show();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(lifetime.Initialization.IsCompleted);
                Assert.Same(main, window.DataContext);
                var list = window.FindControl<ListBox>("GamesList")!;
                Assert.True(((ListBoxItem)list.ContainerFromIndex(0)!).Focus());
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                Pump();
                Assert.Equal(main.Games[1], main.SelectedGame);
                Assert.Equal("Unsaved editor text", main.Editor.Title);
                release.Set();
                await lifetime.Initialization.WaitAsync(TimeSpan.FromSeconds(10));
                await Until(() => main.Devices!.Sources.Length == 1);
                Assert.Same(main, window.DataContext);
                Assert.Equal(main.Games[1].Id, main.Devices!.GameDestination.GameId);
                Assert.Equal("/media/source/GAMES/two.iso", main.Devices.ConstructedSourcePath);
                Assert.Equal("Unsaved editor text", main.Editor.Title);
                Assert.Equal(0, provider.Mounts);
                var initialization = lifetime.Initialization;
                window.Hide(); window.Show(); Pump();
                Assert.Same(initialization, lifetime.Initialization);
                Assert.Equal(1, provider.Watches);
            }
            finally { release.Set(); await lifetime.DisposeAsync(); window.Close(); }
            Assert.Equal(1, provider.Disposals);
            Assert.Equal(1, provider.Unsubscriptions);
        });
    }

    [Fact]
    public async Task PendingInitialInventoryShowsDetectionAndAllowsCatalogInput()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            using var data = new CatalogData();
            var provider = new Provider(Device("source")) { Initial = Signal() };
            var main = data.Open();
            var window = new MainWindow { DataContext = main };
            await using var lifetime = new ApplicationDeviceLifetime(window, main, new Desktop(window).Shutdown, _ => Result(provider));
            try
            {
                window.Show();
                await Until(() => provider.Reads > 0 && provider.Watches > 0);
                Assert.False(lifetime.Initialization.IsCompleted);
                Assert.True(main.Devices!.IsDetecting);
                main.SelectedGame = main.Games[1]; Pump();
                Assert.Equal(main.Games[1], window.FindControl<ListBox>("GamesList")!.SelectedItem);
                provider.Initial.SetResult();
                await lifetime.Initialization;
                await Until(() => !main.Devices!.IsDetecting);
                Assert.Equal(main.Games[1].Id, main.Devices!.GameDestination.GameId);
            }
            finally { await lifetime.DisposeAsync(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServiceFailureDoesNotDisableTheCatalogAndCatalogFailureDoesNotPreventDiscovery(bool corruptCatalog)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            using var data = new CatalogData();
            if (corruptCatalog) File.WriteAllBytes(data.CatalogPath, [0, 1, 2]);
            var main = data.Open();
            var provider = new Provider(Device("source"));
            var window = new MainWindow { DataContext = main };
            await using var lifetime = new ApplicationDeviceLifetime(window, main, new Desktop(window).Shutdown,
                _ => corruptCatalog ? Result(provider) : throw new IOException("Factory unavailable"));
            try
            {
                window.Show(); await lifetime.Initialization; Pump();
                Assert.Equal(!corruptCatalog, main.IsCatalogAvailable);
                if (corruptCatalog)
                {
                    await Until(() => main.Devices!.Sources.Length == 1);
                    Assert.True(main.HasStartupError);
                    Assert.Equal(new byte[] { 0, 1, 2 }, File.ReadAllBytes(data.CatalogPath));
                }
                else
                {
                    Assert.Equal("Device services are unavailable.", main.Devices!.StatusText);
                    Assert.Contains(main.Devices.ProviderErrors, error => error.ErrorText.Contains("DeviceServiceUnavailable"));
                    main.SelectedGame = main.Games[0];
                    Assert.True(main.StartEditCommand.CanExecute(null));
                    Assert.NotNull(main.SelectedGame);
                }
            }
            finally { await lifetime.DisposeAsync(); window.Close(); }
        });
    }

    [Fact]
    public async Task ClosingDuringCompositionCancelsAndDisposesLateProvidersWithoutAttachingThem()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var release = new TaskCompletionSource<DeviceProviderFactoryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = Signal();
            CancellationToken factoryToken = default;
            var main = new MainViewModel();
            var window = new MainWindow { DataContext = main };
            var desktop = new Desktop(window);
            var provider = new Provider(Device("late"));
            await using var lifetime = new ApplicationDeviceLifetime(window, main, desktop.Shutdown, async token =>
            {
                factoryToken = token; entered.SetResult();
                return await release.Task; // Deliberately ignores cancellation until returning owned resources.
            });
            window.Show(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            window.Close(); Pump();
            Assert.True(factoryToken.IsCancellationRequested);
            Assert.True(window.IsVisible);
            Assert.Equal(0, desktop.Shutdowns);
            Assert.Null(main.Devices);
            release.SetResult(new([provider]));
            await lifetime.ShutdownCompletion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, provider.Disposals);
            Assert.Equal(0, provider.Reads);
            Assert.Null(main.Devices);
            Assert.Equal(1, desktop.Shutdowns);
            Assert.Equal(0, desktop.ExitCode);
        });
    }

    [Fact]
    public async Task ClosingDuringInitialInventoryCancelsObservationAndWaitsForSubscriptionDisposal()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var main = new MainViewModel();
            var window = new MainWindow { DataContext = main };
            var desktop = new Desktop(window);
            var provider = new Provider(Device("source")) { Initial = Signal(), DisposalRelease = Signal() };
            await using var lifetime = new ApplicationDeviceLifetime(window, main, desktop.Shutdown, _ => Result(provider));
            window.Show();
            await Until(() => provider.Reads > 0 && provider.Watches > 0);
            var old = main.Devices!;
            window.Close(); Pump();
            window.Close();
            Assert.True(desktop.RequestShutdown(lifetime).Cancel);
            await Until(() => provider.Disposals == 1);
            Assert.Equal(1, provider.Unsubscriptions);
            Assert.True(window.IsVisible);
            Assert.Equal(0, desktop.Shutdowns);
            Assert.Null(main.Devices);
            old.Update(new(new(999, [Device("late")]), new(), ImmutableDictionary<string, long>.Empty,
                ImmutableDictionary<string, DeviceOperationError>.Empty));
            Pump(); Assert.Empty(old.Devices);
            provider.DisposalRelease.SetResult();
            await lifetime.ShutdownCompletion.WaitAsync(TimeSpan.FromSeconds(10));
            await lifetime.DisposeAsync();
            Assert.Equal(1, desktop.Shutdowns);
            Assert.Equal(1, provider.Disposals);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public async Task ShutdownCancelsAnInFlightMountAndRejectsLateUiChanges()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var provider = new Provider(Unmounted(Device("source"))) { HoldMount = true };
            var main = new MainViewModel();
            var window = new MainWindow { DataContext = main };
            var desktop = new Desktop(window);
            await using var lifetime = new ApplicationDeviceLifetime(window, main, desktop.Shutdown, _ => Result(provider));
            window.Show(); await lifetime.Initialization;
            await Until(() => provider.Mounts == 1 && main.Devices!.Devices.Length == 1);
            var old = main.Devices!;
            Assert.Equal(DeviceState.Mounting, old.Devices[0].State);
            var snapshot = old.Devices;
            Assert.True(desktop.RequestShutdown(lifetime).Cancel);
            await lifetime.ShutdownCompletion.WaitAsync(TimeSpan.FromSeconds(10)); Pump();
            Assert.True(provider.MountCancelled);
            Assert.Equal(1, provider.Unsubscriptions);
            Assert.Equal(1, provider.Disposals);
            Assert.Null(main.Devices);
            Assert.Equal(snapshot, old.Devices);
        });
    }

    [Fact]
    public async Task EachExecutionResetsChoicesAndReenablesMountingAfterSuccessfulRemoval()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            using var data = new CatalogData();
            var reader = Device("reader");
            reader = reader with { Volumes = [reader.Volumes[0], reader.Volumes[0] with
                { Key = new(reader.Key, "second"), Root = "/media/second" }] };
            var provider = new Provider(reader, Device("other"), Device("psp-one", true), Device("psp-two", true));
            var main = data.Open(); main.SelectedGame = main.Games[0];
            var window = new MainWindow { DataContext = main };
            await using (var lifetime = new ApplicationDeviceLifetime(window, main, new Desktop(window).Shutdown, _ => Result(provider)))
            {
                window.Show(); await lifetime.Initialization;
                await Until(() => main.Devices!.RemovableSource.Candidates.Length == 2);
                var devices = main.Devices!;
                await devices.RemovableSource.Candidates.Single(choice => choice.Device == reader.Key).SelectCommand.ExecuteAsync(null); Pump();
                await devices.RemovableSource.Volumes.Single(choice => choice.Volume.VolumeId == "second").SelectCommand.ExecuteAsync(null); Pump();
                await devices.GameDestination.Options.Single(choice => choice.Name == "psp-two").SelectCommand.ExecuteAsync(null); Pump();
                Assert.Equal("second", devices.RemovableSource.SelectedVolume!.Value.VolumeId);
                Assert.Equal("psp-two", devices.GameDestination.PreferredDevice!.Value.DeviceId);
                await devices.Devices.Single(row => row.Key.DeviceId == "other").RemoveCommand.ExecuteAsync(null);
                await Until(() => provider.Removals == 1 && devices.Devices.Single(row => row.Key.DeviceId == "other").State == DeviceState.Unmounted);
                Assert.Equal(0, provider.Mounts);
                await lifetime.DisposeAsync(); window.Close();
            }
            var nextProvider = new Provider(provider.Inventory.Devices.ToArray());
            var next = data.Open(); next.SelectedGame = next.Games[0];
            var nextWindow = new MainWindow { DataContext = next };
            await using (var lifetime = new ApplicationDeviceLifetime(nextWindow, next, new Desktop(nextWindow).Shutdown, _ => Result(nextProvider)))
            {
                try
                {
                    nextWindow.Show(); await lifetime.Initialization;
                    await Until(() => nextProvider.Mounts == 1 && next.Devices!.RemovableSource.Candidates.Length == 2);
                    Assert.Null(next.Devices!.RemovableSource.PreferredDevice);
                    Assert.Null(next.Devices.RemovableSource.SelectedVolume);
                    Assert.Equal(SourceResolutionStatus.ChooseDevice, next.Devices.RemovableSource.Status);
                    Assert.Null(next.Devices.GameDestination.PreferredDevice);
                    Assert.Equal(DestinationResolutionStatus.ChooseDevice, next.Devices.GameDestination.Status);
                    Assert.True(nextProvider.Reads > 0);
                    Assert.Equal(0, nextProvider.Removals);
                }
                finally { await lifetime.DisposeAsync(); nextWindow.Close(); }
            }
            Assert.Equal(1, provider.Disposals);
            Assert.Equal(1, nextProvider.Disposals);
        });
    }

    [Fact]
    public async Task DisposalBeforeOpeningNeverStartsCompositionAndIsIdempotent()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var calls = 0;
            var main = new MainViewModel();
            var window = new MainWindow { DataContext = main };
            await using var lifetime = new ApplicationDeviceLifetime(window, main, new Desktop(window).Shutdown, _ =>
            { calls++; return ValueTask.FromResult(new DeviceProviderFactoryResult([])); });
            await lifetime.DisposeAsync(); await lifetime.DisposeAsync();
            Assert.False(lifetime.RequestShutdown());
            window.Show(); Pump(); window.Close();
            Assert.Equal(0, calls);
            Assert.Null(main.Devices);
        });
    }

    [Fact]
    public async Task CleanupFailureCompletesTheControlledExitWithAnErrorCode()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var main = new MainViewModel();
            var window = new MainWindow { DataContext = main };
            var desktop = new Desktop(window);
            var provider = new Provider(Device("source")) { FailDisposal = true };
            var lifetime = new ApplicationDeviceLifetime(window, main, desktop.Shutdown, _ => Result(provider));
            window.Show(); await lifetime.Initialization;
            window.Close();
            await lifetime.ShutdownCompletion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, desktop.ExitCode);
            Assert.Equal(1, desktop.Shutdowns);
            Assert.Equal(1, provider.Unsubscriptions);
            Assert.Equal(1, provider.Disposals);
            await Assert.ThrowsAsync<IOException>(async () => await lifetime.DisposeAsync());
        });
    }

    [Fact]
    public async Task InvalidCompositionIsReportedAndItsReturnedProviderIsReleased()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            using var data = new CatalogData();
            var main = data.Open();
            var window = new MainWindow { DataContext = main };
            var provider = new Provider(Device("source")) { InvalidId = true };
            await using var lifetime = new ApplicationDeviceLifetime(window, main, new Desktop(window).Shutdown, _ => Result(provider));
            try
            {
                window.Show(); await lifetime.Initialization; Pump();
                Assert.Equal("Device services are unavailable.", main.Devices!.StatusText);
                Assert.True(main.IsCatalogAvailable);
                Assert.Equal(1, provider.Disposals);
                Assert.Equal(0, provider.Watches);
            }
            finally { await lifetime.DisposeAsync(); window.Close(); }
            Assert.Equal(1, provider.Disposals);
        });
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) { Pump(); if (condition()) return; await Task.Delay(10, timeout.Token); }
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ValueTask<DeviceProviderFactoryResult> Result(Provider provider) => ValueTask.FromResult(new DeviceProviderFactoryResult([provider]));
    private static DeviceSnapshot Device(string id, bool psp = false)
    {
        var key = new DeviceKey("test", id);
        var identity = new DeviceIdentity(id, DeviceIdentityConfidence.Persistent);
        return new(key, id, psp ? DeviceMedium.Psp : DeviceMedium.Usb,
            (psp ? DeviceCapabilities.PspDestination : DeviceCapabilities.RemovableSource) | DeviceCapabilities.Mount | DeviceCapabilities.SafeRemoval,
            identity, "media", DeviceState.Ready, [new(new(key, "volume"), "Games", "/media/" + id, identity, DeviceState.Ready)]);
    }
    private static DeviceSnapshot Unmounted(DeviceSnapshot device) => device with
    { State = DeviceState.Unmounted, Volumes = device.Volumes.Select(volume => volume with { State = DeviceState.Unmounted, Root = null }).ToImmutableArray() };

    private sealed class Provider(params DeviceSnapshot[] devices) : IDeviceProvider
    {
        public string ProviderId => InvalidId ? "" : "test";
        public DeviceInventorySnapshot Inventory = new(1, devices.ToImmutableArray());
        public readonly Channel<DeviceInventorySnapshot> Changes = Channel.CreateUnbounded<DeviceInventorySnapshot>();
        public TaskCompletionSource? Initial, DisposalRelease;
        public int Reads, Watches, Unsubscriptions, Disposals, Mounts, Removals;
        public bool HoldMount, MountCancelled, FailDisposal, InvalidId;
        public async ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default)
        {
            Interlocked.Increment(ref Reads);
            if (Initial is not null) await Initial.Task.WaitAsync(token);
            return Inventory;
        }
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            Interlocked.Increment(ref Watches);
            try
            {
                if (Initial is not null) await Initial.Task.WaitAsync(token);
                yield return Inventory;
                await foreach (var change in Changes.Reader.ReadAllAsync(token)) yield return change;
            }
            finally { Interlocked.Increment(ref Unsubscriptions); }
        }
        public async ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default)
        {
            Interlocked.Increment(ref Mounts);
            if (HoldMount)
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { MountCancelled = true; throw; }
            }
            Publish(Inventory.Devices.Select(device => device.Key == request.Volume.Device ? device with
            { State = DeviceState.Ready, Volumes = device.Volumes.Select(volume => volume.Key == request.Volume
                ? volume with { State = DeviceState.Ready, Root = "/media/" + device.Key.DeviceId } : volume).ToImmutableArray() } : device));
            return new(DeviceOperationStatus.Succeeded);
        }
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default)
        {
            Interlocked.Increment(ref Removals);
            Publish(Inventory.Devices.Select(device => device.Key == request.Device ? Unmounted(device) : device));
            return ValueTask.FromResult(new DeviceOperationResult(DeviceOperationStatus.Succeeded));
        }
        private void Publish(IEnumerable<DeviceSnapshot> updated)
        { Inventory = new(Inventory.Revision + 1, updated.ToImmutableArray()); Changes.Writer.TryWrite(Inventory); }
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposals);
            if (DisposalRelease is not null) await DisposalRelease.Task;
            if (FailDisposal) throw new IOException("Simulated native cleanup failure");
        }
    }

    private sealed class Desktop(Window window)
    {
        public int Shutdowns, ExitCode;
        public ShutdownRequestedEventArgs RequestShutdown(ApplicationDeviceLifetime lifetime) => new() { Cancel = lifetime.RequestShutdown() };
        public void Shutdown(int exitCode = 0) { Shutdowns++; ExitCode = exitCode; window.Close(); }
    }

    private sealed class CatalogData : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("PSPMan-T44-");
        public string CatalogPath => Path.Combine(directory.FullName, "PSPMan", "catalog.db");
        public CatalogData()
        {
            var catalog = Open().CatalogService!;
            foreach (var name in new[] { "one", "two" })
                Assert.True(catalog.Create(new(name, GameMedium.Usb, null, "/GAMES/" + name + ".iso", GameFormat.Iso, 1, null)).IsSuccess);
        }
        public MainViewModel Open() => CatalogStartup.CreateMainViewModel(directory.FullName);
        public void Dispose() => directory.Delete(true);
    }
}
