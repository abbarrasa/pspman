using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;
using static PSPMan.Desktop.Tests.GameCopyTestContext;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class ConcurrentCopyRowsTests
{
    private static GameCopyRequest Request(string title = "Game")
    {
        var source = Device("source"); var psp = Device("psp", true);
        CopyVolumeEvidence Evidence(DeviceSnapshot device) => new(device.Key, device.Identity, device.MediaGeneration,
            device.Volumes[0].Key, device.Volumes[0].Identity, device.Volumes[0].Root!, MountedPathSyntax.Linux);
        return new(Guid.NewGuid(), new(Guid.NewGuid(), title, GameMedium.Usb, null, "/game.zip", GameFormat.Iso, 3, null), Evidence(source), Evidence(psp));
    }
    private static GameCopyOperationSnapshot Snapshot(GameCopyRequest request, long revision, GameCopyState state,
        GameCopyWaitReason? wait = null) => new(request, revision, new(request.OperationId, request.Game.Title, "/psp/ISO", state,
            new(new(3, 2, 1, 2, 1), .5m), WaitReason: wait));

    [Fact]
    public async Task RealCoordinatorRowsOverlapAndCancelOnlyTheirOwner()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create(post: action => Dispatcher.UIThread.Post(action));
            var view = new GameCopyView { DataContext = f.Copy };
            var window = new Window { Content = view, Width = 900, Height = 450 };
            window.Show();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.IO.Pause = token => release.Task.WaitAsync(token);
            var first = f.Copy.Submit([new(Guid.NewGuid(), f.Game with { Title = "Long captured title " + new string('x', 400) })]).Accepted.Single();
            await Wait(() => f.Copy.Operations.Count == 1 && f.Copy.Operations[0].Status == "Transferring");
            var second = f.Copy.Submit([new(Guid.NewGuid(), f.Game)]).Accepted.Single();
            await Wait(() => f.Copy.Operations.Count == 2 && f.Copy.Operations[1].Input is { IsOverwrite: true });
            f.Copy.Operations[1].Input!.OverwriteCommand.Execute(null);
            await Wait(() => f.Copy.Operations.Count == 2 && f.Copy.Operations.All(r => r.Status == "Transferring"));
            var rows = f.Copy.Operations.ToArray();
            window.UpdateLayout();
            var titles = view.GetVisualDescendants().OfType<TextBlock>().Where(control => control.Name == "CopyTitleText").ToArray();
            Assert.Equal(2, titles.Length);
            Assert.InRange(titles[0].Bounds.Width, 1, 300);
            var bars = view.GetVisualDescendants().OfType<ProgressBar>().Where(control => control.Name == "CopyProgressBar").ToArray();
            Assert.Equal(2, bars.Length);
            Assert.All(bars, bar => Assert.InRange(bar.Bounds.Width, 1, 160));
            var scroll = view.FindControl<ScrollViewer>("CopyScrollViewer")!;
            Assert.True(scroll.ClipToBounds); Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.Equal(first, rows[0].Request); Assert.Equal(second, rows[1].Request);
            Assert.Equal("/source/game.zip", rows[0].Source); Assert.Equal("/psp0", rows[1].Psp);
            await rows[0].CancelCommand.ExecuteAsync(null);
            Assert.Equal(GameCopyState.Cancelled, (await f.Copy.GetCompletion(first.OperationId)).State);
            await Wait(() => rows[0].Status == "Cancelled");
            Assert.True(rows[1].IsBusy); Assert.False(f.Copy.GetCompletion(second.OperationId).IsCompleted);
            release.SetResult();
            Assert.Equal(GameCopyState.Completed, (await f.Copy.GetCompletion(second.OperationId)).State);
            await Wait(() => !rows[1].IsBusy);
            Assert.Contains("not restored", rows[0].Warning); Assert.Equal("Completed", rows[1].Notification);
            Assert.Equal(0, f.Catalog.Writes);
            window.Close();
        });
    }

    [Theory]
    [InlineData(GameCopyState.Completed, "Completed", false)]
    [InlineData(GameCopyState.CompletedWithSkippedFiles, "Completed with skipped files", false)]
    [InlineData(GameCopyState.Cancelled, "Cancelled", true)]
    [InlineData(GameCopyState.Failed, "Failed", true)]
    public void TerminalRowsRejectLateProgress(GameCopyState state, string text, bool warning)
    {
        var request = Request(); var queued = new List<Action>();
        var row = new CopyOperationViewModel(request.OperationId, request, queued.Add, _ => Task.CompletedTask);
        row.Receive(Snapshot(request, 1, GameCopyState.Transferring));
        row.Receive(Snapshot(request, 3, state));
        row.Receive(Snapshot(request, 2, GameCopyState.Preparing));
        row.Receive(Snapshot(request, 4, GameCopyState.Transferring));
        foreach (var action in queued.ToArray().Reverse()) action();
        Assert.Equal(text, row.Status); Assert.Equal(text, row.Notification); Assert.Equal(3, row.Revision);
        Assert.False(row.IsBusy); Assert.False(row.IsIndeterminate);
        Assert.Equal(warning, row.Warning.Length > 0); Assert.False(row.CancelCommand.CanExecute(null));
        row.Dispose();
    }

    [Fact]
    public async Task PasswordAndOverwriteButtonsRemainBoundToTheirOwningRows()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var model = new GameCopyViewModel(action => action());
            var view = new GameCopyView { DataContext = model };
            var window = new Window { Content = view, Width = 900, Height = 450 };
            window.Show();
            var first = new GameCopyPrompt(Guid.NewGuid(), Guid.NewGuid());
            var second = new GameCopyPrompt(Guid.NewGuid(), Guid.NewGuid());
            using var cancellation = new CancellationTokenSource();
            var password = model.RequestPasswordAsync(new(first, GameCopyErrorCode.IndeterminateEncryptedError), cancellation.Token).AsTask();
            var overwrite = model.ConfirmOverwriteAsync(new(second, 0, "/psp/ISO/" + new string('x', 400), new("file", 3, "v1"), "Retained"), cancellation.Token).AsTask();
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, model.Operations.Count);
            var scroll = view.FindControl<ScrollViewer>("CopyScrollViewer")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var inputs = model.Operations.Select(row => row.Input!).ToArray();
            Assert.Contains("Incorrect password or damaged archive", inputs[0].Message);
            var boxes = view.GetVisualDescendants().OfType<TextBox>().Where(control => control.Name == "CopyPasswordInput").ToArray();
            Assert.Equal(2, boxes.Length); Assert.Equal('●', boxes[0].PasswordChar);
            Assert.Equal("Archive password", AutomationProperties.GetName(boxes[0]));
            boxes[0].Text = "private-password";
            var retry = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "RetryCopyPasswordButton" && ReferenceEquals(control.DataContext, inputs[0]));
            Assert.Same(inputs[0].RetryCommand, retry.Command);
            Assert.True(retry.Focus());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("private-password", (await password).Password);
            Assert.Empty(inputs[0].Password); Assert.False(overwrite.IsCompleted);
            inputs[0].CancelCommand.Execute(null); inputs[0].OverwriteCommand.Execute(null);
            Assert.False(overwrite.IsCompleted);
            inputs[1].RetryCommand.Execute(null); Assert.False(overwrite.IsCompleted);
            inputs[1].SkipCommand.Execute(null);
            Assert.Equal(CopyOverwriteDecision.Skip, (await overwrite).Decision);
            window.UpdateLayout();
            window.Close();
        });
    }

    [Fact]
    public async Task CancellingOneInputLeavesTheOtherAndRetiredButtonsCannotAnswerReplacement()
    {
        await using var model = new GameCopyViewModel(action => action());
        var id = Guid.NewGuid(); using var stop = new CancellationTokenSource();
        var first = model.RequestPasswordAsync(new(new(id, Guid.NewGuid()), GameCopyErrorCode.PasswordRequired), stop.Token).AsTask();
        var second = model.RequestPasswordAsync(new(new(Guid.NewGuid(), Guid.NewGuid()), GameCopyErrorCode.PasswordRequired), default).AsTask();
        var old = model.Operations[0].Input!; old.Password = "secret";
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Empty(old.Password); Assert.False(second.IsCompleted);
        var next = model.RequestPasswordAsync(new(new(id, Guid.NewGuid()), GameCopyErrorCode.IncorrectPassword), default).AsTask();
        old.RetryCommand.Execute(null); Assert.False(next.IsCompleted); Assert.False(second.IsCompleted);
        model.Operations[0].Input!.CancelCommand.Execute(null);
        Assert.Equal(CopyPasswordDecision.Cancel, (await next).Decision);
        model.Operations[1].Input!.Password = "other"; model.Operations[1].Input!.RetryCommand.Execute(null);
        Assert.Equal("other", (await second).Password);
    }

    [Fact]
    public async Task FailureRetiresOnlyItsOwnInputAndWaitReasonUsesActualProgress()
    {
        var first = Request(); var second = Request();
        var a = new CopyOperationViewModel(first.OperationId, first, action => action(), _ => Task.CompletedTask);
        var b = new CopyOperationViewModel(second.OperationId, second, action => action(), _ => Task.CompletedTask);
        var one = a.RequestAsync(new(first.OperationId, Guid.NewGuid()), true, "Password", "", "", default);
        var two = b.RequestAsync(new(second.OperationId, Guid.NewGuid()), false, "Overwrite", "/psp/ISO/a", "", default);
        a.Receive(Snapshot(first, 1, GameCopyState.Failed));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => one);
        Assert.False(two.IsCompleted); Assert.Null(a.Input);
        b.Receive(Snapshot(second, 1, GameCopyState.WaitingForResource, GameCopyWaitReason.DestinationFile));
        Assert.Contains("destination file", b.WaitText); Assert.Equal(50, b.ProgressPercentage);
        Assert.Contains("3 bytes total", b.WorkText); Assert.Contains("2 decompressed", b.WorkText); Assert.Contains("1 written", b.WorkText);
        b.Input!.SkipCommand.Execute(null); Assert.Equal("skip", (await two).Decision);
        a.Dispose(); b.Dispose();
    }

    [Fact]
    public async Task DisposalCancelsAllInputsAndIgnoresQueuedPublication()
    {
        var queued = new Queue<Action>(); var model = new GameCopyViewModel(queued.Enqueue);
        var first = model.RequestPasswordAsync(new(new(Guid.NewGuid(), Guid.NewGuid()), GameCopyErrorCode.PasswordRequired), default).AsTask();
        var second = model.ConfirmOverwriteAsync(new(new(Guid.NewGuid(), Guid.NewGuid()), 0, "/psp/ISO/a", new("a", 1, "v"), "Retained"), default).AsTask();
        await model.DisposeAsync();
        while (queued.TryDequeue(out var action)) action();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Empty(model.Operations); Assert.Null(model.Input);
    }

    [Fact]
    public async Task WrongOperationAndReusedPromptCannotAuthorizeInput()
    {
        var request = Request(); var row = new CopyOperationViewModel(request.OperationId, request, action => action(), _ => Task.CompletedTask);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => row.RequestAsync(new(Guid.NewGuid(), Guid.NewGuid()), true, "", "", "", default));
        var prompt = new GameCopyPrompt(request.OperationId, Guid.NewGuid());
        var response = row.RequestAsync(prompt, true, "", "", "", default);
        row.Input!.CancelCommand.Execute(null); await response;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => row.RequestAsync(prompt, true, "", "", "", default));
        row.Dispose();
    }

    [Fact]
    public async Task CancelWaitsForOwningCompletionAndCannotReviveRetiredInput()
    {
        var request = Request();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid? cancelled = null;
        var row = new CopyOperationViewModel(request.OperationId, request, action => action(), id => { cancelled = id; return release.Task; });
        var response = row.RequestAsync(new(request.OperationId, Guid.NewGuid()), true, "Password", "", "", default);
        var old = row.Input!; old.Password = "private";
        var stopping = row.CancelCommand.ExecuteAsync(null);
        Assert.Equal(request.OperationId, cancelled); Assert.False(stopping.IsCompleted);
        Assert.True(row.IsBusy); Assert.Equal("Stopping", row.Status); Assert.Empty(old.Password);
        old.RetryCommand.Execute(null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
        row.Receive(Snapshot(request, 1, GameCopyState.Transferring));
        Assert.Equal("Stopping", row.Status);
        row.Receive(Snapshot(request, 2, GameCopyState.Cancelled));
        release.SetResult(); await stopping;
        Assert.False(row.IsBusy); Assert.Equal("Cancelled", row.Status);
        row.Dispose();
    }

    [Fact]
    public async Task AcceptedCloseRejectsPromptResponsesUntilCancellationReleasesThem()
    {
        var request = Request(); bool closing = false;
        var row = new CopyOperationViewModel(request.OperationId, request, action => action(), _ => Task.CompletedTask, () => closing);
        using var stop = new CancellationTokenSource();
        var pending = row.RequestAsync(new(request.OperationId, Guid.NewGuid()), true, "Password", "", "", stop.Token);
        var old = row.Input!; old.Password = "secret"; closing = true;
        old.RetryCommand.Execute(null); Assert.False(pending.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => row.RequestAsync(new(request.OperationId, Guid.NewGuid()), true, "", "", "", default));
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(old.Password); row.Dispose();
    }

    [Fact]
    public async Task DisposalReservesItsSharedTaskBeforeReentrantRowCallbacks()
    {
        var model = new GameCopyViewModel(action => action());
        var pending = model.RequestPasswordAsync(new(new(Guid.NewGuid(), Guid.NewGuid()), GameCopyErrorCode.PasswordRequired), default).AsTask();
        Task? reentrant = null;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(GameCopyViewModel.Input)) reentrant = model.DisposeAsync().AsTask(); };
        var disposal = model.DisposeAsync().AsTask(); await disposal;
        Assert.Same(disposal, reentrant);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(model.Input);
    }
}
