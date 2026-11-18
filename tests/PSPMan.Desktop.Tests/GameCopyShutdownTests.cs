using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;
using PSPMan.Infraestructure.Devices;
using static PSPMan.Desktop.Tests.GameCopyTestContext;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class GameCopyShutdownTests
{
    [Theory]
    [InlineData("io")]
    [InlineData("password")]
    [InlineData("overwrite")]
    [InlineData("ambiguous")]
    public async Task DecliningClosePreservesCopyDevicesAndPendingInput(string stage)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create();
            var operation = await f.PauseAt(stage);
            var devices = f.Main.Devices; var input = f.Copy.Input;
            f.Window.Close();
            Assert.True(f.Window.IsVisible);
            Assert.True(f.Copy.HasCloseConfirmation);
            Assert.Contains("remain", f.Copy.CloseConfirmation!.Message);
            Assert.Same(devices, f.Main.Devices); Assert.Same(f.Copy, f.Main.Copy);
            Assert.False(operation.IsCompleted); Assert.Equal(0, f.Provider.Disposals);
            f.Copy.CloseConfirmation.KeepOpenCommand.Execute(null);
            await f.Lifetime.ShutdownCompletion;
            Assert.Same(devices, f.Main.Devices); Assert.Same(input, f.Copy.Input);
            Assert.True(f.Copy.IsBusy); Assert.Equal(0, f.Exits);
            await f.Copy.CancelCommand.ExecuteAsync(null); await operation;
        });
    }

    [Theory]
    [InlineData("io")]
    [InlineData("password")]
    [InlineData("overwrite")]
    [InlineData("ambiguous")]
    public async Task AcceptedCloseStopsInputAndReleasesFilesBeforeProviderAndExit(string stage)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create();
            f.IO.Files["previous.iso"] = new([7, 8]);
            var operation = await f.PauseAt(stage);
            var retired = f.Copy.Input;
            f.Provider.OnDispose = () => { Assert.True(f.IO.OutputClosed); return ValueTask.CompletedTask; };
            Assert.True(f.Lifetime.RequestShutdown()); // Same route used by App.ShutdownRequested.
            f.Copy.CloseConfirmation!.AcceptCommand.Execute(null);
            retired?.RetryCommand.Execute(null); retired?.OverwriteCommand.Execute(null);
            await f.Lifetime.ShutdownCompletion.WaitAsync(TimeSpan.FromSeconds(10));
            await operation;
            Assert.Equal(1, f.Exits); Assert.Equal(0, f.ExitCode);
            Assert.Equal(1, f.Provider.Disposals); Assert.True(f.IO.OutputClosed);
            Assert.Equal(new byte[] { 7, 8 }, f.IO.Files["previous.iso"].ToArray());
            Assert.Null(f.Main.Copy); Assert.Null(f.Main.Devices);
            if (stage is "io" or "ambiguous") Assert.Equal(new byte[] { 1 }, f.IO.Files["game.iso"].ToArray());
            if (stage == "overwrite") Assert.Equal(new byte[] { 9 }, f.IO.Files["game.iso"].ToArray());
            Assert.Equal(0, f.Catalog.Writes);
        });
    }

    [Fact]
    public async Task DuplicateRequestsShareOneDecisionAndDeclineAllowsAnotherFreshRequest()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create(); var operation = await f.PauseAt("io");
            f.Window.Close(); var first = f.Copy.CloseConfirmation!; var waiting = f.Lifetime.ShutdownCompletion;
            f.Window.Close(); Assert.True(f.Lifetime.RequestShutdown());
            Assert.Same(first, f.Copy.CloseConfirmation); Assert.Same(waiting, f.Lifetime.ShutdownCompletion);
            first.KeepOpenCommand.Execute(null); await waiting;
            f.Window.Close(); var second = f.Copy.CloseConfirmation!;
            Assert.NotSame(first, second);
            first.AcceptCommand.Execute(null);
            Assert.False(f.Lifetime.ShutdownCompletion.IsCompleted);
            Assert.True(f.Copy.IsBusy); Assert.Equal(0, f.Exits);
            second.AcceptCommand.Execute(null); await f.Lifetime.ShutdownCompletion; await operation;
            Assert.Equal(1, f.Exits);
        });
    }

    [Fact]
    public async Task AcceptedCloseKeepsStateAttachedWhileOutputIsClosing()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create();
            var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.IO.ClosePause = () => { closing.TrySetResult(); return release.Task; };
            var operation = await f.PauseAt("io");
            try
            {
                f.Window.Close(); f.Copy.CloseConfirmation!.AcceptCommand.Execute(null);
                await closing.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Same(f.Copy, f.Main.Copy); Assert.NotNull(f.Main.Devices);
                Assert.True(f.Copy.IsClosing); Assert.True(f.Copy.IsBusy);
                Assert.Equal(0, f.Provider.Disposals); Assert.Equal(0, f.Exits);
                Assert.False(f.Lifetime.ShutdownCompletion.IsCompleted);
            }
            finally { release.TrySetResult(); }
            await f.Lifetime.ShutdownCompletion; await operation;
            Assert.Equal(1, f.Exits);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletingWhileConfirmationIsOpenRequiresTheDecisionAndPreventsAnotherCopy(bool accept)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.IO.Pause = token => release.Task.WaitAsync(token);
            var operation = f.Copy.StartAsync(); await Wait(() => f.Copy.ProgressPercentage > 0);
            f.Window.Close(); var confirmation = f.Copy.CloseConfirmation!;
            release.SetResult(); await operation;
            Assert.Equal("Completed", f.Copy.Status); Assert.True(f.Copy.HasCloseConfirmation);
            Assert.False(f.Copy.CanStart); Assert.False(f.Main.CopySelectedGameCommand.CanExecute(null));
            await f.Copy.StartAsync(); Assert.Equal(1, f.IO.Opens);
            if (accept) confirmation.AcceptCommand.Execute(null); else confirmation.KeepOpenCommand.Execute(null);
            await f.Lifetime.ShutdownCompletion;
            Assert.Equal(accept ? 1 : 0, f.Exits);
            Assert.Equal(new byte[] { 1, 2, 3 }, f.IO.Files["game.iso"].ToArray());
            if (!accept) Assert.True(f.Copy.CanStart);
        });
    }

    [Fact]
    public async Task CompletedEarlierCopySurvivesCancellingTheNextOneOnClose()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create(); await f.Copy.StartAsync();
            var completedBytes = f.IO.Files["game.iso"].ToArray();
            f.IO.Entries = [new(0, "next.iso", 3)];
            var next = await f.PauseAt("io"); f.Window.Close();
            f.Copy.CloseConfirmation!.AcceptCommand.Execute(null);
            await f.Lifetime.ShutdownCompletion; await next;
            Assert.Equal(completedBytes, f.IO.Files["game.iso"].ToArray());
            Assert.Equal(new byte[] { 1 }, f.IO.Files["next.iso"].ToArray());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProgrammaticDisposalRetiresPendingDecisionAndWaitsForCopyRelease(bool requestClose)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create(); var operation = await f.PauseAt("ambiguous");
            if (requestClose) f.Window.Close();
            var retired = f.Copy.CloseConfirmation; var input = f.Copy.Input!;
            input.Password = "private-before-close";
            await f.Lifetime.DisposeAsync(); await operation;
            await f.Lifetime.ShutdownCompletion;
            await Wait(() => f.Copy.Input is null);
            retired?.AcceptCommand.Execute(null); input.RetryCommand.Execute(null);
            Assert.Empty(input.Password);
            Assert.Null(f.Copy.CloseConfirmation); Assert.Null(f.Copy.Input);
            Assert.Equal(requestClose ? 1 : 0, f.Exits);
            Assert.True(f.IO.OutputClosed); Assert.Equal(1, f.Provider.Disposals);
            Assert.Null(f.Main.Copy); Assert.Null(f.Main.Devices);
        });
    }

    [Fact]
    public async Task ProviderCleanupFailureStillReleasesCopyAndExitsOnceWithError()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var f = await Fixture.Create();
            try
            {
                var operation = await f.PauseAt("io");
                f.Provider.OnDispose = () => throw new IOException("Provider cleanup failed");
                f.Window.Close(); f.Copy.CloseConfirmation!.AcceptCommand.Execute(null);
                await f.Lifetime.ShutdownCompletion; await operation;
                Assert.Equal(1, f.Exits); Assert.Equal(1, f.ExitCode);
                Assert.True(f.IO.OutputClosed); Assert.Equal(1, f.Provider.Disposals);
                await Assert.ThrowsAsync<IOException>(() => f.Lifetime.DisposeAsync().AsTask());
            }
            finally { f.Window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MinimumWindowKeepsCloseChoicesVisibleAndKeyboardDeclineWorks(bool longDestination)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create(); var operation = await f.PauseAt("password");
            if (longDestination) f.Copy.Destination = "/psp/ISO/" + new string('a', 5000);
            f.Window.Close(); Dispatcher.UIThread.RunJobs();
            var panel = f.Window.FindControl<GameCopyView>("CopyPanel")!;
            foreach (var name in new[] { "KeepCopyingButton", "ConfirmCopyCloseButton" })
            {
                var button = panel.FindControl<Button>(name)!;
                var point = button.TranslatePoint(default, f.Window)!.Value;
                Assert.InRange(point.Y, 0, 450);
                Assert.InRange(point.Y + button.Bounds.Height, 1, 450);
                Assert.True(button.IsEffectivelyVisible);
            }
            var keep = panel.FindControl<Button>("KeepCopyingButton")!;
            Assert.Same(f.Copy.CloseConfirmation!.KeepOpenCommand, keep.Command);
            Assert.True(keep.Focus());
            f.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            f.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await f.Lifetime.ShutdownCompletion;
            Assert.False(f.Copy.HasCloseConfirmation); Assert.False(operation.IsCompleted);
        });
    }

    [Fact]
    public async Task SafeRemovalDuringCloseDecisionWaitsForCopyThenRetainsItsNativeError()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create(); var operation = await f.PauseAt("io");
            f.Provider.OnRemove = _ =>
            {
                Assert.True(f.IO.OutputClosed);
                return ValueTask.FromResult(new DeviceOperationResult(DeviceOperationStatus.Failed, new("Veto", "Native removal veto")));
            };
            f.Window.Close();
            var psp = f.Main.Devices!.Devices.Single(d => d.Key.DeviceId == "psp0");
            await psp.RemoveCommand.ExecuteAsync(null); await operation;
            await Wait(() => f.Main.Devices.Devices.Any(d => d.ErrorText?.Contains("Veto") == true));
            Assert.Equal("Failed", f.Copy.Status); Assert.Equal(1, f.Provider.Removals);
            Assert.True(f.Copy.HasCloseConfirmation);
            f.Copy.CloseConfirmation!.KeepOpenCommand.Execute(null); await f.Lifetime.ShutdownCompletion;
            Assert.Equal(0, f.Exits); Assert.Equal(new byte[] { 1 }, f.IO.Files["game.iso"].ToArray());
        });
    }

    [Fact]
    public async Task AcceptedCloseWaitsForNonCooperativeRemovalAfterCopyRelease()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create(); var operation = await f.PauseAt("io");
            var release = new TaskCompletionSource<DeviceOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Provider.OnRemove = _ => new(release.Task);
            try
            {
                f.Window.Close();
                await f.Main.Devices!.Devices.Single(d => d.Key.DeviceId == "psp0").RemoveCommand.ExecuteAsync(null);
                await Wait(() => f.Provider.Removals == 1); await operation;
                f.Copy.CloseConfirmation!.AcceptCommand.Execute(null);
                await Wait(() => f.Main.Devices is null);
                Assert.True(f.IO.OutputClosed); Assert.Equal(0, f.Exits);
                Assert.Equal(0, f.Provider.Disposals);
            }
            finally { release.TrySetResult(new(DeviceOperationStatus.Failed, new("Veto", "Native removal veto"))); }
            await f.Lifetime.ShutdownCompletion;
            Assert.Equal(1, f.Exits); Assert.Equal(1, f.Provider.Disposals);
        });
    }

    [Fact]
    public async Task AcceptedCloseWaitsForNonCooperativeDecoderBeforeClosingOutput()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.IO.Pause = _ => release.Task; // Models a native callback that has not returned yet.
            var operation = f.Copy.StartAsync(); await Wait(() => f.Copy.ProgressPercentage > 0);
            try
            {
                f.Window.Close(); f.Copy.CloseConfirmation!.AcceptCommand.Execute(null);
                await Wait(() => f.Copy.IsClosing && f.Copy.Status == "Stopping");
                Assert.False(operation.IsCompleted); Assert.False(f.IO.OutputClosed);
                Assert.Equal(0, f.Exits); Assert.Equal(0, f.Provider.Disposals);
                Assert.Same(f.Copy, f.Main.Copy);
            }
            finally { release.TrySetResult(); }
            await f.Lifetime.ShutdownCompletion; await operation;
            Assert.True(f.IO.OutputClosed); Assert.Equal(1, f.Exits);
            Assert.Equal(new byte[] { 1 }, f.IO.Files["game.iso"].ToArray());
        });
    }

    [Theory]
    [InlineData("password")]
    [InlineData("ambiguous")]
    public async Task ChangingGameDuringCloseDecisionFailsAndRetiresInputWithoutRestarting(string stage)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create(); var operation = await f.PauseAt(stage);
            var input = f.Copy.Input!;
            f.Window.Close(); f.Main.SelectedGame = null;
            await operation; await Wait(() => f.Copy.Input is null);
            f.Copy.CloseConfirmation!.KeepOpenCommand.Execute(null);
            await f.Lifetime.ShutdownCompletion;
            input.RetryCommand.Execute(null);
            Assert.Equal("Failed", f.Copy.Status); Assert.Equal(0, f.Exits);
            Assert.False(f.Copy.HasCloseConfirmation); Assert.False(f.Copy.IsBusy);
            Assert.Equal(1, f.IO.Opens);
        });
    }

    [Fact]
    public async Task NoActiveCopyClosesWithoutCreatingAConfirmation()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create();
            f.Window.Close(); await f.Lifetime.ShutdownCompletion;
            Assert.False(f.Copy.HasCloseConfirmation); Assert.Equal(1, f.Exits);
            Assert.Equal(0, f.IO.Opens); Assert.Equal(1, f.Provider.Disposals);
        });
    }

    [Theory]
    [InlineData("construction")]
    [InlineData("copy")]
    [InlineData("devices")]
    public async Task ReentrantDisposalDuringStartupNeverAttachesLateStateOrStartsDiscovery(string stage)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = new Fixture();
            Task? disposal = null;
            if (stage == "construction") f.AfterCopyCreated = () => disposal = f.Lifetime.DisposeAsync().AsTask();
            else f.Main.PropertyChanged += (_, args) =>
            {
                if (disposal is null && f.Main.Copy is not null &&
                    args.PropertyName == (stage == "copy" ? nameof(MainViewModel.Copy) : nameof(MainViewModel.Devices)))
                    disposal = f.Lifetime.DisposeAsync().AsTask();
            };
            f.Window.Show(); await f.Lifetime.Initialization;
            Assert.NotNull(disposal); await disposal;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(f.Main.Copy); Assert.Null(f.Main.Devices);
            Assert.Equal(1, f.Provider.Disposals); Assert.Equal(0, f.Provider.Reads);
        });
    }

    [Fact]
    public async Task ReentrantDisposalFromUiDetachmentSharesTheReservedCleanupTask()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Fixture.Create();
            Task? nested = null;
            f.Main.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Devices) && f.Main.Devices is null)
                    nested = f.Lifetime.DisposeAsync().AsTask();
            };
            var cleanup = f.Lifetime.DisposeAsync().AsTask();
            Assert.Same(cleanup, nested);
            await cleanup;
            Assert.Equal(1, f.Provider.Disposals);
        });
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly CopyIo IO = new();
        internal readonly Provider Provider = new() { Inventory = new(1, [Device("source"), Device("psp0", true)]) };
        internal readonly GameRecord Game = new(Guid.NewGuid(), "Game", GameMedium.Usb, null, "/game.zip", GameFormat.Iso, 3, null);
        internal readonly Repository Catalog;
        internal readonly MainViewModel Main;
        internal readonly MainWindow Window;
        internal readonly ApplicationDeviceLifetime Lifetime;
        internal GameCopyViewModel Copy = null!;
        internal int Exits, ExitCode;
        internal Action? AfterCopyCreated;
        internal Fixture()
        {
            Catalog = new(Game); Main = new(new GameCatalogService(Catalog), null); Main.SelectedGame = Game;
            Window = new() { DataContext = Main, Width = 900, Height = 450 };
            Lifetime = new(Window, Main, code => { ExitCode = code; ++Exits; Window.Hide(); },
                _ => ValueTask.FromResult(new DeviceProviderFactoryResult([Provider])),
                createCopy: (session, error) =>
                {
                    var copy = new CopyStartup(session, error, IO, IO, MountedPathSyntax.Linux, resources: new TestCopyResources());
                    AfterCopyCreated?.Invoke(); return copy;
                });
        }
        internal static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Window.Show(); await f.Lifetime.Initialization;
            await Wait(() => f.Main.Devices!.RemovableSource.Candidates.Length == 1);
            await f.Main.Devices!.ChooseSourceCommand.ExecuteAsync(f.Main.Devices.RemovableSource.Candidates[0]);
            f.Copy = f.Main.Copy!; return f;
        }
        internal async Task<Task> PauseAt(string stage)
        {
            switch (stage)
            {
                case "password": IO.OpenErrorOnce = GameCopyErrorCode.PasswordRequired; break;
                case "overwrite": IO.Files["game.iso"] = new([9]); break;
                case "ambiguous": IO.ExtractErrorOnce = GameCopyErrorCode.IndeterminateEncryptedError; break;
                default: IO.Pause = token => Task.Delay(Timeout.Infinite, token); break;
            }
            var operation = Copy.StartAsync();
            await Wait(() => stage == "io" ? Copy.ProgressPercentage > 0 : Copy.HasInput);
            return operation;
        }
        public async ValueTask DisposeAsync()
        { await Lifetime.DisposeAsync(); await Lifetime.ShutdownCompletion; Window.Close(); }
    }
}
