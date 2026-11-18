using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using PSPMan.Core.Devices;
using PSPMan.Core.Transfers;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;
using static PSPMan.Desktop.Tests.GameCopyTestContext;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class GameCopyFlowTests
{
    [Fact]
    public async Task CopyAcceptanceCapturesTheGameBeforeReturningControlToTheUi()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create();
            f.IO.Pause = token => Task.Delay(Timeout.Infinite, token);
            var operation = f.Copy.StartAsync();
            // No await/UI yield between submitting the action and changing the selection.
            f.Main.SelectedGame = null;
            await Wait(() => f.Copy.ProgressPercentage > 0);
            Assert.False(operation.IsCompleted);
            Assert.Equal(f.Game.Title, f.Copy.Title);
            await f.Copy.CancelCommand.ExecuteAsync(null);
            await operation;
            Assert.Equal("Cancelled", f.Copy.Status);
            Assert.Equal(0, f.Catalog.Writes);
        });
    }

    [Fact]
    public async Task MinimumWindowShowsActualSizeProgressAndRetainsResponsiveSelections()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.IO.Pause = token => release.Task.WaitAsync(token);
            var window = new MainWindow { DataContext = f.Main, Width = 900, Height = 450 };
            window.Show();
            var start = f.Main.CopySelectedGameCommand.ExecuteAsync(null);
            await Wait(() => f.Copy.ProgressPercentage > 0);
            Assert.False(f.IO.RanOnUi);
            Assert.True(f.Copy.IsBusy);
            Assert.False(f.Main.CopySelectedGameCommand.CanExecute(null));
            Assert.Equal("Test game", window.FindControl<GameCopyView>("CopyPanel")!.FindControl<TextBlock>("CopyTitleText")!.Text);
            Assert.Contains("3 bytes total", f.Copy.WorkText);
            Assert.InRange(f.Copy.ProgressPercentage, 1, 99);
            Assert.Equal("/psp0/ISO", f.Copy.Destination);
            var panel = window.FindControl<GameCopyView>("CopyPanel")!;
            var scroll = panel.FindControl<ScrollViewer>("CopyScrollViewer")!;
            Assert.InRange(scroll.Viewport.Height, 1, 220);
            Assert.InRange(panel.TranslatePoint(default, window)!.Value.Y + panel.Bounds.Height, 1, 450);
            Assert.True(window.FindControl<ListBox>("GamesList")!.IsEnabled);
            Assert.True(window.FindControl<Button>("ImportCatalogButton")!.IsVisible);
            Assert.True(f.Main.CanStartEdit);
            release.SetResult(); await start;
            Assert.Equal("Completed", f.Copy.Notification);
            Assert.Equal(100, f.Copy.ProgressPercentage);
            Assert.True(f.IO.OutputClosed && f.IO.Closed);
            Assert.False(f.Copy.IsBusy);
            Assert.Equal(new byte[] { 1, 2, 3 }, f.IO.Files["game.iso"].ToArray());
            Assert.Equal(0, f.Catalog.Writes);
            window.Close();
        });
    }

    [Theory]
    [InlineData(-1, "Select a game")]
    [InlineData(0, "No selected PSP")]
    [InlineData(2, "Select the PSP")]
    public async Task ExplicitCopyReportsMissingGameOrUnavailableOrAmbiguousPsp(int psps, string message)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create(Math.Max(0, psps));
            if (psps < 0) f.Main.SelectedGame = null;
            await f.Main.CopySelectedGameCommand.ExecuteAsync(null);
            Assert.Contains(message, f.Copy.Notification);
            Assert.True(f.Copy.IsOpen); Assert.False(f.Copy.IsBusy);
            Assert.Equal(0, f.IO.Opens); Assert.Empty(f.IO.Files);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialOrAllSkippedEntriesProduceTheQualifiedCompletion(bool all)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create();
            f.IO.Entries = all ? [new(0, "../unsafe.iso", 3)] : [new(0, "../unsafe.iso", 3), new(1, "game.iso", 3)];
            await f.Copy.StartAsync();
            Assert.Equal("Completed with skipped files", f.Copy.Notification);
            Assert.Equal(all ? 0 : 1, f.IO.Files.Count);
            Assert.Equal(100, f.Copy.ProgressPercentage);
        });
    }

    [Fact]
    public async Task ChangedTargetRenewsConsentAndOldButtonCannotAcceptNewPrompt()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create();
            f.IO.Files["game.iso"] = new([9]); f.IO.ChangeOnce = true;
            var operation = f.Copy.StartAsync();
            await Wait(() => f.Copy.Input is { IsOverwrite: true });
            var old = f.Copy.Input!;
            old.OverwriteCommand.Execute(null);
            await Wait(() => f.Copy.Input is { } input && !ReferenceEquals(input, old));
            old.OverwriteCommand.Execute(null);
            Assert.False(operation.IsCompleted);
            f.Copy.Input!.SkipCommand.Execute(null);
            await operation;
            Assert.Equal("Completed with skipped files", f.Copy.Notification);
            Assert.Equal(new byte[] { 9 }, f.IO.Files["game.iso"].ToArray());
        });
    }

    [Theory]
    [InlineData(GameCopyErrorCode.PasswordRequired)]
    [InlineData(GameCopyErrorCode.IncorrectPassword)]
    public async Task PasswordRetryReturnsToTheSameCopy(GameCopyErrorCode error)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create(); f.IO.OpenErrorOnce = error;
            var operation = f.Copy.StartAsync();
            await Wait(() => f.Copy.Input is { IsPassword: true });
            Assert.True(f.Copy.IsBusy); var input = f.Copy.Input!;
            input.Password = "secret"; input.RetryCommand.Execute(null);
            await operation;
            Assert.Empty(input.Password); Assert.Null(f.Copy.Input);
            Assert.Equal("Completed", f.Copy.Notification);
        });
    }

    [Fact]
    public async Task AmbiguousEncryptedErrorRetainsPartialFileAndRequiresRenewedOverwriteAfterRetry()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create(); f.IO.ExtractErrorOnce = GameCopyErrorCode.IndeterminateEncryptedError;
            var operation = f.Copy.StartAsync();
            await Wait(() => f.Copy.Input is { IsPassword: true });
            Assert.True(f.IO.OutputClosed);
            Assert.Equal(new byte[] { 1 }, f.IO.Files["game.iso"].ToArray());
            Assert.Equal("Incorrect password or damaged archive", f.Copy.Input!.Message);
            Assert.Contains("not restored", f.Copy.Input.Warning);
            f.Copy.Input.Password = "retry"; f.Copy.Input.RetryCommand.Execute(null);
            await Wait(() => f.Copy.Input is { IsOverwrite: true });
            f.Copy.Input!.OverwriteCommand.Execute(null); await operation;
            Assert.Equal("Completed", f.Copy.Notification);
        });
    }

    [Theory]
    [InlineData("game")]
    [InlineData("source")]
    [InlineData("psp")]
    [InlineData("cancel")]
    public async Task SelectionChangesPreserveCapturedCopyAndExplicitCancellationRetainsBytes(string change)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create(change == "psp" ? 2 : 1);
            if (change == "psp")
                await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseDestination(f.Session.Snapshot.Associations, f.Game.Id, new("copy-ui", "psp0")));
            f.IO.Pause = token => Task.Delay(Timeout.Infinite, token);
            var operation = f.Copy.StartAsync();
            await Wait(() => f.Copy.ProgressPercentage > 0);
            switch (change)
            {
                case "game": f.Main.SelectedGame = f.Game with { Id = Guid.NewGuid(), Title = "Another" }; break;
                case "source": await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseVolume(f.Session.Snapshot.Associations, SourceGroup.Removable, new(new("copy-ui", "alternate"), "volume"))); break;
                case "psp": await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseDestination(f.Session.Snapshot.Associations, f.Game.Id, new("copy-ui", "psp1"))); break;
                default: await f.Copy.CancelCommand.ExecuteAsync(null); break;
            }
            if (change != "cancel")
            {
                Assert.False(operation.IsCompleted);
                Assert.True(f.Copy.IsBusy);
                await f.Copy.CancelCommand.ExecuteAsync(null);
            }
            await operation;
            Assert.Equal("Cancelled", f.Copy.Status);
            Assert.Contains("remain on the PSP", f.Copy.Warning);
            Assert.Equal(new byte[] { 1 }, f.IO.Files["game.iso"].ToArray());
            Assert.True(f.IO.OutputClosed); Assert.False(f.Copy.IsBusy);
        });
    }

    [Theory]
    [InlineData(GameCopyErrorCode.CorruptArchive)]
    [InlineData(GameCopyErrorCode.WriteError)]
    public async Task DefiniteFailureHasNoPasswordPromptAndReportsRetainedOutput(GameCopyErrorCode error)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create(); f.IO.ExtractErrorOnce = error;
            await f.Copy.StartAsync();
            Assert.Equal("Failed", f.Copy.Status); Assert.Null(f.Copy.Input);
            Assert.Contains("Decode failed", f.Copy.Notification);
            Assert.Contains("remain on the PSP", f.Copy.Warning);
            Assert.True(f.Copy.ProgressPercentage < 100);
        });
    }

    [Fact]
    public async Task QueuedProgressCannotReplaceTerminalStateOrAnotherOperation()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var queue = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            await using var f = await Create(post: queue.Enqueue);
            await f.Copy.StartAsync();
            Assert.Equal("Completed", f.Copy.Status);
            Assert.False(queue.IsEmpty);
            f.Main.SelectedGame = null;
            await f.Copy.StartAsync();
            // Previous-operation progress is deliberately delivered after the new operation ends.
            while (queue.TryDequeue(out var action)) action();
            Assert.Equal("Failed", f.Copy.Status); Assert.Contains("Select a game", f.Copy.Notification);
        });
    }

    [Fact]
    public async Task StopKeepsTheActiveRowUntilOutputClosureAndRejectsAnotherStart()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create();
            var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.IO.ClosePause = () => { closing.TrySetResult(); return release.Task; };
            var operation = f.Copy.StartAsync();
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var cancel = f.Copy.CancelCommand.ExecuteAsync(null);
            Assert.True(f.Copy.IsBusy); Assert.False(cancel.IsCompleted);
            await f.Copy.StartAsync();
            Assert.Equal(1, f.IO.Opens);
            release.SetResult(); await cancel; await operation;
            Assert.Equal("Cancelled", f.Copy.Status);
            Assert.True(f.IO.OutputClosed); Assert.False(f.Copy.IsBusy);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellingEitherInputRetainsFilesAndEndsTheOperation(bool password)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create();
            if (password) f.IO.OpenErrorOnce = GameCopyErrorCode.PasswordRequired;
            else f.IO.Files["game.iso"] = new([9]);
            var operation = f.Copy.StartAsync();
            await Wait(() => f.Copy.HasInput);
            f.Copy.Input!.CancelCommand.Execute(null);
            await operation;
            Assert.Equal("Cancelled", f.Copy.Status);
            Assert.Contains("remain on the PSP", f.Copy.Warning);
            Assert.Null(f.Copy.Input);
            if (!password) Assert.Equal(new byte[] { 9 }, f.IO.Files["game.iso"].ToArray());
        });
    }

    [Fact]
    public async Task UnavailableCompositionIsVisibleAndCatalogOnlyConsumersRemainOptional()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var main = new MainViewModel();
            Assert.False(main.HasCopy); Assert.False(main.CopySelectedGameCommand.CanExecute(null));
            await using var startup = new CopyStartup(null, "Devices could not be initialized.");
            main.AttachCopy(startup.ViewModel);
            await main.CopySelectedGameCommand.ExecuteAsync(null);
            Assert.Equal("Devices could not be initialized.", main.Copy!.Notification);
            main.AttachCopy(null);
            Assert.False(main.HasCopy);
        });
    }

    [Fact]
    public async Task DisposingCopyOwnerClosesPendingIoAndLeavesCallerSessionUsable()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create();
            f.IO.Pause = token => Task.Delay(Timeout.Infinite, token);
            var operation = f.Copy.StartAsync();
            await Wait(() => f.Copy.ProgressPercentage > 0);
            await f.Startup.DisposeAsync(); await operation;
            Assert.True(f.IO.OutputClosed);
            Assert.Equal(new byte[] { 1 }, f.IO.Files["game.iso"].ToArray());
            await f.Session.SetAssociationsAsync(new());
            Assert.Null(f.Session.Snapshot.Associations.RemovableSource);
        });
    }

    [Theory]
    [InlineData("space", "enough free")]
    [InlineData("unknown", "space")]
    [InlineData("empty", "empty")]
    [InlineData("missing", "Open failed")]
    public async Task PreparationFailuresAreVisibleAndNeverWrite(string failure, string message)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create();
            switch (failure)
            {
                case "space": f.IO.Available = 2; break;
                case "unknown": f.IO.Available = null; break;
                case "empty": f.IO.Entries = []; break;
                default: f.IO.OpenErrorOnce = GameCopyErrorCode.MissingArchive; break;
            }
            await f.Copy.StartAsync();
            Assert.Equal("Failed", f.Copy.Status);
            Assert.Contains(message, f.Copy.Notification, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(f.IO.Files); Assert.Equal(0, f.Catalog.Writes);
        });
    }
}
