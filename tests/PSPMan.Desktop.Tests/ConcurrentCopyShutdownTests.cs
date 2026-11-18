using System.Collections.Concurrent;
using Avalonia.Headless;
using PSPMan.Core.Transfers;
using static PSPMan.Desktop.Tests.GameCopyTestContext;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class ConcurrentCopyShutdownTests
{
    [Fact]
    public async Task CloseUsesRegisteredOwnersEvenBeforeAnyRowHasBeenPublished()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var queued = new ConcurrentQueue<Action>();
            await using var f = await Create(post: queued.Enqueue);
            f.IO.Pause = token => Task.Delay(Timeout.Infinite, token);
            var accepted = f.Copy.Submit([new(Guid.NewGuid(), f.Game)]).Accepted.Single();
            Assert.Empty(f.Copy.Operations); Assert.False(f.Copy.IsBusy);
            var closing = f.Copy.ConfirmCloseAsync();
            Assert.False(closing.IsCompleted); Assert.True(f.Copy.HasCloseConfirmation);
            Assert.False(f.Startup.Coordinator!.IsAdmissionOpen);
            f.Copy.CloseConfirmation!.AcceptCommand.Execute(null);
            Assert.True(await closing.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(GameCopyState.Cancelled, (await f.Copy.GetCompletion(accepted.OperationId)).State);
            while (queued.TryDequeue(out var action)) action();
            Assert.False(f.Copy.CanSubmitCopies);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllOwnersCompletingDuringDecisionCannotReopenAdmission(bool accept)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.IO.Pause = token => release.Task.WaitAsync(token);
            var first = f.Copy.Submit([new(Guid.NewGuid(), f.Game)]).Accepted.Single();
            await Wait(() => f.Copy.Operations.Any(row => row.ProgressPercentage > 0));
            f.IO.Entries = [new(0, "other.iso", 3)];
            var second = f.Copy.Submit([new(Guid.NewGuid(), f.Game)]).Accepted.Single();
            var requests = new[] { first, second };
            await Wait(() => f.Copy.Operations.Count == 2 && f.Copy.Operations.All(row => row.ProgressPercentage > 0));
            var closing = f.Copy.ConfirmCloseAsync(); var decision = f.Copy.CloseConfirmation!;
            release.TrySetResult();
            await Task.WhenAll(requests.Select(request => f.Copy.GetCompletion(request.OperationId)));
            await Wait(() => !f.Copy.IsBusy);
            Assert.False(closing.IsCompleted); Assert.False(f.Copy.CanSubmitCopies);
            Assert.False(f.Startup.Coordinator!.IsAdmissionOpen);
            if (accept) decision.AcceptCommand.Execute(null); else decision.KeepOpenCommand.Execute(null);
            Assert.Equal(accept, await closing);
            Assert.Equal(!accept, f.Startup.Coordinator.IsAdmissionOpen);
            decision.KeepOpenCommand.Execute(null);
            Assert.Equal(!accept, f.Startup.Coordinator.IsAdmissionOpen);
        });
    }

    [Fact]
    public async Task NoActiveOwnerClosesAdmissionBeforeReentrantUiObserversCanSubmit()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var f = await Create();
            GameCopyAdmission? late = null;
            f.Copy.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(f.Copy.IsClosing))
                    late = f.Startup.Coordinator!.Submit([new(Guid.NewGuid(), f.Game)]);
            };
            Assert.True(await f.Copy.ConfirmCloseAsync());
            Assert.NotNull(late); Assert.Empty(late.Accepted);
            await f.Copy.StopForShutdownAsync();
            Assert.False(f.Copy.HasCloseConfirmation);
        });
    }
}
